using Akbura.CompilerAnotations;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    /// <summary>Runs after the layout pass updates the component.</summary>
    [UseHook]
    public static void useLayoutUpdated([Self] this AkburaControl control, Action action) =>
        useEvent(control, handler => control.LayoutUpdated += handler, handler => control.LayoutUpdated -= handler, action);

    /// <summary>Runs with the layout-update arguments.</summary>
    [UseHook]
    public static void useLayoutUpdated([Self] this AkburaControl control, Action<EventArgs> action) =>
        useEvent(control, handler => control.LayoutUpdated += handler, handler => control.LayoutUpdated -= handler, action);

    /// <summary>Runs with the event sender after layout updates.</summary>
    [UseHook]
    public static void useLayoutUpdated([Self] this AkburaControl control, EventHandler listener) =>
        useEvent(control, handler => control.LayoutUpdated += handler, handler => control.LayoutUpdated -= handler, listener);

    /// <summary>Runs when the component's effective viewport changes.</summary>
    [UseHook]
    public static void useEffectiveViewportChanged([Self] this AkburaControl control, Action action) =>
        useEvent<EffectiveViewportChangedEventArgs>(control, handler => control.EffectiveViewportChanged += handler, handler => control.EffectiveViewportChanged -= handler, action);

    /// <summary>Runs with the effective viewport arguments.</summary>
    [UseHook]
    public static void useEffectiveViewportChanged([Self] this AkburaControl control, Action<EffectiveViewportChangedEventArgs> action) =>
        useEvent(control, handler => control.EffectiveViewportChanged += handler, handler => control.EffectiveViewportChanged -= handler, action);

    /// <summary>Runs with the event sender and effective viewport arguments.</summary>
    [UseHook]
    public static void useEffectiveViewportChanged([Self] this AkburaControl control, EventHandler<EffectiveViewportChangedEventArgs> listener) =>
        useEvent(control, handler => control.EffectiveViewportChanged += handler, handler => control.EffectiveViewportChanged -= handler, listener);

    /// <summary>Runs when the component's arranged size changes.</summary>
    [UseHook]
    public static void useSizeChanged([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.SizeChangedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the component size-change arguments.</summary>
    [UseHook]
    public static void useSizeChanged([Self] this AkburaControl control, Action<SizeChangedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.SizeChangedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the size-change event sender and arguments.</summary>
    [UseHook]
    public static void useSizeChanged([Self] this AkburaControl control, EventHandler<SizeChangedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.SizeChangedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives a request to bring content into view.</summary>
    [UseHook]
    public static void useRequestBringIntoView([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.RequestBringIntoViewEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the request-to-bring-into-view arguments.</summary>
    [UseHook]
    public static void useRequestBringIntoView([Self] this AkburaControl control, Action<RequestBringIntoViewEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.RequestBringIntoViewEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the request event sender and arguments.</summary>
    [UseHook]
    public static void useRequestBringIntoView([Self] this AkburaControl control, EventHandler<RequestBringIntoViewEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.RequestBringIntoViewEvent, listener, routes, handledEventsToo);
}
