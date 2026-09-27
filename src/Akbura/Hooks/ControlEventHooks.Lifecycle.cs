using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    private static readonly UseHookKey s_initializedKey = new();
    private static readonly UseHookKey s_detachedFromVisualTreeKey = new();
    private static readonly UseHookKey s_unloadedKey = new();

    /// <summary>Runs once when initialization completes, or catches up after commit when initialization already completed.</summary>
    [UseHook]
    public static void useInitialized([Self] this AkburaControl control, Action action, bool invokeIfAlreadyInitialized = true) =>
        useInitializedCore(control, (_, _) => action(), invokeIfAlreadyInitialized);

    /// <summary>Runs once with initialization arguments, including an optional late-registration catch-up.</summary>
    [UseHook]
    public static void useInitialized([Self] this AkburaControl control, Action<EventArgs> action, bool invokeIfAlreadyInitialized = true) =>
        useInitializedCore(control, (_, args) => action(args), invokeIfAlreadyInitialized);

    /// <summary>Runs once with the component sender and initialization arguments, including an optional late-registration catch-up.</summary>
    [UseHook]
    public static void useInitialized([Self] this AkburaControl control, EventHandler listener, bool invokeIfAlreadyInitialized = true) =>
        useInitializedCore(control, listener, invokeIfAlreadyInitialized);

    /// <summary>Runs when the component is attached to a visual tree after the hook is registered.</summary>
    [UseHook]
    public static void useAttachedToVisualTree([Self] this AkburaControl control, Action action) =>
        useEvent<VisualTreeAttachmentEventArgs>(control, handler => control.AttachedToVisualTree += handler, handler => control.AttachedToVisualTree -= handler, action);

    /// <summary>Runs with the visual-tree attachment arguments.</summary>
    [UseHook]
    public static void useAttachedToVisualTree([Self] this AkburaControl control, Action<VisualTreeAttachmentEventArgs> action) =>
        useEvent(control, handler => control.AttachedToVisualTree += handler, handler => control.AttachedToVisualTree -= handler, action);

    /// <summary>Runs with the event sender and visual-tree attachment arguments.</summary>
    [UseHook]
    public static void useAttachedToVisualTree([Self] this AkburaControl control, EventHandler<VisualTreeAttachmentEventArgs> listener) =>
        useEvent(control, handler => control.AttachedToVisualTree += handler, handler => control.AttachedToVisualTree -= handler, listener);

    /// <summary>Runs exactly once for each real visual-tree detachment committed before the transition.</summary>
    [UseHook]
    public static void useDetachedFromVisualTree([Self] this AkburaControl control, Action action) =>
        useTerminalEvent<VisualTreeAttachmentEventArgs>(control, s_detachedFromVisualTreeKey, TerminalControlEvent.DetachedFromVisualTree, (_, _) => action());

    /// <summary>Runs with the actual visual-tree detachment arguments.</summary>
    [UseHook]
    public static void useDetachedFromVisualTree([Self] this AkburaControl control, Action<VisualTreeAttachmentEventArgs> action) =>
        useTerminalEvent<VisualTreeAttachmentEventArgs>(control, s_detachedFromVisualTreeKey, TerminalControlEvent.DetachedFromVisualTree, (_, args) => action(args));

    /// <summary>Runs with the actual event sender and visual-tree detachment arguments.</summary>
    [UseHook]
    public static void useDetachedFromVisualTree([Self] this AkburaControl control, EventHandler<VisualTreeAttachmentEventArgs> listener) =>
        useTerminalEvent(control, s_detachedFromVisualTreeKey, TerminalControlEvent.DetachedFromVisualTree, listener);

    /// <summary>Runs for the routed Loaded event after the hook is registered.</summary>
    [UseHook]
    public static void useLoaded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.LoadedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the routed Loaded event arguments.</summary>
    [UseHook]
    public static void useLoaded([Self] this AkburaControl control, Action<RoutedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.LoadedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the Loaded event sender and routed arguments.</summary>
    [UseHook]
    public static void useLoaded([Self] this AkburaControl control, EventHandler<RoutedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(Control.LoadedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs exactly once for each real Unloaded transition committed before detachment.</summary>
    [UseHook]
    public static void useUnloaded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        useUnloadedCore(control, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the actual routed Unloaded event arguments.</summary>
    [UseHook]
    public static void useUnloaded([Self] this AkburaControl control, Action<RoutedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        useUnloadedCore(control, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the actual Unloaded event sender and routed arguments.</summary>
    [UseHook]
    public static void useUnloaded([Self] this AkburaControl control, EventHandler<RoutedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        useUnloadedCore(control, listener, routes, handledEventsToo);

    private static void useInitializedCore(AkburaControl control, EventHandler listener, bool invokeIfAlreadyInitialized)
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(listener);

        var arguments = new InitializedHookArguments(
            control.ControlEventLifecycle,
            control.GetCurrentUseHookIndex(),
            listener,
            invokeIfAlreadyInitialized);
        arguments.Bridge.MarkInitializedIdentity(arguments.Identity);
        control.UseHook(
            s_initializedKey,
            arguments,
            static current => new InitializedControlEventHookState(current.Bridge, current.Identity, current.Listener, current.InvokeIfAlreadyInitialized),
            static (state, current) => state.Apply(current.Listener, current.InvokeIfAlreadyInitialized),
            static state => state.Stop());
    }

    private static void useTerminalEvent<TArgs>(AkburaControl control, UseHookKey key, TerminalControlEvent @event, EventHandler<TArgs> listener)
        where TArgs : EventArgs
    {
        ArgumentNullException.ThrowIfNull(control);
        ArgumentNullException.ThrowIfNull(listener);

        var arguments = new TerminalHookArguments<TArgs>(control.ControlEventLifecycle, @event, listener);
        control.UseHook(
            key,
            arguments,
            static current => new TerminalControlEventHookState<TArgs>(current.Bridge, current.Event, current.Listener),
            static (state, current) => state.Apply(current.Listener),
            static state => state.Stop());
    }

    private static void useUnloadedCore(AkburaControl control, EventHandler<RoutedEventArgs> listener, RoutingStrategies routes, bool handledEventsToo)
    {
        EventHandler<RoutedEventArgs> filteredListener = (sender, args) =>
        {
            if ((routes & args.Route) != 0 && (handledEventsToo || !args.Handled))
            {
                listener(sender, args);
            }
        };
        useTerminalEvent(control, s_unloadedKey, TerminalControlEvent.Unloaded, filteredListener);
    }

    private readonly record struct InitializedHookArguments(ControlEventLifecycleBridge Bridge, int Identity, EventHandler Listener, bool InvokeIfAlreadyInitialized);

    private readonly record struct TerminalHookArguments<TArgs>(ControlEventLifecycleBridge Bridge, TerminalControlEvent Event, EventHandler<TArgs> Listener)
        where TArgs : EventArgs;
}
