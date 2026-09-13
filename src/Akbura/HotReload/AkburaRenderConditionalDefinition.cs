using System.ComponentModel;

namespace Akbura.HotReload;

/// <summary>Describes one nonvisual conditional content region.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkburaRenderConditionalDefinition
{
    /// <summary>Initializes a current-plan conditional region definition.</summary>
    public AkburaRenderConditionalDefinition(
        int localId,
        int ownerLocalId,
        string slot,
        string syntaxIdentity,
        AkburaRenderConditionalBranchDefinition[] branches,
        int parentRegionId = -1,
        int parentBranchId = -1)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(localId);
        ArgumentOutOfRangeException.ThrowIfNegative(ownerLocalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(syntaxIdentity);
        ArgumentNullException.ThrowIfNull(branches);
        if (branches.Length == 0 || parentRegionId < -1 || parentRegionId >= localId ||
            parentBranchId < -1 || (parentRegionId < 0) != (parentBranchId < 0))
        {
            throw new ArgumentException("A conditional region requires branches and a valid ancestor path.");
        }

        LocalId = localId;
        OwnerLocalId = ownerLocalId;
        Slot = slot;
        SyntaxIdentity = syntaxIdentity;
        Branches = (AkburaRenderConditionalBranchDefinition[])branches.Clone();
        ParentRegionId = parentRegionId;
        ParentBranchId = parentBranchId;
    }

    /// <summary>Gets the dense current-plan region identifier.</summary>
    public int LocalId { get; }

    /// <summary>Gets the real node owning the content destination.</summary>
    public int OwnerLocalId { get; }

    /// <summary>Gets the semantic destination slot.</summary>
    public string Slot { get; }

    /// <summary>Gets the normalized declaration identity used as matching evidence.</summary>
    public string SyntaxIdentity { get; }

    /// <summary>Gets the containing region, or <c>-1</c>.</summary>
    public int ParentRegionId { get; }

    /// <summary>Gets the containing branch ordinal, or <c>-1</c>.</summary>
    public int ParentBranchId { get; }

    internal AkburaRenderConditionalBranchDefinition[] Branches { get; }
}

/// <summary>Describes matching evidence for one conditional alternative.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
[Browsable(false)]
public readonly struct AkburaRenderConditionalBranchDefinition
{
    /// <summary>Initializes one branch without assigning it a runtime identity.</summary>
    public AkburaRenderConditionalBranchDefinition(string conditionIdentity, string shapeIdentity)
    {
        ArgumentNullException.ThrowIfNull(conditionIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(shapeIdentity);
        ConditionIdentity = conditionIdentity;
        ShapeIdentity = shapeIdentity;
    }

    /// <summary>Gets the normalized condition; the empty string denotes an else branch.</summary>
    public string ConditionIdentity { get; }

    /// <summary>Gets structural matching evidence independent of literal property values.</summary>
    public string ShapeIdentity { get; }
}
