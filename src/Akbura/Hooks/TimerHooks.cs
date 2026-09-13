using Akbura.CompilerAnotations;

namespace Akbura.Hooks;

/// <summary>
/// Runs cancellable timers with the latest successfully committed callback.
/// </summary>
public static class TimerHooks
{
    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Action callback,
        int? milliseconds) =>
        useTimeout(control, callback, milliseconds, []);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Action callback,
        int? milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useTimeout(control, callback, ToTimeSpan(milliseconds), dependencies);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan? delay) =>
        useTimeout(control, callback, delay, []);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan? delay,
        ReadOnlySpan<object?> dependencies) =>
        useTimeout(control, HookTimerExecution.Wrap(callback), delay, dependencies);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        int? milliseconds) =>
        useTimeout(control, callback, milliseconds, []);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        int? milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useTimeout(control, callback, ToTimeSpan(milliseconds), dependencies);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? delay) =>
        useTimeout(control, callback, delay, []);

    [UseHook]
    public static void useTimeout(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? delay,
        ReadOnlySpan<object?> dependencies) =>
        useTimeout(control, callback, delay, dependencies, TimeProvider.System);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Action callback,
        int? milliseconds) =>
        useInterval(control, callback, milliseconds, []);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Action callback,
        int? milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useInterval(control, callback, ToTimeSpan(milliseconds), dependencies);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan? interval) =>
        useInterval(control, callback, interval, []);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan? interval,
        ReadOnlySpan<object?> dependencies) =>
        useInterval(control, HookTimerExecution.Wrap(callback), interval, dependencies);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        int? milliseconds) =>
        useInterval(control, callback, milliseconds, []);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        int? milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useInterval(control, callback, ToTimeSpan(milliseconds), dependencies);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? interval) =>
        useInterval(control, callback, interval, []);

    [UseHook]
    public static void useInterval(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? interval,
        ReadOnlySpan<object?> dependencies) =>
        useInterval(control, callback, interval, dependencies, TimeProvider.System);

    internal static void useTimeout(
        AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? delay,
        ReadOnlySpan<object?> dependencies,
        TimeProvider timeProvider) =>
        UseTimer(control, callback, delay, dependencies, timeProvider, repeat: false);

    internal static void useInterval(
        AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? interval,
        ReadOnlySpan<object?> dependencies,
        TimeProvider timeProvider) =>
        UseTimer(control, callback, interval, dependencies, timeProvider, repeat: true);

    private static void UseTimer(
        AkburaControl control,
        Func<CancellationToken, Task> callback,
        TimeSpan? delay,
        ReadOnlySpan<object?> dependencies,
        TimeProvider timeProvider,
        bool repeat)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(timeProvider);
        HookTimerExecution.ValidateDelay(delay, allowZero: !repeat);

        var latest = control.useHookState(static () => new CallbackCell()).Value;
        control.useEffect(() => { latest.Callback = callback; });
        control.useEffect(
            cancellationToken => delay is { } value
                ? RunAsync(latest, value, timeProvider, repeat, cancellationToken)
                : Task.CompletedTask,
            HookTimerExecution.AddTimingDependencies(dependencies, delay, timeProvider));
    }

    private static async Task RunAsync(
        CallbackCell latest,
        TimeSpan delay,
        TimeProvider timeProvider,
        bool repeat,
        CancellationToken cancellationToken)
    {
        do
        {
            await Task.Delay(delay, timeProvider, cancellationToken).ConfigureAwait(false);
            await HookTimerExecution.InvokeAsync(
                token => latest.Callback!(token), cancellationToken).ConfigureAwait(false);
        }
        while (repeat);
    }

    private static TimeSpan? ToTimeSpan(int? milliseconds) =>
        milliseconds is { } value ? TimeSpan.FromMilliseconds(value) : null;

    private sealed class CallbackCell
    {
        public Func<CancellationToken, Task>? Callback { get; set; }
    }
}
