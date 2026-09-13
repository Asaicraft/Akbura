using Avalonia.Threading;

namespace Akbura.Hooks;

internal static class HookTimerExecution
{
    private static readonly TimeSpan s_maxDelay = TimeSpan.FromMilliseconds(int.MaxValue);

    public static void ValidateDelay(TimeSpan? delay, bool allowZero)
    {
        if (delay is { } value &&
            (value < (allowZero ? TimeSpan.Zero : TimeSpan.FromMilliseconds(1)) ||
             value > s_maxDelay))
        {
            throw new ArgumentOutOfRangeException(
                nameof(delay),
                delay,
                allowZero
                    ? "Delay must be between zero and Int32.MaxValue milliseconds."
                    : "Interval must be between one and Int32.MaxValue milliseconds.");
        }
    }

    public static object?[] AddTimingDependencies(
        ReadOnlySpan<object?> dependencies,
        TimeSpan? delay,
        TimeProvider timeProvider)
    {
        var result = new object?[dependencies.Length + 2];
        dependencies.CopyTo(result);
        result[^2] = delay;
        result[^1] = timeProvider;
        return result;
    }

    public static async Task InvokeAsync(
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
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

    public static Func<CancellationToken, Task> Wrap(Action callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return cancellationToken =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            callback();
            return Task.CompletedTask;
        };
    }
}
