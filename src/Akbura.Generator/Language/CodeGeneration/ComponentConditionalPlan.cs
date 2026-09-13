using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentConditionalBranchPlan
{
    public ComponentConditionalBranchPlan(
        int scopeId, AkburaSyntax syntax, CSharpExpressionSyntax? condition,
        ComponentPlanRange items, string shapeIdentity,
        ImmutableArray<ComponentConditionalCapturePlan> captures = default)
    {
        ScopeId = scopeId;
        Syntax = syntax;
        Condition = condition;
        Items = items;
        ShapeIdentity = shapeIdentity;
        Captures = captures.IsDefault ? [] : captures;
    }

    public int ScopeId { get; }

    public AkburaSyntax Syntax { get; }

    public CSharpExpressionSyntax? Condition { get; }

    public ComponentPlanRange Items { get; }

    public string ShapeIdentity { get; }

    public ImmutableArray<ComponentConditionalCapturePlan> Captures { get; }
}

internal readonly struct ComponentConditionalRegionPlan
{
    public ComponentConditionalRegionPlan(
        int id, int ownerElementId, int parentScopeId, int parentRegionId,
        int parentBranchId, MarkupIfStatementSyntax syntax,
        ImmutableArray<ComponentConditionalBranchPlan> branches,
        int runtimeStorageRootScopeId = 0,
        int reservedCapacity = 0)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        ParentScopeId = parentScopeId;
        ParentRegionId = parentRegionId;
        ParentBranchId = parentBranchId;
        Syntax = syntax;
        Branches = branches;
        RuntimeStorageRootScopeId = runtimeStorageRootScopeId;
        ReservedCapacity = reservedCapacity;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public int ParentScopeId { get; }

    public int ParentRegionId { get; }

    public int ParentBranchId { get; }

    public MarkupIfStatementSyntax Syntax { get; }

    public ImmutableArray<ComponentConditionalBranchPlan> Branches { get; }

    public int RuntimeStorageRootScopeId { get; }

    public int ReservedCapacity { get; }
}

internal readonly struct ComponentConditionalContentPlan
{
    public ComponentConditionalContentPlan(
        int id, int ownerElementId, PropertyWritePlan property,
        CollectionWritePlan collection, ComponentPlanRange items,
        AkburaSyntax syntax, Akbura.Language.Symbols.MarkupDictionaryShape dictionaryShape = default,
        bool replacesStyles = false)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Property = property;
        Collection = collection;
        Items = items;
        Syntax = syntax;
        DictionaryShape = dictionaryShape;
        ReplacesStyles = replacesStyles;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public PropertyWritePlan Property { get; }

    public CollectionWritePlan Collection { get; }

    public ComponentPlanRange Items { get; }

    public AkburaSyntax Syntax { get; }

    public Akbura.Language.Symbols.MarkupDictionaryShape DictionaryShape { get; }

    public bool ReplacesStyles { get; }
}
