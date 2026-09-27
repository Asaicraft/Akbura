namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    private static void useEvent<TArgs>(AkburaControl control, Action<EventHandler<TArgs>> subscribe, Action<EventHandler<TArgs>> unsubscribe, Action action) =>
        control.useEventListener<TArgs>(subscribe, unsubscribe, (_, _) => action(), []);

    private static void useEvent<TArgs>(AkburaControl control, Action<EventHandler<TArgs>> subscribe, Action<EventHandler<TArgs>> unsubscribe, Action<TArgs> action) =>
        control.useEventListener<TArgs>(subscribe, unsubscribe, (_, args) => action(args), []);

    private static void useEvent<TArgs>(AkburaControl control, Action<EventHandler<TArgs>> subscribe, Action<EventHandler<TArgs>> unsubscribe, EventHandler<TArgs> listener) =>
        control.useEventListener(subscribe, unsubscribe, listener, []);

    private static void useEvent(AkburaControl control, Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, Action action) =>
        control.useEventListener(subscribe, unsubscribe, (_, _) => action(), []);

    private static void useEvent(AkburaControl control, Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, Action<EventArgs> action) =>
        control.useEventListener(subscribe, unsubscribe, (_, args) => action(args), []);

    private static void useEvent(AkburaControl control, Action<EventHandler> subscribe, Action<EventHandler> unsubscribe, EventHandler listener) =>
        control.useEventListener(subscribe, unsubscribe, listener, []);
}
