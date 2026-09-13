using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>
/// Describes one node in a generated render plan.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkburaRenderNodeDefinition
{
    /// <summary>Initializes an unconditional render node definition.</summary>
    public AkburaRenderNodeDefinition(
        int localId,
        int parentId,
        string slot,
        Type type,
        string? explicitKey,
        string syntaxIdentity)
        : this(localId, parentId, slot, type, explicitKey, syntaxIdentity, -1, -1)
    {
    }

    /// <summary>
    /// Initializes a render node definition.
    /// </summary>
    /// <param name="localId">The dense identifier in the current render plan.</param>
    /// <param name="parentId">The current-plan parent identifier, or <c>-1</c> for the root.</param>
    /// <param name="slot">The stable semantic slot within the parent.</param>
    /// <param name="type">The exact runtime type created for the node.</param>
    /// <param name="explicitKey">An optional explicit source identity.</param>
    /// <param name="syntaxIdentity">The normalized identity of the node declaration.</param>
    /// <param name="conditionalRegionId">The containing conditional region, or <c>-1</c>.</param>
    /// <param name="conditionalBranchId">The current-plan branch ordinal, or <c>-1</c>.</param>
    public AkburaRenderNodeDefinition(
        int localId,
        int parentId,
        string slot,
        Type type,
        string? explicitKey,
        string syntaxIdentity,
        int conditionalRegionId,
        int conditionalBranchId)
    {
        if (localId < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(localId),
                localId,
                "A render node local identifier cannot be negative.");
        }

        if (parentId < -1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(parentId),
                parentId,
                "A render node parent identifier cannot be less than -1.");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntaxIdentity);

        if (type.IsValueType)
        {
            throw new ArgumentException(
                $"Render node type '{type.FullName}' must be a reference type.",
                nameof(type));
        }

        if (explicitKey != null && string.IsNullOrWhiteSpace(explicitKey))
        {
            throw new ArgumentException(
                "An explicit render node key cannot be empty or whitespace.",
                nameof(explicitKey));
        }

        LocalId = localId;
        ParentId = parentId;
        Slot = slot;
        Type = type;
        ExplicitKey = explicitKey;
        SyntaxIdentity = syntaxIdentity;
        if (conditionalRegionId < -1 || conditionalBranchId < -1 ||
            (conditionalRegionId < 0) != (conditionalBranchId < 0))
        {
            throw new ArgumentException("A conditional node must identify both its region and branch.");
        }

        ConditionalRegionId = conditionalRegionId;
        ConditionalBranchId = conditionalBranchId;
    }

    /// <summary>
    /// Gets the dense identifier in the current render plan.
    /// </summary>
    public int LocalId { get; }

    /// <summary>
    /// Gets the current-plan parent identifier, or <c>-1</c> for the root.
    /// </summary>
    public int ParentId { get; }

    /// <summary>
    /// Gets the stable semantic slot within the parent.
    /// </summary>
    public string Slot { get; }

    /// <summary>
    /// Gets the exact runtime type created for the node.
    /// </summary>
    public Type Type { get; }

    /// <summary>
    /// Gets the optional explicit source identity.
    /// </summary>
    public string? ExplicitKey { get; }

    /// <summary>
    /// Gets the normalized identity of the node declaration.
    /// </summary>
    public string SyntaxIdentity { get; }

    /// <summary>Gets the containing conditional region, or <c>-1</c>.</summary>
    public int ConditionalRegionId { get; }

    /// <summary>Gets the branch ordinal in the current source plan, or <c>-1</c>.</summary>
    public int ConditionalBranchId { get; }
}
