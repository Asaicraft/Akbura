using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Diagnostics;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.CodeGeneration;

internal static class ComponentMemberPlanner
{
    public static ComponentMemberPlan Create(
        IAkburaComponentSymbol component,
        AkburaSemanticModel semanticModel)
    {
        Debug.Assert(component != null);
        Debug.Assert(semanticModel != null);

        using var planner = new Planner(component!, semanticModel!);
        return planner.Create();
    }

    private ref struct Planner
    {
        private readonly IAkburaComponentSymbol _component;
        private readonly AkburaSemanticModel _semanticModel;
        private readonly INamedTypeSymbol? _nonGenericListType;
        private readonly INamedTypeSymbol? _genericListType;
        private readonly INamedTypeSymbol? _genericCollectionType;
        private readonly INamedTypeSymbol? _observableCollectionType;
        private readonly INamedTypeSymbol? _listType;
        private readonly ITypeSymbol _objectType;

        private ImmutableArrayBuilder<ComponentParameterPlan> _parameters;
        private ImmutableArrayBuilder<ComponentStatePlan> _states;
        private ImmutableArrayBuilder<ComponentInjectServicePlan> _services;
        private ImmutableArrayBuilder<ComponentCommandPlan> _commands;
        private ImmutableArrayBuilder<ComponentCommandParameterPlan> _commandParameters;
        private ImmutableArrayBuilder<ComponentUserMemberPlan> _userMembers;

        public Planner(
            IAkburaComponentSymbol component,
            AkburaSemanticModel semanticModel)
        {
            _component = component;
            _semanticModel = semanticModel;

            var compilation = semanticModel.Compilation.CSharpCompilation;
            _nonGenericListType = compilation.GetTypeByMetadataName(
                "System.Collections.IList");
            _genericListType = compilation.GetTypeByMetadataName(
                "System.Collections.Generic.IList`1");
            _genericCollectionType = compilation.GetTypeByMetadataName(
                "System.Collections.Generic.ICollection`1");
            _observableCollectionType = compilation.GetTypeByMetadataName(
                "System.Collections.ObjectModel.ObservableCollection`1");
            _listType = compilation.GetTypeByMetadataName(
                "System.Collections.Generic.List`1");
            _objectType = compilation.GetSpecialType(SpecialType.System_Object);

            _parameters = ImmutableArrayBuilder<ComponentParameterPlan>.Rent(
                component.Parameters.Length);
            _states = ImmutableArrayBuilder<ComponentStatePlan>.Rent(
                component.States.Length);
            _services = ImmutableArrayBuilder<ComponentInjectServicePlan>.Rent(
                component.InjectedServices.Length);
            _commands = ImmutableArrayBuilder<ComponentCommandPlan>.Rent(
                component.Commands.Length);
            _commandParameters = ImmutableArrayBuilder<ComponentCommandParameterPlan>.Rent();
            _userMembers = ImmutableArrayBuilder<ComponentUserMemberPlan>.Rent();
        }

        public ComponentMemberPlan Create()
        {
            LowerParameters();
            LowerServices();
            LowerCommands();
            LowerStates();
            LowerUserMembers();

            return new ComponentMemberPlan(
                _parameters.ToPooledImmutableList(),
                _states.ToPooledImmutableList(),
                _services.ToPooledImmutableList(),
                _commands.ToPooledImmutableList(),
                _commandParameters.ToPooledImmutableList(),
                _userMembers.ToPooledImmutableList());
        }

        public void Dispose()
        {
            _parameters.Dispose();
            _states.Dispose();
            _services.Dispose();
            _commands.Dispose();
            _commandParameters.Dispose();
            _userMembers.Dispose();
        }

        private void LowerParameters()
        {
            var parameters = _component.Parameters;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                if (parameter.Type.Symbol is not ITypeSymbol type)
                {
                    continue;
                }

                var flags = ComponentParameterFlags.None;
                if (parameter.HasDefaultValue)
                {
                    flags |= ComponentParameterFlags.HasDefaultValue;
                }

                if (string.Equals(parameter.Name, "Content", StringComparison.Ordinal))
                {
                    flags |= ComponentParameterFlags.IsContent;
                }

                if (parameter.ReceivesValueFromParent)
                {
                    flags |= ComponentParameterFlags.ReceivesValue;
                }

                if (parameter.SendsValueToParent)
                {
                    flags |= ComponentParameterFlags.SendsValue;
                }

                var kind = ComponentParameterKind.Value;
                var collection = default(ComponentParameterCollectionPlan);
                var dictionary = default(ComponentParameterDictionaryPlan);
                if (TryCreateDictionaryPlan(parameter, type, out dictionary))
                {
                    kind = ComponentParameterKind.Dictionary;
                }
                else if (TryCreateCollectionPlan(parameter, type, out collection))
                {
                    kind = ComponentParameterKind.Collection;
                }

                var defaultValue = parameter.DefaultValueSyntax?.GetRawCSharpExpression();
                _parameters.Add(new ComponentParameterPlan(
                    i,
                    parameter.Name,
                    type,
                    parameter.BindingKind,
                    kind,
                    flags,
                    defaultValue,
                    collection,
                    parameter.DeclarationSyntax,
                    dictionary));
            }
        }

