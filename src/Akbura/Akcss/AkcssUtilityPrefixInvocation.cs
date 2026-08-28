using Avalonia.Data;
using System.ComponentModel;

namespace Akbura.Akcss;

/// <summary>
/// Carries the value and binding priority produced by one markup extension
/// instance used as an AKCSS utility prefix.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkcssUtilityPrefixInvocation<TValue>
{
    public AkcssUtilityPrefixInvocation(
        TValue value,
        BindingPriority priority)
    {
        Value = value;
        Priority = priority;
    }

    public TValue Value { get; }

    public BindingPriority Priority { get; }
}

internal static class AkcssBindingPriority
{
    public static BindingPriority Validate(BindingPriority priority)
    {
        if (priority is BindingPriority.Animation or
            BindingPriority.StyleTrigger or
            BindingPriority.Template or
            BindingPriority.Style)
        {
            return priority;
        }

        throw new InvalidOperationException(
            $"AKCSS utility binding priority '{priority}' is not reversible. " +
            "Use Animation, StyleTrigger, Template, or Style.");
    }
}
