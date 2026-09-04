using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Language.Binder;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using Akbura.Pools;

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
    RequiresContentPresenterRefresh = 1 << 8,
}

internal enum ComponentElementScopeKind : byte
{
    Component,
    DeferredContent,
    DataTemplate,
}

[Flags]
internal enum ComponentScopeFlags : byte
{
    None = 0,
    RequiresNameScope = 1 << 0,
}

internal readonly struct ComponentScopePlan
{
    public ComponentScopePlan(
        int id,
        int parentScopeId,
        int ownerElementId,
        ComponentElementScopeKind kind,
        ComponentPlanRange elements,
        ComponentPlanRange roots,
        ComponentScopeFlags flags)
    {
        Id = id;
        ParentScopeId = parentScopeId;
        OwnerElementId = ownerElementId;
        Kind = kind;
        Elements = elements;
        Roots = roots;
        Flags = flags;
    }

    public int Id { get; }

    /// <summary>
    /// -1 for the component scope.
    /// </summary>
    public int ParentScopeId { get; }

    /// <summary>
    /// The element whose content property introduced this scope.
    /// -1 for the component scope.
    /// </summary>
    public int OwnerElementId { get; }

    public ComponentElementScopeKind Kind { get; }

    /// <summary>
    /// Range inside <see cref="ComponentPlan.ScopeElementIds"/>.
    /// </summary>
    public ComponentPlanRange Elements { get; }

    /// <summary>
    /// Range inside <see cref="ComponentPlan.ScopeRootElementIds"/>.
    /// </summary>
    public ComponentPlanRange Roots { get; }

    public ComponentScopeFlags Flags { get; }

    public bool RequiresNameScope =>
        (Flags & ComponentScopeFlags.RequiresNameScope) != 0;
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

    public bool IsControl => (Flags & ComponentElementFlags.IsControl) != 0;

    public bool RequiresContentPresenterRefresh =>
        (Flags & ComponentElementFlags.RequiresContentPresenterRefresh) != 0;

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

[Flags]
internal enum ComponentPropertyWritePhase : byte
{
    None = 0,
    FirstUpdate = 1 << 0,
    Update = 1 << 1,
    Both = FirstUpdate | Update,
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
        ComponentPropertyWritePhase phase)
    {
        Debug.Assert(destination.IsValid);
        Debug.Assert(valueKind != ComponentPropertyValueKind.None);
        Debug.Assert(payloadIndex >= 0);
        Debug.Assert(syntax != null);
        Debug.Assert(phase != ComponentPropertyWritePhase.None);

        Destination = destination;
        ValueKind = valueKind;
        PayloadIndex = payloadIndex;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Phase = phase;
    }

    public PropertyWritePlan Destination { get; }

    public ComponentPropertyValueKind ValueKind { get; }

    public int PayloadIndex { get; }

    public AkburaSyntax Syntax { get; }

    public ComponentPropertyWritePhase Phase { get; }

    public bool WritesDuringFirstUpdate =>
        (Phase & ComponentPropertyWritePhase.FirstUpdate) != 0;

    public bool WritesDuringUpdate =>
        (Phase & ComponentPropertyWritePhase.Update) != 0;
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
        ITypeSymbol resultType,
        AkburaSyntax syntax)
    {
        Id = id;
        ScopeId = scopeId;
        TargetElementId = targetElementId;
        ResultType = resultType ?? throw new ArgumentNullException(nameof(resultType));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public int Id { get; }

    public int ScopeId { get; }

    public int TargetElementId { get; }

    public ITypeSymbol ResultType { get; }

    public AkburaSyntax Syntax { get; }
}

internal readonly struct ComponentTemplatePlan
{
    public ComponentTemplatePlan(
        int id,
        int scopeId,
        int ownerElementId,
        ITypeSymbol dataType,
        string itemName,
        MarkupElementSyntax syntax)
    {
        Id = id;
        ScopeId = scopeId;
        OwnerElementId = ownerElementId;
        DataType = dataType ?? throw new ArgumentNullException(nameof(dataType));
        ItemName = itemName ?? throw new ArgumentNullException(nameof(itemName));
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
    }

    public int Id { get; }

    public int ScopeId { get; }

    public int OwnerElementId { get; }

    public ITypeSymbol DataType { get; }

    public string ItemName { get; }

    public MarkupElementSyntax Syntax { get; }
}

