using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Converts component syntax and semantic information into one immutable,
/// densely indexed generation plan.
/// </summary>
internal static class ComponentPlanner
{
    public static ComponentPlan Create(
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames)
    {
        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        var resultEnvironment = MarkupExtensionResultEnvironment.Create(semanticModel);
        return Create(component, semanticModel, akcssModuleTypeNames, in resultEnvironment);
    }

    internal static ComponentPlan Create(
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel,
        IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames,
        in MarkupExtensionResultEnvironment resultEnvironment)
    {
        if (component == null)
        {
            throw new ArgumentNullException(nameof(component));
        }

        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        if (akcssModuleTypeNames == null)
        {
            throw new ArgumentNullException(nameof(akcssModuleTypeNames));
        }

        if (!resultEnvironment.IsValid)
        {
            throw new ArgumentException(
                "The markup-extension result environment is not initialized.",
                nameof(resultEnvironment));
        }

        using var planner = new Planner(component, semanticModel, akcssModuleTypeNames, in resultEnvironment);
        return planner.Create();
    }

    private ref struct Planner
    {
        private readonly IAkburaComponentSymbol _component;
        private readonly AkburaSemanticModel _semanticModel;
        private readonly IReadOnlyDictionary<AkburaSyntax, string> _akcssModuleTypeNames;
        private readonly CSharpCompilation _compilation;
        private readonly MarkupExtensionResultEnvironment _resultEnvironment;
        private readonly INamedTypeSymbol? _controlType;
        private readonly INamedTypeSymbol? _initializeType;
        private readonly ArrayBuilder<PendingElementPlan> _elements;
        private readonly ArrayBuilder<ComponentPropertyElementPlan> _propertyElements;
        private readonly ArrayBuilder<ComponentDeferredContentPlan> _deferredContents;
        private readonly ArrayBuilder<ComponentTemplatePlan> _templates;
        private ImmutableArrayBuilder<int> _rootElementIds;
        private ImmutableArrayBuilder<int> _childElementIds;
        private ImmutableArrayBuilder<ComponentPropertyWritePlan> _propertyWrites;
        private ImmutableArrayBuilder<BindingElementReference> _elementReferences;
        private int _nextScopeId;

        public Planner(
            IAkburaComponentSymbol component,
            AkburaSemanticModel semanticModel,
            IReadOnlyDictionary<AkburaSyntax, string> akcssModuleTypeNames,
            in MarkupExtensionResultEnvironment resultEnvironment)
        {
            _component = component;
            _semanticModel = semanticModel;
            _akcssModuleTypeNames = akcssModuleTypeNames;
            _compilation = semanticModel.Compilation.CSharpCompilation;
            _resultEnvironment = resultEnvironment;
            _controlType = _compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
            _initializeType = _compilation.GetTypeByMetadataName("System.ComponentModel.ISupportInitialize");
            _elements = ArrayBuilder<PendingElementPlan>.GetInstance();
            _propertyElements = ArrayBuilder<ComponentPropertyElementPlan>.GetInstance();
            _deferredContents = ArrayBuilder<ComponentDeferredContentPlan>.GetInstance();
            _templates = ArrayBuilder<ComponentTemplatePlan>.GetInstance();
            _rootElementIds = ImmutableArrayBuilder<int>.Rent();
            _childElementIds = ImmutableArrayBuilder<int>.Rent();
            _propertyWrites = ImmutableArrayBuilder<ComponentPropertyWritePlan>.Rent();
            _elementReferences = ImmutableArrayBuilder<BindingElementReference>.Rent();
            _nextScopeId = 1;
        }

        public ComponentPlan Create()
        {
            foreach (var root in _component.DeclarationSyntax.Members.OfType<MarkupRootSyntax>())
            {
                if (TryBuildElement(root.Element, parentId: -1, default, isRoot: true, out var rootId))
                {
                    _rootElementIds.Add(rootId);
                }
            }

            using var akcssInputs = ImmutableArrayBuilder<AkcssActivatorElementInput>.Rent(_elements.Count);
            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                akcssInputs.Add(new AkcssActivatorElementInput(
                    element.Id,
                    element.Symbol,
                    element.Type,
                    element.RequiresLocalMarkupContext));
            }

            var akcss = AkcssActivatorPlanner.Create(
                _semanticModel,
                akcssInputs.WrittenSpan,
                _akcssModuleTypeNames);
            Debug.Assert(akcss.Elements.Length == _elements.Count);

            using var elements = ImmutableArrayBuilder<ComponentElementPlan>.Rent(_elements.Count);
            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                var elementAkcss = akcss.Elements[i];
                Debug.Assert(elementAkcss.ElementId == i);

                elements.Add(new ComponentElementPlan(
                    element.Id,
                    element.Syntax,
                    element.Type,
                    element.Identifier,
                    element.ParentId,
                    element.ScopeOwnerId,
                    element.ScopeKind,
                    element.Flags,
                    element.Children,
                    element.PropertyWrites,
                    element.PropertyElements,
                    elementAkcss));
            }

