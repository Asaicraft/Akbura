using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia.Threading;

namespace Akbura.Hooks;

public static class AsyncHooks
{
    /// <summary>
    /// Loads a snapshot after commit and whenever the explicit dependencies change.
    /// Results from canceled runs cannot replace the current snapshot.
    /// </summary>
    [UseHook]
    public static State<AsyncSnapshot<T>> useAsync<T>(
        [Self] this AkburaControl control,
        Func<CancellationToken, Task<T>> loader,
        ReadOnlySpan<object?> dependencies,
        bool keepPreviousValue = true)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(loader);

        var snapshot = control.useHookState(default(AsyncSnapshot<T>));
        control.useEffect(
            cancellationToken => LoadAsync(snapshot, loader, keepPreviousValue, cancellationToken),
            dependencies);

        return snapshot;
    }

    private static async Task LoadAsync<T>(
        State<AsyncSnapshot<T>> state,
        Func<CancellationToken, Task<T>> loader,
        bool keepPreviousValue,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var previous = state.Value;
        var loading = new AsyncSnapshot<T>(
            isLoading: true,
            hasValue: keepPreviousValue && previous.HasValue,
            value: keepPreviousValue ? previous.Value : default,
            error: null);
        state.Value = loading;

        AsyncSnapshot<T> completed;
        try
        {
            // The loader starts on the effect's UI thread. Only its asynchronous
            // continuation is allowed to leave that thread.
            var task = loader(cancellationToken)
                ?? throw new InvalidOperationException("An asynchronous hook loader returned null Task.");
            var value = await task.ConfigureAwait(false);
            completed = new AsyncSnapshot<T>(false, true, value, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception exception)
        {
            completed = new AsyncSnapshot<T>(false, loading.HasValue, loading.Value, exception);
        }

        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await Dispatcher.UIThread.InvokeAsync(
                () =>
                {
                    if (!cancellationToken.IsCancellationRequested)
                    {
                        state.Value = completed;
                    }
                },
                DispatcherPriority.Default,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // A run can be canceled after completion but before UI delivery.
        }
    }
}
