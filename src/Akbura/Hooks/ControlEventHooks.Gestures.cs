using Akbura.CompilerAnotations;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    /// <summary>Runs when the component receives the tapped routed event.</summary>
    [UseHook]
    public static void useTapped([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TappedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the tapped routed event arguments.</summary>
    [UseHook]
    public static void useTapped([Self] this AkburaControl control, Action<TappedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TappedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the tapped event sender and routed arguments.</summary>
    [UseHook]
    public static void useTapped([Self] this AkburaControl control, EventHandler<TappedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TappedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the right tapped routed event.</summary>
    [UseHook]
    public static void useRightTapped([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.RightTappedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the right tapped routed event arguments.</summary>
    [UseHook]
    public static void useRightTapped([Self] this AkburaControl control, Action<TappedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.RightTappedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the right tapped event sender and routed arguments.</summary>
    [UseHook]
    public static void useRightTapped([Self] this AkburaControl control, EventHandler<TappedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.RightTappedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the double tapped routed event.</summary>
    [UseHook]
    public static void useDoubleTapped([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.DoubleTappedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the double tapped routed event arguments.</summary>
    [UseHook]
    public static void useDoubleTapped([Self] this AkburaControl control, Action<TappedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.DoubleTappedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the double tapped event sender and routed arguments.</summary>
    [UseHook]
    public static void useDoubleTapped([Self] this AkburaControl control, EventHandler<TappedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.DoubleTappedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the holding routed event.</summary>
    [UseHook]
    public static void useHolding([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.HoldingEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the holding routed event arguments.</summary>
    [UseHook]
    public static void useHolding([Self] this AkburaControl control, Action<HoldingRoutedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.HoldingEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the holding event sender and routed arguments.</summary>
    [UseHook]
    public static void useHolding([Self] this AkburaControl control, EventHandler<HoldingRoutedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.HoldingEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pinch routed event.</summary>
    [UseHook]
    public static void usePinch([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pinch routed event arguments.</summary>
    [UseHook]
    public static void usePinch([Self] this AkburaControl control, Action<PinchEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pinch event sender and routed arguments.</summary>
    [UseHook]
    public static void usePinch([Self] this AkburaControl control, EventHandler<PinchEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pinch ended routed event.</summary>
    [UseHook]
    public static void usePinchEnded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEndedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pinch ended routed event arguments.</summary>
    [UseHook]
    public static void usePinchEnded([Self] this AkburaControl control, Action<PinchEndedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEndedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pinch ended event sender and routed arguments.</summary>
    [UseHook]
    public static void usePinchEnded([Self] this AkburaControl control, EventHandler<PinchEndedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PinchEndedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pull gesture routed event.</summary>
    [UseHook]
    public static void usePullGesture([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pull gesture routed event arguments.</summary>
    [UseHook]
    public static void usePullGesture([Self] this AkburaControl control, Action<PullGestureEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pull gesture event sender and routed arguments.</summary>
    [UseHook]
    public static void usePullGesture([Self] this AkburaControl control, EventHandler<PullGestureEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pull gesture ended routed event.</summary>
    [UseHook]
    public static void usePullGestureEnded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEndedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pull gesture ended routed event arguments.</summary>
    [UseHook]
    public static void usePullGestureEnded([Self] this AkburaControl control, Action<PullGestureEndedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEndedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pull gesture ended event sender and routed arguments.</summary>
    [UseHook]
    public static void usePullGestureEnded([Self] this AkburaControl control, EventHandler<PullGestureEndedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PullGestureEndedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the scroll gesture routed event.</summary>
    [UseHook]
    public static void useScrollGesture([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture routed event arguments.</summary>
    [UseHook]
    public static void useScrollGesture([Self] this AkburaControl control, Action<ScrollGestureEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture event sender and routed arguments.</summary>
    [UseHook]
    public static void useScrollGesture([Self] this AkburaControl control, EventHandler<ScrollGestureEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the scroll gesture inertia starting routed event.</summary>
    [UseHook]
    public static void useScrollGestureInertiaStarting([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureInertiaStartingEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture inertia starting routed event arguments.</summary>
    [UseHook]
    public static void useScrollGestureInertiaStarting([Self] this AkburaControl control, Action<ScrollGestureInertiaStartingEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureInertiaStartingEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture inertia starting event sender and routed arguments.</summary>
    [UseHook]
    public static void useScrollGestureInertiaStarting([Self] this AkburaControl control, EventHandler<ScrollGestureInertiaStartingEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureInertiaStartingEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the scroll gesture ended routed event.</summary>
    [UseHook]
    public static void useScrollGestureEnded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEndedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture ended routed event arguments.</summary>
    [UseHook]
    public static void useScrollGestureEnded([Self] this AkburaControl control, Action<ScrollGestureEndedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEndedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the scroll gesture ended event sender and routed arguments.</summary>
    [UseHook]
    public static void useScrollGestureEnded([Self] this AkburaControl control, EventHandler<ScrollGestureEndedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ScrollGestureEndedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the swipe gesture routed event.</summary>
    [UseHook]
    public static void useSwipeGesture([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the swipe gesture routed event arguments.</summary>
    [UseHook]
    public static void useSwipeGesture([Self] this AkburaControl control, Action<SwipeGestureEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the swipe gesture event sender and routed arguments.</summary>
    [UseHook]
    public static void useSwipeGesture([Self] this AkburaControl control, EventHandler<SwipeGestureEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the swipe gesture ended routed event.</summary>
    [UseHook]
    public static void useSwipeGestureEnded([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEndedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the swipe gesture ended routed event arguments.</summary>
    [UseHook]
    public static void useSwipeGestureEnded([Self] this AkburaControl control, Action<SwipeGestureEndedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEndedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the swipe gesture ended event sender and routed arguments.</summary>
    [UseHook]
    public static void useSwipeGestureEnded([Self] this AkburaControl control, EventHandler<SwipeGestureEndedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.SwipeGestureEndedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer touch pad gesture magnify routed event.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureMagnify([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureMagnifyEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture magnify routed event arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureMagnify([Self] this AkburaControl control, Action<PointerDeltaEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureMagnifyEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture magnify event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureMagnify([Self] this AkburaControl control, EventHandler<PointerDeltaEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureMagnifyEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer touch pad gesture rotate routed event.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureRotate([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureRotateEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture rotate routed event arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureRotate([Self] this AkburaControl control, Action<PointerDeltaEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureRotateEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture rotate event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureRotate([Self] this AkburaControl control, EventHandler<PointerDeltaEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureRotateEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer touch pad gesture swipe routed event.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureSwipe([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureSwipeEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture swipe routed event arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureSwipe([Self] this AkburaControl control, Action<PointerDeltaEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureSwipeEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer touch pad gesture swipe event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerTouchPadGestureSwipe([Self] this AkburaControl control, EventHandler<PointerDeltaEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerTouchPadGestureSwipeEvent, listener, routes, handledEventsToo);

}
