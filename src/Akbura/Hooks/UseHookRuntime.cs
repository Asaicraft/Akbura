using Akbura.ComponentTree;

namespace Akbura.Hooks;

internal interface IUseHookRegistration
{
    UseHookKey Key { get; }

    Type StateType { get; }

    IUseHookSlot CreateSlot();

    void Apply(IUseHookSlot slot);
}

internal interface IUseHookSlot
{
    UseHookKey Key { get; }

    Type StateType { get; }

    void StopForDetach();
}

internal readonly struct DelegateUseHookRegistration<TState, TArguments> : IUseHookRegistration
    where TState : class
{
    private readonly TArguments _arguments;
    private readonly Func<TArguments, TState> _createState;
    private readonly Action<TState, TArguments> _apply;
    private readonly Action<TState>? _detach;

    public DelegateUseHookRegistration(
        UseHookKey key,
        TArguments arguments,
        Func<TArguments, TState> createState,
        Action<TState, TArguments> apply,
        Action<TState>? detach)
    {
        Key = key ?? throw new ArgumentNullException(nameof(key));
        _arguments = arguments;
        _createState = createState ?? throw new ArgumentNullException(nameof(createState));
        _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        _detach = detach;
    }

    public UseHookKey Key { get; }

    public Type StateType => typeof(TState);

    public IUseHookSlot CreateSlot()
    {
        var state = _createState(_arguments);
        if (state == null)
        {
            throw new InvalidOperationException(
                "A use hook runtime state factory returned null.");
        }

        return new DelegateUseHookSlot<TState>(Key, state, _detach);
    }

    public void Apply(IUseHookSlot slot)
    {
        var typedSlot = (DelegateUseHookSlot<TState>)slot;
        typedSlot.Apply(_arguments, _apply, _detach);
    }
}

internal sealed class DelegateUseHookSlot<TState> : IUseHookSlot
    where TState : class
{
    private readonly TState _state;
    private Action<TState>? _detach;
    private bool _isDetached;

    public DelegateUseHookSlot(UseHookKey key, TState state, Action<TState>? detach)
    {
        Key = key;
        _state = state;
        _detach = detach;
    }

    public UseHookKey Key { get; }

    public Type StateType => typeof(TState);

    public void Apply<TArguments>(
        TArguments arguments,
        Action<TState, TArguments> apply,
        Action<TState>? detach)
    {
        _isDetached = false;
        _detach = detach;
        apply(_state, arguments);
    }

    public void StopForDetach()
    {
        if (_isDetached)
        {
            return;
        }

        _isDetached = true;
        _detach?.Invoke(_state);
    }
}

internal sealed class UseHookRuntime
{
    private readonly AkburaControl _owner;
    private readonly List<IUseHookRegistration> _pending = [];
    private List<IUseHookSlot>? _slots;
    private bool _isCollecting;
    private bool _isCompleting;
    private bool _isInitializingState;
    private int _frameThreadId;
    private int _lifecycleCallbackDepth;
    private bool _needsRestart;
    private bool _isSuspended;
    private bool _resetForHotReloadPending;

