using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
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
        private readonly BindingWriterEnvironment _bindingEnvironment;
        private readonly MarkupExtensionResultEnvironment _resultEnvironment;
        private readonly INamedTypeSymbol? _controlType;
        private readonly INamedTypeSymbol? _contentPresenterType;
        private readonly IFieldSymbol? _dataContextProperty;
        private readonly INamedTypeSymbol? _initializeType;
        private readonly ArrayBuilder<PendingElementPlan> _elements;
        private readonly ArrayBuilder<PendingScopePlan> _pendingScopes;
        private readonly ArrayBuilder<ComponentPropertyElementPlan> _propertyElements;
        private readonly ArrayBuilder<ComponentDeferredContentPlan> _deferredContents;
        private readonly ArrayBuilder<ComponentTemplatePlan> _templates;
        private readonly ArrayBuilder<PendingPropertyWritePlan> _pendingPropertyWrites;
        private readonly ArrayBuilder<PendingFirstUpdateActionPlan> _pendingFirstUpdateActions;
        private readonly ArrayBuilder<PendingContentPlan> _pendingContents;
        private ImmutableArrayBuilder<int> _rootElementIds;
        private ImmutableArrayBuilder<int> _childElementIds;
        private ImmutableArrayBuilder<ComponentScopePlan> _scopes;
        private ImmutableArrayBuilder<int> _scopeElementIds;
        private ImmutableArrayBuilder<int> _scopeRootElementIds;
        private ImmutableArrayBuilder<ComponentPropertyWritePlan> _propertyWrites;
        private ImmutableArrayBuilder<ComponentCSharpValuePlan> _csharpValues;
        private ImmutableArrayBuilder<MarkupExtensionResultPlan> _markupExtensions;
        private ImmutableArrayBuilder<BindingWritePlan> _bindings;
        private ImmutableArrayBuilder<ComponentPropertySubscriptionPlan> _propertySubscriptions;
        private ImmutableArrayBuilder<ComponentNameAssignmentPlan> _nameAssignments;
        private ImmutableArrayBuilder<ComponentRoutedEventPlan> _routedEvents;
        private ImmutableArrayBuilder<ComponentCommandBindingPlan> _commandBindings;
        private ImmutableArrayBuilder<ComponentFirstUpdateActionPlan> _firstUpdateActions;
        private ImmutableArrayBuilder<ComponentPropertyContentPlan> _propertyContents;
        private ImmutableArrayBuilder<ComponentCollectionContentPlan> _collectionContents;
        private ImmutableArrayBuilder<ComponentContentItemPlan> _contentItems;
        private ImmutableArrayBuilder<BindingElementReference> _elementReferences;
        private ImmutableArrayBuilder<ComponentRenderStatementPlan> _renderStatements;
        private int _nextCachedBindingPathId;

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
            _bindingEnvironment = BindingWriterEnvironment.Create(semanticModel, component);
            _resultEnvironment = resultEnvironment;
            _controlType = _compilation.GetTypeByMetadataName("Avalonia.Controls.Control");
            _contentPresenterType = _compilation.GetTypeByMetadataName(
                "Avalonia.Controls.Presenters.ContentPresenter");
            _dataContextProperty = GetStaticField(
                _compilation.GetTypeByMetadataName("Avalonia.StyledElement"),
                "DataContextProperty");
            _initializeType = _compilation.GetTypeByMetadataName("System.ComponentModel.ISupportInitialize");
            _elements = ArrayBuilder<PendingElementPlan>.GetInstance();
            _pendingScopes = ArrayBuilder<PendingScopePlan>.GetInstance();
            _pendingScopes.Add(new PendingScopePlan(
                id: 0,
                parentScopeId: -1,
                ownerElementId: -1,
                ComponentElementScopeKind.Component));
            _propertyElements = ArrayBuilder<ComponentPropertyElementPlan>.GetInstance();
            _deferredContents = ArrayBuilder<ComponentDeferredContentPlan>.GetInstance();
            _templates = ArrayBuilder<ComponentTemplatePlan>.GetInstance();
            _pendingPropertyWrites = ArrayBuilder<PendingPropertyWritePlan>.GetInstance();
            _pendingFirstUpdateActions = ArrayBuilder<PendingFirstUpdateActionPlan>.GetInstance();
            _pendingContents = ArrayBuilder<PendingContentPlan>.GetInstance();
            _rootElementIds = ImmutableArrayBuilder<int>.Rent();
            _childElementIds = ImmutableArrayBuilder<int>.Rent();
            _scopes = ImmutableArrayBuilder<ComponentScopePlan>.Rent();
            _scopeElementIds = ImmutableArrayBuilder<int>.Rent();
            _scopeRootElementIds = ImmutableArrayBuilder<int>.Rent();
            _propertyWrites = ImmutableArrayBuilder<ComponentPropertyWritePlan>.Rent();
            _csharpValues = ImmutableArrayBuilder<ComponentCSharpValuePlan>.Rent();
            _markupExtensions = ImmutableArrayBuilder<MarkupExtensionResultPlan>.Rent();
            _bindings = ImmutableArrayBuilder<BindingWritePlan>.Rent();
            _propertySubscriptions = ImmutableArrayBuilder<ComponentPropertySubscriptionPlan>.Rent();
            _nameAssignments = ImmutableArrayBuilder<ComponentNameAssignmentPlan>.Rent();
            _routedEvents = ImmutableArrayBuilder<ComponentRoutedEventPlan>.Rent();
            _commandBindings = ImmutableArrayBuilder<ComponentCommandBindingPlan>.Rent();
            _firstUpdateActions = ImmutableArrayBuilder<ComponentFirstUpdateActionPlan>.Rent();
            _propertyContents = ImmutableArrayBuilder<ComponentPropertyContentPlan>.Rent();
            _collectionContents = ImmutableArrayBuilder<ComponentCollectionContentPlan>.Rent();
            _contentItems = ImmutableArrayBuilder<ComponentContentItemPlan>.Rent();
            _elementReferences = ImmutableArrayBuilder<BindingElementReference>.Rent();
            _renderStatements = ImmutableArrayBuilder<ComponentRenderStatementPlan>.Rent();
            _nextCachedBindingPathId = 0;
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

            LowerFirstUpdateActions();
            LowerContent();
            LowerRenderStatements();
            BuildScopeIndex();

            var owner = new ComponentPlanBufferOwner(CreateAkcssPlan());

            try
            {
                var lifecycle = CreateLifecyclePlan(owner.Akcss);

                CapturePlanBuffers(ref owner);

                return owner.MoveToPlan(lifecycle);
            }
            finally
            {
                owner.Dispose();
            }
        }

        private AkcssComponentActivatorPlan CreateAkcssPlan()
        {
            using var inputs = ImmutableArrayBuilder<AkcssActivatorElementInput>.Rent(_elements.Count);

            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];

                inputs.Add(new AkcssActivatorElementInput(
                    element.Id,
                    element.Symbol,
                    element.Type,
                    element.RequiresLocalMarkupContext));
            }

            return AkcssActivatorPlanner.Create(
                _semanticModel,
                inputs.WrittenSpan,
                _akcssModuleTypeNames);
        }

        private void CapturePlanBuffers(ref ComponentPlanBufferOwner owner)
        {
            owner.Elements = CreateElementPlans(owner.Akcss);

            owner.RootElementIds = _rootElementIds.ToPooledImmutableList();
            owner.ChildElementIds = _childElementIds.ToPooledImmutableList();
            owner.Scopes = _scopes.ToPooledImmutableList();
            owner.ScopeElementIds = _scopeElementIds.ToPooledImmutableList();
            owner.ScopeRootElementIds = _scopeRootElementIds.ToPooledImmutableList();

            owner.PropertyWrites = _propertyWrites.ToPooledImmutableList();
            owner.CSharpValues = _csharpValues.ToPooledImmutableList();
            owner.MarkupExtensions = _markupExtensions.ToPooledImmutableList();
            owner.Bindings = _bindings.ToPooledImmutableList();
            owner.PropertySubscriptions = _propertySubscriptions.ToPooledImmutableList();

            owner.NameAssignments = _nameAssignments.ToPooledImmutableList();
            owner.RoutedEvents = _routedEvents.ToPooledImmutableList();
            owner.CommandBindings = _commandBindings.ToPooledImmutableList();
            owner.FirstUpdateActions = _firstUpdateActions.ToPooledImmutableList();

            owner.PropertyElements = _propertyElements.ToPooledImmutableList();
            owner.PropertyContents = _propertyContents.ToPooledImmutableList();
            owner.CollectionContents = _collectionContents.ToPooledImmutableList();
            owner.ContentItems = _contentItems.ToPooledImmutableList();
            owner.DeferredContents = _deferredContents.ToPooledImmutableList();
            owner.Templates = _templates.ToPooledImmutableList();

            owner.ElementReferences = _elementReferences.ToPooledImmutableList();
            owner.RenderStatements = _renderStatements.ToPooledImmutableList();
        }

        private PooledImmutableList<ComponentElementPlan> CreateElementPlans(in AkcssComponentActivatorPlan akcss)
        {
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
                    element.ScopeId,
                    element.ScopeKind,
                    element.Flags,
                    element.Children,
                    element.PropertyWrites,
                    element.PropertySubscriptions,
                    element.FirstUpdateActions,
                    element.PropertyElements,
                    element.Content,
                    elementAkcss));
            }

            return elements.ToPooledImmutableList();
        }

        public void Dispose()
        {
            _elements.Free();
            _pendingScopes.Free();
            _propertyElements.Free();
            _deferredContents.Free();
            _templates.Free();
            _pendingPropertyWrites.Free();
            _pendingFirstUpdateActions.Free();
            _pendingContents.Free();
            _rootElementIds.Dispose();
            _childElementIds.Dispose();
            _scopes.Dispose();
            _scopeElementIds.Dispose();
            _scopeRootElementIds.Dispose();
            _propertyWrites.Dispose();
            _csharpValues.Dispose();
            _markupExtensions.Dispose();
            _bindings.Dispose();
            _propertySubscriptions.Dispose();
            _nameAssignments.Dispose();
            _routedEvents.Dispose();
            _commandBindings.Dispose();
            _firstUpdateActions.Dispose();
            _propertyContents.Dispose();
            _collectionContents.Dispose();
            _contentItems.Dispose();
            _elementReferences.Dispose();
            _renderStatements.Dispose();
        }

        private ComponentLifecyclePlan CreateLifecyclePlan(in AkcssComponentActivatorPlan akcss)
        {
            var rootElementId = -1;
            var flags = ComponentLifecycleFlags.None;

            if (_rootElementIds.Count == 1)
            {
                var candidateId = _rootElementIds.WrittenSpan[0];

                if ((uint)candidateId < (uint)_elements.Count)
                {
                    var candidate = _elements[candidateId];
                    if (candidate.ScopeId == 0 &&
                        (candidate.Flags & ComponentElementFlags.IsControl) != 0)
                    {
                        rootElementId = candidateId;

                        if (HasExplicitDataContextSetter(candidate))
                        {
                            flags |= ComponentLifecycleFlags.HasExplicitRootDataContext;
                        }
                    }
                }
            }

            if (rootElementId < 0)
            {
                flags |= ComponentLifecycleFlags.UsesFallbackRoot;
            }

            if (RequiresBaseUri(akcss))
            {
                flags |= ComponentLifecycleFlags.RequiresBaseUri;
            }

            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                if (element.ScopeId == 0 &&
                    (element.Flags & ComponentElementFlags.RequiresContentPresenterRefresh) != 0)
                {
                    flags |= ComponentLifecycleFlags.HasComponentContentPresenters;
                    break;
                }
            }

            return new ComponentLifecyclePlan(rootElementId, flags);
        }

        private bool HasExplicitDataContextSetter(
            in PendingElementPlan element)
        {
            var attributes = element.Symbol.AttributeOperations;

            for (var i = 0; i < attributes.Length; i++)
            {
                if (attributes[i] is IMarkupPropertySetterOperation operation &&
                    !operation.HasErrors &&
                    IsDataContextProperty(operation.Property))
                {
                    return true;
                }
            }

            var propertyElements = element.PropertyElements;
            for (var i = 0; i < propertyElements.Length; i++)
            {
                var propertyElement = _propertyElements[
                    propertyElements.Start + i];
                if (_semanticModel.GetOperation(propertyElement.Syntax) is
                    IMarkupContentOperation operation &&
                    !operation.HasErrors &&
                    IsDataContextProperty(operation.Property))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDataContextProperty(AkburaPropertySymbol? property)
        {
            return _dataContextProperty != null &&
                SymbolEqualityComparer.Default.Equals(
                    property?.AvaloniaPropertyDefinition.Symbol,
                    _dataContextProperty);
        }

        private static IFieldSymbol? GetStaticField(
            INamedTypeSymbol? type,
            string name)
        {
            if (type == null)
            {
                return null;
            }

            var members = type.GetMembers(name);
            for (var i = 0; i < members.Length; i++)
            {
                if (members[i] is IFieldSymbol { IsStatic: true } field)
                {
                    return field;
                }
            }

            return null;
        }

        private bool RequiresBaseUri(in AkcssComponentActivatorPlan akcss)
        {
            if (_deferredContents.Count != 0)
            {
                return true;
            }

            var extensions = _markupExtensions.WrittenSpan;
            for (var i = 0; i < extensions.Length; i++)
            {
                if (RequiresMarkupServiceProvider(extensions[i].Extension))
                {
                    return true;
                }
            }

            var bindings = _bindings.WrittenSpan;
            for (var i = 0; i < bindings.Length; i++)
            {
                if (NestedValuesRequireMarkupServiceProvider(bindings[i].Extension))
                {
                    return true;
                }
            }

            var slots = akcss.MarkupExtensionSlots;
            for (var i = 0; i < slots.Length; i++)
            {
                if (RequiresMarkupServiceProvider(slots[i].Extension))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool RequiresMarkupServiceProvider(
            MarkupExtensionValue extension)
        {
            if (extension.Binding == null &&
                extension.ProvideValueMethod.Symbol is
                    IMethodSymbol { Parameters.Length: 1 })
            {
                return true;
            }

            return NestedValuesRequireMarkupServiceProvider(extension);
        }

        private static bool NestedValuesRequireMarkupServiceProvider(
            MarkupExtensionValue extension)
        {
            var arguments = extension.Arguments;
            for (var i = 0; i < arguments.Length; i++)
            {
                if (arguments[i].NestedValue is { } nested &&
                    RequiresMarkupServiceProvider(nested))
                {
                    return true;
                }
            }

            var properties = extension.Properties;
            for (var i = 0; i < properties.Length; i++)
            {
                if (properties[i].NestedValue is { } nested &&
                    RequiresMarkupServiceProvider(nested))
                {
                    return true;
                }
            }

            return false;
        }

        private void LowerRenderStatements()
        {
            var members = _component.DeclarationSyntax.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] is not CSharpStatementSyntax syntax)
                {
                    continue;
                }

                var statement = syntax.GetRawCSharpStatement();
                if (statement == null ||
                    statement is CSharp.LocalFunctionStatementSyntax)
                {
                    continue;
                }

                if (syntax.Body != null)
                {
                    statement = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseStatement(
                        syntax.ToFullString());
                }

                if (statement.ContainsDiagnostics)
                {
                    continue;
                }

                if (_semanticModel.GetOperation(syntax) is IUseHookOperation hook)
                {
                    if (!hook.HasErrors)
                    {
                        _renderStatements.Add(new ComponentRenderStatementPlan(
                            ComponentRenderStatementKind.UseHookInvocation,
                            hook.EffectiveInvocation,
                            syntax,
                            ComponentRenderStatementPhase.Update));
                    }

                    continue;
                }

                if (HasSemanticErrors(syntax))
                {
                    continue;
                }

                _renderStatements.Add(new ComponentRenderStatementPlan(
                    ComponentRenderStatementKind.Statement,
                    statement,
                    syntax,
                    statement is CSharp.LocalDeclarationStatementSyntax
                        ? ComponentRenderStatementPhase.Both
                        : ComponentRenderStatementPhase.Update));
            }
        }

        private bool HasSemanticErrors(AkburaSyntax syntax)
        {
            var diagnostics = _semanticModel.GetSemanticDiagnostics(syntax);

            for (var i = 0; i < diagnostics.Length; i++)
            {
                if (diagnostics[i].Severity == AkburaDiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
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
                ? EscapeIdentifier(name.IdentifierText)
                : "__element" + elementId.ToString(CultureInfo.InvariantCulture);

            _elements.Add(default);

            var pendingFirstUpdateActionStart = _pendingFirstUpdateActions.Count;
            AddPendingTemplateDataType(elementId, syntax, type);
            AddPendingFirstUpdateActions(elementId, scope.ScopeId, type, symbol);
            var pendingFirstUpdateActions = new ComponentPlanRange(
                pendingFirstUpdateActionStart,
                _pendingFirstUpdateActions.Count - pendingFirstUpdateActionStart);

            if (nameOperation?.NameSymbol is { } nameSymbol)
            {
                _elementReferences.Add(new BindingElementReference(
                    nameSymbol.Name,
                    identifier,
                    scope.ScopeId,
                    isClassMember: !scope.IsLocal));
            }

            var contentOperation = _semanticModel.GetOperation(syntax) as IMarkupContentOperation;
            var implicitBoundary = CreateImplicitBoundary(
                elementId,
                syntax,
                contentOperation,
                scope.ScopeId);
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

            var boundaryValue = CompleteBoundary(
                implicitBoundary,
                deferredRoots.WrittenSpan,
                templateRoots.WrittenSpan);

            var children = AddElementIds(directChildren.WrittenSpan);
            if (contentOperation != null)
            {
                _pendingContents.Add(new PendingContentPlan(
                    elementId,
                    contentOperation,
                    children,
                    propertyElementId: -1,
                    boundaryValue));
            }

            var propertyElementStart = _propertyElements.Count;
            for (var i = 0; i < directPropertyElements.Count; i++)
            {
                var propertyElement = directPropertyElements.WrittenSpan[i];
                var propertyElementId = _propertyElements.Count;
                _propertyElements.Add(new ComponentPropertyElementPlan(
                    propertyElementId,
                    propertyElement.OwnerElementId,
                    propertyElement.Syntax,
                    content: default));
                _pendingContents.Add(new PendingContentPlan(
                    propertyElement.OwnerElementId,
                    propertyElement.Operation,
                    propertyElement.Children,
                    propertyElementId,
                    propertyElement.BoundaryValue));
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
                pendingFirstUpdateActions,
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
            var boundary = CreatePropertyBoundary(
                ownerElementId,
                syntax,
                property,
                operation,
                inheritedContext.GetEffectiveScope().ScopeId);
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

            var boundaryValue = CompleteBoundary(
                boundary,
                deferredRoots.WrittenSpan,
                templateRoots.WrittenSpan);
            return new PendingPropertyElementPlan(
                ownerElementId,
                syntax,
                property,
                operation,
                AddElementIds(children.WrittenSpan),
                boundaryValue);
        }

        private ContentBoundary CreateImplicitBoundary(
            int ownerElementId,
            MarkupElementSyntax syntax,
            IMarkupContentOperation? operation,
            int parentScopeId)
        {
            if (operation?.Property is not { } property || !IsDeferredContentProperty(property))
            {
                return default;
            }

            return new ContentBoundary(
                AddPendingScope(
                    parentScopeId,
                    ownerElementId,
                    ComponentElementScopeKind.DeferredContent),
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
            IMarkupContentOperation operation,
            int parentScopeId)
        {
            var isDeferred = IsDeferredContentProperty(property);
            var isTemplate = IsDataTemplateProperty(property);
            if (!isDeferred && !isTemplate)
            {
                return default;
            }

            return new ContentBoundary(
                AddPendingScope(
                    parentScopeId,
                    ownerElementId,
                    isDeferred
                        ? ComponentElementScopeKind.DeferredContent
                        : ComponentElementScopeKind.DataTemplate),
                ownerElementId,
                syntax,
                property,
                operation,
                isDeferred,
                isTemplate);
        }

        private ComponentContentValueReference CompleteBoundary(
            in ContentBoundary boundary,
            scoped ReadOnlySpan<int> deferredRoots,
            scoped ReadOnlySpan<int> templateRoots)
        {
            if (!boundary.IsValid || boundary.Operation.HasErrors)
            {
                return default;
            }

            if (boundary.IsDeferred)
            {
                if (deferredRoots.Length != 1)
                {
                    return default;
                }

                var id = _deferredContents.Count;
                _deferredContents.Add(new ComponentDeferredContentPlan(
                    id,
                    boundary.ScopeId,
                    boundary.OwnerElementId,
                    GetDeferredResultType(boundary.Property),
                    boundary.Syntax));
                return new ComponentContentValueReference(
                    ComponentContentValueKind.DeferredContent,
                    id);
            }

            if (boundary.IsTemplate)
            {
                return CompleteTemplateBoundary(boundary, templateRoots);
            }

            return default;
        }

        private ComponentContentValueReference CompleteTemplateBoundary(
            in ContentBoundary boundary,
            scoped ReadOnlySpan<int> roots)
        {
            Debug.Assert(boundary.IsTemplate);

            if (roots.Length != 1)
            {
                return default;
            }

            var root = _elements[roots[0]];
            if (root.ScopeId != boundary.ScopeId ||
                (root.Flags & ComponentElementFlags.IsControl) == 0)
            {
                return default;
            }

            var dataType =
                (ITypeSymbol)_compilation.GetSpecialType(
                    SpecialType.System_Object);
            var itemName = "__item";
            var dataTypes =
                _semanticModel.BindingSession.MarkupDataTypes;

            if (dataTypes.TryGetTemplateContract(
                    boundary.Syntax,
                    out var resolvedDataType,
                    out var resolvedItemName))
            {
                dataType = resolvedDataType;
                if (!string.IsNullOrEmpty(resolvedItemName))
                {
                    itemName = resolvedItemName!;
                }
            }

            var id = _templates.Count;
            _templates.Add(new ComponentTemplatePlan(
                id,
                boundary.ScopeId,
                boundary.OwnerElementId,
                dataType,
                itemName,
                boundary.Syntax));
            return new ComponentContentValueReference(
                ComponentContentValueKind.Template,
                id);
        }

        private int AddPendingScope(
            int parentScopeId,
            int ownerElementId,
            ComponentElementScopeKind kind)
        {
            Debug.Assert((uint)parentScopeId < (uint)_pendingScopes.Count);
            Debug.Assert(ownerElementId >= 0);
            Debug.Assert(kind != ComponentElementScopeKind.Component);

            var id = _pendingScopes.Count;
            _pendingScopes.Add(new PendingScopePlan(
                id,
                parentScopeId,
                ownerElementId,
                kind));
            return id;
        }

        private void BuildScopeIndex()
        {
            var scopeCount = _pendingScopes.Count;
            var elementCount = _elements.Count;
            var counts = ArrayPool<int>.Shared.Rent(scopeCount);
            var offsets = ArrayPool<int>.Shared.Rent(scopeCount);
            var cursors = ArrayPool<int>.Shared.Rent(scopeCount);
            var elementIds = ArrayPool<int>.Shared.Rent(Math.Max(elementCount, 1));

            try
            {
                Array.Clear(counts, 0, scopeCount);

                for (var i = 0; i < elementCount; i++)
                {
                    var scopeId = _elements[i].ScopeId;
                    Debug.Assert((uint)scopeId < (uint)scopeCount);
                    counts[scopeId]++;
                }

                var offset = 0;
                for (var i = 0; i < scopeCount; i++)
                {
                    offsets[i] = offset;
                    cursors[i] = offset;
                    offset += counts[i];
                }

                for (var i = 0; i < elementCount; i++)
                {
                    var scopeId = _elements[i].ScopeId;
                    elementIds[cursors[scopeId]++] = i;
                }

                _scopeElementIds.AddRange(elementIds.AsSpan(0, elementCount));

                for (var i = 0; i < scopeCount; i++)
                {
                    _pendingScopes[i] = _pendingScopes[i].WithElements(
                        new ComponentPlanRange(offsets[i], counts[i]));
                }

                Array.Clear(counts, 0, scopeCount);
                var rootCount = 0;

                for (var i = 0; i < elementCount; i++)
                {
                    var element = _elements[i];
                    if (element.ParentId >= 0 &&
                        _elements[element.ParentId].ScopeId == element.ScopeId)
                    {
                        continue;
                    }

                    counts[element.ScopeId]++;
                    rootCount++;
                }

                offset = 0;
                for (var i = 0; i < scopeCount; i++)
                {
                    offsets[i] = offset;
                    cursors[i] = offset;
                    offset += counts[i];
                }

                for (var i = 0; i < elementCount; i++)
                {
                    var element = _elements[i];
                    if (element.ParentId >= 0 &&
                        _elements[element.ParentId].ScopeId == element.ScopeId)
                    {
                        continue;
                    }

                    elementIds[cursors[element.ScopeId]++] = i;
                }

                _scopeRootElementIds.AddRange(elementIds.AsSpan(0, rootCount));

                for (var i = 0; i < scopeCount; i++)
                {
                    var pending = _pendingScopes[i];
                    Debug.Assert(pending.Id == i);
                    _scopes.Add(new ComponentScopePlan(
                        pending.Id,
                        pending.ParentScopeId,
                        pending.OwnerElementId,
                        pending.Kind,
                        pending.Elements,
                        new ComponentPlanRange(offsets[i], counts[i]),
                        pending.Flags));
                }
            }
            finally
            {
                ArrayPool<int>.Shared.Return(counts);
                ArrayPool<int>.Shared.Return(offsets);
                ArrayPool<int>.Shared.Return(cursors);
                ArrayPool<int>.Shared.Return(elementIds);
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
                child.ScopeId == boundary.ScopeId &&
                child.ScopeKind == ComponentElementScopeKind.DeferredContent)
            {
                deferredRoots.Add(childId);
            }

            if (boundary.IsTemplate &&
                child.ScopeId == boundary.ScopeId &&
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

        private void AddPendingTemplateDataType(
            int elementId,
            MarkupElementSyntax syntax,
            ITypeSymbol elementType)
        {
            if (elementType is not INamedTypeSymbol namedType)
            {
                return;
            }

            var templates =
                _semanticModel.BindingSession.MarkupTemplateContent;
            if (!templates.IsDataTemplateType(namedType) ||
                templates.FindDataTypeProperty(namedType) is not { } property ||
                !TryGetTemplateDataType(syntax, out var dataType))
            {
                return;
            }

            _pendingFirstUpdateActions.Add(
                PendingFirstUpdateActionPlan.CreateTemplateDataType(
                    new PendingTemplateDataTypePlan(
                        elementId,
                        property,
                        dataType,
                        syntax)));
        }

        private bool TryGetTemplateDataType(
            MarkupElementSyntax syntax,
            out INamedTypeSymbol dataType)
        {
            var dataTypes =
                _semanticModel.BindingSession.MarkupDataTypes;
            if (dataTypes.TryGetDataType(syntax, out dataType))
            {
                return true;
            }

            for (var ancestor = syntax.Parent;
                 ancestor != null;
                 ancestor = ancestor.Parent)
            {
                if (ancestor is not MarkupElementSyntax propertyElement ||
                    _semanticModel.GetSymbolInfo(propertyElement).Symbol
                        is not AkburaPropertySymbol property ||
                    !IsDataTemplateProperty(property))
                {
                    continue;
                }

                return dataTypes.TryGetDataType(
                    propertyElement,
                    out dataType);
            }

            dataType = null!;
            return false;
        }

        private void AddPendingFirstUpdateActions(
            int elementId,
            int scopeId,
            ITypeSymbol targetType,
            IMarkupComponentSymbol symbol)
        {
            var operations = symbol.AttributeOperations;
            for (var i = 0; i < operations.Length; i++)
            {
                var operation = operations[i];
                if (operation.HasErrors)
                {
                    continue;
                }

                switch (operation)
                {
                    case IMarkupPropertySetterOperation { Property: { } property } propertyOperation:
                    {
                        var index = _pendingPropertyWrites.Count;
                        _pendingPropertyWrites.Add(new PendingPropertyWritePlan(
                            elementId,
                            scopeId,
                            i,
                            PropertyWritePlan.Create(property, targetType),
                            propertyOperation));
                        _pendingFirstUpdateActions.Add(PendingFirstUpdateActionPlan.CreateProperty(index));
                        break;
                    }
                    case IMarkupNameAssignmentOperation { NameSymbol: not null } name:
                        _pendingFirstUpdateActions.Add(PendingFirstUpdateActionPlan.CreateNameAssignment(name));
                        break;
                    case IMarkupRoutedEventBindingOperation routedEvent:
                        _pendingFirstUpdateActions.Add(PendingFirstUpdateActionPlan.CreateRoutedEvent(routedEvent));
                        break;
                    case IMarkupCommandBindingOperation command:
                        _pendingFirstUpdateActions.Add(PendingFirstUpdateActionPlan.CreateCommandBinding(command));
                        break;
                }
            }
        }

        private void LowerFirstUpdateActions()
        {
            for (var i = 0; i < _elements.Count; i++)
            {
                var element = _elements[i];
                var writeStart = _propertyWrites.Count;
                var subscriptionStart = _propertySubscriptions.Count;
                var actionStart = _firstUpdateActions.Count;
                var pending = element.PendingFirstUpdateActions;

                for (var j = 0; j < pending.Length; j++)
                {
                    LowerFirstUpdateAction(_pendingFirstUpdateActions[pending.Start + j], element.Type);
                }

                _elements[i] = element.WithPropertyPlans(
                    new ComponentPlanRange(writeStart, _propertyWrites.Count - writeStart),
                    new ComponentPlanRange(
                        subscriptionStart,
                        _propertySubscriptions.Count - subscriptionStart),
                    new ComponentPlanRange(actionStart, _firstUpdateActions.Count - actionStart));
            }
        }

        private void LowerFirstUpdateAction(
            in PendingFirstUpdateActionPlan pending,
            ITypeSymbol targetType)
        {
            switch (pending.Kind)
            {
                case PendingFirstUpdateActionKind.PropertyWrite:
                    LowerPropertyWrite(_pendingPropertyWrites[pending.PropertyWriteIndex]);
                    return;
                case PendingFirstUpdateActionKind.TemplateDataType:
                    LowerTemplateDataType(pending.TemplateDataType);
                    return;
                case PendingFirstUpdateActionKind.NameAssignment:
                    LowerNameAssignment((IMarkupNameAssignmentOperation)pending.Operation!);
                    return;
                case PendingFirstUpdateActionKind.RoutedEvent:
                    LowerRoutedEvent((IMarkupRoutedEventBindingOperation)pending.Operation!);
                    return;
                case PendingFirstUpdateActionKind.CommandBinding:
                    LowerCommandBinding((IMarkupCommandBindingOperation)pending.Operation!, targetType);
                    return;
                default:
                    Debug.Fail("An invalid pending first-update action reached lowering.");
                    return;
            }
        }

        private void LowerTemplateDataType(
            in PendingTemplateDataTypePlan pending)
        {
            Debug.Assert(
                (uint)pending.ElementId < (uint)_elements.Count);

            var destination = PropertyWritePlan.Create(pending.Property);
            if (!destination.IsValid)
            {
                return;
            }

            var valueIndex = _csharpValues.Count;
            _csharpValues.Add(new ComponentCSharpValuePlan(
                operation: default,
                convertedValue: pending.DataType,
                literalValue: null,
                targetType: pending.Property.Type));

            var writeIndex = _propertyWrites.Count;
            _propertyWrites.Add(new ComponentPropertyWritePlan(
                destination,
                ComponentPropertyValueKind.Constant,
                valueIndex,
                pending.Syntax,
                ComponentPropertyWritePhase.FirstUpdate));
            _firstUpdateActions.Add(
                ComponentFirstUpdateActionPlan.CreateWrite(writeIndex));
        }

        private void LowerNameAssignment(IMarkupNameAssignmentOperation operation)
        {
            if (!operation.IsAssignedDuringFirstUpdate || operation.NameSymbol is not { } name)
            {
                return;
            }

            var index = _nameAssignments.Count;
            _nameAssignments.Add(new ComponentNameAssignmentPlan(name.Name, operation.Syntax));
            _firstUpdateActions.Add(ComponentFirstUpdateActionPlan.CreateNameAssignment(index));
        }

        private void LowerRoutedEvent(IMarkupRoutedEventBindingOperation operation)
        {
            var handlerExpression = GetEventHandlerExpression(operation);
            ComponentRoutedEventPlan plan;

            if (operation.Event.ClrEventDefinition.Symbol is IEventSymbol { IsStatic: false } clrEvent)
            {
                plan = ComponentRoutedEventPlan.CreateClrEvent(clrEvent, handlerExpression, operation.Syntax);
            }
            else if (operation.Event.RoutedEventDefinition.Symbol is { } routedEvent &&
                routedEvent is IFieldSymbol { IsStatic: true } or RoslynPropertySymbol { IsStatic: true })
            {
                plan = ComponentRoutedEventPlan.CreateAvaloniaRoutedEvent(
                    routedEvent,
                    handlerExpression,
                    operation.Syntax);
            }
            else
            {
                return;
            }

            var index = _routedEvents.Count;
            _routedEvents.Add(plan);
            _firstUpdateActions.Add(ComponentFirstUpdateActionPlan.CreateRoutedEvent(index));
        }

        private void LowerCommandBinding(
            IMarkupCommandBindingOperation operation,
            ITypeSymbol targetType)
        {
            var plan = new ComponentCommandBindingPlan(
                PropertyWritePlan.Create(operation.Property, targetType),
                operation.Command.Name,
                operation.Syntax);
            if (!plan.IsValid)
            {
                return;
            }

            var index = _commandBindings.Count;
            _commandBindings.Add(plan);
            _firstUpdateActions.Add(ComponentFirstUpdateActionPlan.CreateCommandBinding(index));
        }

        private void LowerPropertyWrite(in PendingPropertyWritePlan pending)
        {
            var operation = pending.Operation;
            if (operation.BindingKind is MarkupAttributeBindingKind.Bind or MarkupAttributeBindingKind.Out)
            {
                var subscriptionIndex = TryAddPropertySubscription(pending);
                if (subscriptionIndex >= 0)
                {
                    _firstUpdateActions.Add(
                        ComponentFirstUpdateActionPlan.CreateSubscription(subscriptionIndex));
                }
            }

            if (operation.BindingKind == MarkupAttributeBindingKind.Out || !pending.Destination.IsValid)
            {
                return;
            }

            var value = LowerPropertyValue(pending);
            if (!value.IsValid)
            {
                return;
            }

            var writeIndex = _propertyWrites.Count;
            var phase = GetWritePhase(operation);
            _propertyWrites.Add(new ComponentPropertyWritePlan(
                pending.Destination,
                value.Kind,
                value.Index,
                operation.Syntax,
                phase));

            if ((phase & ComponentPropertyWritePhase.FirstUpdate) != 0)
            {
                _firstUpdateActions.Add(ComponentFirstUpdateActionPlan.CreateWrite(writeIndex));
            }
        }

        private void LowerContent()
        {
            for (var i = 0; i < _pendingContents.Count; i++)
            {
                var pending = _pendingContents[i];
                var content = LowerContent(pending);

                if (pending.PropertyElementId >= 0)
                {
                    var propertyElement = _propertyElements[pending.PropertyElementId];
                    _propertyElements[pending.PropertyElementId] = new ComponentPropertyElementPlan(
                        propertyElement.Id,
                        propertyElement.OwnerElementId,
                        propertyElement.Syntax,
                        content);
                }
                else
                {
                    _elements[pending.OwnerElementId] =
                        _elements[pending.OwnerElementId].WithContent(content);
                }
            }
        }

        private ComponentContentTargetReference LowerContent(in PendingContentPlan pending)
        {
            var operation = pending.Operation;
            if (operation.HasErrors || operation.Property == null)
            {
                return default;
            }

            if (pending.BoundaryValue.IsValid)
            {
                return LowerPropertyContent(pending, pending.BoundaryValue);
            }

            return operation.ContentModel.IsCollection
                ? LowerCollectionContent(pending)
                : LowerPropertyContent(pending, boundaryValue: default);
        }

        private ComponentContentTargetReference LowerPropertyContent(
            in PendingContentPlan pending,
            ComponentContentValueReference boundaryValue)
        {
            var operation = pending.Operation;
            var destination = PropertyWritePlan.Create(
                operation.Property!,
                _elements[pending.OwnerElementId].Type);
            if (!destination.IsValid)
            {
                return default;
            }

            var firstUpdateValue = boundaryValue;
            var updateValue = default(ComponentContentValueReference);
            var content = operation.Content;

            if (!firstUpdateValue.IsValid &&
                content.Length == 1 &&
                content[0].Kind == MarkupChildKind.Element &&
                pending.ChildElements.Length == 1)
            {
                firstUpdateValue = new ComponentContentValueReference(
                    ComponentContentValueKind.Element,
                    _childElementIds.WrittenSpan[pending.ChildElements.Start]);
            }
            else if (!firstUpdateValue.IsValid && HasExpressionContent(content))
            {
                updateValue = AddWholeContentValue(
                    operation,
                    ComponentContentValueKind.CSharpExpression);
            }
            else if (!firstUpdateValue.IsValid && HasTextContent(content))
            {
                firstUpdateValue = AddWholeContentValue(
                    operation,
                    ComponentContentValueKind.Constant);
            }

            if (!firstUpdateValue.IsValid && !updateValue.IsValid)
            {
                return default;
            }

            var index = _propertyContents.Count;
            _propertyContents.Add(new ComponentPropertyContentPlan(
                index,
                pending.OwnerElementId,
                destination,
                firstUpdateValue,
                updateValue,
                operation.Syntax));
            return new ComponentContentTargetReference(
                ComponentContentTargetKind.Property,
                index);
        }

        private ComponentContentTargetReference LowerCollectionContent(
            in PendingContentPlan pending)
        {
            var operation = pending.Operation;
            var destination = CreateCollectionWritePlan(operation);
            if (!destination.IsValid)
            {
                return default;
            }

            var itemStart = _contentItems.Count;
            var elementOffset = 0;
            var content = operation.Content;

            for (var i = 0; i < content.Length; i++)
            {
                var child = content[i];
                ComponentContentValueReference value;

                switch (child.Kind)
                {
                    case MarkupChildKind.Element:
                        if (elementOffset >= pending.ChildElements.Length)
                        {
                            Debug.Fail("Content element IDs are not aligned with semantic content.");
                            continue;
                        }

                        value = new ComponentContentValueReference(
                            ComponentContentValueKind.Element,
                            _childElementIds.WrittenSpan[
                                pending.ChildElements.Start + elementOffset]);
                        elementOffset++;
                        break;

                    case MarkupChildKind.Text:
                        value = AddTextContentValue(child, operation.ContentModel);
                        break;

                    case MarkupChildKind.Expression:
                        value = AddExpressionContentValue(child, operation.ContentModel);
                        break;

                    default:
                        continue;
                }

                if (value.IsValid)
                {
                    _contentItems.Add(new ComponentContentItemPlan(value, child.Syntax));
                }
            }

            Debug.Assert(elementOffset == pending.ChildElements.Length);

            var itemCount = _contentItems.Count - itemStart;
            if (itemCount == 0)
            {
                return default;
            }

            var index = _collectionContents.Count;
            _collectionContents.Add(new ComponentCollectionContentPlan(
                index,
                pending.OwnerElementId,
                destination,
                new ComponentPlanRange(itemStart, itemCount),
                operation.Syntax));
            return new ComponentContentTargetReference(
                ComponentContentTargetKind.Collection,
                index);
        }

        private ComponentContentValueReference AddWholeContentValue(
            IMarkupContentOperation operation,
            ComponentContentValueKind kind)
        {
            var targetType = operation.ContentModel.AllowedChildType.Symbol as ITypeSymbol ??
                operation.Property?.Type.Symbol as ITypeSymbol;
            var valueOperation = operation.ValueOperation;
            var literalValue = operation.LiteralValue;

            if (valueOperation.IsDefault)
            {
                var content = operation.Content;
                var whitespaceMode = content.IsDefaultOrEmpty
                    ? MarkupWhitespaceMode.Default
                    : content[0].WhitespaceMode;
                if (!AkburaSemanticModel.TryCreateMarkupContentValueExpression(
                        operation.Syntax,
                        whitespaceMode,
                        out var expression,
                        out literalValue,
                        out _,
                        out _,
                        out var diagnosticSyntax))
                {
                    return default;
                }

                var binding = _semanticModel.BindMarkupAttributeExpression(
                    diagnosticSyntax,
                    expression,
                    targetType);
                if (binding.OperationDefinition.IsDefault || HasErrors(binding.Diagnostics))
                {
                    return default;
                }

                valueOperation = binding.OperationDefinition;
            }

            var index = _csharpValues.Count;
            _csharpValues.Add(new ComponentCSharpValuePlan(
                valueOperation,
                convertedValue: null,
                literalValue,
                targetType));
            return new ComponentContentValueReference(kind, index);
        }

        private ComponentContentValueReference AddTextContentValue(
            in MarkupChildContent child,
            in MarkupContentModel contentModel)
        {
            var index = _csharpValues.Count;
            _csharpValues.Add(new ComponentCSharpValuePlan(
                operation: default,
                convertedValue: null,
                child.Text,
                contentModel.AllowedChildType.Symbol as ITypeSymbol));
            return new ComponentContentValueReference(
                ComponentContentValueKind.Constant,
                index);
        }

        private ComponentContentValueReference AddExpressionContentValue(
            in MarkupChildContent child,
            in MarkupContentModel contentModel)
        {
            if (child.Syntax is not MarkupInlineExpressionSyntax inlineExpression)
            {
                return default;
            }

            var expression = AkburaSemanticModel.ParseInlineExpression(
                inlineExpression.Expression);
            if (expression == null)
            {
                return default;
            }

            var targetType = contentModel.AllowedChildType.Symbol as ITypeSymbol;
            var binding = _semanticModel.BindMarkupAttributeExpression(
                inlineExpression,
                expression,
                targetType);
            if (binding.OperationDefinition.IsDefault || HasErrors(binding.Diagnostics))
            {
                return default;
            }

            var index = _csharpValues.Count;
            _csharpValues.Add(new ComponentCSharpValuePlan(
                binding.OperationDefinition,
                convertedValue: null,
                literalValue: null,
                targetType));
            return new ComponentContentValueReference(
                ComponentContentValueKind.CSharpExpression,
                index);
        }

        private static CollectionWritePlan CreateCollectionWritePlan(IMarkupContentOperation operation)
        {
            var property = operation.Property;
            Debug.Assert(property != null);

            if (property?.Parameter is { } parameter)
            {
                if (parameter.BindingKind != ParamBindingKind.Default ||
                    parameter.Type.Symbol is not ITypeSymbol parameterType)
                {
                    return default;
                }

                return CollectionWritePlan.CreateComponentParameter(
                    parameterType,
                    parameter.Name);
            }

            if (property == null)
            {
                return default;
            }

            var read = PropertyReadPlan.Create(property);
            var collectionType = GetCollectionType(property, operation.ContentModel);

            return !read.IsValid || collectionType == null
                ? default
                : CollectionWritePlan.CreateProperty(read, collectionType);
        }

        private static ITypeSymbol? GetCollectionType(
            AkburaPropertySymbol property,
            in MarkupContentModel contentModel)
        {
            return (property.ClrPropertyDefinition.Symbol as RoslynPropertySymbol)?.Type ??
                (property.ReadDefinition.Symbol as RoslynPropertySymbol)?.Type ??
                (contentModel.ContentProperty.Symbol as RoslynPropertySymbol)?.Type ??
                property.Type.Symbol as ITypeSymbol;
        }

        private static bool HasExpressionContent(ImmutableArray<MarkupChildContent> content)
        {
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i].Kind == MarkupChildKind.Expression)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasTextContent(ImmutableArray<MarkupChildContent> content)
        {
            for (var i = 0; i < content.Length; i++)
            {
                if (content[i].Kind == MarkupChildKind.Text)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasErrors(ImmutableArray<Diagnostic> diagnostics)
        {
            for (var i = 0; i < diagnostics.Length; i++)
            {
                if (diagnostics[i].Severity == DiagnosticSeverity.Error)
                {
                    return true;
                }
            }

            return false;
        }

        private ComponentPropertyValueReference LowerPropertyValue(in PendingPropertyWritePlan pending)
        {
            return pending.Operation.ConvertedValue is MarkupExtensionValue extension
                ? LowerMarkupExtension(extension, pending.ScopeId, pending.Destination)
                : LowerCSharpValue(pending.Operation);
        }

        private ComponentPropertyValueReference LowerCSharpValue(IMarkupPropertySetterOperation operation)
        {
            var kind = operation.ValueKind switch
            {
                MarkupAttributeValueKind.Literal => ComponentPropertyValueKind.Constant,
                MarkupAttributeValueKind.DynamicExpression => ComponentPropertyValueKind.CSharpExpression,
                _ => ComponentPropertyValueKind.None,
            };

            if (kind == ComponentPropertyValueKind.None)
            {
                return default;
            }

            var index = _csharpValues.Count;
            _csharpValues.Add(new ComponentCSharpValuePlan(
                operation.ValueOperation,
                operation.ConvertedValue,
                operation.LiteralValue,
                operation.Property?.Type.Symbol as ITypeSymbol));

            return new ComponentPropertyValueReference(kind, index);
        }

        private ComponentPropertyValueReference LowerMarkupExtension(
            MarkupExtensionValue extension,
            int scopeId,
            in PropertyWritePlan destination)
        {
            if (extension.Binding != null)
            {
                return LowerMarkupBinding(extension, scopeId, destination);
            }

            var result = MarkupExtensionResultPlan.Create(in _resultEnvironment, extension);
            var kind = GetComponentValueKind(result.Kind);
            if (!result.IsValid || kind == ComponentPropertyValueKind.None)
            {
                return default;
            }

            if (kind != ComponentPropertyValueKind.MarkupExtensionValue &&
                !destination.HasAvaloniaPropertyTarget)
            {
                return default;
            }

            var index = _markupExtensions.Count;
            _markupExtensions.Add(result);
            return new ComponentPropertyValueReference(kind, index);
        }

        private ComponentPropertyValueReference LowerMarkupBinding(
            MarkupExtensionValue extension,
            int scopeId,
            in PropertyWritePlan destination)
        {
            if (!destination.HasAvaloniaPropertyTarget)
            {
                return default;
            }

            var binding = BindingWritePlan.Create(
                in _bindingEnvironment,
                extension,
                scopeId,
                GetNameScopeCapability(scopeId),
                _elementReferences.WrittenSpan,
                ref _nextCachedBindingPathId);
            if (!binding.IsValid)
            {
                return default;
            }

            var index = _bindings.Count;
            _bindings.Add(binding);
            return new ComponentPropertyValueReference(ComponentPropertyValueKind.MarkupBinding, index);
        }

        private int TryAddPropertySubscription(in PendingPropertyWritePlan pending)
        {
            var operation = pending.Operation;
            if (operation.ValueSyntax is not MarkupDynamicAttributeValueSyntax ||
                operation.ValueOperation.IsDefault ||
                operation.Property is not { } property ||
                property.Type.Symbol is not ITypeSymbol valueType)
            {
                return -1;
            }

            var observation = CreateObservation(pending);
            if (!observation.IsValid)
            {
                return -1;
            }

            var index = _propertySubscriptions.Count;
            var kind = operation.BindingKind == MarkupAttributeBindingKind.Bind
                ? ComponentPropertySynchronizationKind.Bind
                : ComponentPropertySynchronizationKind.Out;
            _propertySubscriptions.Add(new ComponentPropertySubscriptionPlan(
                index,
                pending.ElementId,
                pending.SourceOrder,
                kind,
                observation,
                operation.ValueOperation,
                valueType,
                operation.Syntax));
            return index;
        }

        private PropertyObservationPlan CreateObservation(in PendingPropertyWritePlan pending)
        {
            var property = pending.Operation.Property;
            Debug.Assert(property != null);

            return property == null
                ? default
                : PropertyObservationPlan.Create(property, _elements[pending.ElementId].Type);
        }

        private static ComponentPropertyValueKind GetComponentValueKind(MarkupExtensionResultKind kind)
        {
            return kind switch
            {
                MarkupExtensionResultKind.Value => ComponentPropertyValueKind.MarkupExtensionValue,
                MarkupExtensionResultKind.DynamicResource => ComponentPropertyValueKind.DynamicResource,
                MarkupExtensionResultKind.StaticResource => ComponentPropertyValueKind.StaticResource,
                MarkupExtensionResultKind.BindingBase => ComponentPropertyValueKind.BindingBaseResult,
                MarkupExtensionResultKind.Runtime => ComponentPropertyValueKind.RuntimeMarkupExtensionResult,
                _ => ComponentPropertyValueKind.None,
            };
        }

        private static string? GetNameScopeCapability(int scopeId)
        {
            Debug.Assert(scopeId >= 0);

            // BindingWritePlan only tests whether a name scope will be available.
            // The concrete expression is supplied by MarkupExtensionWriteContext.
            return scopeId == 0 ? null : "__nameScope";
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

            if (IsDataTemplateType(type))
            {
                flags |= ComponentElementFlags.IsTemplateElement;
            }

            if (IsImplicitConversion(type, _controlType))
            {
                flags |= ComponentElementFlags.IsControl;
            }

            if (!scope.IsLocal &&
                IsImplicitConversion(type, _contentPresenterType))
            {
                flags |= ComponentElementFlags.RequiresContentPresenterRefresh;
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
                flags |= ComponentElementFlags.IsLocal |
                    ComponentElementFlags.RequiresLocalMarkupContext;
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

        private ITypeSymbol GetDeferredResultType(AkburaPropertySymbol property)
        {
            var clrProperty = GetClrProperty(property);
            if (clrProperty != null)
            {
                return _semanticModel.BindingSession.MarkupTemplateContent
                    .GetDeferredResultType(clrProperty);
            }

            return _controlType ??
                _compilation.GetSpecialType(SpecialType.System_Object);
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

        private static ComponentPropertyWritePhase GetWritePhase(
            IMarkupPropertySetterOperation operation)
        {
            var isParameter = operation.Property?.Parameter != null;
            var isInitialValue = operation.ValueKind is
                MarkupAttributeValueKind.Literal or
                MarkupAttributeValueKind.MarkupExtension;

            if (isParameter &&
                operation.ValueKind == MarkupAttributeValueKind.DynamicExpression)
            {
                return ComponentPropertyWritePhase.Both;
            }

            return isInitialValue
                ? ComponentPropertyWritePhase.FirstUpdate
                : ComponentPropertyWritePhase.Update;
        }

        private static string GetEventHandlerExpression(IMarkupRoutedEventBindingOperation operation)
        {
            var parameterCount = GetEventHandlerParameterCount(operation);
            var expression = operation.ValueSyntax is MarkupDynamicAttributeValueSyntax dynamicValue
                ? dynamicValue.Expression.Expression.GetRawCSharpExpression()?.ToFullString().Trim()
                : null;

            if (string.IsNullOrWhiteSpace(expression))
            {
                return "static " + GetEventHandlerParameterList(parameterCount) + " => { }";
            }

            if (operation.HandlerKind == MarkupCommandHandlerKind.Lambda)
            {
                return operation.HandlerParameterCount == 0
                    ? AdaptParameterlessEventLambda(expression!, parameterCount)
                    : expression!;
            }

            if (operation.HandlerKind == MarkupCommandHandlerKind.DirectReference)
            {
                return expression!;
            }

            var asyncPrefix = operation.IsAsync ? "async " : string.Empty;
            return asyncPrefix + GetEventHandlerParameterList(parameterCount) +
                " => { " + expression + "; }";
        }

        private static int GetEventHandlerParameterCount(IMarkupRoutedEventBindingOperation operation)
        {
            return operation.HandlerType.Symbol is INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod }
                ? invokeMethod.Parameters.Length
                : 2;
        }

        private static string GetEventHandlerParameterList(int parameterCount)
        {
            return "(" + string.Join(
                ", ",
                Enumerable.Range(0, parameterCount)
                    .Select(static index =>
                        "__eventArgument" + index.ToString(CultureInfo.InvariantCulture))) + ")";
        }

        private static string AdaptParameterlessEventLambda(string expression, int parameterCount)
        {
            if (parameterCount == 0)
            {
                return expression;
            }

            var parsedExpression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseExpression(expression);
            var parameterList = Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseParameterList(
                GetEventHandlerParameterList(parameterCount));

            return parsedExpression switch
            {
                CSharp.ParenthesizedLambdaExpressionSyntax lambda when lambda.ParameterList.Parameters.Count == 0 =>
                    lambda.WithParameterList(parameterList).ToFullString().Trim(),
                CSharp.AnonymousMethodExpressionSyntax anonymousMethod when anonymousMethod.ParameterList is
                { Parameters.Count: 0 } =>
                    anonymousMethod.WithParameterList(parameterList).ToFullString().Trim(),
                _ => expression,
            };
        }

        private static string EscapeIdentifier(string identifier)
        {
            return identifier.IdentifierRequiresEscaping() ? "@" + identifier : identifier;
        }
    }

    private readonly struct PendingScopePlan
    {
        public PendingScopePlan(
            int id,
            int parentScopeId,
            int ownerElementId,
            ComponentElementScopeKind kind,
            ComponentPlanRange elements = default)
        {
            Id = id;
            ParentScopeId = parentScopeId;
            OwnerElementId = ownerElementId;
            Kind = kind;
            Elements = elements;
        }

        public int Id { get; }
        public int ParentScopeId { get; }
        public int OwnerElementId { get; }
        public ComponentElementScopeKind Kind { get; }
        public ComponentPlanRange Elements { get; }
        public ComponentScopeFlags Flags =>
            Id == 0
                ? ComponentScopeFlags.None
                : ComponentScopeFlags.RequiresNameScope;

        public PendingScopePlan WithElements(ComponentPlanRange elements)
        {
            return new PendingScopePlan(
                Id,
                ParentScopeId,
                OwnerElementId,
                Kind,
                elements);
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
            int scopeId,
            ComponentElementScopeKind scopeKind,
            ComponentElementFlags flags,
            ComponentPlanRange children,
            ComponentPlanRange pendingFirstUpdateActions,
            ComponentPlanRange propertyElements,
            ComponentPlanRange propertyWrites = default,
            ComponentPlanRange propertySubscriptions = default,
            ComponentPlanRange firstUpdateActions = default,
            ComponentContentTargetReference content = default)
        {
            Id = id;
            Syntax = syntax;
            Symbol = symbol;
            Type = type;
            Identifier = identifier;
            ParentId = parentId;
            ScopeId = scopeId;
            ScopeKind = scopeKind;
            Flags = flags;
            Children = children;
            PendingFirstUpdateActions = pendingFirstUpdateActions;
            PropertyWrites = propertyWrites;
            PropertySubscriptions = propertySubscriptions;
            FirstUpdateActions = firstUpdateActions;
            PropertyElements = propertyElements;
            Content = content;
        }

        public int Id { get; }
        public MarkupElementSyntax Syntax { get; }
        public IMarkupComponentSymbol Symbol { get; }
        public ITypeSymbol Type { get; }
        public string Identifier { get; }
        public int ParentId { get; }
        public int ScopeId { get; }
        public ComponentElementScopeKind ScopeKind { get; }
        public ComponentElementFlags Flags { get; }
        public ComponentPlanRange Children { get; }
        public ComponentPlanRange PendingFirstUpdateActions { get; }
        public ComponentPlanRange PropertyWrites { get; }
        public ComponentPlanRange PropertySubscriptions { get; }
        public ComponentPlanRange FirstUpdateActions { get; }
        public ComponentPlanRange PropertyElements { get; }
        public ComponentContentTargetReference Content { get; }
        public bool RequiresLocalMarkupContext =>
            (Flags & ComponentElementFlags.RequiresLocalMarkupContext) != 0;

        public PendingElementPlan WithPropertyPlans(
            ComponentPlanRange propertyWrites,
            ComponentPlanRange propertySubscriptions,
            ComponentPlanRange firstUpdateActions)
        {
            return new PendingElementPlan(
                Id,
                Syntax,
                Symbol,
                Type,
                Identifier,
                ParentId,
                ScopeId,
                ScopeKind,
                Flags,
                Children,
                PendingFirstUpdateActions,
                PropertyElements,
                propertyWrites,
                propertySubscriptions,
                firstUpdateActions,
                Content);
        }

        public PendingElementPlan WithContent(ComponentContentTargetReference content)
        {
            return new PendingElementPlan(
                Id,
                Syntax,
                Symbol,
                Type,
                Identifier,
                ParentId,
                ScopeId,
                ScopeKind,
                Flags,
                Children,
                PendingFirstUpdateActions,
                PropertyElements,
                PropertyWrites,
                PropertySubscriptions,
                FirstUpdateActions,
                content);
        }
    }

    private enum PendingFirstUpdateActionKind : byte
    {
        None,
        PropertyWrite,
        TemplateDataType,
        NameAssignment,
        RoutedEvent,
        CommandBinding,
    }

    private readonly struct PendingTemplateDataTypePlan
    {
        public PendingTemplateDataTypePlan(
            int elementId,
            RoslynPropertySymbol property,
            ITypeSymbol dataType,
            AkburaSyntax syntax)
        {
            ElementId = elementId;
            Property = property;
            DataType = dataType;
            Syntax = syntax;
        }

        public int ElementId { get; }
        public RoslynPropertySymbol Property { get; }
        public ITypeSymbol DataType { get; }
        public AkburaSyntax Syntax { get; }
    }

    private readonly struct PendingFirstUpdateActionPlan
    {
        private PendingFirstUpdateActionPlan(
            PendingFirstUpdateActionKind kind,
            int propertyWriteIndex,
            IMarkupAttributeOperation? operation,
            PendingTemplateDataTypePlan templateDataType)
        {
            Kind = kind;
            PropertyWriteIndex = propertyWriteIndex;
            Operation = operation;
            TemplateDataType = templateDataType;
        }

        public PendingFirstUpdateActionKind Kind { get; }
        public int PropertyWriteIndex { get; }
        public IMarkupAttributeOperation? Operation { get; }
        public PendingTemplateDataTypePlan TemplateDataType { get; }

        public static PendingFirstUpdateActionPlan CreateProperty(int propertyWriteIndex)
        {
            return new PendingFirstUpdateActionPlan(
                PendingFirstUpdateActionKind.PropertyWrite,
                propertyWriteIndex,
                operation: null,
                templateDataType: default);
        }

        public static PendingFirstUpdateActionPlan CreateTemplateDataType(
            in PendingTemplateDataTypePlan templateDataType)
        {
            return new PendingFirstUpdateActionPlan(
                PendingFirstUpdateActionKind.TemplateDataType,
                propertyWriteIndex: -1,
                operation: null,
                templateDataType);
        }

        public static PendingFirstUpdateActionPlan CreateNameAssignment(IMarkupNameAssignmentOperation operation)
        {
            return new PendingFirstUpdateActionPlan(
                PendingFirstUpdateActionKind.NameAssignment,
                propertyWriteIndex: -1,
                operation,
                templateDataType: default);
        }

        public static PendingFirstUpdateActionPlan CreateRoutedEvent(IMarkupRoutedEventBindingOperation operation)
        {
            return new PendingFirstUpdateActionPlan(
                PendingFirstUpdateActionKind.RoutedEvent,
                propertyWriteIndex: -1,
                operation,
                templateDataType: default);
        }

        public static PendingFirstUpdateActionPlan CreateCommandBinding(IMarkupCommandBindingOperation operation)
        {
            return new PendingFirstUpdateActionPlan(
                PendingFirstUpdateActionKind.CommandBinding,
                propertyWriteIndex: -1,
                operation,
                templateDataType: default);
        }
    }

    private readonly struct PendingPropertyWritePlan
    {
        public PendingPropertyWritePlan(
            int elementId,
            int scopeId,
            int sourceOrder,
            PropertyWritePlan destination,
            IMarkupPropertySetterOperation operation)
        {
            ElementId = elementId;
            ScopeId = scopeId;
            SourceOrder = sourceOrder;
            Destination = destination;
            Operation = operation;
        }

        public int ElementId { get; }
        public int ScopeId { get; }
        public int SourceOrder { get; }
        public PropertyWritePlan Destination { get; }
        public IMarkupPropertySetterOperation Operation { get; }
    }

    private readonly struct PendingPropertyElementPlan
    {
        public PendingPropertyElementPlan(
            int ownerElementId,
            MarkupElementSyntax syntax,
            AkburaPropertySymbol property,
            IMarkupContentOperation operation,
            ComponentPlanRange children,
            ComponentContentValueReference boundaryValue)
        {
            OwnerElementId = ownerElementId;
            Syntax = syntax;
            Property = property;
            Operation = operation;
            Children = children;
            BoundaryValue = boundaryValue;
        }

        public int OwnerElementId { get; }
        public MarkupElementSyntax Syntax { get; }
        public AkburaPropertySymbol Property { get; }
        public IMarkupContentOperation Operation { get; }
        public ComponentPlanRange Children { get; }
        public ComponentContentValueReference BoundaryValue { get; }
    }

    private readonly struct PendingContentPlan
    {
        public PendingContentPlan(
            int ownerElementId,
            IMarkupContentOperation operation,
            ComponentPlanRange childElements,
            int propertyElementId,
            ComponentContentValueReference boundaryValue)
        {
            OwnerElementId = ownerElementId;
            Operation = operation;
            ChildElements = childElements;
            PropertyElementId = propertyElementId;
            BoundaryValue = boundaryValue;
        }

        public int OwnerElementId { get; }
        public IMarkupContentOperation Operation { get; }
        public ComponentPlanRange ChildElements { get; }
        public int PropertyElementId { get; }
        public ComponentContentValueReference BoundaryValue { get; }
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