        private bool TryCreateDictionaryPlan(IParamSymbol parameter, ITypeSymbol parameterType,
            out ComponentParameterDictionaryPlan dictionary)
        {
            dictionary = default;
            if (parameter.BindingKind != ParamBindingKind.Default || parameter.Name != "Content")
            {
                return false;
            }

            var compilation = _semanticModel.Compilation.CSharpCompilation;
            var shape = MarkupDictionaryShape.Create(parameterType, compilation);
            if (shape.ContractType == null || shape.IsAmbiguous || shape.IsReadOnlyOnly)
            {
                return false;
            }

            ITypeSymbol backingType = parameterType;
            var canCreate = false;
            var usesStandardDictionaryFactory = false;
            if (parameterType is INamedTypeSymbol namedType && namedType.TypeKind == TypeKind.Class &&
                !namedType.IsAbstract)
            {
                foreach (var constructor in namedType.InstanceConstructors)
                {
                    if (constructor.DeclaredAccessibility == Accessibility.Public && constructor.Parameters.Length == 0)
                    {
                        canCreate = true;
                        break;
                    }
                }
            }
            else
            {
                var standardBacking = shape.IsGeneric
                    ? compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")?
                        .Construct(shape.KeyType!, shape.ValueType!)
                    : compilation.GetTypeByMetadataName("System.Collections.Hashtable");
                if (standardBacking != null && compilation.ClassifyConversion(standardBacking, parameterType).IsImplicit)
                {
                    backingType = shape.IsGeneric ? parameterType : standardBacking;
                    canCreate = true;
                    usesStandardDictionaryFactory = shape.IsGeneric;
                }
            }

            if (parameter.HasDefaultValue)
            {
                backingType = parameterType;
                canCreate = true;
                usesStandardDictionaryFactory = false;
            }

            dictionary = new ComponentParameterDictionaryPlan(parameterType, backingType, shape, canCreate,
                usesStandardDictionaryFactory);
            return true;
        }

        private bool TryCreateCollectionPlan(
            IParamSymbol parameter,
            ITypeSymbol parameterType,
            out ComponentParameterCollectionPlan collection)
        {
            collection = default;
            if (parameter.BindingKind != ParamBindingKind.Default ||
                parameterType is not INamedTypeSymbol namedType ||
                _observableCollectionType == null)
            {
                return false;
            }

            if (ObservableListParameterShape.TryCreate(parameter, _semanticModel.Compilation.CSharpCompilation,
                    out var ownedElementType, out var ownedBackingType))
            {
                collection = new ComponentParameterCollectionPlan(namedType, ownedElementType,
                    ownedBackingType, observesChanges: true);
                return true;
            }

            var originalType = namedType.OriginalDefinition;
            if (_nonGenericListType != null &&
                SymbolEqualityComparer.Default.Equals(originalType, _nonGenericListType))
            {
                collection = new ComponentParameterCollectionPlan(
                    namedType,
                    _objectType,
                    _observableCollectionType.Construct(_objectType),
                    observesChanges: true);
                return true;
            }

            if (namedType.TypeArguments.Length != 1)
            {
                return false;
            }

            var elementType = namedType.TypeArguments[0];
            if (IsOriginalDefinition(originalType, _genericListType) ||
                IsOriginalDefinition(originalType, _genericCollectionType))
            {
                collection = new ComponentParameterCollectionPlan(
                    namedType,
                    elementType,
                    _observableCollectionType.Construct(elementType),
                    observesChanges: true);
                return true;
            }

            if (IsOriginalDefinition(originalType, _observableCollectionType))
            {
                collection = new ComponentParameterCollectionPlan(
                    namedType,
                    elementType,
                    WithoutNullableAnnotation(namedType),
                    observesChanges: true);
                return true;
            }

            if (IsOriginalDefinition(originalType, _listType))
            {
                collection = new ComponentParameterCollectionPlan(
                    namedType,
                    elementType,
                    WithoutNullableAnnotation(namedType),
                    observesChanges: false);
                return true;
            }

            return false;
        }