    public UseHookRuntime(AkburaControl owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public bool HasSlots => _slots is { Count: > 0 };

    public bool NeedsRestart => _needsRestart;

    public bool IsFrameCommitted { get; private set; }

    public void BeginFrame()
    {
        if (_isCollecting || _isCompleting)
        {
            throw new InvalidOperationException("A use hook frame is already active.");
        }

        _pending.Clear();
        IsFrameCommitted = false;
        _frameThreadId = Environment.CurrentManagedThreadId;
        _isCollecting = true;
    }

    public void Register(IUseHookRegistration registration)
    {
        EnsureCollecting();
        _pending.Add(registration);
    }

    public State<T> GetState<T>(StateInfo<T> info, T initialValue)
    {
        return GetState(info, initialValue, static (_, value) => new State<T>(value));
    }

    public State<T> GetState<T>(StateInfo<T> info, Func<T> initialize)
    {
        ArgumentNullException.ThrowIfNull(initialize);
        return GetState(info, initialize, static (_, factory) => new State<T>(factory()));
    }

    public State<T> GetState<T>(StateInfo<T> info)
    {
        return GetState(info, info, static (owner, descriptor) => descriptor.CreateUnattachedState(owner));
    }

    public void ValidateStateAlias(State state)
    {
        ArgumentNullException.ThrowIfNull(state);
        EnsureCollecting();
        if (state.IsOwnedBy(_owner))
        {
            return;
        }

        for (var index = 0; index < _pending.Count; index++)
        {
            if (_pending[index] is IHookStateSlot slot && ReferenceEquals(slot.UntypedState, state))
            {
                return;
            }
        }

        throw new InvalidOperationException(
            "A composable hook must return a state owned by its component or created by useHookState.");
    }

    private State<T> GetState<T, TArgument>(
        StateInfo<T> info,
        TArgument argument,
        Func<AkburaControl, TArgument, State<T>> createState)
    {
        ArgumentNullException.ThrowIfNull(info);
        EnsureCollecting();

        HookStateSlot<T> slot;
        if (_slots != null)
        {
            var index = _pending.Count;
            if (index >= _slots.Count ||
                _slots[index] is not HookStateSlot<T> existing ||
                !ReferenceEquals(existing.Info, info))
            {
                throw new AkburaUseHooksFrameChangedException(
                    _owner,
                    _slots.Count,
                    index + 1,
                    index);
            }

            slot = existing;
        }
        else
        {
            if (_isSuspended)
            {
                throw new InvalidOperationException(
                    "A new hook state cannot be created while its component is detached.");
            }

            _isInitializingState = true;
            try
            {
                var state = createState(_owner, argument);
                if (state.IsAttached)
                {
                    throw new InvalidOperationException(
                        "A hook state factory must return an unattached state.");
                }

                slot = new HookStateSlot<T>(info, state);
            }
            finally
            {
                _isInitializingState = false;
            }
        }

        _pending.Add(slot);
        return slot.State;
    }

    private void EnsureCollecting()
    {
        if (!_isCollecting ||
            _isInitializingState ||
            _lifecycleCallbackDepth != 0 ||
            _frameThreadId != Environment.CurrentManagedThreadId)
        {
            throw new AkburaUseHookOutsideRenderException(_owner);
        }
    }

    public void CompleteFrame() => CompleteFrame(null);

    public void CompleteFrame(Action? commitFrame)
    {
        if (!_isCollecting)
        {
            throw new InvalidOperationException("There is no active use hook frame.");
        }

        _isCollecting = false;
        _isCompleting = true;

        try
        {
            ValidateFrame();
            if (_isSuspended)
            {
                if (_slots != null)
                {
                    IsFrameCommitted = true;
                    commitFrame?.Invoke();
                }
                else
                {
                    _needsRestart = true;
                }

                if (_resetForHotReloadPending)
                {
                    ResetForHotReloadCore();
                }

                return;
            }

            if (_slots == null)
            {
                _slots = CreateInitialSlots();
            }

            IsFrameCommitted = true;
            commitFrame?.Invoke();
            ApplyExistingSlots(_slots);

            _needsRestart = _isSuspended && _slots.Count != 0;
            if (_resetForHotReloadPending)
            {
                ResetForHotReloadCore();
            }
        }
        finally
        {
            _isCompleting = false;
            _pending.Clear();
        }
    }

    public void AbortFrame()
    {
        _isCollecting = false;
        _pending.Clear();

        if (_resetForHotReloadPending)
        {
            ResetForHotReloadCore();
        }
    }

    public void StopForDetach()
    {
        _isSuspended = true;

        if (_slots == null)
        {
            return;
        }

        List<Exception>? failures = null;
        StopSlots(_slots, ref failures);
        _needsRestart = _slots.Count != 0;

        UseHookFailures.ThrowIfAny(
            failures,
            "One or more use hooks could not be stopped for detach.");
    }

    public void Resume()
    {
        _isSuspended = false;
    }

    public void ResetForHotReload()
    {
        if (_isCollecting || _isCompleting)
        {
            _resetForHotReloadPending = true;
            return;
        }

        ResetForHotReloadCore();
    }

    private void ResetForHotReloadCore()
    {
        var slots = _slots;
        _slots = null;
        _needsRestart = false;
        _resetForHotReloadPending = false;
        if (slots == null)
        {
            return;
        }

        List<Exception>? failures = null;
        StopSlots(slots, ref failures);
        ReleaseStates(slots);
        UseHookFailures.ThrowIfAny(
            failures,
            "One or more use hooks could not be reset for Hot Reload.");
    }

    private List<IUseHookSlot> CreateInitialSlots()
    {
        var slots = new List<IUseHookSlot>(_pending.Count);

        try
        {
            for (var index = 0; index < _pending.Count; index++)
            {
                var registration = _pending[index];
                var slot = registration.CreateSlot();
                slots.Add(slot);
            }

            for (var index = 0; index < slots.Count; index++)
            {
                if (slots[index] is IHookStateSlot stateSlot)
                {
                    stateSlot.Attach(_owner);
                }
            }

            return slots;
        }
        catch (Exception exception)
        {
            List<Exception>? failures = null;
            UseHookFailures.Capture(ref failures, exception);
            StopSlots(slots, ref failures);
            ReleaseStates(slots);
            UseHookFailures.ThrowIfAny(
                failures,
                "A use hook frame could not be created or cleaned up.");
            throw;
        }
    }

    private void ApplyExistingSlots(List<IUseHookSlot> slots)
    {
        try
        {
            for (var index = 0; index < _pending.Count; index++)
            {
                if (_isSuspended)
                {
                    break;
                }

                _pending[index].Apply(slots[index]);
            }
        }
        catch (Exception exception)
        {
            List<Exception>? failures = null;
            UseHookFailures.Capture(ref failures, exception);
            StopSlots(slots, ref failures);
            _needsRestart = slots.Count != 0;
            UseHookFailures.ThrowIfAny(
                failures,
                "A use hook frame could not be applied or stopped.");
            throw;
        }
    }

    private void StopSlots(
        List<IUseHookSlot> slots,
        ref List<Exception>? failures)
    {
        _lifecycleCallbackDepth++;
        try
        {
            for (var index = 0; index < slots.Count; index++)
            {
                try
                {
                    slots[index].StopForDetach();
                }
                catch (Exception exception)
                {
                    UseHookFailures.Capture(ref failures, exception);
                }
            }
        }
        finally
        {
            _lifecycleCallbackDepth--;
        }
    }

    private void ReleaseStates(List<IUseHookSlot> slots)
    {
        for (var index = 0; index < slots.Count; index++)
        {
            if (slots[index] is IHookStateSlot stateSlot)
            {
                stateSlot.Release(_owner);
            }
        }
    }

    private void ValidateFrame()
    {
        if (_slots == null)
        {
            return;
        }

        if (_slots.Count != _pending.Count)
        {
            throw new AkburaUseHooksFrameChangedException(
                _owner,
                _slots.Count,
                _pending.Count);
        }

        for (var index = 0; index < _slots.Count; index++)
        {
            if (!ReferenceEquals(_slots[index].Key, _pending[index].Key) ||
                _slots[index].StateType != _pending[index].StateType ||
                _slots[index] is IHookStateSlot &&
                !ReferenceEquals(_slots[index], _pending[index]))
            {
                throw new AkburaUseHooksFrameChangedException(
                    _owner,
                    _slots.Count,
                    _pending.Count,
                    index);
            }
        }
    }
}

internal interface IHookStateSlot : IUseHookSlot
{
    State UntypedState { get; }

    void Attach(AkburaControl owner);

    void Release(AkburaControl owner);
}

internal sealed class HookStateSlot<T> : IHookStateSlot, IUseHookRegistration
{
    private static readonly UseHookKey s_key = new();

    public HookStateSlot(StateInfo<T> info, State<T> state)
    {
        Info = info;
        State = state;
    }

    public StateInfo<T> Info { get; }

    public State<T> State { get; }

    public State UntypedState => State;

    public UseHookKey Key => s_key;

    public Type StateType => typeof(State<T>);

    public IUseHookSlot CreateSlot() => this;

    public void Apply(IUseHookSlot slot)
    {
    }

    public void Attach(AkburaControl owner) => State.Attach(owner, Info);

    public void Release(AkburaControl owner) => State.Detach(owner);

    public void StopForDetach()
    {
    }
}
