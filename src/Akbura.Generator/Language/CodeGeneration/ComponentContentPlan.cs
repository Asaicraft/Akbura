using Akbura.Language.Syntax;

using Akbura.Language.Symbols;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal enum ComponentContentValueKind : byte
{
    None,
    Element,
    Constant,
    CSharpExpression,
    DeferredContent,
    Template,
    Conditional,
}

internal readonly struct ComponentContentValueReference
{
    public ComponentContentValueReference(
        ComponentContentValueKind kind,
        int index)
    {
        Kind = kind;
        Index = index;
    }

    public ComponentContentValueKind Kind { get; }

    /// <summary>
    /// Element ID, C# value index, deferred-content ID, or template ID,
    /// depending on <see cref="Kind"/>.
    /// </summary>
    public int Index { get; }

    public bool IsValid =>
        Kind != ComponentContentValueKind.None &&
        Index >= 0;

    public bool IsEager =>
        Kind is ComponentContentValueKind.Element or
            ComponentContentValueKind.Constant or
            ComponentContentValueKind.CSharpExpression;
}

internal readonly struct ComponentPropertyContentPlan
{
    public ComponentPropertyContentPlan(
        int id,
        int ownerElementId,
        PropertyWritePlan destination,
        ComponentContentValueReference firstUpdateValue,
        ComponentContentValueReference updateValue,
        AkburaSyntax syntax)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Destination = destination;
        FirstUpdateValue = firstUpdateValue;
        UpdateValue = updateValue;
        Syntax = syntax;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public PropertyWritePlan Destination { get; }

    public ComponentContentValueReference FirstUpdateValue { get; }

    public ComponentContentValueReference UpdateValue { get; }

    public AkburaSyntax Syntax { get; }
}

internal readonly struct ComponentCollectionContentPlan
{
    public ComponentCollectionContentPlan(
        int id,
        int ownerElementId,
        CollectionWritePlan destination,
        ComponentPlanRange items,
        AkburaSyntax syntax,
        MarkupDictionaryShape dictionaryShape = default,
        bool replacesStyles = false)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Destination = destination;
        Items = items;
        Syntax = syntax;
        DictionaryShape = dictionaryShape;
        ReplacesStyles = replacesStyles;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public CollectionWritePlan Destination { get; }

    public ComponentPlanRange Items { get; }

    public MarkupDictionaryShape DictionaryShape { get; }

    public bool ReplacesStyles { get; }

    public AkburaSyntax Syntax { get; }
}

internal readonly struct ComponentContentItemPlan
{
    public ComponentContentItemPlan(
        ComponentContentValueReference value,
        AkburaSyntax syntax,
        IMethodSymbol? insertionMethod = null,
        ComponentContentValueReference key = default)
    {
        Value = value;
        Syntax = syntax;
        InsertionMethod = insertionMethod;
        Key = key;
    }

    public ComponentContentValueReference Value { get; }

    public IMethodSymbol? InsertionMethod { get; }

    public ComponentContentValueReference Key { get; }

    public AkburaSyntax Syntax { get; }
}

internal enum ComponentContentTargetKind : byte
{
    None,
    Property,
    Collection,
    Conditional,
}

internal readonly struct ComponentContentTargetReference
{
    public ComponentContentTargetReference(
        ComponentContentTargetKind kind,
        int index)
    {
        Kind = kind;
        Index = index;
    }

    public ComponentContentTargetKind Kind { get; }

    public int Index { get; }

    public bool IsValid =>
        Kind != ComponentContentTargetKind.None &&
        Index >= 0;
}
