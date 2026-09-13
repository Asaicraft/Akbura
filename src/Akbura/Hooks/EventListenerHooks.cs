using Akbura.CompilerAnotations;

namespace Akbura.Hooks;

public static class EventListenerHooks
{
    /// <summary>
    /// Installs one event delegate per effect run. The listener follows the latest
    /// committed frame without changing the subscription's restart dependencies.
    /// </summary>
    [UseHook]
    public static void useEventListener<TArgs>(
        [Self] this AkburaControl control,
        Action<EventHandler<TArgs>> subscribe,
        Action<EventHandler<TArgs>> unsubscribe,
        EventHandler<TArgs> listener,
        ReadOnlySpan<object?> dependencies)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(subscribe);
        ArgumentNullException.ThrowIfNull(unsubscribe);
        ArgumentNullException.ThrowIfNull(listener);

        var callback = control.useHookState(() => new SourceHookCallback<EventHandler<TArgs>>(listener));
        control.useEffect(() => { callback.Value.Current = listener; });
        control.useEffect(
            (Func<CancellationToken, Action?>)(cancellationToken =>
            {
                EventHandler<TArgs> handler = (sender, args) => SourceHookDelivery.Dispatch(
                    () => callback.Value.Current(sender, args), cancellationToken);
                subscribe(handler);
                return () => unsubscribe(handler);
            }),
            dependencies);
    }
}