        private void LowerStates()
        {
            var states = _component.States;

            for (var i = 0; i < states.Length; i++)
            {
                var state = states[i];
                if (state.Type.Symbol is not ITypeSymbol valueType ||
                    state.InitializerExpression.GetRawCSharpExpression() is not { } initializer)
                {
                    continue;
                }

                var flags = state.IsReadOnly
                    ? ComponentStateFlags.IsReadOnly
                    : ComponentStateFlags.None;
                var factoryKind = ComponentStateFactoryKind.Value;
                IMethodSymbol? hookMethod = null;
                var stateArguments = ImmutableArray<UseHookStateArgument>.Empty;
                var bindingRootKind = ComponentStateBindingRootKind.Component;
                string? bindingRootName = null;
                CSharp.ExpressionSyntax? bindingRootExpression = null;
                ITypeSymbol? bindingSourceType = null;
                var bindingPathElements = ImmutableArray<MarkupBindingPathElement>.Empty;
                var bindingFullPathElementCount = 0;
                var bindingDependencyStateGeneratedNames = ImmutableArray<string>.Empty;
                var bindingPropertyDependencies =
                    ImmutableArray<ComponentStateBindingPropertyDependencyPlan>.Empty;
                string? bindingRootHotReloadIdentity = null;

                if (state.BindingKind != StateBindingKind.None)
                {
                    factoryKind = ComponentStateFactoryKind.State;
                    if (state.CanReadBindingSource)
                    {
                        flags |= ComponentStateFlags.CanReadBindingSource;
                    }

                    if (state.InitializerType.Symbol is ITypeSymbol initializerType &&
                        AkburaSemanticModel.TryGetIObservableElementType(initializerType, out _))
                    {
                        flags |= ComponentStateFlags.IsObservableSource;
                    }

                    var rootName = GetBindingRootIdentifier(initializer);
                    using var dependencies = ImmutableArrayBuilder<string>.Rent();
                    for (var stateIndex = 0; stateIndex < _states.Count; stateIndex++)
                    {
                        ref readonly var candidate = ref _states.WrittenSpan[stateIndex];
                        if (string.Equals(candidate.Name, rootName, StringComparison.Ordinal))
                        {
                            bindingRootKind = ComponentStateBindingRootKind.State;
                            bindingRootName = candidate.GeneratedName;
                            bindingSourceType = candidate.ValueType;
                            bindingRootHotReloadIdentity = candidate.HotReloadKey;
                            dependencies.Add(candidate.GeneratedName);
                        }
                        else if (UsesStateInIndexer(initializer, candidate.Name))
                        {
                            dependencies.Add(candidate.GeneratedName);
                        }
                    }

                    if (bindingRootKind == ComponentStateBindingRootKind.Component)
                    {
                        TryGetGeneratedBindingRoot(
                            state,
                            rootName,
                            out bindingRootKind,
                            out bindingRootName,
                            out bindingSourceType,
                            out bindingRootHotReloadIdentity);
                    }

                    if (bindingRootKind == ComponentStateBindingRootKind.Component &&
                        TryGetStaticBindingRoot(
                            state,
                            initializer,
                            out bindingRootExpression,
                            out var staticRootType))
                    {
                        bindingRootKind = ComponentStateBindingRootKind.Static;
                        bindingSourceType = staticRootType;
                    }

                    bindingDependencyStateGeneratedNames = dependencies.ToImmutable();
                    using var propertyDependencies =
                        ImmutableArrayBuilder<ComponentStateBindingPropertyDependencyPlan>.Rent();
                    AddBindingPropertyDependencies(
                        state,
                        initializer,
                        propertyDependencies);
                    bindingPropertyDependencies = propertyDependencies.ToImmutable();
                    bindingSourceType ??= _component.ComponentType ?? _objectType;
                    var includeRootIdentifier = bindingRootKind == ComponentStateBindingRootKind.Component;
                    bindingFullPathElementCount = CountStateBindingPathElements(
                        initializer,
                        includeRootIdentifier,
                        bindingRootExpression);
                    var pathExpression = state.BindingKind == StateBindingKind.In
                        ? GetStateBindingOwnerExpression(initializer)
                        : initializer;
                    if (pathExpression != null)
                    {
                        bindingPathElements = CreateStateBindingPath(
                            state,
                            pathExpression,
                            includeRootIdentifier,
                            bindingRootExpression,
                            bindingSourceType);
                    }
                }

                if (state.BindingKind == StateBindingKind.None &&
                    _semanticModel.GetOperation(state.InitializerSyntax) is IUseHookOperation hook)
                {
                    initializer = hook.EffectiveInvocation;
                    hookMethod = hook.Method;
                    stateArguments = hook.StateArguments;
                    factoryKind = ComponentStateFactoryKind.State;
                    flags |= ComponentStateFlags.UsesHook;
                    if (!ComponentStatePlan.IsInitializerHook(hook.Method))
                    {
                        flags |= ComponentStateFlags.IsComposable;
                    }
                }

                _states.Add(new ComponentStatePlan(
                    i,
                    state.Name,
                    valueType,
                    state.BindingKind,
                    factoryKind,
                    flags,
                    initializer,
                    state.DeclarationSyntax,
                    bindingRootKind,
                    bindingRootName,
                    bindingRootExpression,
                    bindingSourceType,
                    state.InitializerType.Symbol as ITypeSymbol,
                    bindingPathElements,
                    bindingFullPathElementCount,
                    bindingDependencyStateGeneratedNames,
                    bindingPropertyDependencies,
                    bindingRootHotReloadIdentity,
                    hookMethod,
                    stateArguments));
            }
        }