internal readonly struct ComponentPlan
{
    public ComponentPlan(
    PooledImmutableList<ComponentElementPlan> elements,
    PooledImmutableList<int> rootElementIds,
    PooledImmutableList<int> childElementIds,
    PooledImmutableList<ComponentScopePlan> scopes,
    PooledImmutableList<int> scopeElementIds,
    PooledImmutableList<int> scopeRootElementIds,
    PooledImmutableList<ComponentPropertyWritePlan> propertyWrites,
    PooledImmutableList<ComponentCSharpValuePlan> csharpValues,
    PooledImmutableList<MarkupExtensionResultPlan> markupExtensions,
    PooledImmutableList<BindingWritePlan> bindings,
    PooledImmutableList<ComponentPropertySubscriptionPlan> propertySubscriptions,
    PooledImmutableList<ComponentNameAssignmentPlan> nameAssignments,
    PooledImmutableList<ComponentRoutedEventPlan> routedEvents,
    PooledImmutableList<ComponentCommandBindingPlan> commandBindings,
    PooledImmutableList<ComponentFirstUpdateActionPlan> firstUpdateActions,
    PooledImmutableList<ComponentPropertyElementPlan> propertyElements,
    PooledImmutableList<ComponentPropertyContentPlan> propertyContents,
    PooledImmutableList<ComponentCollectionContentPlan> collectionContents,
    PooledImmutableList<ComponentContentItemPlan> contentItems,
    PooledImmutableList<ComponentDeferredContentPlan> deferredContents,
    PooledImmutableList<ComponentTemplatePlan> templates,
    ImmutableArray<BindingElementReference> elementReferences,
    ComponentLifecyclePlan lifecycle,
    ImmutableArray<ComponentRenderStatementPlan> renderStatements,
    AkcssComponentActivatorPlan akcss)
    {
        Elements = elements;
        RootElementIds = rootElementIds;
        ChildElementIds = childElementIds;
        Scopes = scopes;
        ScopeElementIds = scopeElementIds;
        ScopeRootElementIds = scopeRootElementIds;

        PropertyWrites = propertyWrites;
        CSharpValues = csharpValues;
        MarkupExtensions = markupExtensions;
        Bindings = bindings;
        PropertySubscriptions = propertySubscriptions;

        NameAssignments = nameAssignments;
        RoutedEvents = routedEvents;
        CommandBindings = commandBindings;
        FirstUpdateActions = firstUpdateActions;

        PropertyElements = propertyElements;
        PropertyContents = propertyContents;
        CollectionContents = collectionContents;
        ContentItems = contentItems;
        DeferredContents = deferredContents;
        Templates = templates;

        ElementReferences = elementReferences.IsDefault
            ? []
            : elementReferences;

        Lifecycle = lifecycle;

        RenderStatements = renderStatements.IsDefault
            ? []
            : renderStatements;

        Akcss = akcss;
    }

    public PooledImmutableList<ComponentElementPlan> Elements { get; }

    public PooledImmutableList<int> RootElementIds { get; }

    public PooledImmutableList<int> ChildElementIds { get; }

    public PooledImmutableList<ComponentScopePlan> Scopes { get; }

    public PooledImmutableList<int> ScopeElementIds { get; }

    public PooledImmutableList<int> ScopeRootElementIds { get; }

    public PooledImmutableList<ComponentPropertyWritePlan> PropertyWrites { get; }

    public PooledImmutableList<ComponentCSharpValuePlan> CSharpValues { get; }

    public PooledImmutableList<MarkupExtensionResultPlan> MarkupExtensions { get; }

    public PooledImmutableList<BindingWritePlan> Bindings { get; }

    public PooledImmutableList<ComponentPropertySubscriptionPlan> PropertySubscriptions { get; }

    public PooledImmutableList<ComponentNameAssignmentPlan> NameAssignments { get; }

    public PooledImmutableList<ComponentRoutedEventPlan> RoutedEvents { get; }

    public PooledImmutableList<ComponentCommandBindingPlan> CommandBindings { get; }

    public PooledImmutableList<ComponentFirstUpdateActionPlan> FirstUpdateActions { get; }

    public PooledImmutableList<ComponentPropertyElementPlan> PropertyElements { get; }

    public PooledImmutableList<ComponentPropertyContentPlan> PropertyContents { get; }

    public PooledImmutableList<ComponentCollectionContentPlan> CollectionContents { get; }

    public PooledImmutableList<ComponentContentItemPlan> ContentItems { get; }

    public PooledImmutableList<ComponentDeferredContentPlan> DeferredContents { get; }

    public PooledImmutableList<ComponentTemplatePlan> Templates { get; }

    public ImmutableArray<BindingElementReference> ElementReferences { get; }

    public ComponentLifecyclePlan Lifecycle { get; }

    public ImmutableArray<ComponentRenderStatementPlan> RenderStatements { get; }

    public AkcssComponentActivatorPlan Akcss { get; }

    public bool IsEmpty => Elements.IsEmpty;

    internal void ReturnToPool()
    {
        Elements.ReturnToPool();
        RootElementIds.ReturnToPool();
        ChildElementIds.ReturnToPool();
        Scopes.ReturnToPool();
        ScopeElementIds.ReturnToPool();
        ScopeRootElementIds.ReturnToPool();

        PropertyWrites.ReturnToPool();
        CSharpValues.ReturnToPool();
        MarkupExtensions.ReturnToPool();
        Bindings.ReturnToPool();
        PropertySubscriptions.ReturnToPool();

        NameAssignments.ReturnToPool();
        RoutedEvents.ReturnToPool();
        CommandBindings.ReturnToPool();
        FirstUpdateActions.ReturnToPool();

        PropertyElements.ReturnToPool();
        PropertyContents.ReturnToPool();
        CollectionContents.ReturnToPool();
        ContentItems.ReturnToPool();
        DeferredContents.ReturnToPool();
        Templates.ReturnToPool();

        Akcss.ReturnToPool();
    }
}
