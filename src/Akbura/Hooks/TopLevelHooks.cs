using Akbura.CompilerAnotations;
using Akbura.ComponentTree;
using Avalonia;
using Avalonia.Controls;

namespace Akbura.Hooks;

public static class TopLevelHooks
{
    /// <summary>
    /// Gets the client width of the top-level containing this component, or zero while detached.
    /// </summary>
    [UseHook]
    public static State<double> useTopLevelWidth([Self] this AkburaControl control)
    {
        ArgumentNullException.ThrowIfNull(control);

        var topLevel = control.useHookState(() => TopLevel.GetTopLevel(control));
        control.useAttachedToVisualTree(() => { topLevel.Value = TopLevel.GetTopLevel(control); });
        control.useDetachedFromVisualTree(() => { topLevel.Value = null; });

        return control.useExternalStore(
            onChanged => topLevel.Value is { } currentTopLevel
                ? currentTopLevel.GetObservable(TopLevel.ClientSizeProperty)
                    .Subscribe(new TopLevelClientSizeObserver(onChanged))
                : EmptySubscription.Instance,
            () => TopLevel.GetTopLevel(control)?.ClientSize.Width ?? 0d,
            [topLevel.Value]);
    }

    private sealed class TopLevelClientSizeObserver(Action onChanged) : IObserver<Size>
    {
        public void OnCompleted()
        {
        }

        public void OnError(Exception error) => throw error;

        public void OnNext(Size value) => onChanged();
    }

    private sealed class EmptySubscription : IDisposable
    {
        public static readonly EmptySubscription Instance = new();

        public void Dispose()
        {
        }
    }
}