        private ImmutableArray<MarkupBindingPathElement> CreateStateBindingPath(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            bool includeRootIdentifier,
            CSharp.ExpressionSyntax? bindingRootExpression,
            ITypeSymbol sourceType)
        {
            using var elements = ImmutableArrayBuilder<MarkupBindingPathElement>.Rent();
            AddStateBindingPathElements(
                state,
                expression,
                includeRootIdentifier,
                bindingRootExpression,
                sourceType,
                elements);
            return elements.ToImmutable();
        }

        private bool TryGetGeneratedBindingRoot(
            IStateSymbol state,
            string? rootName,
            out ComponentStateBindingRootKind kind,
            out string? name,
            out ITypeSymbol? sourceType,
            out string? hotReloadIdentity)
        {
            kind = ComponentStateBindingRootKind.Component;
            name = null;
            sourceType = null;
            hotReloadIdentity = null;
            if (string.IsNullOrEmpty(rootName))
            {
                return false;
            }

            for (var index = 0; index < _parameters.Count; index++)
            {
                ref readonly var parameter = ref _parameters.WrittenSpan[index];
                if (string.Equals(parameter.Name, rootName, StringComparison.Ordinal))
                {
                    kind = ComponentStateBindingRootKind.Parameter;
                    name = parameter.Name;
                    sourceType = parameter.Type;
                    hotReloadIdentity = parameter.HotReloadKey;
                    return true;
                }
            }

            for (var index = 0; index < _services.Count; index++)
            {
                ref readonly var service = ref _services.WrittenSpan[index];
                if (string.Equals(service.Name, rootName, StringComparison.Ordinal))
                {
                    kind = ComponentStateBindingRootKind.Service;
                    name = service.Name;
                    sourceType = service.ServiceType;
                    hotReloadIdentity = service.HotReloadKey;
                    return true;
                }
            }

            for (var index = 0; index < _commands.Count; index++)
            {
                ref readonly var command = ref _commands.WrittenSpan[index];
                if (string.Equals(command.Name, rootName, StringComparison.Ordinal))
                {
                    kind = ComponentStateBindingRootKind.Command;
                    name = command.Name;
                    sourceType = _semanticModel.BindCSharpExpression(
                        Microsoft.CodeAnalysis.CSharp.SyntaxFactory.IdentifierName(rootName!),
                        state.DeclarationSyntax).TypeSymbol;
                    hotReloadIdentity = command.HotReloadKey;
                    return true;
                }
            }

            return false;
        }

