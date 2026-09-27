using Akbura.CompilerAnotations;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    /// <summary>Runs when the component receives the getting focus routed event.</summary>
    [UseHook]
    public static void useGettingFocus([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GettingFocusEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the getting focus routed event arguments.</summary>
    [UseHook]
    public static void useGettingFocus([Self] this AkburaControl control, Action<FocusChangingEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GettingFocusEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the getting focus event sender and routed arguments.</summary>
    [UseHook]
    public static void useGettingFocus([Self] this AkburaControl control, EventHandler<FocusChangingEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GettingFocusEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the got focus routed event.</summary>
    [UseHook]
    public static void useGotFocus([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GotFocusEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the got focus routed event arguments.</summary>
    [UseHook]
    public static void useGotFocus([Self] this AkburaControl control, Action<FocusChangedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GotFocusEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the got focus event sender and routed arguments.</summary>
    [UseHook]
    public static void useGotFocus([Self] this AkburaControl control, EventHandler<FocusChangedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.GotFocusEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the losing focus routed event.</summary>
    [UseHook]
    public static void useLosingFocus([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LosingFocusEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the losing focus routed event arguments.</summary>
    [UseHook]
    public static void useLosingFocus([Self] this AkburaControl control, Action<FocusChangingEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LosingFocusEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the losing focus event sender and routed arguments.</summary>
    [UseHook]
    public static void useLosingFocus([Self] this AkburaControl control, EventHandler<FocusChangingEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LosingFocusEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the lost focus routed event.</summary>
    [UseHook]
    public static void useLostFocus([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LostFocusEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the lost focus routed event arguments.</summary>
    [UseHook]
    public static void useLostFocus([Self] this AkburaControl control, Action<FocusChangedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LostFocusEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the lost focus event sender and routed arguments.</summary>
    [UseHook]
    public static void useLostFocus([Self] this AkburaControl control, EventHandler<FocusChangedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.LostFocusEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the key down routed event.</summary>
    [UseHook]
    public static void useKeyDown([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyDownEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the key down routed event arguments.</summary>
    [UseHook]
    public static void useKeyDown([Self] this AkburaControl control, Action<KeyEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyDownEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the key down event sender and routed arguments.</summary>
    [UseHook]
    public static void useKeyDown([Self] this AkburaControl control, EventHandler<KeyEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyDownEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the key up routed event.</summary>
    [UseHook]
    public static void useKeyUp([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyUpEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the key up routed event arguments.</summary>
    [UseHook]
    public static void useKeyUp([Self] this AkburaControl control, Action<KeyEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyUpEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the key up event sender and routed arguments.</summary>
    [UseHook]
    public static void useKeyUp([Self] this AkburaControl control, EventHandler<KeyEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.KeyUpEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the text input routed event.</summary>
    [UseHook]
    public static void useTextInput([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the text input routed event arguments.</summary>
    [UseHook]
    public static void useTextInput([Self] this AkburaControl control, Action<TextInputEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the text input event sender and routed arguments.</summary>
    [UseHook]
    public static void useTextInput([Self] this AkburaControl control, EventHandler<TextInputEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the text input method client requested routed event.</summary>
    [UseHook]
    public static void useTextInputMethodClientRequested([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputMethodClientRequestedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the text input method client requested routed event arguments.</summary>
    [UseHook]
    public static void useTextInputMethodClientRequested([Self] this AkburaControl control, Action<TextInputMethodClientRequestedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputMethodClientRequestedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the text input method client requested event sender and routed arguments.</summary>
    [UseHook]
    public static void useTextInputMethodClientRequested([Self] this AkburaControl control, EventHandler<TextInputMethodClientRequestedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.TextInputMethodClientRequestedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer entered routed event.</summary>
    [UseHook]
    public static void usePointerEntered([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerEnteredEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer entered routed event arguments.</summary>
    [UseHook]
    public static void usePointerEntered([Self] this AkburaControl control, Action<PointerEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerEnteredEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer entered event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerEntered([Self] this AkburaControl control, EventHandler<PointerEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerEnteredEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer exited routed event.</summary>
    [UseHook]
    public static void usePointerExited([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerExitedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer exited routed event arguments.</summary>
    [UseHook]
    public static void usePointerExited([Self] this AkburaControl control, Action<PointerEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerExitedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer exited event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerExited([Self] this AkburaControl control, EventHandler<PointerEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerExitedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer moved routed event.</summary>
    [UseHook]
    public static void usePointerMoved([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerMovedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer moved routed event arguments.</summary>
    [UseHook]
    public static void usePointerMoved([Self] this AkburaControl control, Action<PointerEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerMovedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer moved event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerMoved([Self] this AkburaControl control, EventHandler<PointerEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerMovedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer pressed routed event.</summary>
    [UseHook]
    public static void usePointerPressed([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerPressedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer pressed routed event arguments.</summary>
    [UseHook]
    public static void usePointerPressed([Self] this AkburaControl control, Action<PointerPressedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerPressedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer pressed event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerPressed([Self] this AkburaControl control, EventHandler<PointerPressedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerPressedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer released routed event.</summary>
    [UseHook]
    public static void usePointerReleased([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerReleasedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer released routed event arguments.</summary>
    [UseHook]
    public static void usePointerReleased([Self] this AkburaControl control, Action<PointerReleasedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerReleasedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer released event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerReleased([Self] this AkburaControl control, EventHandler<PointerReleasedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerReleasedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer capture lost routed event.</summary>
    [UseHook]
    public static void usePointerCaptureLost([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerCaptureLostEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer capture lost routed event arguments.</summary>
    [UseHook]
    public static void usePointerCaptureLost([Self] this AkburaControl control, Action<PointerCaptureLostEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerCaptureLostEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer capture lost event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerCaptureLost([Self] this AkburaControl control, EventHandler<PointerCaptureLostEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerCaptureLostEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the pointer wheel changed routed event.</summary>
    [UseHook]
    public static void usePointerWheelChanged([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerWheelChangedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the pointer wheel changed routed event arguments.</summary>
    [UseHook]
    public static void usePointerWheelChanged([Self] this AkburaControl control, Action<PointerWheelEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerWheelChangedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the pointer wheel changed event sender and routed arguments.</summary>
    [UseHook]
    public static void usePointerWheelChanged([Self] this AkburaControl control, EventHandler<PointerWheelEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.PointerWheelChangedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the context requested routed event.</summary>
    [UseHook]
    public static void useContextRequested([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextRequestedEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the context requested routed event arguments.</summary>
    [UseHook]
    public static void useContextRequested([Self] this AkburaControl control, Action<ContextRequestedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextRequestedEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the context requested event sender and routed arguments.</summary>
    [UseHook]
    public static void useContextRequested([Self] this AkburaControl control, EventHandler<ContextRequestedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextRequestedEvent, listener, routes, handledEventsToo);

    /// <summary>Runs when the component receives the context canceled routed event.</summary>
    [UseHook]
    public static void useContextCanceled([Self] this AkburaControl control, Action action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextCanceledEvent, (_, _) => action(), routes, handledEventsToo);

    /// <summary>Runs with the context canceled routed event arguments.</summary>
    [UseHook]
    public static void useContextCanceled([Self] this AkburaControl control, Action<RoutedEventArgs> action, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextCanceledEvent, (_, args) => action(args), routes, handledEventsToo);

    /// <summary>Runs with the context canceled event sender and routed arguments.</summary>
    [UseHook]
    public static void useContextCanceled([Self] this AkburaControl control, EventHandler<RoutedEventArgs> listener, RoutingStrategies routes = RoutingStrategies.Direct | RoutingStrategies.Bubble, bool handledEventsToo = false) =>
        control.useRoutedEvent(InputElement.ContextCanceledEvent, listener, routes, handledEventsToo);

}
