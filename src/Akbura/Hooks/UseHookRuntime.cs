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

        return new DelegateUseHookSlot<TState>(Key, state);
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

    public DelegateUseHookSlot(UseHookKey key, TState state)
    {
        Key = key;
        _state = state;
    }

    public UseHookKey Key { get; }

    public Type StateType => typeof(TState);

    public void Apply<TArguments>(
        TArguments arguments,
        Action<TState, TArguments> apply,
        Action<TState>? detach)
    {
        apply(_state, arguments);
        _isDetached = false;
        _detach = detach;
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
    private bool _needsRestart;

    public UseHookRuntime(AkburaControl owner)
    {
        _owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    public bool HasSlots => _slots is { Count: > 0 };

    public bool NeedsRestart => _needsRestart;

    public void BeginFrame()
    {
        if (_isCollecting)
        {
            throw new InvalidOperationException("A use hook frame is already active.");
        }

        _pending.Clear();
        _isCollecting = true;
    }

    public void Register(IUseHookRegistration registration)
    {
        if (!_isCollecting)
        {
            throw new AkburaUseHookOutsideRenderException(_owner);
        }

        _pending.Add(registration);
    }

    public void CompleteFrame()
    {
        if (!_isCollecting)
        {
            throw new InvalidOperationException("There is no active use hook frame.");
        }

        _isCollecting = false;
        ValidateFrame();

        if (_slots == null)
        {
            _slots = new List<IUseHookSlot>(_pending.Count);
            foreach (var registration in _pending)
            {
                _slots.Add(registration.CreateSlot());
            }
        }

        for (var index = 0; index < _pending.Count; index++)
        {
            _pending[index].Apply(_slots[index]);
        }

        _needsRestart = false;
        _pending.Clear();
    }

    public void AbortFrame()
    {
        _isCollecting = false;
        _pending.Clear();
    }

    public void StopForDetach()
    {
        if (_slots == null)
        {
            return;
        }

        foreach (var slot in _slots)
        {
            slot.StopForDetach();
        }

        _needsRestart = _slots.Count != 0;
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
                _slots[index].StateType != _pending[index].StateType)
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