        private void AddBindingPropertyDependencies(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            ImmutableArrayBuilder<ComponentStateBindingPropertyDependencyPlan> dependencies)
        {
            foreach (var node in expression.DescendantNodesAndSelf())
            {
                if (node is not CSharp.ElementAccessExpressionSyntax elementAccess)
                {
                    continue;
                }

                foreach (var argument in elementAccess.ArgumentList.Arguments)
                {
                    foreach (var argumentNode in argument.Expression.DescendantNodesAndSelf())
                    {
                        if (argumentNode is CSharp.IdentifierNameSyntax identifier)
                        {
                            if (identifier.Parent is CSharp.MemberAccessExpressionSyntax memberAccess &&
                                ReferenceEquals(memberAccess.Name, identifier))
                            {
                                continue;
                            }

                            TryAddBindingPropertyDependency(
                                state,
                                identifier,
                                identifier.Identifier.ValueText,
                                dependencies);
                            continue;
                        }

                        if (argumentNode is CSharp.MemberAccessExpressionSyntax
                            {
                                Expression: CSharp.ThisExpressionSyntax or CSharp.BaseExpressionSyntax,
                            } componentMember)
                        {
                            TryAddBindingPropertyDependency(
                                state,
                                componentMember,
                                componentMember.Name.Identifier.ValueText,
                                dependencies);
                        }
                    }
                }
            }
        }

        private void TryAddBindingPropertyDependency(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            string name,
            ImmutableArrayBuilder<ComponentStateBindingPropertyDependencyPlan> dependencies)
        {
            for (var index = 0; index < _parameters.Count; index++)
            {
                ref readonly var parameter = ref _parameters.WrittenSpan[index];
                if (string.Equals(parameter.Name, name, StringComparison.Ordinal))
                {
                    AddBindingPropertyDependency(
                        dependencies,
                        new ComponentStateBindingPropertyDependencyPlan(
                            ComponentStateBindingPropertyDependencyKind.Parameter,
                            parameter.Name,
                            default,
                            parameter.HotReloadKey));
                    return;
                }
            }

            for (var index = 0; index < _services.Count; index++)
            {
                ref readonly var service = ref _services.WrittenSpan[index];
                if (string.Equals(service.Name, name, StringComparison.Ordinal))
                {
                    AddBindingPropertyDependency(
                        dependencies,
                        new ComponentStateBindingPropertyDependencyPlan(
                            ComponentStateBindingPropertyDependencyKind.Service,
                            service.Name,
                            default,
                            service.HotReloadKey));
                    return;
                }
            }

            for (var index = 0; index < _commands.Count; index++)
            {
                ref readonly var command = ref _commands.WrittenSpan[index];
                if (string.Equals(command.Name, name, StringComparison.Ordinal))
                {
                    AddBindingPropertyDependency(
                        dependencies,
                        new ComponentStateBindingPropertyDependencyPlan(
                            ComponentStateBindingPropertyDependencyKind.Command,
                            command.Name,
                            default,
                            command.HotReloadKey));
                    return;
                }
            }

            var binding = _semanticModel.BindCSharpExpression(
                expression,
                state.DeclarationSyntax);
            if (binding.Symbol is not Microsoft.CodeAnalysis.IPropertySymbol property ||
                !TryGetAvaloniaProperty(property, out var avaloniaProperty))
            {
                return;
            }

            AddBindingPropertyDependency(
                dependencies,
                new ComponentStateBindingPropertyDependencyPlan(
                    ComponentStateBindingPropertyDependencyKind.AvaloniaProperty,
                    name: null,
                    new CSharpSymbolDefinition(avaloniaProperty),
                    ComponentHotReloadIdentity.CreateAvaloniaPropertyKey(
                        avaloniaProperty)));
        }

