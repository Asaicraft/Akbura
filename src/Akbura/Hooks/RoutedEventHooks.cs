using Avalonia.Interactivity;

namespace Akbura.Hooks;

internal static class RoutedEventHooks
{
    public const RoutingStrategies DefaultRoutes = RoutingStrategies.Direct | RoutingStrategies.Bubble;

    public static void useRoutedEvent<TArgs>(this AkburaControl control, RoutedEvent<TArgs> routedEvent, EventHandler<TArgs> listener, RoutingStrategies routes, bool handledEventsToo)
        where TArgs : RoutedEventArgs
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(routedEvent);
        ArgumentNullException.ThrowIfNull(listener);

        control.useEventListener<TArgs>(
            handler => control.AddHandler(routedEvent, handler, routes, handledEventsToo),
            handler => control.RemoveHandler(routedEvent, handler),
            listener,
            [routedEvent, routes, handledEventsToo]);
    }
}
