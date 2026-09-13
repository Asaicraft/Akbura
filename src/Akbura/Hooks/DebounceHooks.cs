using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia.Threading;

namespace Akbura.Hooks;

/// <summary>
/// Composes persistent hook state and effects to publish a value after a quiet period.
/// </summary>
public static class DebounceHooks
{
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    [UseHook]
    public static State<T> useDebounce<T>(
        [Self] this AkburaControl control,
        State<T> state,
        int milliseconds) =>
        useDebounce(control, state, TimeSpan.FromMilliseconds(milliseconds));

    [UseHook]
    public static State<T> useDebounce<T>(
        [Self] this AkburaControl control,
        State<T> state,
        TimeSpan delay) =>
        useDebounce(control, state, delay, TimeProvider.System);

    [UseHook]
    public static State<TResult> useDebounce<T, TResult>(
        [Self] this AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        int milliseconds) =>
        useDebounce(control, state, selector, TimeSpan.FromMilliseconds(milliseconds));

    [UseHook]
    public static State<TResult> useDebounce<T, TResult>(
        [Self] this AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        TimeSpan delay) =>
        useDebounce(control, state, selector, delay, TimeProvider.System);

    [UseHook]
    public static void useDebounce(
        [Self] this AkburaControl control,
        Action callback,
        int milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useDebounce(control, callback, TimeSpan.FromMilliseconds(milliseconds), dependencies);

    [UseHook]
    public static void useDebounce(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan delay,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(callback);

        useDebounce(
            control,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                callback();
                return Task.CompletedTask;
            },
            delay,
            dependencies);
    }

    [UseHook]
    public static void useDebounce(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        int milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useDebounce(control, callback, TimeSpan.FromMilliseconds(milliseconds), dependencies);

    [UseHook]
    public static void useDebounce(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan delay,
        ReadOnlySpan<object?> dependencies) =>
        useDebounce(control, callback, delay, dependencies, TimeProvider.System);

    internal static State<T> useDebounce<T>(
        AkburaControl control,
        State<T> state,
        TimeSpan delay,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateDelay(delay);

        // Keep the source value from the frame that starts this run.
        var value = state.Value;
        var debouncedState = control.useHookState<T>(value);

        useDebounce(
            control,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                debouncedState.Value = value;
                return Task.CompletedTask;
            },
            delay,
            [state, value],
            timeProvider);

        return debouncedState;
    }

    internal static State<TResult> useDebounce<T, TResult>(
        AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        TimeSpan delay,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateDelay(delay);

        var value = state.Value;
        var debouncedState = control.useHookState<TResult>(() => selector(value));

        // Inline selectors may be new delegates each frame. The source and delay
        // determine when to restart; an unchanged run retains its original selector.
        useDebounce(
            control,
            cancellationToken =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                debouncedState.Value = selector(value);
                return Task.CompletedTask;
            },
            delay,
            [state, value],
            timeProvider);

        return debouncedState;
    }

    internal static void useDebounce(
        AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan delay,
        ReadOnlySpan<object?> dependencies,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ValidateDelay(delay);

        var effectDependencies = new object?[dependencies.Length + 1];
        dependencies.CopyTo(effectDependencies);
        effectDependencies[^1] = delay;

        control.useEffect(
            cancellationToken => RunAfterDelayAsync(
                callback, delay, timeProvider, cancellationToken),
            effectDependencies);
    }

    private static async Task RunAfterDelayAsync(
        Func<CancellationToken, Task> callback,
        TimeSpan delay,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        // InvokeAsync queues the callback even when delay is zero and this is
        // already the UI thread. Observe both dispatch and the returned task.
        var execution = await Dispatcher.UIThread.InvokeAsync<Task>(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return callback(cancellationToken)
                    ?? throw new InvalidOperationException("A hook callback returned null Task.");
            },
            DispatcherPriority.Default,
            cancellationToken);

        await execution.ConfigureAwait(false);
    }

    private static void ValidateDelay(TimeSpan delay)
    {
        if (delay < TimeSpan.Zero || delay > s_maxDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                delay,
                "Delay must be between zero and Int32.MaxValue milliseconds.");
        }
    }
}