        private static void AddBindingPropertyDependency(
            ImmutableArrayBuilder<ComponentStateBindingPropertyDependencyPlan> dependencies,
            in ComponentStateBindingPropertyDependencyPlan dependency)
        {
            for (var index = 0; index < dependencies.Count; index++)
            {
                ref readonly var candidate = ref dependencies.WrittenSpan[index];
                if (candidate.Kind == dependency.Kind &&
                    string.Equals(
                        candidate.HotReloadIdentity,
                        dependency.HotReloadIdentity,
                        StringComparison.Ordinal))
                {
                    return;
                }
            }

            dependencies.Add(dependency);
        }

        private static bool TryGetAvaloniaProperty(
            Microsoft.CodeAnalysis.IPropertySymbol property,
            out Microsoft.CodeAnalysis.ISymbol avaloniaProperty)
        {
            var propertyName = property.Name + "Property";
            for (var type = property.ContainingType; type != null; type = type.BaseType)
            {
                foreach (var member in type.GetMembers(propertyName))
                {
                    var memberType = member switch
                    {
                        Microsoft.CodeAnalysis.IFieldSymbol { IsStatic: true } field =>
                            field.Type,
                        Microsoft.CodeAnalysis.IPropertySymbol { IsStatic: true } staticProperty =>
                            staticProperty.Type,
                        _ => null,
                    };
                    if (memberType != null && IsAvaloniaPropertyType(memberType))
                    {
                        avaloniaProperty = member;
                        return true;
                    }
                }
            }

            avaloniaProperty = null!;
            return false;
        }

