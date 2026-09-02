using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.CodeGeneration;

internal readonly struct ComponentPlanRange
{
    public ComponentPlanRange(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public int Start { get; }

    public int Length { get; }

    public bool IsEmpty => Length == 0;
}

[Flags]
internal enum ComponentElementFlags : ushort
{
    None = 0,
    IsRoot = 1 << 0,
    IsDeferred = 1 << 1,
    IsTemplateElement = 1 << 2,
    IsControl = 1 << 3,
    SupportsInitialize = 1 << 4,
    HasName = 1 << 5,
    RequiresLocalMarkupContext = 1 << 6,
    IsLocal = 1 << 7,
}

internal enum ComponentElementScopeKind : byte
{
    Component,
    DeferredContent,
    DataTemplate,
}

internal readonly struct ComponentElementPlan
{
    public ComponentElementPlan(
        int id,
        MarkupElementSyntax syntax,
        ITypeSymbol type,
        string identifier,
        int parentId,
        int scopeId,
        ComponentElementScopeKind scopeKind,
        ComponentElementFlags flags,
        ComponentPlanRange children,
        ComponentPlanRange propertyWrites,
        ComponentPlanRange propertyElements,
        AkcssElementActivatorPlan akcss)
        : this(
            id,
            syntax,
            type,
            identifier,
            parentId,
            scopeId,
            scopeKind,
            flags,
            children,
            propertyWrites,
            propertySubscriptions: default,
            firstUpdateActions: default,
            propertyElements,
            content: default,
            akcss)
    {
    }

    public ComponentElementPlan(
        int id,
        MarkupElementSyntax syntax,
        ITypeSymbol type,
        string identifier,
        int parentId,
        int scopeId,
        ComponentElementScopeKind scopeKind,
        ComponentElementFlags flags,
        ComponentPlanRange children,
        ComponentPlanRange propertyWrites,
        ComponentPlanRange propertySubscriptions,
        ComponentPlanRange firstUpdateActions,
        ComponentPlanRange propertyElements,
        ComponentContentTargetReference content,
        AkcssElementActivatorPlan akcss)
    {
        Id = id;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        ParentId = parentId;
        ScopeId = scopeId;
        ScopeKind = scopeKind;
        Flags = flags;
        Children = children;
        PropertyWrites = propertyWrites;
        PropertySubscriptions = propertySubscriptions;
        FirstUpdateActions = firstUpdateActions;
        PropertyElements = propertyElements;
        Content = content;
        Akcss = akcss;
    }

    public int Id { get; }

    public MarkupElementSyntax Syntax { get; }

    public ITypeSymbol Type { get; }

    public string Identifier { get; }

    public int ParentId { get; }

    public int ScopeId { get; }

    public ComponentElementScopeKind ScopeKind { get; }

    public ComponentElementFlags Flags { get; }

    public ComponentPlanRange Children { get; }

    public ComponentPlanRange PropertyWrites { get; }

    public ComponentPlanRange PropertySubscriptions { get; }

    public ComponentPlanRange FirstUpdateActions { get; }

    public ComponentPlanRange PropertyElements { get; }

    public ComponentContentTargetReference Content { get; }

    public AkcssElementActivatorPlan Akcss { get; }

    public bool IsRoot => (Flags & ComponentElementFlags.IsRoot) != 0;

    public bool IsLocal => (Flags & ComponentElementFlags.IsLocal) != 0;

    public bool HasName => (Flags & ComponentElementFlags.HasName) != 0;

    public bool IsDeferred => (Flags & ComponentElementFlags.IsDeferred) != 0;

    public bool IsTemplateElement => (Flags & ComponentElementFlags.IsTemplateElement) != 0;

    public bool SupportsInitialize => (Flags & ComponentElementFlags.SupportsInitialize) != 0;

    public bool RequiresLocalMarkupContext =>
        (Flags & ComponentElementFlags.RequiresLocalMarkupContext) != 0;
}

internal enum ComponentPropertyValueKind : byte
{
    None,
    Constant,
    CSharpExpression,
    ElementReference,
    MarkupExtensionValue,
    MarkupBinding,
    DynamicResource,
    StaticResource,
    BindingBaseResult,
    RuntimeMarkupExtensionResult,
}

internal enum ComponentPropertySynchronizationKind : byte
{
    None,
    Bind,
    Out,
}

internal readonly struct ComponentCSharpValuePlan
{
    public ComponentCSharpValuePlan(
        CSharpOperationDefinition operation,
        object? convertedValue,
        string? literalValue,
        ITypeSymbol? targetType)
    {
        Operation = operation;
        ConvertedValue = convertedValue;
        LiteralValue = literalValue;
        TargetType = targetType;
    }

    public CSharpOperationDefinition Operation { get; }

    public object? ConvertedValue { get; }

    public string? LiteralValue { get; }

    public ITypeSymbol? TargetType { get; }
}

internal readonly struct ComponentPropertyValueReference
{
    public ComponentPropertyValueReference(
        ComponentPropertyValueKind kind,
        int index)
    {
        Kind = kind;
        Index = index;
    }

    public ComponentPropertyValueKind Kind { get; }

    public int Index { get; }

    public bool IsValid => Kind != ComponentPropertyValueKind.None && Index >= 0;
}

internal readonly struct ComponentPropertySubscriptionPlan
{
    public ComponentPropertySubscriptionPlan(
        int id,
        int elementId,
        int sourceOrder,
        ComponentPropertySynchronizationKind kind,
        PropertyObservationPlan observation,
        CSharpOperationDefinition targetOperation,
        ITypeSymbol valueType,
        AkburaSyntax syntax)
    {
        Id = id;
        ElementId = elementId;
        SourceOrder = sourceOrder;
        Kind = kind;
        Observation = observation;
        TargetOperation = targetOperation;
        ValueType = valueType ?? throw new ArgumentNullException(nameof(valueType));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public int Id { get; }

    public int ElementId { get; }

    public int SourceOrder { get; }

    public ComponentPropertySynchronizationKind Kind { get; }

    public PropertyObservationPlan Observation { get; }

    public CSharpOperationDefinition TargetOperation { get; }

    public ITypeSymbol ValueType { get; }

    public AkburaSyntax Syntax { get; }
}

internal readonly struct ComponentPropertyWritePlan
{
    public ComponentPropertyWritePlan(
        PropertyWritePlan destination,
        ComponentPropertyValueKind valueKind,
        int payloadIndex,
        AkburaSyntax syntax,
        bool isFirstUpdate)
    {
        Destination = destination;
        ValueKind = valueKind;
        PayloadIndex = payloadIndex;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        IsFirstUpdate = isFirstUpdate;
    }

    public PropertyWritePlan Destination { get; }

    public ComponentPropertyValueKind ValueKind { get; }

    public int PayloadIndex { get; }

    public AkburaSyntax Syntax { get; }

    public bool IsFirstUpdate { get; }
}

internal readonly struct ComponentPropertyElementPlan
{
    public ComponentPropertyElementPlan(
        int id,
        int ownerElementId,
        MarkupElementSyntax syntax,
        ComponentContentTargetReference content)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Content = content;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public MarkupElementSyntax Syntax { get; }

    public ComponentContentTargetReference Content { get; }
}

internal readonly struct ComponentDeferredContentPlan
{
    public ComponentDeferredContentPlan(
        int id,
        int scopeId,
        int targetElementId,
        AkburaSyntax syntax,
        ComponentPlanRange roots)
    {
        Id = id;
        ScopeId = scopeId;
        TargetElementId = targetElementId;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Roots = roots;
    }

    public int Id { get; }

    public int ScopeId { get; }

    public int TargetElementId { get; }

    public AkburaSyntax Syntax { get; }

    public ComponentPlanRange Roots { get; }
}

internal readonly struct ComponentTemplatePlan
{
    public ComponentTemplatePlan(
        int id,
        int scopeId,
        int ownerElementId,
        MarkupElementSyntax syntax,
        ComponentPlanRange roots)
    {
        Id = id;
        ScopeId = scopeId;
        OwnerElementId = ownerElementId;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Roots = roots;
    }

    public int Id { get; }

    public int ScopeId { get; }

    public int OwnerElementId { get; }

    public MarkupElementSyntax Syntax { get; }

    public ComponentPlanRange Roots { get; }
}

internal readonly struct ComponentPlan
{
    public ComponentPlan(
        ImmutableArray<ComponentElementPlan> elements,
        ImmutableArray<int> rootElementIds,
        ImmutableArray<int> childElementIds,
        ImmutableArray<ComponentPropertyWritePlan> propertyWrites,
        ImmutableArray<ComponentCSharpValuePlan> csharpValues,
        ImmutableArray<MarkupExtensionResultPlan> markupExtensions,
        ImmutableArray<BindingWritePlan> bindings,
        ImmutableArray<ComponentPropertySubscriptionPlan> propertySubscriptions,
        ImmutableArray<ComponentNameAssignmentPlan> nameAssignments,
        ImmutableArray<ComponentRoutedEventPlan> routedEvents,
        ImmutableArray<ComponentCommandBindingPlan> commandBindings,
        ImmutableArray<ComponentFirstUpdateActionPlan> firstUpdateActions,
        ImmutableArray<ComponentPropertyElementPlan> propertyElements,
        ImmutableArray<ComponentPropertyContentPlan> propertyContents,
        ImmutableArray<ComponentCollectionContentPlan> collectionContents,
        ImmutableArray<ComponentContentItemPlan> contentItems,
        ImmutableArray<ComponentDeferredContentPlan> deferredContents,
        ImmutableArray<ComponentTemplatePlan> templates,
        ImmutableArray<BindingElementReference> elementReferences,
        AkcssComponentActivatorPlan akcss)
    {
        Elements = elements.IsDefault ? ImmutableArray<ComponentElementPlan>.Empty : elements;
        RootElementIds = rootElementIds.IsDefault ? ImmutableArray<int>.Empty : rootElementIds;
        ChildElementIds = childElementIds.IsDefault ? ImmutableArray<int>.Empty : childElementIds;
        PropertyWrites = propertyWrites.IsDefault ? ImmutableArray<ComponentPropertyWritePlan>.Empty : propertyWrites;
        CSharpValues = csharpValues.IsDefault ? ImmutableArray<ComponentCSharpValuePlan>.Empty : csharpValues;
        MarkupExtensions = markupExtensions.IsDefault
            ? ImmutableArray<MarkupExtensionResultPlan>.Empty
            : markupExtensions;
        Bindings = bindings.IsDefault ? ImmutableArray<BindingWritePlan>.Empty : bindings;
        PropertySubscriptions = propertySubscriptions.IsDefault
            ? ImmutableArray<ComponentPropertySubscriptionPlan>.Empty
            : propertySubscriptions;
        NameAssignments = nameAssignments.IsDefault
            ? ImmutableArray<ComponentNameAssignmentPlan>.Empty
            : nameAssignments;
        RoutedEvents = routedEvents.IsDefault
            ? ImmutableArray<ComponentRoutedEventPlan>.Empty
            : routedEvents;
        CommandBindings = commandBindings.IsDefault
            ? ImmutableArray<ComponentCommandBindingPlan>.Empty
            : commandBindings;
        FirstUpdateActions = firstUpdateActions.IsDefault
            ? ImmutableArray<ComponentFirstUpdateActionPlan>.Empty
            : firstUpdateActions;
        PropertyElements = propertyElements.IsDefault
            ? ImmutableArray<ComponentPropertyElementPlan>.Empty
            : propertyElements;
        PropertyContents = propertyContents.IsDefault
            ? ImmutableArray<ComponentPropertyContentPlan>.Empty
            : propertyContents;
        CollectionContents = collectionContents.IsDefault
            ? ImmutableArray<ComponentCollectionContentPlan>.Empty
            : collectionContents;
        ContentItems = contentItems.IsDefault
            ? ImmutableArray<ComponentContentItemPlan>.Empty
            : contentItems;
        DeferredContents = deferredContents.IsDefault
            ? ImmutableArray<ComponentDeferredContentPlan>.Empty
            : deferredContents;
        Templates = templates.IsDefault ? ImmutableArray<ComponentTemplatePlan>.Empty : templates;
        ElementReferences = elementReferences.IsDefault
            ? ImmutableArray<BindingElementReference>.Empty
            : elementReferences;
        Akcss = akcss;
    }

    public ImmutableArray<ComponentElementPlan> Elements { get; }

    public ImmutableArray<int> RootElementIds { get; }

    public ImmutableArray<int> ChildElementIds { get; }

    public ImmutableArray<ComponentPropertyWritePlan> PropertyWrites { get; }

    public ImmutableArray<ComponentCSharpValuePlan> CSharpValues { get; }

    public ImmutableArray<MarkupExtensionResultPlan> MarkupExtensions { get; }

    public ImmutableArray<BindingWritePlan> Bindings { get; }

    public ImmutableArray<ComponentPropertySubscriptionPlan> PropertySubscriptions { get; }

    public ImmutableArray<ComponentNameAssignmentPlan> NameAssignments { get; }

    public ImmutableArray<ComponentRoutedEventPlan> RoutedEvents { get; }

    public ImmutableArray<ComponentCommandBindingPlan> CommandBindings { get; }

    public ImmutableArray<ComponentFirstUpdateActionPlan> FirstUpdateActions { get; }

    public ImmutableArray<ComponentPropertyElementPlan> PropertyElements { get; }

    public ImmutableArray<ComponentPropertyContentPlan> PropertyContents { get; }

    public ImmutableArray<ComponentCollectionContentPlan> CollectionContents { get; }

    public ImmutableArray<ComponentContentItemPlan> ContentItems { get; }

    public ImmutableArray<ComponentDeferredContentPlan> DeferredContents { get; }

    public ImmutableArray<ComponentTemplatePlan> Templates { get; }

    public ImmutableArray<BindingElementReference> ElementReferences { get; }

    public AkcssComponentActivatorPlan Akcss { get; }

    public bool IsEmpty => Elements.IsDefaultOrEmpty;
}
