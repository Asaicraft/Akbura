using Avalonia.Threading;
using System.Runtime.ExceptionServices;

namespace Akbura.Hooks;

internal readonly struct UseHookKey : IEquatable<UseHookKey>
{
    public UseHookKey(Type declaringType, int overloadId)
    {
        DeclaringType = declaringType ?? throw new ArgumentNullException(nameof(declaringType));
        OverloadId = overloadId;
    }

    public Type DeclaringType { get; }

    public int OverloadId { get; }

    public bool Equals(UseHookKey other)
    {
        return DeclaringType == other.DeclaringType && OverloadId == other.OverloadId;
    }

    public override bool Equals(object? obj)
    {
        return obj is UseHookKey other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(DeclaringType, OverloadId);
    }
}

internal delegate ValueTask<IDisposable?> UseEffectCallback(CancellationToken cancellationToken);

internal readonly struct UseEffectRegistration
{
    public UseEffectRegistration(
        UseHookKey key,
        UseEffectCallback callback,
        bool hasDependencies,
        ReadOnlySpan<object?> dependencies,
        IUseHookDependenciesComparer? comparer)
    {
        Key = key;
        Callback = callback ?? throw new ArgumentNullException(nameof(callback));
        HasDependencies = hasDependencies;
        Dependencies = hasDependencies ? dependencies.ToArray() : null;
        Comparer = comparer;
    }

    public UseHookKey Key { get; }

    public UseEffectCallback Callback { get; }

    public bool HasDependencies { get; }

    public object?[]? Dependencies { get; }

    public IUseHookDependenciesComparer? Comparer { get; }
}

internal sealed class UseEffectSlot
{
    private readonly UseHookKey _key;
    private CancellationTokenSource? _cancellation;
    private IDisposable? _cleanup;
    private UseEffectRegistration _registration;
    private object?[]? _dependencies;
    private long _generation;
    private bool _hasRun;
    private bool _restartRequired;

    public UseEffectSlot(UseHookKey key)
    {
        _key = key;
    }

    public UseHookKey Key => _key;

    public void Apply(UseEffectRegistration registration)
    {
        var shouldRun = ShouldRun(registration);
        _registration = registration;
        _dependencies = registration.Dependencies;

        if (shouldRun)
        {
            Restart();
        }
    }

    public void StopForDetach()
    {
        if (!_hasRun)
        {
            return;
        }

        StopCurrentRun();
        _restartRequired = true;
    }

    private bool ShouldRun(UseEffectRegistration registration)
    {
        if (_restartRequired || !_hasRun || !registration.HasDependencies)
        {
            return true;
        }

        var previous = _dependencies ?? Array.Empty<object?>();
        var current = registration.Dependencies ?? Array.Empty<object?>();
        if (registration.Comparer != null)
        {
            return !registration.Comparer.Equals(previous, current);
        }

        if (previous.Length != current.Length)
        {
            return true;
        }

        for (var index = 0; index < previous.Length; index++)
        {
            if (!EqualityComparer<object?>.Default.Equals(previous[index], current[index]))
            {
                return true;
            }
        }

        return false;
    }

    private void Restart()
    {
        StopCurrentRun();
        _restartRequired = false;
        _hasRun = true;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = _generation;

        ValueTask<IDisposable?> pendingCleanup;
        try
        {
            pendingCleanup = _registration.Callback(cancellation.Token);
        }
        catch
        {
            StopCurrentRun();
            throw;
        }

        if (pendingCleanup.IsCompletedSuccessfully)
        {
            CompleteRun(generation, cancellation, pendingCleanup.Result);
            return;
        }

        _ = ObserveRunAsync(generation, cancellation, pendingCleanup.AsTask());
    }

    private async Task ObserveRunAsync(
        long generation,
        CancellationTokenSource cancellation,
        Task<IDisposable?> pendingCleanup)
    {
        IDisposable? cleanup = null;
        Exception? failure = null;
        try
        {
            cleanup = await pendingCleanup.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            failure = exception;
        }

        if (cancellation.IsCancellationRequested)
        {
            cleanup?.Dispose();
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (failure != null)
            {
                ExceptionDispatchInfo.Capture(failure).Throw();
            }

            CompleteRun(generation, cancellation, cleanup);
        });
    }

    private void CompleteRun(
        long generation,
        CancellationTokenSource cancellation,
        IDisposable? cleanup)
    {
        if (generation != _generation ||
            !ReferenceEquals(cancellation, _cancellation) ||
            cancellation.IsCancellationRequested)
        {
            cleanup?.Dispose();
            return;
        }

        _cleanup = cleanup;
    }

    private void StopCurrentRun()
    {
        _generation++;

        var cancellation = _cancellation;
        _cancellation = null;
        cancellation?.Cancel();

        var cleanup = _cleanup;
        _cleanup = null;
        cleanup?.Dispose();
        cancellation?.Dispose();
    }
}

internal sealed class UseHookRuntime
{
    private readonly AkburaControl _owner;
    private readonly List<UseEffectRegistration> _pending = [];
    private List<UseEffectSlot>? _slots;
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

    public void Register(UseEffectRegistration registration)
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
            _slots = new List<UseEffectSlot>(_pending.Count);
            foreach (var registration in _pending)
            {
                _slots.Add(new UseEffectSlot(registration.Key));
            }
        }

        for (var index = 0; index < _pending.Count; index++)
        {
            _slots[index].Apply(_pending[index]);
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
            if (!_slots[index].Key.Equals(_pending[index].Key))
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

internal sealed class ActionUseEffectCleanup : IDisposable
{
    private Action? _cleanup;

    public ActionUseEffectCleanup(Action cleanup)
    {
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _cleanup, null)?.Invoke();
    }
}