            return new ComponentPlan(
                elements.ToImmutable(),
                _rootElementIds.ToImmutable(),
                _childElementIds.ToImmutable(),
                _propertyWrites.ToImmutable(),
                _propertyElements.ToImmutable(),
                _deferredContents.ToImmutable(),
                _templates.ToImmutable(),
                _elementReferences.ToImmutable(),
                akcss);
        }

        public void Dispose()
        {
            _elements.Free();
            _propertyElements.Free();
            _deferredContents.Free();
            _templates.Free();
            _rootElementIds.Dispose();
            _childElementIds.Dispose();
            _propertyWrites.Dispose();
            _elementReferences.Dispose();
        }

        private bool TryBuildElement(
            MarkupElementSyntax syntax,
            int parentId,
            TraversalContext context,
            bool isRoot,
            out int elementId)
        {
            if (_semanticModel.GetSymbolInfo(syntax).Symbol is not IMarkupComponentSymbol symbol)
            {
                elementId = -1;
                return false;
            }

            var type = _semanticModel.TryGetMarkupElementReferenceType(syntax, out var referenceType) &&
                referenceType.Symbol is ITypeSymbol resolvedType
                    ? resolvedType
                    : symbol.ComponentType ?? symbol.AkburaComponent?.ComponentType ??
                        _compilation.GetSpecialType(SpecialType.System_Object);
            var scope = context.GetEffectiveScope();
            var nameOperation = FindNameOperation(symbol);
            elementId = _elements.Count;
            var identifier = nameOperation?.NameSymbol is { } name
                ? name.IdentifierText
                : "__element" + elementId.ToString(CultureInfo.InvariantCulture);

            _elements.Add(default);

            var propertyWriteStart = _propertyWrites.Count;
            AddPropertyWrites(symbol);
            var propertyWriteLength = _propertyWrites.Count - propertyWriteStart;

            if (nameOperation?.NameSymbol is { } nameSymbol)
            {
                _elementReferences.Add(new BindingElementReference(
                    nameSymbol.Name,
                    EscapeIdentifier(identifier),
                    scope.ScopeId,
                    isClassMember: !scope.IsLocal));
            }

            var contentOperation = _semanticModel.GetOperation(syntax) as IMarkupContentOperation;
            var implicitBoundary = CreateImplicitBoundary(elementId, syntax, contentOperation);
            using var directChildren = ImmutableArrayBuilder<int>.Rent();
            using var deferredRoots = ImmutableArrayBuilder<int>.Rent();
            using var templateRoots = ImmutableArrayBuilder<int>.Rent();
            using var directPropertyElements = ImmutableArrayBuilder<PendingPropertyElementPlan>.Rent();

            foreach (var content in syntax.Body.OfType<MarkupElementContentSyntax>())
            {
                var semanticChild = FindMarkupChild(contentOperation, content);
                var childContext = ResolveChildContext(
                    content.Element,
                    semanticChild,
                    context,
                    implicitBoundary);

                if (TryBuildElement(content.Element, elementId, childContext, isRoot: false, out var childId))
                {
                    directChildren.Add(childId);
                    TrackBoundaryRoot(implicitBoundary, childId, deferredRoots, templateRoots);
                    continue;
                }

                var propertySymbol = _semanticModel.GetSymbolInfo(content.Element).Symbol;
                if (propertySymbol is not AkburaPropertySymbol property ||
                    _semanticModel.GetOperation(content.Element) is not IMarkupContentOperation propertyOperation)
                {
                    continue;
                }

                directPropertyElements.Add(BuildPropertyElement(
                    elementId,
                    content.Element,
                    property,
                    propertyOperation,
                    context));
            }

            CompleteBoundary(implicitBoundary, deferredRoots.WrittenSpan, templateRoots.WrittenSpan);

            var children = AddElementIds(directChildren.WrittenSpan);
            var propertyElementStart = _propertyElements.Count;
            for (var i = 0; i < directPropertyElements.Count; i++)
            {
                var propertyElement = directPropertyElements.WrittenSpan[i];
                _propertyElements.Add(new ComponentPropertyElementPlan(
                    _propertyElements.Count,
                    propertyElement.OwnerElementId,
                    propertyElement.Syntax,
                    propertyElement.Property,
                    propertyElement.Operation,
                    propertyElement.Children));
            }

            var flags = GetElementFlags(
                type,
                isRoot,
                nameOperation != null,
                scope);
            _elements[elementId] = new PendingElementPlan(
                elementId,
                syntax,
                symbol,
                type,
                identifier,
                parentId,
                scope.ScopeId,
                scope.Kind,
                flags,
                children,
                new ComponentPlanRange(propertyWriteStart, propertyWriteLength),
                new ComponentPlanRange(propertyElementStart, _propertyElements.Count - propertyElementStart));
            return true;
        }

        private PendingPropertyElementPlan BuildPropertyElement(
            int ownerElementId,
            MarkupElementSyntax syntax,
            AkburaPropertySymbol property,
            IMarkupContentOperation operation,
            TraversalContext inheritedContext)
        {
            var boundary = CreatePropertyBoundary(ownerElementId, syntax, property, operation);
            using var children = ImmutableArrayBuilder<int>.Rent();
            using var deferredRoots = ImmutableArrayBuilder<int>.Rent();
            using var templateRoots = ImmutableArrayBuilder<int>.Rent();

            foreach (var content in syntax.Body.OfType<MarkupElementContentSyntax>())
            {
                var semanticChild = FindMarkupChild(operation, content);
                var childContext = ResolveChildContext(
                    content.Element,
                    semanticChild,
                    inheritedContext,
                    boundary);

                if (!TryBuildElement(content.Element, ownerElementId, childContext, isRoot: false, out var childId))
                {
                    continue;
                }

                children.Add(childId);
                TrackBoundaryRoot(boundary, childId, deferredRoots, templateRoots);
            }

            CompleteBoundary(boundary, deferredRoots.WrittenSpan, templateRoots.WrittenSpan);
            return new PendingPropertyElementPlan(
                ownerElementId,
                syntax,
                property,
                operation,
                AddElementIds(children.WrittenSpan));
        }

        private ContentBoundary CreateImplicitBoundary(
            int ownerElementId,
            MarkupElementSyntax syntax,
            IMarkupContentOperation? operation)
        {
            if (operation?.Property is not { } property || !IsDeferredContentProperty(property))
            {
                return default;
            }

            return new ContentBoundary(
                _nextScopeId++,
                ownerElementId,
                syntax,
                property,
                operation,
                isDeferred: true,
                isTemplate: false);
        }

        private ContentBoundary CreatePropertyBoundary(
            int ownerElementId,
            MarkupElementSyntax syntax,
            AkburaPropertySymbol property,
            IMarkupContentOperation operation)
        {
            var isDeferred = IsDeferredContentProperty(property);
            var isTemplate = IsDataTemplateProperty(property);
            if (!isDeferred && !isTemplate)
            {
                return default;
            }

            return new ContentBoundary(
                _nextScopeId++,
                ownerElementId,
                syntax,
                property,
                operation,
                isDeferred,
                isTemplate);
        }

        private void CompleteBoundary(
            in ContentBoundary boundary,
            scoped ReadOnlySpan<int> deferredRoots,
            scoped ReadOnlySpan<int> templateRoots)
        {
            if (!boundary.IsValid)
            {
                return;
            }

            if (boundary.IsDeferred)
            {
                _deferredContents.Add(new ComponentDeferredContentPlan(
                    _deferredContents.Count,
                    boundary.ScopeId,
                    boundary.OwnerElementId,
                    boundary.Syntax,
                    boundary.Property,
                    boundary.Operation,
                    AddElementIds(deferredRoots)));
            }

            if (boundary.IsTemplate && !templateRoots.IsEmpty)
            {
                _templates.Add(new ComponentTemplatePlan(
                    _templates.Count,
                    boundary.ScopeId,
                    boundary.OwnerElementId,
                    boundary.Syntax,
                    AddElementIds(templateRoots)));
            }
        }

        private TraversalContext ResolveChildContext(
            MarkupElementSyntax syntax,
            MarkupChildContent? child,
            in TraversalContext inherited,
            in ContentBoundary boundary)
        {
            var template = boundary.IsTemplate && !IsDataTemplateElement(syntax)
                ? boundary.CreateTemplateScope()
                : inherited.Template;
            ScopeReference deferred;

            if (child is { IsDeferred: false })
            {
                deferred = default;
            }
            else if (boundary.IsDeferred)
            {
                deferred = boundary.CreateDeferredScope();
            }
            else
            {
                deferred = inherited.Deferred;
            }

            return new TraversalContext(template, deferred);
        }

        private bool IsDataTemplateElement(MarkupElementSyntax syntax)
        {
            if (_semanticModel.TryGetMarkupElementReferenceType(syntax, out var referenceType) &&
                referenceType.Symbol is ITypeSymbol resolvedType)
            {
                return IsDataTemplateType(resolvedType);
            }

            if (_semanticModel.GetSymbolInfo(syntax).Symbol is IMarkupComponentSymbol symbol &&
                (symbol.ComponentType ?? symbol.AkburaComponent?.ComponentType) is { } componentType)
            {
                return IsDataTemplateType(componentType);
            }

            return false;
        }

        private void TrackBoundaryRoot(
            in ContentBoundary boundary,
            int childId,
            scoped ImmutableArrayBuilder<int> deferredRoots,
            scoped ImmutableArrayBuilder<int> templateRoots)
        {
            var child = _elements[childId];
            if (boundary.IsDeferred &&
                child.ScopeOwnerId == boundary.ScopeId &&
                child.ScopeKind == ComponentElementScopeKind.DeferredContent)
            {
                deferredRoots.Add(childId);
            }

            if (boundary.IsTemplate &&
                child.ScopeOwnerId == boundary.ScopeId &&
                child.ScopeKind == ComponentElementScopeKind.DataTemplate)
            {
                templateRoots.Add(childId);
            }
        }

        private ComponentPlanRange AddElementIds(scoped ReadOnlySpan<int> elementIds)
        {
            var start = _childElementIds.Count;
            _childElementIds.AddRange(elementIds);
            return new ComponentPlanRange(start, elementIds.Length);
        }

        private void AddPropertyWrites(IMarkupComponentSymbol symbol)
        {
            var operations = symbol.AttributeOperations;
            for (var i = 0; i < operations.Length; i++)
            {
                if (operations[i] is not IMarkupPropertySetterOperation
                    {
                        HasErrors: false,
                        Property: { } property,
                    } operation)
                {
                    continue;
                }

                var destination = PropertyWritePlan.Create(property);
                if (!destination.IsValid)
                {
                    continue;
                }

                _propertyWrites.Add(new ComponentPropertyWritePlan(
                    destination,
                    GetPropertyValueKind(operation),
                    _propertyWrites.Count,
                    operation.Syntax,
                    IsFirstUpdateValue(operation)));
            }
        }

        private ComponentPropertyValueKind GetPropertyValueKind(IMarkupPropertySetterOperation operation)
        {
            if (operation.BindingKind != MarkupAttributeBindingKind.None)
            {
                return ComponentPropertyValueKind.Binding;
            }

            if (operation.ConvertedValue is MarkupExtensionValue extension)
            {
                if (extension.Binding != null)
                {
                    return ComponentPropertyValueKind.Binding;
                }

                return _resultEnvironment.GetResultKind(extension) switch
                {
                    MarkupExtensionResultKind.DynamicResource => ComponentPropertyValueKind.DynamicResource,
                    MarkupExtensionResultKind.StaticResource => ComponentPropertyValueKind.StaticResource,
                    MarkupExtensionResultKind.BindingBase => ComponentPropertyValueKind.BindingBaseResult,
                    MarkupExtensionResultKind.Runtime => ComponentPropertyValueKind.RuntimeMarkupExtensionResult,
                    _ => ComponentPropertyValueKind.MarkupExtensionValue,
                };
            }

            return operation.ValueKind switch
            {
                MarkupAttributeValueKind.Literal => ComponentPropertyValueKind.Constant,
                MarkupAttributeValueKind.DynamicExpression => ComponentPropertyValueKind.CSharpExpression,
                _ => ComponentPropertyValueKind.None,
            };
        }

        private ComponentElementFlags GetElementFlags(
            ITypeSymbol type,
            bool isRoot,
            bool hasName,
            in EffectiveScope scope)
        {
            var flags = ComponentElementFlags.None;
            if (isRoot)
            {
                flags |= ComponentElementFlags.IsRoot;
            }

            if (scope.IsDeferred)
            {
                flags |= ComponentElementFlags.IsDeferred;
            }

            if (scope.Kind == ComponentElementScopeKind.DataTemplate || IsDataTemplateType(type))
            {
                flags |= ComponentElementFlags.IsTemplateElement;
            }

            if (IsImplicitConversion(type, _controlType))
            {
                flags |= ComponentElementFlags.IsControl;
            }

            if (IsImplicitConversion(type, _initializeType))
            {
                flags |= ComponentElementFlags.SupportsInitialize;
            }

            if (hasName)
            {
                flags |= ComponentElementFlags.HasName;
            }

            if (scope.IsLocal)
            {
                flags |= ComponentElementFlags.RequiresLocalMarkupContext;
            }

            return flags;
        }

        private bool IsImplicitConversion(ITypeSymbol type, ITypeSymbol? targetType)
        {
            return targetType != null && AkburaSemanticModel.IsAssignableTo(type, targetType);
        }

        private bool IsDeferredContentProperty(AkburaPropertySymbol property)
        {
            var clrProperty = GetClrProperty(property);
            return clrProperty != null &&
                _semanticModel.BindingSession.MarkupTemplateContent.IsDeferredContentProperty(clrProperty);
        }

        private bool IsDataTemplateProperty(AkburaPropertySymbol property)
        {
            return property.Type.Symbol is ITypeSymbol propertyType && IsDataTemplateType(propertyType);
        }

        private bool IsDataTemplateType(ITypeSymbol type)
        {
            return _semanticModel.BindingSession.MarkupTemplateContent.IsDataTemplateType(type);
        }

        private static RoslynPropertySymbol? GetClrProperty(AkburaPropertySymbol property)
        {
            return property.ClrPropertyDefinition.Symbol as RoslynPropertySymbol ??
                property.WriteDefinition.Symbol as RoslynPropertySymbol ??
                property.ReadDefinition.Symbol as RoslynPropertySymbol;
        }

        private static IMarkupNameAssignmentOperation? FindNameOperation(IMarkupComponentSymbol symbol)
        {
            return symbol.AttributeOperations
                .OfType<IMarkupNameAssignmentOperation>()
                .FirstOrDefault(static operation => !operation.HasErrors && operation.NameSymbol != null);
        }

        private static MarkupChildContent? FindMarkupChild(
            IMarkupContentOperation? operation,
            MarkupElementContentSyntax syntax)
        {
            if (operation == null)
            {
                return null;
            }

            foreach (var child in operation.Content)
            {
                if (ReferenceEquals(child.Syntax, syntax) || child.Syntax.FullSpan.Equals(syntax.FullSpan))
                {
                    return child;
                }
            }

            return null;
        }

        private static bool IsFirstUpdateValue(IMarkupPropertySetterOperation operation)
        {
            return operation.ValueKind is MarkupAttributeValueKind.Literal or MarkupAttributeValueKind.MarkupExtension;
        }

        private static string EscapeIdentifier(string identifier)
        {
            return identifier.IdentifierRequiresEscaping() ? "@" + identifier : identifier;
        }

    }

    private readonly struct PendingElementPlan
    {
        public PendingElementPlan(
            int id,
            MarkupElementSyntax syntax,
            IMarkupComponentSymbol symbol,
            ITypeSymbol type,
            string identifier,
            int parentId,
            int scopeOwnerId,
            ComponentElementScopeKind scopeKind,
            ComponentElementFlags flags,
            ComponentPlanRange children,
            ComponentPlanRange propertyWrites,
            ComponentPlanRange propertyElements)
        {
            Id = id;
            Syntax = syntax;
            Symbol = symbol;
            Type = type;
            Identifier = identifier;
            ParentId = parentId;
            ScopeOwnerId = scopeOwnerId;
            ScopeKind = scopeKind;
            Flags = flags;
            Children = children;
            PropertyWrites = propertyWrites;
            PropertyElements = propertyElements;
        }

        public int Id { get; }
        public MarkupElementSyntax Syntax { get; }
        public IMarkupComponentSymbol Symbol { get; }
        public ITypeSymbol Type { get; }
        public string Identifier { get; }
        public int ParentId { get; }
        public int ScopeOwnerId { get; }
        public ComponentElementScopeKind ScopeKind { get; }
        public ComponentElementFlags Flags { get; }
        public ComponentPlanRange Children { get; }
        public ComponentPlanRange PropertyWrites { get; }
        public ComponentPlanRange PropertyElements { get; }
        public bool RequiresLocalMarkupContext =>
            (Flags & ComponentElementFlags.RequiresLocalMarkupContext) != 0;
    }

    private readonly struct PendingPropertyElementPlan
    {
        public PendingPropertyElementPlan(
            int ownerElementId,
            MarkupElementSyntax syntax,
            AkburaPropertySymbol property,
            IMarkupContentOperation operation,
            ComponentPlanRange children)
        {
            OwnerElementId = ownerElementId;
            Syntax = syntax;
            Property = property;
            Operation = operation;
            Children = children;
        }

        public int OwnerElementId { get; }
        public MarkupElementSyntax Syntax { get; }
        public AkburaPropertySymbol Property { get; }
        public IMarkupContentOperation Operation { get; }
        public ComponentPlanRange Children { get; }
    }

    private readonly struct ContentBoundary
    {
        public ContentBoundary(
            int scopeId,
            int ownerElementId,
            MarkupElementSyntax syntax,
            AkburaPropertySymbol property,
            IMarkupContentOperation operation,
            bool isDeferred,
            bool isTemplate)
        {
            ScopeId = scopeId;
            OwnerElementId = ownerElementId;
            Syntax = syntax;
            Property = property;
            Operation = operation;
            IsDeferred = isDeferred;
            IsTemplate = isTemplate;
        }

        public int ScopeId { get; }
        public int OwnerElementId { get; }
        public MarkupElementSyntax Syntax { get; }
        public AkburaPropertySymbol Property { get; }
        public IMarkupContentOperation Operation { get; }
        public bool IsDeferred { get; }
        public bool IsTemplate { get; }
        public bool IsValid => ScopeId > 0;

        public ScopeReference CreateDeferredScope()
        {
            return new ScopeReference(
                ScopeId,
                OwnerElementId,
                ComponentElementScopeKind.DeferredContent,
                isDeferred: true);
        }

        public ScopeReference CreateTemplateScope()
        {
            return new ScopeReference(
                ScopeId,
                OwnerElementId,
                ComponentElementScopeKind.DataTemplate,
                isDeferred: false);
        }
    }

    private readonly struct ScopeReference
    {
        public ScopeReference(
            int scopeId,
            int ownerElementId,
            ComponentElementScopeKind kind,
            bool isDeferred)
        {
            ScopeId = scopeId;
            OwnerElementId = ownerElementId;
            Kind = kind;
            IsDeferred = isDeferred;
        }

        public int ScopeId { get; }
        public int OwnerElementId { get; }
        public ComponentElementScopeKind Kind { get; }
        public bool IsDeferred { get; }
        public bool IsValid => ScopeId > 0;
    }

    private readonly struct TraversalContext
    {
        public TraversalContext(ScopeReference template, ScopeReference deferred)
        {
            Template = template;
            Deferred = deferred;
        }

        public ScopeReference Template { get; }
        public ScopeReference Deferred { get; }

        public EffectiveScope GetEffectiveScope()
        {
            if (Deferred.IsValid && (!Template.IsValid || Deferred.ScopeId >= Template.ScopeId))
            {
                return new EffectiveScope(
                    Deferred.ScopeId,
                    Deferred.OwnerElementId,
                    ComponentElementScopeKind.DeferredContent,
                    isDeferred: true);
            }

            return Template.IsValid
                ? new EffectiveScope(Template.ScopeId, Template.OwnerElementId, Template.Kind, isDeferred: false)
                : default;
        }
    }

    private readonly struct EffectiveScope
    {
        public EffectiveScope(
            int scopeId,
            int ownerElementId,
            ComponentElementScopeKind kind,
            bool isDeferred)
        {
            ScopeId = scopeId;
            OwnerElementId = ownerElementId;
            Kind = kind;
            IsDeferred = isDeferred;
        }

        public int ScopeId { get; }
        public int OwnerElementId { get; }
        public ComponentElementScopeKind Kind { get; }
        public bool IsDeferred { get; }
        public bool IsLocal => ScopeId > 0;
    }
}
