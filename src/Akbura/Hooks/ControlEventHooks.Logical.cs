using Akbura.CompilerAnotations;
using Avalonia.LogicalTree;

namespace Akbura.Hooks;

public static partial class ControlEventHooks
{
    /// <summary>Runs when the component is attached to a logical tree.</summary>
    [UseHook]
    public static void useAttachedToLogicalTree([Self] this AkburaControl control, Action action) =>
        useEvent<LogicalTreeAttachmentEventArgs>(control, handler => control.AttachedToLogicalTree += handler, handler => control.AttachedToLogicalTree -= handler, action);

    /// <summary>Runs with the logical-tree attachment arguments.</summary>
    [UseHook]
    public static void useAttachedToLogicalTree([Self] this AkburaControl control, Action<LogicalTreeAttachmentEventArgs> action) =>
        useEvent(control, handler => control.AttachedToLogicalTree += handler, handler => control.AttachedToLogicalTree -= handler, action);

    /// <summary>Runs with the event sender and logical-tree attachment arguments.</summary>
    [UseHook]
    public static void useAttachedToLogicalTree([Self] this AkburaControl control, EventHandler<LogicalTreeAttachmentEventArgs> listener) =>
        useEvent(control, handler => control.AttachedToLogicalTree += handler, handler => control.AttachedToLogicalTree -= handler, listener);

    /// <summary>Runs when the component is detached from a logical tree.</summary>
    [UseHook]
    public static void useDetachedFromLogicalTree([Self] this AkburaControl control, Action action) =>
        useEvent<LogicalTreeAttachmentEventArgs>(control, handler => control.DetachedFromLogicalTree += handler, handler => control.DetachedFromLogicalTree -= handler, action);

    /// <summary>Runs with the logical-tree detachment arguments.</summary>
    [UseHook]
    public static void useDetachedFromLogicalTree([Self] this AkburaControl control, Action<LogicalTreeAttachmentEventArgs> action) =>
        useEvent(control, handler => control.DetachedFromLogicalTree += handler, handler => control.DetachedFromLogicalTree -= handler, action);

    /// <summary>Runs with the event sender and logical-tree detachment arguments.</summary>
    [UseHook]
    public static void useDetachedFromLogicalTree([Self] this AkburaControl control, EventHandler<LogicalTreeAttachmentEventArgs> listener) =>
        useEvent(control, handler => control.DetachedFromLogicalTree += handler, handler => control.DetachedFromLogicalTree -= handler, listener);
}
