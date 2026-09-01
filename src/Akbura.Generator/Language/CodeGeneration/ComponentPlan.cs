using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Globalization;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

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
        int scopeOwnerId,
        ComponentElementScopeKind scopeKind,
        ComponentElementFlags flags,
        ComponentPlanRange children,
        ComponentPlanRange propertyWrites,
        ComponentPlanRange propertyElements,
        AkcssElementActivatorPlan akcss)
    {
        Id = id;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Type = type ?? throw new ArgumentNullException(nameof(type));
        Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
        ParentId = parentId;
        ScopeOwnerId = scopeOwnerId;
        ScopeKind = scopeKind;
        Flags = flags;
        Children = children;
        PropertyWrites = propertyWrites;
        PropertyElements = propertyElements;
        Akcss = akcss;
    }

    public int Id { get; }

    public MarkupElementSyntax Syntax { get; }

    public ITypeSymbol Type { get; }

    public string Identifier { get; }

    public int ParentId { get; }

    public int ScopeOwnerId { get; }

    public ComponentElementScopeKind ScopeKind { get; }

    public ComponentElementFlags Flags { get; }

    public ComponentPlanRange Children { get; }

    public ComponentPlanRange PropertyWrites { get; }

    public ComponentPlanRange PropertyElements { get; }

    public AkcssElementActivatorPlan Akcss { get; }

    public bool IsDeferred => (Flags & ComponentElementFlags.IsDeferred) != 0;

    public bool IsTemplateElement => (Flags & ComponentElementFlags.IsTemplateElement) != 0;

    public bool SupportsInitialize => (Flags & ComponentElementFlags.SupportsInitialize) != 0;
}

internal enum ComponentPropertyValueKind : byte
{
    None,
    Constant,
    CSharpExpression,
    ElementReference,
    MarkupExtensionValue,
    Binding,
    DynamicResource,
    StaticResource,
    BindingBaseResult,
    RuntimeMarkupExtensionResult,
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
        AkburaPropertySymbol property,
        IMarkupContentOperation operation,
        ComponentPlanRange children)
    {
        Id = id;
        OwnerElementId = ownerElementId;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Children = children;
    }

    public int Id { get; }

    public int OwnerElementId { get; }

    public MarkupElementSyntax Syntax { get; }

    public AkburaPropertySymbol Property { get; }

    public IMarkupContentOperation Operation { get; }

    public ComponentPlanRange Children { get; }
}

internal readonly struct ComponentDeferredContentPlan
{
    public ComponentDeferredContentPlan(
        int id,
        int scopeId,
        int targetElementId,
        AkburaSyntax syntax,
        AkburaPropertySymbol property,
        IMarkupContentOperation operation,
        ComponentPlanRange roots)
    {
        Id = id;
        ScopeId = scopeId;
        TargetElementId = targetElementId;
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        Property = property ?? throw new ArgumentNullException(nameof(property));
        Operation = operation ?? throw new ArgumentNullException(nameof(operation));
        Roots = roots;
    }

    public int Id { get; }

    public int ScopeId { get; }

    public int TargetElementId { get; }

    public AkburaSyntax Syntax { get; }

    public AkburaPropertySymbol Property { get; }

    public IMarkupContentOperation Operation { get; }

    public ComponentPlanRange Roots { get; }

    public string BuilderName => "__BuildDeferredContent" + Id.ToString(CultureInfo.InvariantCulture);
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
        ImmutableArray<ComponentPropertyElementPlan> propertyElements,
        ImmutableArray<ComponentDeferredContentPlan> deferredContents,
        ImmutableArray<ComponentTemplatePlan> templates,
        ImmutableArray<BindingElementReference> elementReferences,
        AkcssComponentActivatorPlan akcss)
    {
        Elements = elements.IsDefault ? ImmutableArray<ComponentElementPlan>.Empty : elements;
        RootElementIds = rootElementIds.IsDefault ? ImmutableArray<int>.Empty : rootElementIds;
        ChildElementIds = childElementIds.IsDefault ? ImmutableArray<int>.Empty : childElementIds;
        PropertyWrites = propertyWrites.IsDefault ? ImmutableArray<ComponentPropertyWritePlan>.Empty : propertyWrites;
        PropertyElements = propertyElements.IsDefault
            ? ImmutableArray<ComponentPropertyElementPlan>.Empty
            : propertyElements;
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

    public ImmutableArray<ComponentPropertyElementPlan> PropertyElements { get; }

    public ImmutableArray<ComponentDeferredContentPlan> DeferredContents { get; }

    public ImmutableArray<ComponentTemplatePlan> Templates { get; }

    public ImmutableArray<BindingElementReference> ElementReferences { get; }

    public AkcssComponentActivatorPlan Akcss { get; }
}
