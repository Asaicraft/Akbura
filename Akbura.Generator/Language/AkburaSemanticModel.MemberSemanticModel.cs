using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
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

            var symbolInfo = syntax.Kind switch
            {
                AkburaSyntaxKind.AkburaDocumentSyntax => ResolveAkburaComponent(Unsafe.As<AkburaDocumentSyntax>(syntax)),
                AkburaSyntaxKind.StateDeclarationSyntax => ResolveState(Unsafe.As<StateDeclarationSyntax>(syntax)),
                AkburaSyntaxKind.ParamDeclarationSyntax => ResolveParam(Unsafe.As<ParamDeclarationSyntax>(syntax)),
                AkburaSyntaxKind.InjectDeclarationSyntax => ResolveInject(Unsafe.As<InjectDeclarationSyntax>(syntax)),
                AkburaSyntaxKind.CommandDeclarationSyntax => ResolveCommand(Unsafe.As<CommandDeclarationSyntax>(syntax)),
                AkburaSyntaxKind.UseEffectDeclarationSyntax => ResolveUseEffect(Unsafe.As<UseEffectDeclarationSyntax>(syntax)),
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

            SemanticModel.SetSemanticDiagnostics(
                document,
                CreateUserHookDuplicateComponentMemberDiagnostics(document));

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
            switch (member.Kind)
            {
                case AkburaSyntaxKind.MarkupRootSyntax:
                    var markupRoot = Unsafe.As<MarkupRootSyntax>(member);
                    markupRootsBuilder.Add(markupRoot);
                    break;

                case AkburaSyntaxKind.StateDeclarationSyntax:
                    var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                    if (SemanticModel.GetSymbolInfo(stateDeclaration).Symbol is IStateSymbol stateSymbol)
                    {
                        statesBuilder.Add(stateSymbol);
                    }

                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    if (SemanticModel.GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol)
                    {
                        parametersBuilder.Add(paramSymbol);
                    }

                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    if (SemanticModel.GetSymbolInfo(injectDeclaration).Symbol is IInjectSymbol injectSymbol)
                    {
                        injectedServicesBuilder.Add(injectSymbol);
                    }

                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    if (SemanticModel.GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol commandSymbol)
                    {
                        commandsBuilder.Add(commandSymbol);
                    }

                    break;

                case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                    var useEffectDeclaration = Unsafe.As<UseEffectDeclarationSyntax>(member);
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

                case AkburaSyntaxKind.UserHook:
                    var userHook = Unsafe.As<UserHookSyntax>(member);
                    userHooksBuilder.Add(userHook);
                    AddAkburaComponentBlockMarkup(userHook.Body, markupRootsBuilder);
                    break;

                case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                    var inlineAkcssBlock = Unsafe.As<InlineAkcssBlockSyntax>(member);
                    inlineAkcssBuilder.Add(inlineAkcssBlock);
                    break;

                case AkburaSyntaxKind.CSharpStatementSyntax:
                    var csharpStatement = Unsafe.As<CSharpStatementSyntax>(member);
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
                switch (member.Kind)
                {
                    case AkburaSyntaxKind.MarkupRootSyntax:
                        var markupRoot = Unsafe.As<MarkupRootSyntax>(member);
                        markupRootsBuilder.Add(markupRoot);
                        break;

                    case AkburaSyntaxKind.CSharpStatementSyntax:
                        var csharpStatement = Unsafe.As<CSharpStatementSyntax>(member);
                        if (csharpStatement.Body != null)
                        {
                            AddAkburaComponentBlockMarkup(csharpStatement.Body, markupRootsBuilder);
                        }

                        break;

                    case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                        var useEffectDeclaration = Unsafe.As<UseEffectDeclarationSyntax>(member);
                        AddAkburaComponentBlockMarkup(useEffectDeclaration.Body, markupRootsBuilder);
                        foreach (var tail in useEffectDeclaration.Tails)
                        {
                            AddAkburaComponentBlockMarkup(tail.Body, markupRootsBuilder);
                        }

                        break;

                    case AkburaSyntaxKind.UserHook:
                        var userHook = Unsafe.As<UserHookSyntax>(member);
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
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            diagnosticsBuilder.AddRange(SemanticModel.CreateStateBindingDiagnostics(
                    stateDeclaration,
                    bindingKind,
                    type,
                    initializerBinding));
            AddDuplicateComponentMemberDiagnostics(stateDeclaration, name, diagnosticsBuilder);
            if (userHook == null)
            {
                AddCSharpBindingDiagnostics(
                    stateDeclaration,
                    stateDeclaration.Initializer.Expression.ToFullString().Trim(),
                    initializerBinding,
                    diagnosticsBuilder);
            }
            var diagnostics = diagnosticsBuilder.ToImmutable();
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
            var defaultValueBinding = BindParamDefaultValueExpression(paramDeclaration);
            var defaultValueType = defaultValueBinding.TypeSymbol == null
                ? default
                : new CSharpSymbolDefinition(defaultValueBinding.TypeSymbol);
            var type = hasExplicitType
                ? ResolveExplicitParamType(paramDeclaration)
                : defaultValueType;
            var bindingKind = GetParamBindingKind(paramDeclaration);

            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddDuplicateComponentMemberDiagnostics(paramDeclaration, name, diagnosticsBuilder);
            if (paramDeclaration.DefaultValue != null)
            {
                AddCSharpBindingDiagnostics(
                    paramDeclaration,
                    paramDeclaration.DefaultValue.ToFullString().Trim(),
                    defaultValueBinding,
                    diagnosticsBuilder);
            }

            SemanticModel.SetSemanticDiagnostics(paramDeclaration, diagnosticsBuilder.ToImmutable());

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

            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddDuplicateComponentMemberDiagnostics(injectDeclaration, name, diagnosticsBuilder);
            SemanticModel.SetSemanticDiagnostics(injectDeclaration, diagnosticsBuilder.ToImmutable());

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

            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddDuplicateComponentMemberDiagnostics(commandDeclaration, name, diagnosticsBuilder);
            AddDuplicateCommandParameterDiagnostics(commandDeclaration, diagnosticsBuilder);
            SemanticModel.SetSemanticDiagnostics(commandDeclaration, diagnosticsBuilder.ToImmutable());

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
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            var dependencies = CreateUseEffectDependencies(useEffectDeclaration, diagnosticsBuilder);

            SemanticModel.SetSemanticDiagnostics(useEffectDeclaration, diagnosticsBuilder.ToImmutable());

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

        private CSharpBindingResult BindParamDefaultValueExpression(ParamDeclarationSyntax paramDeclaration)
        {
            var defaultValue = paramDeclaration.DefaultValue;
            if (defaultValue == null)
            {
                return CSharpBindingResult.Empty;
            }

            CSharp.ExpressionSyntax csharpExpression;
            try
            {
                csharpExpression = CSharpSyntaxFactory.ParseExpression(defaultValue.ToFullString());
            }
            catch (ArgumentException)
            {
                return CSharpBindingResult.Empty;
            }

            return SemanticModel.BindCSharpExpression(csharpExpression);
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
            UseEffectDeclarationSyntax useEffectDeclaration,
            ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
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
                AddCSharpBindingDiagnostics(
                    useEffectDeclaration,
                    expressionText,
                    binding,
                    diagnosticsBuilder);
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
                switch (member.Kind)
                {
                    case AkburaSyntaxKind.StateDeclarationSyntax:
                        var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                        if (stateDeclaration.Name.Identifier.ValueText == name)
                        {
                            return SemanticModel.GetSymbolInfo(stateDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.ParamDeclarationSyntax:
                        var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                        if (paramDeclaration.Name.Identifier.ValueText == name)
                        {
                            return SemanticModel.GetSymbolInfo(paramDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.InjectDeclarationSyntax:
                        var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                        if (injectDeclaration.Name.Identifier.ValueText == name)
                        {
                            return SemanticModel.GetSymbolInfo(injectDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.CommandDeclarationSyntax:
                        var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                        if (commandDeclaration.Name.Identifier.ValueText == name)
                        {
                            return SemanticModel.GetSymbolInfo(commandDeclaration).Symbol;
                        }

                        break;
                }
            }

            return null;
        }

        private void AddDuplicateComponentMemberDiagnostics(
            AkTopLevelMemberSyntax member,
            string name,
            ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            foreach (var candidate in Scope.Members)
            {
                if (candidate.Position >= member.Position)
                {
                    return;
                }

                if (TryGetComponentMemberName(candidate, out var candidateName, out var candidateKind) &&
                    string.Equals(candidateName, name, StringComparison.Ordinal))
                {
                    diagnosticsBuilder.Add(CreateDuplicateComponentMemberDiagnostic(
                        member,
                        name,
                        candidateKind));
                    return;
                }
            }
        }

        private ImmutableArray<AkburaSemanticDiagnostic> CreateUserHookDuplicateComponentMemberDiagnostics(
            AkburaDocumentSyntax document)
        {
            using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            foreach (var member in document.Members)
            {
                if (member.Kind != AkburaSyntaxKind.UserHook ||
                    !TryGetComponentMemberName(member, out var name, out _))
                {
                    continue;
                }

                AddDuplicateComponentMemberDiagnostics(member, name, builder);
            }

            return builder.ToImmutable();
        }

        private static bool TryGetComponentMemberName(
            AkTopLevelMemberSyntax member,
            out string name,
            out string kind)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    name = Unsafe.As<StateDeclarationSyntax>(member).Name.Identifier.ValueText;
                    kind = "state";
                    return !string.IsNullOrWhiteSpace(name);
                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    name = Unsafe.As<ParamDeclarationSyntax>(member).Name.Identifier.ValueText;
                    kind = "param";
                    return !string.IsNullOrWhiteSpace(name);
                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    name = Unsafe.As<InjectDeclarationSyntax>(member).Name.Identifier.ValueText;
                    kind = "inject";
                    return !string.IsNullOrWhiteSpace(name);
                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    name = Unsafe.As<CommandDeclarationSyntax>(member).Name.Identifier.ValueText;
                    kind = "command";
                    return !string.IsNullOrWhiteSpace(name);
                case AkburaSyntaxKind.UserHook:
                    name = Unsafe.As<UserHookSyntax>(member).Name.Identifier.ValueText;
                    kind = "userHook";
                    return !string.IsNullOrWhiteSpace(name);
                default:
                    name = string.Empty;
                    kind = string.Empty;
                    return false;
            }
        }

        private static AkburaSemanticDiagnostic CreateDuplicateComponentMemberDiagnostic(
            AkTopLevelMemberSyntax member,
            string name,
            string previousKind)
        {
            return new AkburaSemanticDiagnostic(
                member,
                ErrorCodes.AKBURA_SEMANTIC_DuplicateComponentMember,
                [name, previousKind]);
        }

        private void AddDuplicateCommandParameterDiagnostics(
            CommandDeclarationSyntax commandDeclaration,
            ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
        {
            var csharpParameters = GetCSharpParameterList(commandDeclaration.Parameters);
            if (csharpParameters == null || csharpParameters.Parameters.Count < 2)
            {
                return;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var parameter in csharpParameters.Parameters)
            {
                var name = parameter.Identifier.ValueText;
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                if (!seen.Add(name))
                {
                    diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                        commandDeclaration,
                        ErrorCodes.AKBURA_SEMANTIC_DuplicateCommandParameter,
                        [name]));
                }
            }
        }
    }
}
