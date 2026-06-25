using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal sealed partial class AkburaSemanticModel
{
    internal abstract class MemberSemanticModel
    {
        protected MemberSemanticModel(
            AkburaSemanticModel semanticModel,
            AkburaDocumentSyntax scope)
        {
            SemanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
            Scope = scope ?? throw new ArgumentNullException(nameof(scope));
        }

        protected AkburaSemanticModel SemanticModel { get; }

        public AkburaDocumentSyntax Scope { get; }

        public abstract AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax);
    }

    internal sealed class ComponentMemberSemanticModel : MemberSemanticModel
    {
        public ComponentMemberSemanticModel(
            AkburaSemanticModel semanticModel,
            AkburaDocumentSyntax scope)
            : base(semanticModel, scope)
        {
        }

        public override AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
        {
            if (SemanticModel._symbolInfoCache.TryGetValue(syntax, out var cached))
            {
                return cached;
            }

            var symbolInfo = syntax switch
            {
                AkburaDocumentSyntax document => ResolveAkburaComponent(document),
                StateDeclarationSyntax stateDeclaration => ResolveState(stateDeclaration),
                ParamDeclarationSyntax paramDeclaration => ResolveParam(paramDeclaration),
                InjectDeclarationSyntax injectDeclaration => ResolveInject(injectDeclaration),
                CommandDeclarationSyntax commandDeclaration => ResolveCommand(commandDeclaration),
                UseEffectDeclarationSyntax useEffectDeclaration => ResolveUseEffect(useEffectDeclaration),
                _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
            };

            SemanticModel._symbolInfoCache[syntax] = symbolInfo;
            return symbolInfo;
        }

        private AkburaSymbolInfo ResolveAkburaComponent(AkburaDocumentSyntax document)
        {
            var componentName = SemanticModel.SyntaxTree.ComponentName;
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var namespaceName = SemanticModel.GetAkburaNamespaceText(document, SemanticModel.SyntaxTree);
            var partialTypes = ResolveAkburaComponentPartialTypes(SemanticModel.SyntaxTree);
            var contentModel = partialTypes.Length > 0
                ? SemanticModel.CreateMarkupContentModel(partialTypes[0])
                : default;

            using var markupRootsBuilder = ImmutableArrayBuilder<MarkupRootSyntax>.Rent();
            using var markupRootSymbolsBuilder = ImmutableArrayBuilder<IMarkupComponentSymbol>.Rent();
            using var statesBuilder = ImmutableArrayBuilder<IStateSymbol>.Rent();
            using var parametersBuilder = ImmutableArrayBuilder<IParamSymbol>.Rent();
            using var injectedServicesBuilder = ImmutableArrayBuilder<IInjectSymbol>.Rent();
            using var commandsBuilder = ImmutableArrayBuilder<ICommandSymbol>.Rent();
            using var useEffectsBuilder = ImmutableArrayBuilder<IUseEffectSymbol>.Rent();
            using var userHooksBuilder = ImmutableArrayBuilder<UserHookSyntax>.Rent();
            using var inlineAkcssBuilder = ImmutableArrayBuilder<InlineAkcssBlockSyntax>.Rent();

            foreach (var member in document.Members)
            {
                AddAkburaComponentMember(
                    member,
                    markupRootsBuilder,
                    statesBuilder,
                    parametersBuilder,
                    injectedServicesBuilder,
                    commandsBuilder,
                    useEffectsBuilder,
                    userHooksBuilder,
                    inlineAkcssBuilder);
            }

            var children = markupRootsBuilder.Count == 1
                ? SemanticModel.CreateMarkupChildren(markupRootsBuilder.WrittenSpan[0].Element, contentModel, out _)
                : ImmutableArray<MarkupChildContent>.Empty;

            foreach (var markupRoot in markupRootsBuilder.WrittenSpan)
            {
                if (SemanticModel.GetSymbolInfo(markupRoot.Element).Symbol is IMarkupComponentSymbol markupComponentSymbol)
                {
                    markupRootSymbolsBuilder.Add(markupComponentSymbol);
                }
            }

            var componentSymbol = new AkburaComponentSymbol(
                SemanticModel.SyntaxTree,
                document,
                componentName,
                namespaceName,
                partialTypes,
                contentModel,
                children,
                markupRootSymbolsBuilder.ToImmutable(),
                statesBuilder.ToImmutable(),
                parametersBuilder.ToImmutable(),
                injectedServicesBuilder.ToImmutable(),
                commandsBuilder.ToImmutable(),
                useEffectsBuilder.ToImmutable(),
                userHooksBuilder.ToImmutable(),
                akcssModules: ImmutableArray<IAkcssModuleSymbol>.Empty);
            var symbolInfo = AkburaSymbolInfo.Success(componentSymbol);
            SemanticModel._symbolInfoCache[document] = symbolInfo;

            componentSymbol.SetAkcssModules(SemanticModel.CreateInlineAkcssModuleSymbols(
                inlineAkcssBuilder.WrittenSpan,
                componentSymbol));

            SemanticModel.SetSemanticDiagnostics(document, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return symbolInfo;
        }

        private void AddAkburaComponentMember(
            AkTopLevelMemberSyntax member,
            ImmutableArrayBuilder<MarkupRootSyntax> markupRootsBuilder,
            ImmutableArrayBuilder<IStateSymbol> statesBuilder,
            ImmutableArrayBuilder<IParamSymbol> parametersBuilder,
            ImmutableArrayBuilder<IInjectSymbol> injectedServicesBuilder,
            ImmutableArrayBuilder<ICommandSymbol> commandsBuilder,
            ImmutableArrayBuilder<IUseEffectSymbol> useEffectsBuilder,
            ImmutableArrayBuilder<UserHookSyntax> userHooksBuilder,
            ImmutableArrayBuilder<InlineAkcssBlockSyntax> inlineAkcssBuilder)
        {
            switch (member)
            {
                case MarkupRootSyntax markupRoot:
                    markupRootsBuilder.Add(markupRoot);
                    break;

                case StateDeclarationSyntax stateDeclaration:
                    if (SemanticModel.GetSymbolInfo(stateDeclaration).Symbol is IStateSymbol stateSymbol)
                    {
                        statesBuilder.Add(stateSymbol);
                    }

                    break;

                case ParamDeclarationSyntax paramDeclaration:
                    if (SemanticModel.GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol)
                    {
                        parametersBuilder.Add(paramSymbol);
                    }

                    break;

                case InjectDeclarationSyntax injectDeclaration:
                    if (SemanticModel.GetSymbolInfo(injectDeclaration).Symbol is IInjectSymbol injectSymbol)
                    {
                        injectedServicesBuilder.Add(injectSymbol);
                    }

                    break;

                case CommandDeclarationSyntax commandDeclaration:
                    if (SemanticModel.GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol commandSymbol)
                    {
                        commandsBuilder.Add(commandSymbol);
                    }

                    break;

                case UseEffectDeclarationSyntax useEffectDeclaration:
                    if (SemanticModel.GetSymbolInfo(useEffectDeclaration).Symbol is IUseEffectSymbol useEffectSymbol)
                    {
                        useEffectsBuilder.Add(useEffectSymbol);
                    }

                    AddAkburaComponentBlockMarkup(useEffectDeclaration.Body, markupRootsBuilder);
                    foreach (var tail in useEffectDeclaration.Tails)
                    {
                        AddAkburaComponentBlockMarkup(tail.Body, markupRootsBuilder);
                    }

                    break;

                case UserHookSyntax userHook:
                    userHooksBuilder.Add(userHook);
                    AddAkburaComponentBlockMarkup(userHook.Body, markupRootsBuilder);
                    break;

                case InlineAkcssBlockSyntax inlineAkcssBlock:
                    inlineAkcssBuilder.Add(inlineAkcssBlock);
                    break;

                case CSharpStatementSyntax csharpStatement:
                    if (csharpStatement.Body != null)
                    {
                        AddAkburaComponentBlockMarkup(csharpStatement.Body, markupRootsBuilder);
                    }

                    break;
            }
        }

        private void AddAkburaComponentBlockMarkup(
            CSharpBlockSyntax block,
            ImmutableArrayBuilder<MarkupRootSyntax> markupRootsBuilder)
        {
            foreach (var member in block.Tokens)
            {
                switch (member)
                {
                    case MarkupRootSyntax markupRoot:
                        markupRootsBuilder.Add(markupRoot);
                        break;

                    case CSharpStatementSyntax { Body: { } body }:
                        AddAkburaComponentBlockMarkup(body, markupRootsBuilder);
                        break;

                    case UseEffectDeclarationSyntax useEffectDeclaration:
                        AddAkburaComponentBlockMarkup(useEffectDeclaration.Body, markupRootsBuilder);
                        foreach (var tail in useEffectDeclaration.Tails)
                        {
                            AddAkburaComponentBlockMarkup(tail.Body, markupRootsBuilder);
                        }

                        break;

                    case UserHookSyntax userHook:
                        AddAkburaComponentBlockMarkup(userHook.Body, markupRootsBuilder);
                        break;
                }
            }
        }

        private ImmutableArray<INamedTypeSymbol> ResolveAkburaComponentPartialTypes(AkburaSyntaxTree syntaxTree)
        {
            var metadataName = SemanticModel.GetAkburaComponentMetadataName(syntaxTree);
            if (metadataName.Length == 0)
            {
                return ImmutableArray<INamedTypeSymbol>.Empty;
            }

            var type = SemanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName);
            return type == null
                ? ImmutableArray<INamedTypeSymbol>.Empty
                : ImmutableArray.Create(type);
        }

        private AkburaSymbolInfo ResolveState(StateDeclarationSyntax stateDeclaration)
        {
            var name = stateDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var hasExplicitType = stateDeclaration.Type != null;
            var initializerBinding = BindStateInitializerExpression(stateDeclaration);
            var userHook = ResolveStateUserHook(stateDeclaration);
            var initializerType = initializerBinding.TypeSymbol == null
                ? userHook?.ReturnType ?? default
                : new CSharpSymbolDefinition(initializerBinding.TypeSymbol);
            var type = hasExplicitType
                ? ResolveExplicitStateType(stateDeclaration)
                : initializerType;
            var bindingKind = GetStateBindingKind(stateDeclaration.Initializer);
            var diagnostics = SemanticModel.CreateStateBindingDiagnostics(
                stateDeclaration,
                bindingKind,
                type,
                initializerBinding);
            SemanticModel.SetSemanticDiagnostics(stateDeclaration, diagnostics);

            return AkburaSymbolInfo.Success(new StateSymbol(
                stateDeclaration,
                type,
                initializerType,
                userHook,
                hasExplicitType,
                bindingKind));
        }

        private AkburaSymbolInfo ResolveParam(ParamDeclarationSyntax paramDeclaration)
        {
            var name = paramDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var hasExplicitType = paramDeclaration.Type != null;
            var defaultValueType = ResolveParamDefaultValueType(paramDeclaration);
            var type = hasExplicitType
                ? ResolveExplicitParamType(paramDeclaration)
                : defaultValueType;
            var bindingKind = GetParamBindingKind(paramDeclaration);

            SemanticModel.SetSemanticDiagnostics(paramDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return AkburaSymbolInfo.Success(new ParamSymbol(
                paramDeclaration,
                type,
                defaultValueType,
                hasExplicitType,
                bindingKind));
        }

        private AkburaSymbolInfo ResolveInject(InjectDeclarationSyntax injectDeclaration)
        {
            var name = injectDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var type = ResolveInjectType(injectDeclaration);

            SemanticModel.SetSemanticDiagnostics(injectDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return AkburaSymbolInfo.Success(new InjectSymbol(injectDeclaration, type));
        }

        private AkburaSymbolInfo ResolveCommand(CommandDeclarationSyntax commandDeclaration)
        {
            var name = commandDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var returnType = ResolveCommandReturnType(commandDeclaration);
            var parameters = CreateCommandParameters(commandDeclaration);
            var isVoid = returnType.Symbol is ITypeSymbol { SpecialType: SpecialType.System_Void };
            var isAsyncLike = true;
            var resultType = GetCommandResultType(returnType, isVoid);
            var hasResult = !resultType.IsDefault;

            SemanticModel.SetSemanticDiagnostics(commandDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return AkburaSymbolInfo.Success(new CommandSymbol(
                commandDeclaration,
                returnType,
                resultType,
                parameters,
                isVoid,
                isAsyncLike,
                hasResult));
        }

        private AkburaSymbolInfo ResolveUseEffect(UseEffectDeclarationSyntax useEffectDeclaration)
        {
            var dependencies = CreateUseEffectDependencies(useEffectDeclaration);

            SemanticModel.SetSemanticDiagnostics(useEffectDeclaration, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return AkburaSymbolInfo.Success(new UseEffectSymbol(
                useEffectDeclaration,
                dependencies));
        }

        private CSharpSymbolDefinition ResolveExplicitStateType(StateDeclarationSyntax stateDeclaration)
        {
            var typeSyntax = stateDeclaration.Type;
            if (typeSyntax == null)
            {
                return default;
            }

            CSharp.TypeSyntax csharpType;
            try
            {
                csharpType = typeSyntax.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return default;
            }

            var binding = SemanticModel.BindCSharpType(csharpType);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private CSharpSymbolDefinition ResolveExplicitParamType(ParamDeclarationSyntax paramDeclaration)
        {
            var typeSyntax = paramDeclaration.Type;
            if (typeSyntax == null)
            {
                return default;
            }

            CSharp.TypeSyntax csharpType;
            try
            {
                csharpType = typeSyntax.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return default;
            }

            var binding = SemanticModel.BindCSharpType(csharpType);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private CSharpSymbolDefinition ResolveInjectType(InjectDeclarationSyntax injectDeclaration)
        {
            CSharp.TypeSyntax csharpType;
            try
            {
                csharpType = injectDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return default;
            }

            var binding = SemanticModel.BindCSharpType(csharpType);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private CSharpSymbolDefinition ResolveCommandReturnType(CommandDeclarationSyntax commandDeclaration)
        {
            CSharp.TypeSyntax csharpType;
            try
            {
                csharpType = commandDeclaration.ReturnType.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return default;
            }

            var binding = SemanticModel.BindCSharpType(csharpType);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private ImmutableArray<ICommandParameterSymbol> CreateCommandParameters(
            CommandDeclarationSyntax commandDeclaration)
        {
            var csharpParameters = GetCSharpParameterList(commandDeclaration.Parameters);
            if (csharpParameters == null)
            {
                return ImmutableArray<ICommandParameterSymbol>.Empty;
            }

            using var builder = ImmutableArrayBuilder<ICommandParameterSymbol>.Rent();
            for (var index = 0; index < csharpParameters.Parameters.Count; index++)
            {
                var parameter = csharpParameters.Parameters[index];
                var name = parameter.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var type = parameter.Type == null
                    ? default
                    : ResolveCSharpParameterType(parameter.Type);

                builder.Add(new CommandParameterSymbol(
                    name,
                    index,
                    type));
            }

            return builder.ToImmutable();
        }

        private CSharpSymbolDefinition ResolveCSharpParameterType(CSharp.TypeSyntax typeSyntax)
        {
            var binding = SemanticModel.BindCSharpType(typeSyntax);
            return binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol);
        }

        private CSharpSymbolDefinition GetCommandResultType(
            CSharpSymbolDefinition returnType,
            bool isVoid)
        {
            if (returnType.Symbol is not ITypeSymbol ||
                isVoid)
            {
                return default;
            }

            return returnType;
        }

        private CSharpSymbolDefinition ResolveParamDefaultValueType(ParamDeclarationSyntax paramDeclaration)
        {
            var defaultValue = paramDeclaration.DefaultValue;
            if (defaultValue == null)
            {
                return default;
            }

            CSharp.ExpressionSyntax csharpExpression;
            try
            {
                csharpExpression = CSharpSyntaxFactory.ParseExpression(defaultValue.ToFullString());
            }
            catch (ArgumentException)
            {
                return default;
            }

            var binding = SemanticModel.BindCSharpExpression(csharpExpression);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private CSharpBindingResult BindStateInitializerExpression(StateDeclarationSyntax stateDeclaration)
        {
            CSharp.ExpressionSyntax csharpExpression;
            try
            {
                csharpExpression = CSharpSyntaxFactory.ParseExpression(stateDeclaration.Initializer.Expression.ToFullString());
            }
            catch (ArgumentException)
            {
                return CSharpBindingResult.Empty;
            }

            return SemanticModel.BindCSharpExpression(
                csharpExpression,
                stateDeclaration,
                isBindingPath: IsStateBindingPath(csharpExpression));
        }

        private IUserHookSymbol? ResolveStateUserHook(StateDeclarationSyntax stateDeclaration)
        {
            if (!TryGetStateUserHookInvocation(stateDeclaration, out var invocationName))
            {
                return null;
            }

            return SemanticModel.ResolveUserHookInvocation(invocationName);
        }

        private ImmutableArray<UseEffectDependency> CreateUseEffectDependencies(
            UseEffectDeclarationSyntax useEffectDeclaration)
        {
            var argumentList = GetCSharpArgumentList(useEffectDeclaration.Arguments);
            if (argumentList == null || argumentList.Arguments.Count == 0)
            {
                return ImmutableArray<UseEffectDependency>.Empty;
            }

            using var builder = ImmutableArrayBuilder<UseEffectDependency>.Rent(argumentList.Arguments.Count);
            foreach (var argument in argumentList.Arguments)
            {
                var expression = argument.Expression;
                var expressionText = expression.ToFullString().Trim();
                if (string.IsNullOrWhiteSpace(expressionText))
                {
                    continue;
                }

                var binding = SemanticModel.BindMarkupAttributeExpression(expression);
                var csharpDefinition = binding.Symbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.Symbol);

                builder.Add(new UseEffectDependency(
                    expressionText,
                    ResolveUseEffectDependencyAkburaSymbol(expression),
                    csharpDefinition));
            }

            return builder.ToImmutable();
        }

        private AkburaSymbol? ResolveUseEffectDependencyAkburaSymbol(CSharp.ExpressionSyntax expression)
        {
            return TryGetUseEffectDependencyRootName(expression, out var rootName)
                ? ResolveTopLevelAkburaSymbol(rootName)
                : null;
        }

        private AkburaSymbol? ResolveTopLevelAkburaSymbol(string name)
        {
            foreach (var member in Scope.Members)
            {
                switch (member)
                {
                    case StateDeclarationSyntax stateDeclaration
                        when stateDeclaration.Name.Identifier.ValueText == name:
                        return SemanticModel.GetSymbolInfo(stateDeclaration).Symbol;

                    case ParamDeclarationSyntax paramDeclaration
                        when paramDeclaration.Name.Identifier.ValueText == name:
                        return SemanticModel.GetSymbolInfo(paramDeclaration).Symbol;

                    case InjectDeclarationSyntax injectDeclaration
                        when injectDeclaration.Name.Identifier.ValueText == name:
                        return SemanticModel.GetSymbolInfo(injectDeclaration).Symbol;

                    case CommandDeclarationSyntax commandDeclaration
                        when commandDeclaration.Name.Identifier.ValueText == name:
                        return SemanticModel.GetSymbolInfo(commandDeclaration).Symbol;
                }
            }

            return null;
        }
    }
}
