using System.Threading.Channels;
using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia.Threading;

namespace Akbura.Hooks;

/// <summary>
/// Publishes leading and latest trailing inputs without restarting a running window.
/// </summary>
public static class ThrottleHooks
{
    [UseHook]
    public static State<T> useThrottle<T>(
        [Self] this AkburaControl control,
        State<T> state,
        int milliseconds) =>
        useThrottle(control, state, TimeSpan.FromMilliseconds(milliseconds));

    [UseHook]
    public static State<T> useThrottle<T>(
        [Self] this AkburaControl control,
        State<T> state,
        TimeSpan interval) =>
        useThrottle(control, state, interval, TimeProvider.System);

    [UseHook]
    public static State<TResult> useThrottle<T, TResult>(
        [Self] this AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        int milliseconds) =>
        useThrottle(control, state, selector, TimeSpan.FromMilliseconds(milliseconds));

    [UseHook]
    public static State<TResult> useThrottle<T, TResult>(
        [Self] this AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        TimeSpan interval) =>
        useThrottle(control, state, selector, interval, TimeProvider.System);

    [UseHook]
    public static void useThrottle(
        [Self] this AkburaControl control,
        Action callback,
        int milliseconds,
        ReadOnlySpan<object?> dependencies) =>
        useThrottle(control, callback, TimeSpan.FromMilliseconds(milliseconds), dependencies);

    [UseHook]
    public static void useThrottle(
        [Self] this AkburaControl control,
        Action callback,
        TimeSpan interval,
        ReadOnlySpan<object?> dependencies) =>
        useThrottle(control, callback, interval, dependencies, TimeProvider.System);

    internal static State<T> useThrottle<T>(
        AkburaControl control,
        State<T> state,
        TimeSpan interval,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(timeProvider);
        HookTimerExecution.ValidateDelay(interval, allowZero: false);

        var value = state.Value;
        var result = control.useHookState(value);
        useThrottle(control, () => { result.Value = value; }, interval, [state, value], timeProvider);
        return result;
    }

    internal static State<TResult> useThrottle<T, TResult>(
        AkburaControl control,
        State<T> state,
        Func<T, TResult> selector,
        TimeSpan interval,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(selector);
        ArgumentNullException.ThrowIfNull(timeProvider);
        HookTimerExecution.ValidateDelay(interval, allowZero: false);

        var value = state.Value;
        var result = control.useHookState(() => selector(value));
        useThrottle(
            control, () => { result.Value = selector(value); }, interval, [state, value], timeProvider);
        return result;
    }

    internal static void useThrottle(
        AkburaControl control,
        Action callback,
        TimeSpan interval,
        ReadOnlySpan<object?> dependencies,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(callback);
        ArgumentNullException.ThrowIfNull(timeProvider);
        HookTimerExecution.ValidateDelay(interval, allowZero: false);

        var cell = control.useHookState(static () => new ThrottleCell()).Value;
        control.useEffect(
            cancellationToken => RunAsync(cell, interval, timeProvider, cancellationToken),
            [interval, timeProvider]);

        // Only a changed accepted input replaces the pending callback. A fresh
        // inline delegate on an unrelated frame does not alter the pending input.
        control.useEffect(
            () => { Volatile.Read(ref cell.Channel)?.Writer.TryWrite(callback); },
            HookTimerExecution.AddTimingDependencies(dependencies, interval, timeProvider));
    }

    private static async Task RunAsync(
        ThrottleCell cell,
        TimeSpan interval,
        TimeProvider timeProvider,
        CancellationToken cancellationToken)
    {
        var channel = Channel.CreateBounded<Action>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = true,
            AllowSynchronousContinuations = false
        });
        cancellationToken.ThrowIfCancellationRequested();
        Volatile.Write(ref cell.Channel, channel);

        try
        {
            while (true)
            {
                var callback = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(
                    () =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        while (channel.Reader.TryRead(out var latest))
                        {
                            callback = latest;
                        }

                        callback();
                    },
                    DispatcherPriority.Default,
                    cancellationToken);

                // The window starts after the callback finishes. New inputs do
                // not reset it, and an empty mailbox does not produce a trailing tick.
                await Task.Delay(interval, timeProvider, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            channel.Writer.TryComplete();
            Interlocked.CompareExchange(ref cell.Channel, null, channel);
        }
    }

    private sealed class ThrottleCell
    {
        public Channel<Action>? Channel;
    }
}
