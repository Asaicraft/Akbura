using Avalonia;
using Avalonia.Animation;
using Avalonia.Threading;

namespace Akbura.Hooks;

/// <summary>
/// Runs finite animations during the lifetime of a committed <c>useAnimate</c> hook.
/// </summary>
/// <remarks>
/// A new run cancels this controller's earlier runs on the same target that animate
/// any of the same properties. Animations owned by other callers are not stopped.
/// All public operations must be called on the UI thread. Disabled runs retain the
/// target's base values without applying keyframes. Stopping an existing animation
/// obeys Avalonia's fill mode, including the last interpolated Forward/Both value.
/// </remarks>
public sealed class AnimationController
{
    private readonly Func<Animation, Animatable, CancellationToken, Task>? _runner;
    private readonly List<ActiveRun> _runs = [];
    private Lifetime? _lifetime;

    internal AnimationController(Func<Animation, Animatable, CancellationToken, Task>? runner = null)
    {
        _runner = runner;
    }

    public Task RunAsync(
        Animatable target,
        Animation animation,
        CancellationToken cancellationToken = default)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(animation);
        cancellationToken.ThrowIfCancellationRequested();
        var properties = AnimationPlayback.GetProperties(animation);
        for (var i = _runs.Count - 1; i >= 0; i--)
        {
            var previous = _runs[i];
            if (ReferenceEquals(previous.Target, target) && previous.Properties.Overlaps(properties))
            {
                previous.Cancel();
            }
        }

        if (!_lifetime!.Enabled)
        {
            return Task.CompletedTask;
        }

        var run = new ActiveRun(target, properties);
        _runs.Add(run);
        return RunCoreAsync(run, animation, cancellationToken);
    }

    /// <summary>
    /// Stops all unfinished animations owned by this controller.
    /// </summary>
    public void Stop()
    {
        VerifyActive();
        CancelRuns(null);
    }

    /// <summary>
    /// Stops this controller's unfinished animations on the specified target.
    /// </summary>
    public void Stop(Animatable target)
    {
        VerifyActive();
        ArgumentNullException.ThrowIfNull(target);
        CancelRuns(target);
    }

    internal IDisposable Activate(bool enabled)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_lifetime is not null)
        {
            throw new InvalidOperationException("This animation controller already has an active lifetime.");
        }

        return _lifetime = new Lifetime(this, enabled);
    }

    private async Task RunCoreAsync(
        ActiveRun run,
        Animation animation,
        CancellationToken cancellationToken)
    {
        using var cancellation = cancellationToken.Register(() => CancelOnDispatcher(run));
        try
        {
            await AnimationPlayback.RunAsync(run.Target, animation, run.Token, _runner)
                .ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        finally
        {
            await OnDispatcherAsync(() =>
            {
                _runs.Remove(run);
                run.Dispose();
            }).ConfigureAwait(false);
        }
    }

    private void VerifyActive()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_lifetime is null)
        {
            throw new InvalidOperationException(
                "This animation controller does not belong to a committed, active hook lifetime.");
        }
    }

    private void CancelRuns(Animatable? target)
    {
        for (var i = _runs.Count - 1; i >= 0; i--)
        {
            var run = _runs[i];
            if (target is null || ReferenceEquals(run.Target, target))
            {
                run.Cancel();
            }
        }
    }

    private void Deactivate(Lifetime lifetime)
    {
        Dispatcher.UIThread.VerifyAccess();
        if (!ReferenceEquals(_lifetime, lifetime))
        {
            return;
        }

        _lifetime = null;
        CancelRuns(null);
    }

    private static void CancelOnDispatcher(ActiveRun run)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            run.Cancel();
        }
        else
        {
            Dispatcher.UIThread.Post(run.Cancel);
        }
    }

    private static async Task OnDispatcherAsync(Action callback)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            callback();
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(callback);
        }
    }

    private sealed class Lifetime(AnimationController controller, bool enabled) : IDisposable
    {
        public bool Enabled { get; } = enabled;

        public void Dispose() => controller.Deactivate(this);
    }

    private sealed class ActiveRun(Animatable target, HashSet<AvaloniaProperty> properties) : IDisposable
    {
        private readonly CancellationTokenSource _cancellation = new();
        private bool _disposed;

        public Animatable Target { get; } = target;

        public HashSet<AvaloniaProperty> Properties { get; } = properties;

        public CancellationToken Token => _cancellation.Token;

        public void Cancel()
        {
            if (!_disposed)
            {
                _cancellation.Cancel();
            }
        }

        public void Dispose()
        {
            _disposed = true;
            _cancellation.Dispose();
        }
    }
}
