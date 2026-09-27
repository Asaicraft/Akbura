---
title: Avalonia Control Event Hooks
summary: Subscribe to Avalonia Control lifecycle, input, gesture, layout and property events without manual effects.
---

Event hooks subscribe the current Akbura component to events inherited from
`Avalonia.Controls.Control`. Import the hooks namespace and call them at the top
level of the component:

```akbura
using Akbura.Hooks;
using Avalonia.Input;

useKeyDown(args =>
{
    if (args.Key == Key.Escape)
    {
        args.Handled = true;
        ClosePanel();
    }
});

<Border/>
```

The hooks are render hooks. Keep their order and count stable, just like
`useEffect` and the other composable hooks.

## Callback forms

Every named event hook has three forms:

```akbura
useKeyDown(() => RecordKey());
useKeyDown(args => RecordKey(args.Key));
useKeyDown((sender, args) => RecordKey(sender, args));
```

The first form receives no values, the second receives the exact event arguments,
and the third receives Avalonia's original sender and arguments. Non-generic
events such as `DataContextChanged` use `EventHandler` in the third form.

The installed delegate remains stable while the callback follows the latest
successfully committed render. A failed speculative render cannot publish a new
callback. Detaching the component removes ordinary subscriptions; reattaching it
restores them.

## Routed events

Routed hooks accept the same routing options in every callback form:

```akbura
using Avalonia.Interactivity;

useKeyDown(
    args => args.Handled = true,
    routes: RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
    handledEventsToo: true);
```

The defaults are `RoutingStrategies.Direct | RoutingStrategies.Bubble` and
`handledEventsToo: false`. Changing either option restarts only the routed-event
subscription. Changing the callback does not.

`sender` is the component on which the handler is installed. `args.Source` is
Avalonia's routed source and can be a child control; Akbura does not replace or
filter it. Routed callbacks run synchronously in Avalonia's dispatch stack, so
changes to `Handled` and other mutable event arguments are visible immediately.

## Lifecycle details

`useAttachedToVisualTree` and `useLoaded` report only future real events. They do
not invent an attachment that happened before registration.

`useDetachedFromVisualTree` and `useUnloaded` are terminal lifecycle hooks. Akbura
captures callbacks committed at the start of a real detach transition, performs
normal effect cleanup, and then delivers the actual Avalonia event and its actual
arguments exactly once. A dependency restart or Hot Reload cleanup does not
produce either event. Hooks are registered again after reattachment, so a later
detach is delivered again.

`useInitialized` has one additional option:

```akbura
useInitialized(
    () => LoadOnce(),
    invokeIfAlreadyInitialized: true);
```

The default `true` performs a one-time catch-up after the first successful commit
when Avalonia initialization already completed. Use `false` to listen only for a
future real event. Renders, reattachment and Hot Reload do not repeat an already
delivered initialization callback.

For work that must run immediately and on each later visual attachment, keep the
initial action explicit:

```akbura
useAttachedToVisualTree(() =>
{
    Dispatcher.UIThread.Post(ResolveComponent);
});

useEffect(() =>
{
    Dispatcher.UIThread.Post(ResolveComponent);
}, []);
```

## Available hooks

| Area | Hooks |
| --- | --- |
| Properties | `usePropertyChanged`, `useActualThemeVariantChanged`, `useDataContextChanged`, `useResourcesChanged` |
| Logical tree | `useAttachedToLogicalTree`, `useDetachedFromLogicalTree` |
| Lifecycle | `useInitialized`, `useAttachedToVisualTree`, `useDetachedFromVisualTree`, `useLoaded`, `useUnloaded` |
| Layout | `useLayoutUpdated`, `useEffectiveViewportChanged`, `useSizeChanged`, `useRequestBringIntoView` |
| Focus | `useGettingFocus`, `useGotFocus`, `useLosingFocus`, `useLostFocus` |
| Keyboard and text | `useKeyDown`, `useKeyUp`, `useTextInput`, `useTextInputMethodClientRequested` |
| Pointer and context | `usePointerEntered`, `usePointerExited`, `usePointerMoved`, `usePointerPressed`, `usePointerReleased`, `usePointerCaptureLost`, `usePointerWheelChanged`, `useContextRequested`, `useContextCanceled` |
| Gestures | `useTapped`, `useRightTapped`, `useDoubleTapped`, `useHolding`, `usePinch`, `usePinchEnded`, `usePullGesture`, `usePullGestureEnded`, `useScrollGesture`, `useScrollGestureInertiaStarting`, `useScrollGestureEnded`, `useSwipeGesture`, `useSwipeGestureEnded`, `usePointerTouchPadGestureMagnify`, `usePointerTouchPadGestureRotate`, `usePointerTouchPadGestureSwipe` |

These hooks cover the public instance events on the Avalonia `Control` base chain.
They intentionally do not add events belonging only to derived controls, such as
`Button.Click`, `TextBox.TextChanged`, or `Window.Closing`.
