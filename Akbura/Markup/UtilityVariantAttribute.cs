using System;

namespace Akbura.Markup;

/// <summary>
/// Specifies how an active prefixed utility competes with an unprefixed
/// utility that writes the same target property.
/// </summary>
/// <remarks>
/// This setting is consulted only after Akbura has selected the winning
/// prefixed candidate. It does not affect ordering between prefixed variants
/// in the same <see cref="UtilityVariantAttribute.ConflictGroup"/>; that
/// ordering is controlled by <see cref="UtilityVariantAttribute.Order"/>.
/// </remarks>
public enum UnprefixedUtilityPrecedence
{
    /// <summary>
    /// The unprefixed utility always wins while both candidates are active,
    /// regardless of their order in markup.
    /// </summary>
    Below = -1,

    /// <summary>
    /// The candidate written later in markup wins.
    /// </summary>
    SourceOrder = 0,

    /// <summary>
    /// The active prefixed utility always wins, regardless of its position in
    /// markup.
    /// </summary>
    Above = 1
}

/// <summary>
/// Marks a markup extension as an AKCSS utility variant and describes how
/// active uses of that variant participate in utility conflict resolution.
/// </summary>
/// <remarks>
/// <para>
/// A variant is used as a utility prefix, for example
/// <c>&lt;Border p-1 ${md}:p-3 /&gt;</c>. Its markup extension must produce
/// <see cref="bool"/> or <see cref="IObservable{T}"/> of
/// <see cref="bool"/>. A candidate participates only while that value is
/// <see langword="true"/>; a false or not-yet-available value allows another
/// candidate to provide the property value.
/// </para>
/// <para>
/// Utilities do not conflict because their names or variant groups match.
/// Akbura first expands each utility into its property-writing operations and
/// resolves each target property independently. For example, if one utility
/// writes <c>Width</c>, <c>Background</c>, and <c>Padding</c>, while another
/// writes <c>Width</c> and <c>Height</c>, only the two <c>Width</c> operations
/// compete. The other four operations remain active.
/// </para>
/// <para>
/// For one conflicting property, active prefixed candidates with the same
/// non-empty <see cref="ConflictGroup"/> are first ordered by
/// <see cref="Order"/>. A greater value wins; equal values are resolved by
/// source order. Winners from different groups and candidates without a group
/// are compared only by source order, so <see cref="Order"/> is never a global
/// priority. The resulting prefixed winner is then compared with the last
/// unprefixed candidate according to <see cref="UnprefixedPrecedence"/>.
/// </para>
/// </remarks>
/// <example>
/// The built-in breakpoint variants use one conflict group, increasing order
/// values, and <see cref="UnprefixedUtilityPrecedence.Above"/>:
/// <code>
/// [UtilityVariant(
///     10d,
///     ConflictGroup = "Breakpoints",
///     UnprefixedPrecedence = UnprefixedUtilityPrecedence.Above)]
/// public sealed class mdExtension
/// {
///     public IObservable&lt;bool&gt; ProvideValue(IServiceProvider services)
///     {
///         // Return whether this breakpoint is currently active.
///     }
/// }
/// </code>
/// With <c>${lg}:w-10 w-5 ${md}:w-7</c>, the greatest active breakpoint order
/// wins the <c>Width</c> operation. Because the breakpoint variants use
/// <see cref="UnprefixedUtilityPrecedence.Above"/>, the unprefixed
/// <c>w-5</c> cannot override that active breakpoint even though it appears
/// between the two prefixed candidates.
/// </example>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class UtilityVariantAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="UtilityVariantAttribute"/>
    /// class with the specified ordering value.
    /// </summary>
    /// <param name="order">
    /// The priority used between active prefixed candidates in the same
    /// non-empty <see cref="ConflictGroup"/>. Greater values have higher
    /// priority.
    /// </param>
    public UtilityVariantAttribute(double order)
    {
        Order = order;
    }

    /// <summary>
    /// Gets the priority of this variant within its
    /// <see cref="ConflictGroup"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This value is compared only when two active prefixed candidates write
    /// the same property, have the same AKCSS operation priority, and declare
    /// the same non-empty <see cref="ConflictGroup"/>. The candidate with the
    /// greater value wins. If the values are equal, the candidate written
    /// later wins.
    /// </para>
    /// <para>
    /// The value is ignored between different groups, for variants without a
    /// group, and when comparing a prefixed candidate with an unprefixed one.
    /// </para>
    /// </remarks>
    public double Order
    {
        get;
    }

    /// <summary>
    /// Gets or initializes the key of the ordered variant group to which this
    /// variant belongs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Variants with the same non-empty key are ordered by <see cref="Order"/>
    /// when their operations already conflict. Assigning the same group to two
    /// variants does not make unrelated property operations conflict.
    /// </para>
    /// <para>
    /// A <see langword="null"/>, empty, or whitespace value means that the
    /// variant has no ordered group. Such candidates are compared with other
    /// groups by their position in markup rather than by <see cref="Order"/>.
    /// </para>
    /// </remarks>
    public string? ConflictGroup
    {
        get;
        init;
    }

    /// <summary>
    /// Gets or initializes how the winning active prefixed candidate competes
    /// with an unprefixed candidate that writes the same property.
    /// </summary>
    /// <remarks>
    /// This setting is applied only after the prefixed winner has been chosen.
    /// It defaults to <see cref="UnprefixedUtilityPrecedence.SourceOrder"/>.
    /// </remarks>
    public UnprefixedUtilityPrecedence UnprefixedPrecedence
    {
        get;
        init;
    } = UnprefixedUtilityPrecedence.SourceOrder;
}