        private static bool IsAvaloniaPropertyType(ITypeSymbol type)
        {
            for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
            {
                if (current.Name == "AvaloniaProperty" &&
                    current.ContainingNamespace.Name == "Avalonia" &&
                    current.ContainingNamespace.ContainingNamespace.IsGlobalNamespace)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryGetStaticBindingRoot(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            out CSharp.ExpressionSyntax? rootExpression,
            out ITypeSymbol? sourceType)
        {
            while (expression is CSharp.ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            var receiver = expression switch
            {
                CSharp.MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
                CSharp.ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
                _ => null,
            };
            if (receiver != null &&
                TryGetStaticBindingRoot(
                    state,
                    receiver,
                    out rootExpression,
                    out sourceType))
            {
                return true;
            }

            var binding = _semanticModel.BindCSharpExpression(
                expression,
                state.DeclarationSyntax);
            if (binding.Symbol is not Microsoft.CodeAnalysis.IPropertySymbol { IsStatic: true } and
                not Microsoft.CodeAnalysis.IFieldSymbol { IsStatic: true } ||
                binding.TypeSymbol == null)
            {
                rootExpression = null;
                sourceType = null;
                return false;
            }

            rootExpression = expression;
            sourceType = binding.TypeSymbol;
            return true;
        }

        private void AddStateBindingPathElements(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            bool includeRootIdentifier,
            CSharp.ExpressionSyntax? bindingRootExpression,
            ITypeSymbol sourceType,
            ImmutableArrayBuilder<MarkupBindingPathElement> elements)
        {
            if (ReferenceEquals(expression, bindingRootExpression))
            {
                return;
            }

            switch (expression)
            {
                case CSharp.ParenthesizedExpressionSyntax parenthesized:
                    AddStateBindingPathElements(
                        state,
                        parenthesized.Expression,
                        includeRootIdentifier,
                        bindingRootExpression,
                        sourceType,
                        elements);
                    return;

                case CSharp.MemberAccessExpressionSyntax memberAccess:
                    AddStateBindingPathElements(
                        state,
                        memberAccess.Expression,
                        includeRootIdentifier,
                        bindingRootExpression,
                        sourceType,
                        elements);
                    AddStateBindingMember(
                        state,
                        memberAccess,
                        memberAccess.Name.Identifier.ValueText,
                        sourceType,
                        elements);
                    return;

                case CSharp.ElementAccessExpressionSyntax elementAccess:
                    AddStateBindingPathElements(
                        state,
                        elementAccess.Expression,
                        includeRootIdentifier,
                        bindingRootExpression,
                        sourceType,
                        elements);
                    AddStateBindingIndexer(state, elementAccess, elements);
                    return;

                case CSharp.IdentifierNameSyntax identifier when includeRootIdentifier:
                    AddStateBindingMember(
                        state,
                        identifier,
                        identifier.Identifier.ValueText,
                        sourceType,
                        elements);
                    return;
            }
        }

        private void AddStateBindingMember(
            IStateSymbol state,
            CSharp.ExpressionSyntax expression,
            string name,
            ITypeSymbol sourceType,
            ImmutableArrayBuilder<MarkupBindingPathElement> elements)
        {
            var binding = _semanticModel.BindCSharpExpression(
                expression,
                state.DeclarationSyntax);
            var kind = binding.Symbol switch
            {
                Microsoft.CodeAnalysis.IPropertySymbol => MarkupBindingPathElementKind.Property,
                Microsoft.CodeAnalysis.IFieldSymbol => MarkupBindingPathElementKind.Field,
                _ => MarkupBindingPathElementKind.Unknown,
            };
            var elementSourceType = elements.Count == 0
                ? new CSharpSymbolDefinition(binding.ReceiverType ?? sourceType)
                : default;
            elements.Add(new MarkupBindingPathElement(
                kind,
                name,
                binding.Symbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.Symbol),
                binding.TypeSymbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.TypeSymbol),
                sourceType: elementSourceType));
        }

        private void AddStateBindingIndexer(
            IStateSymbol state,
            CSharp.ElementAccessExpressionSyntax elementAccess,
            ImmutableArrayBuilder<MarkupBindingPathElement> elements)
        {
            var binding = _semanticModel.BindCSharpExpression(
                elementAccess,
                state.DeclarationSyntax);
            using var arguments = ImmutableArrayBuilder<string>.Rent(
                elementAccess.ArgumentList.Arguments.Count);
            foreach (var argument in elementAccess.ArgumentList.Arguments)
            {
                arguments.Add(argument.Expression.ToString());
            }

            elements.Add(new MarkupBindingPathElement(
                MarkupBindingPathElementKind.Indexer,
                elementAccess.ArgumentList.ToString(),
                binding.Symbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.Symbol),
                binding.TypeSymbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.TypeSymbol),
                arguments: arguments.ToImmutable()));
        }

        private static CSharp.ExpressionSyntax? GetStateBindingOwnerExpression(
            CSharp.ExpressionSyntax expression)
        {
            while (expression is CSharp.ParenthesizedExpressionSyntax parenthesized)
            {
                expression = parenthesized.Expression;
            }

            return expression switch
            {
                CSharp.MemberAccessExpressionSyntax memberAccess => memberAccess.Expression,
                CSharp.ElementAccessExpressionSyntax elementAccess => elementAccess.Expression,
                _ => null,
            };
        }

        private static int CountStateBindingPathElements(
            CSharp.ExpressionSyntax expression,
            bool includeRootIdentifier,
            CSharp.ExpressionSyntax? bindingRootExpression)
        {
            if (ReferenceEquals(expression, bindingRootExpression))
            {
                return 0;
            }

            return expression switch
            {
                CSharp.ParenthesizedExpressionSyntax parenthesized =>
                    CountStateBindingPathElements(
                        parenthesized.Expression,
                        includeRootIdentifier,
                        bindingRootExpression),
                CSharp.MemberAccessExpressionSyntax memberAccess =>
                    CountStateBindingPathElements(
                        memberAccess.Expression,
                        includeRootIdentifier,
                        bindingRootExpression) + 1,
                CSharp.ElementAccessExpressionSyntax elementAccess =>
                    CountStateBindingPathElements(
                        elementAccess.Expression,
                        includeRootIdentifier,
                        bindingRootExpression) + 1,
                CSharp.IdentifierNameSyntax when includeRootIdentifier => 1,
                _ => 0,
            };
        }

        private static bool UsesStateInIndexer(
            CSharp.ExpressionSyntax expression,
            string stateName)
        {
            foreach (var node in expression.DescendantNodesAndSelf())
            {
                if (node is not CSharp.ElementAccessExpressionSyntax elementAccess)
                {
                    continue;
                }

                foreach (var argument in elementAccess.ArgumentList.Arguments)
                {
                    foreach (var argumentNode in argument.Expression.DescendantNodesAndSelf())
                    {
                        if (argumentNode is not CSharp.IdentifierNameSyntax candidate)
                        {
                            continue;
                        }

                        if (candidate.Parent is CSharp.MemberAccessExpressionSyntax memberAccess &&
                            ReferenceEquals(memberAccess.Name, candidate))
                        {
                            continue;
                        }

                        if (string.Equals(
                            candidate.Identifier.ValueText,
                            stateName,
                            StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private static string? GetBindingRootIdentifier(CSharp.ExpressionSyntax expression)
        {
            while (true)
            {
                switch (expression)
                {
                    case CSharp.ParenthesizedExpressionSyntax parenthesized:
                        expression = parenthesized.Expression;
                        continue;
                    case CSharp.MemberAccessExpressionSyntax memberAccess:
                        expression = memberAccess.Expression;
                        continue;
                    case CSharp.ElementAccessExpressionSyntax elementAccess:
                        expression = elementAccess.Expression;
                        continue;
                    case CSharp.IdentifierNameSyntax identifier:
                        return identifier.Identifier.ValueText;
                    default:
                        return null;
                }
            }
        }

        private void LowerServices()
        {
            var services = _component.InjectedServices;

            for (var i = 0; i < services.Length; i++)
            {
                var service = services[i];
                if (service.Type.Symbol is not ITypeSymbol serviceType)
                {
                    continue;
                }

                _services.Add(new ComponentInjectServicePlan(
                    i,
                    service.Name,
                    serviceType.WithNullableAnnotation(NullableAnnotation.NotAnnotated),
                    service.IsOptional,
                    service.DeclarationSyntax));
            }
        }

        private void LowerCommands()
        {
            var commands = _component.Commands;

            for (var i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                var declaredResultType = command.ResultType.Symbol as ITypeSymbol ??
                    command.ReturnType.Symbol as ITypeSymbol ??
                    _objectType;
                var resultType = declaredResultType;

                if (resultType.SpecialType == SpecialType.System_Void)
                {
                    resultType = _objectType;
                }

                var parameterStart = _commandParameters.Count;
                var parameters = command.Parameters;
                for (var j = 0; j < parameters.Length; j++)
                {
                    var parameter = parameters[j];
                    if (parameter.Type.Symbol is not ITypeSymbol parameterType)
                    {
                        continue;
                    }

                    Debug.Assert(parameter.Ordinal == j);
                    _commandParameters.Add(new ComponentCommandParameterPlan(
                        parameter.Ordinal,
                        parameter.Name,
                        parameterType));
                }

                var parameterCount = _commandParameters.Count - parameterStart;
                var parameterRange = new ComponentPlanRange(
                    parameterStart,
                    parameterCount);
                var hotReloadKey = ComponentHotReloadIdentity.CreateCommandKey(
                    command.Name,
                    declaredResultType,
                    _commandParameters.WrittenSpan.Slice(
                        parameterStart,
                        parameterCount));

                _commands.Add(new ComponentCommandPlan(
                    i,
                    command.Name,
                    resultType,
                    declaredResultType,
                    parameterRange,
                    hotReloadKey,
                    command.DeclarationSyntax));
            }
        }

        private void LowerUserMembers()
        {
            var members = _component.DeclarationSyntax.Members;

            for (var i = 0; i < members.Count; i++)
            {
                if (members[i] is not CSharpStatementSyntax syntax ||
                    syntax.GetRawCSharpStatement() is not CSharp.LocalFunctionStatementSyntax localFunction ||
                    localFunction.ContainsDiagnostics ||
                    HasSemanticErrors(syntax))
                {
                    continue;
                }

                _userMembers.Add(new ComponentUserMemberPlan(localFunction, syntax));
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

        private static bool IsOriginalDefinition(
            INamedTypeSymbol type,
            INamedTypeSymbol? expectedType)
        {
            return expectedType != null &&
                SymbolEqualityComparer.Default.Equals(type, expectedType);
        }

        private static INamedTypeSymbol WithoutNullableAnnotation(
            INamedTypeSymbol type)
        {
            return (INamedTypeSymbol)type.WithNullableAnnotation(
                NullableAnnotation.NotAnnotated);
        }
    }
}
