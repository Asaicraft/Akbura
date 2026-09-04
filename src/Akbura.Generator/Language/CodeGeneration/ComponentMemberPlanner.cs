using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
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
            LowerStates();
            LowerServices();
            LowerCommands();
            LowerUserMembers();

            return new ComponentMemberPlan(
                _parameters.ToImmutable(),
                _states.ToImmutable(),
                _services.ToImmutable(),
                _commands.ToImmutable(),
                _commandParameters.ToImmutable(),
                _userMembers.ToImmutable());
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
                if (TryCreateCollectionPlan(parameter, type, out collection))
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
                    parameter.DeclarationSyntax));
            }
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

                if (_semanticModel.GetOperation(state.InitializerSyntax) is IUseHookOperation hook)
                {
                    initializer = hook.EffectiveInvocation;
                    factoryKind = ComponentStateFactoryKind.State;
                    flags |= ComponentStateFlags.UsesHook;
                }

                _states.Add(new ComponentStatePlan(
                    i,
                    state.Name,
                    valueType,
                    state.BindingKind,
                    factoryKind,
                    flags,
                    initializer,
                    state.DeclarationSyntax));
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
                var resultType = command.ResultType.Symbol as ITypeSymbol ??
                    command.ReturnType.Symbol as ITypeSymbol;
                if (resultType == null)
                {
                    resultType = _objectType;
                }
                else if (resultType.SpecialType == SpecialType.System_Void)
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

                _commands.Add(new ComponentCommandPlan(
                    i,
                    command.Name,
                    resultType,
                    new ComponentPlanRange(
                        parameterStart,
                        _commandParameters.Count - parameterStart),
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
