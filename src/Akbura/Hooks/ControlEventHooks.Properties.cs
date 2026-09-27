using Akbura.CompilerAnotations;
using Avalonia;
using Avalonia.Controls;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    /// <summary>Runs when an Avalonia property value changes on the component.</summary>
    [UseHook]
    public static void usePropertyChanged([Self] this AkburaControl control, Action action) =>
        useEvent<AvaloniaPropertyChangedEventArgs>(control, handler => control.PropertyChanged += handler, handler => control.PropertyChanged -= handler, action);

    /// <summary>Runs with the Avalonia property change arguments.</summary>
    [UseHook]
    public static void usePropertyChanged([Self] this AkburaControl control, Action<AvaloniaPropertyChangedEventArgs> action) =>
        useEvent(control, handler => control.PropertyChanged += handler, handler => control.PropertyChanged -= handler, action);

    /// <summary>Runs with the event sender and Avalonia property change arguments.</summary>
    [UseHook]
    public static void usePropertyChanged([Self] this AkburaControl control, EventHandler<AvaloniaPropertyChangedEventArgs> listener) =>
        useEvent(control, handler => control.PropertyChanged += handler, handler => control.PropertyChanged -= handler, listener);

    /// <summary>Runs when the component's effective theme variant changes.</summary>
    [UseHook]
    public static void useActualThemeVariantChanged([Self] this AkburaControl control, Action action) =>
        useEvent(control, handler => control.ActualThemeVariantChanged += handler, handler => control.ActualThemeVariantChanged -= handler, action);

    /// <summary>Runs with the effective theme variant event arguments.</summary>
    [UseHook]
    public static void useActualThemeVariantChanged([Self] this AkburaControl control, Action<EventArgs> action) =>
        useEvent(control, handler => control.ActualThemeVariantChanged += handler, handler => control.ActualThemeVariantChanged -= handler, action);

    /// <summary>Runs with the event sender when the effective theme variant changes.</summary>
    [UseHook]
    public static void useActualThemeVariantChanged([Self] this AkburaControl control, EventHandler listener) =>
        useEvent(control, handler => control.ActualThemeVariantChanged += handler, handler => control.ActualThemeVariantChanged -= handler, listener);

    /// <summary>Runs when the component's data context changes.</summary>
    [UseHook]
    public static void useDataContextChanged([Self] this AkburaControl control, Action action) =>
        useEvent(control, handler => control.DataContextChanged += handler, handler => control.DataContextChanged -= handler, action);

    /// <summary>Runs with the data-context event arguments.</summary>
    [UseHook]
    public static void useDataContextChanged([Self] this AkburaControl control, Action<EventArgs> action) =>
        useEvent(control, handler => control.DataContextChanged += handler, handler => control.DataContextChanged -= handler, action);

    /// <summary>Runs with the event sender when the data context changes.</summary>
    [UseHook]
    public static void useDataContextChanged([Self] this AkburaControl control, EventHandler listener) =>
        useEvent(control, handler => control.DataContextChanged += handler, handler => control.DataContextChanged -= handler, listener);

    /// <summary>Runs when resources visible to the component change.</summary>
    [UseHook]
    public static void useResourcesChanged([Self] this AkburaControl control, Action action) =>
        useEvent<ResourcesChangedEventArgs>(control, handler => control.ResourcesChanged += handler, handler => control.ResourcesChanged -= handler, action);

    /// <summary>Runs with the resources-change arguments.</summary>
    [UseHook]
    public static void useResourcesChanged([Self] this AkburaControl control, Action<ResourcesChangedEventArgs> action) =>
        useEvent(control, handler => control.ResourcesChanged += handler, handler => control.ResourcesChanged -= handler, action);

    /// <summary>Runs with the event sender and resources-change arguments.</summary>
    [UseHook]
    public static void useResourcesChanged([Self] this AkburaControl control, EventHandler<ResourcesChangedEventArgs> listener) =>
        useEvent(control, handler => control.ResourcesChanged += handler, handler => control.ResourcesChanged -= handler, listener);
}
