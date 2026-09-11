using Avalonia.Threading;

namespace Akbura.Hooks;

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
    private bool _isActive;
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

        _restartRequired = true;
        StopCurrentRun();
    }

    public void Trigger()
    {
        if (_isActive)
        {
            Restart();
        }
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
        _restartRequired = true;
        StopCurrentRun();
        StartCurrentRun();
    }

    private void StartCurrentRun()
    {
        _restartRequired = false;
        _hasRun = true;
        _isActive = true;

        var cancellation = new CancellationTokenSource();
        _cancellation = cancellation;
        var generation = _generation;

        ValueTask<IDisposable?> pendingCleanup;
        try
        {
            pendingCleanup = _registration.Callback(cancellation.Token);
        }
        catch (Exception exception)
        {
            List<Exception>? failures = null;
            UseHookFailures.Capture(ref failures, exception);
            try
            {
                StopCurrentRun();
            }
            catch (Exception stopException)
            {
                UseHookFailures.Capture(ref failures, stopException);
            }

            _restartRequired = true;
            UseHookFailures.ThrowIfAny(
                failures,
                "A use effect failed while its run was being started.");
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

        Dispatcher.UIThread.Post(() =>
        {
            CompleteObservedRun(
                generation,
                cancellation,
                cleanup,
                failure);
        });
    }

    private void CompleteObservedRun(
        long generation,
        CancellationTokenSource cancellation,
        IDisposable? cleanup,
        Exception? failure)
    {
        if (!IsCurrentRun(generation, cancellation))
        {
            cleanup?.Dispose();
            return;
        }

        if (failure == null)
        {
            _cleanup = cleanup;
            return;
        }

        List<Exception>? failures = null;
        UseHookFailures.Capture(ref failures, failure);
        try
        {
            StopCurrentRun();
        }
        catch (Exception stopException)
        {
            UseHookFailures.Capture(ref failures, stopException);
        }

        _restartRequired = true;
        UseHookFailures.ThrowIfAny(
            failures,
            "An asynchronous use effect failed while its run was being stopped.");
    }

    private void CompleteRun(
        long generation,
        CancellationTokenSource cancellation,
        IDisposable? cleanup)
    {
        if (!IsCurrentRun(generation, cancellation))
        {
            cleanup?.Dispose();
            return;
        }

        _cleanup = cleanup;
    }

    private bool IsCurrentRun(
        long generation,
        CancellationTokenSource cancellation)
    {
        return generation == _generation &&
            ReferenceEquals(cancellation, _cancellation) &&
            !cancellation.IsCancellationRequested;
    }

    private void StopCurrentRun()
    {
        _generation++;

        var cancellation = _cancellation;
        _cancellation = null;
        var cleanup = _cleanup;
        _cleanup = null;
        _isActive = false;

        List<Exception>? failures = null;
        try
        {
            cancellation?.Cancel();
        }
        catch (Exception exception)
        {
            UseHookFailures.Capture(ref failures, exception);
        }

        try
        {
            cleanup?.Dispose();
        }
        catch (Exception exception)
        {
            UseHookFailures.Capture(ref failures, exception);
        }

        try
        {
            cancellation?.Dispose();
        }
        catch (Exception exception)
        {
            UseHookFailures.Capture(ref failures, exception);
        }

        UseHookFailures.ThrowIfAny(
            failures,
            "A use effect could not cancel and clean up its current run.");
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
