using Avalonia.Data;

namespace Akbura.Markup;

/// <summary>
/// Selects the Avalonia binding layer used by property operations produced by an
/// AKCSS utility whose conditional prefix is represented by the attributed markup
/// extension.
/// </summary>
/// <remarks>
/// <para>
/// This attribute does not participate in AKCSS conflict resolution. The compiler
/// first selects the winning utility operation using source order and
/// <see cref="UtilityVariantAttribute"/>, then applies that operation at the
/// configured Avalonia <see cref="BindingPriority"/>.
/// </para>
/// <para>
/// Specify exactly one of <see cref="Priority"/> or <see cref="PriorityMember"/>.
/// A priority member is read from the same markup extension instance whose
/// <c>ProvideValue</c> result controls the utility prefix.
/// </para>
/// <para>
/// Only reversible Avalonia layers are supported: <see cref="BindingPriority.Animation"/>,
/// <see cref="BindingPriority.StyleTrigger"/>, <see cref="BindingPriority.Template"/>,
/// and <see cref="BindingPriority.Style"/>.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UtilityBindingPriorityAttribute : Attribute
{
    /// <summary>
    /// Gets or initializes the constant Avalonia binding priority used by every
    /// invocation of the attributed markup extension.
    /// </summary>
    public BindingPriority Priority { get; init; }

    /// <summary>
    /// Gets or initializes the name of a readable instance field or property on
    /// the markup extension whose type is exactly <see cref="BindingPriority"/>.
    /// </summary>
    public string? PriorityMember { get; init; }
}
