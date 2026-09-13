using System.Runtime.ExceptionServices;
using Akbura.CompilerAnotations;
using Akbura.ComponentTree;

namespace Akbura.Hooks;

public static class ExternalSourceHooks
{
    private static readonly IUseHookDependenciesComparer s_sourceIdentityComparer =
        new SourceIdentityComparer();

    /// <summary>
    /// Subscribes after commit, retaining the last value when the source completes
    /// or changes. Error callbacks follow the latest successfully committed frame.
    /// </summary>
    [UseHook]
    public static State<T> useObservable<T>(
        [Self] this AkburaControl control,
        IObservable<T> source,
        T initialValue = default!,
        Action<Exception>? onError = null)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(source);

        var state = control.useHookState(initialValue);
        var callback = control.useHookState(() => new SourceHookCallback<Action<Exception>?>(onError));
        control.useEffect(() => { callback.Value.Current = onError; });
        control.useEffect(
            (Func<CancellationToken, IDisposable?>)(cancellationToken => source.Subscribe(
                new HookObserver<T>(state, callback.Value, cancellationToken))),
            [source],
            s_sourceIdentityComparer);

        return state;
    }

    /// <summary>
    /// Reads a snapshot lazily, subscribes after commit, then reads again to close
    /// the gap between rendering and subscribing. Getter changes require explicit
    /// restart dependencies.
    /// </summary>
    [UseHook]
    public static State<T> useExternalStore<T>(
        [Self] this AkburaControl control,
        Func<Action, IDisposable> subscribe,
        Func<T> getSnapshot,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(getSnapshot);

        var state = control.useHookState(getSnapshot);
        control.useEffect(
            (Func<CancellationToken, IDisposable?>)(cancellationToken =>
            {
                var subscription = subscribe(() => SourceHookDelivery.Dispatch(
                    () => { state.Value = getSnapshot(); }, cancellationToken))
                    ?? throw new InvalidOperationException("An external store subscription returned null.");

                try
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        state.Value = getSnapshot();
                    }

                    return subscription;
                }
                catch
                {
                    subscription.Dispose();
                    throw;
                }
            }),
            dependencies);

        return state;
    }

    private sealed class HookObserver<T>(
        State<T> state,
        SourceHookCallback<Action<Exception>?> onError,
        CancellationToken cancellationToken) : IObserver<T>
    {
        private int _isCompleted;

        public void OnCompleted() => Interlocked.Exchange(ref _isCompleted, 1);

        public void OnError(Exception error)
        {
            ArgumentNullException.ThrowIfNull(error);
            if (Interlocked.Exchange(ref _isCompleted, 1) != 0)
            {
                return;
            }

            SourceHookDelivery.Dispatch(() =>
            {
                if (onError.Current is { } callback)
                {
                    callback(error);
                }
                else
                {
                    ExceptionDispatchInfo.Capture(error).Throw();
                }
            }, cancellationToken);
        }

        public void OnNext(T value)
        {
            if (Volatile.Read(ref _isCompleted) == 0)
            {
                SourceHookDelivery.Dispatch(() => { state.Value = value; }, cancellationToken);
            }
        }
    }

    private sealed class SourceIdentityComparer : IUseHookDependenciesComparer
    {
        public bool Equals(
            ReadOnlySpan<object?> previousDependencies,
            ReadOnlySpan<object?> currentDependencies) =>
            previousDependencies.Length == 1 &&
            currentDependencies.Length == 1 &&
            ReferenceEquals(previousDependencies[0], currentDependencies[0]);
    }
}
