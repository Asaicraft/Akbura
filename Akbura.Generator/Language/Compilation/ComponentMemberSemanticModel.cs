using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

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
            if (TryGetCachedSymbolInfo(syntax, out var cached))
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

            SetCachedSymbolInfo(syntax, symbolInfo);
            return symbolInfo;
        }

        public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
        {
            if (TryGetCachedBoundNode(syntax, out var cached))
            {
                return cached;
            }

            var symbolInfo = GetSymbolInfo(syntax);
            if (TryGetCachedBoundNode(syntax, out cached))
            {
                return cached;
            }

            return syntax.Kind switch
            {
                AkburaSyntaxKind.AkburaDocumentSyntax =>
                    CacheBoundNode(Unsafe.As<AkburaDocumentSyntax>(syntax), new BoundComponentDeclaration(
                        Unsafe.As<AkburaDocumentSyntax>(syntax),
                        GetBinder(syntax, BinderUsage.Expression),
                        symbolInfo,
                        GetCachedSemanticDiagnostics(syntax),
                        CreateBoundChildrenForComponent(Unsafe.As<AkburaDocumentSyntax>(syntax)))),
                AkburaSyntaxKind.StateDeclarationSyntax =>
                    CreateAndCacheBoundStateDeclaration(Unsafe.As<StateDeclarationSyntax>(syntax), symbolInfo),
                AkburaSyntaxKind.ParamDeclarationSyntax =>
                    CreateAndCacheBoundParamDeclaration(Unsafe.As<ParamDeclarationSyntax>(syntax), symbolInfo),
                AkburaSyntaxKind.InjectDeclarationSyntax =>
                    CacheBoundNode(Unsafe.As<InjectDeclarationSyntax>(syntax), new BoundInjectDeclaration(
                        Unsafe.As<InjectDeclarationSyntax>(syntax),
                        GetBinder(syntax, BinderUsage.Expression),
                        symbolInfo,
                        GetCachedSemanticDiagnostics(syntax))),
                AkburaSyntaxKind.CommandDeclarationSyntax =>
                    CacheBoundNode(Unsafe.As<CommandDeclarationSyntax>(syntax), new BoundCommandDeclaration(
                        Unsafe.As<CommandDeclarationSyntax>(syntax),
                        GetBinder(syntax, BinderUsage.Expression),
                        symbolInfo,
                        GetCachedSemanticDiagnostics(syntax))),
                AkburaSyntaxKind.UseEffectDeclarationSyntax =>
                    CreateAndCacheBoundUseEffectDeclaration(Unsafe.As<UseEffectDeclarationSyntax>(syntax), symbolInfo),
                _ => new BoundDeclaration(
                    syntax,
                    GetBinder(syntax, BinderUsage.Expression),
                    AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
            };
        }

        private AkburaSymbolInfo ResolveAkburaComponent(AkburaDocumentSyntax document)
        {
            var componentName = SyntaxTree.ComponentName;
            if (string.IsNullOrWhiteSpace(componentName))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var namespaceName = GetAkburaNamespaceText(document, SyntaxTree);
            var componentTypeInfo = AkburaComponentTypeResolver.Resolve(
                Compilation.CSharpCompilation,
                GetAkburaComponentMetadataName(SyntaxTree));
            var partialTypes = componentTypeInfo.DeclaredType == null
                ? ImmutableArray<INamedTypeSymbol>.Empty
                : ImmutableArray.Create(componentTypeInfo.DeclaredType);
            var contentModel = partialTypes.Length > 0
                ? CreateMarkupContentModel(partialTypes[0])
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
                ? CreateMarkupChildren(markupRootsBuilder.WrittenSpan[0].Element, contentModel, out _)
                : ImmutableArray<MarkupChildContent>.Empty;

            foreach (var markupRoot in markupRootsBuilder.WrittenSpan)
            {
                if (GetSyntaxTreeSymbolInfo(markupRoot.Element).Symbol is IMarkupComponentSymbol markupComponentSymbol)
                {
                    markupRootSymbolsBuilder.Add(markupComponentSymbol);
                }
            }

            var componentSymbol = new AkburaComponentSymbol(
                SyntaxTree,
                document,
                componentName,
                namespaceName,
                partialTypes,
                componentTypeInfo.BaseType == null
                    ? default
                    : new CSharpSymbolDefinition(componentTypeInfo.BaseType),
                componentTypeInfo.HasExplicitBaseType,
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
            SetCachedSymbolInfo(document, symbolInfo);

            componentSymbol.SetAkcssModules(CreateInlineAkcssModuleSymbols(
                inlineAkcssBuilder.WrittenSpan,
                componentSymbol));

            SetSemanticDiagnostics(document, CreateComponentDeclarationDiagnostics(
                document,
                componentName,
                componentTypeInfo));
            SetCachedBoundNode(document, new BoundComponentDeclaration(
                document,
                GetBinder(document, BinderUsage.Expression),
                symbolInfo,
                GetCachedSemanticDiagnostics(document),
                CreateBoundChildrenForComponent(document)));

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
                    if (GetSymbolInfo(stateDeclaration).Symbol is IStateSymbol stateSymbol)
                    {
                        statesBuilder.Add(stateSymbol);
                    }

                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    if (GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol)
                    {
                        parametersBuilder.Add(paramSymbol);
                    }

                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    if (GetSymbolInfo(injectDeclaration).Symbol is IInjectSymbol injectSymbol)
                    {
                        injectedServicesBuilder.Add(injectSymbol);
                    }

                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    if (GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol commandSymbol)
                    {
                        commandsBuilder.Add(commandSymbol);
                    }

                    break;

                case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                    var useEffectDeclaration = Unsafe.As<UseEffectDeclarationSyntax>(member);
                    if (GetSymbolInfo(useEffectDeclaration).Symbol is IUseEffectSymbol useEffectSymbol)
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

        private AkburaSymbolInfo ResolveState(StateDeclarationSyntax stateDeclaration)
        {
            var name = stateDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var hasExplicitType = stateDeclaration.Type != null;
            var explicitType = hasExplicitType
                ? ResolveExplicitStateType(stateDeclaration)
                : default;
            var bindingKind = AkburaSemanticModel.GetStateBindingKind(stateDeclaration.Initializer);
            var initializerBinding = BindStateInitializerExpression(
                stateDeclaration,
                bindingKind == StateBindingKind.None
                    ? explicitType.Symbol as ITypeSymbol
                    : null);
            var userHook = ResolveStateUserHook(stateDeclaration);
            var initializerType = initializerBinding.TypeSymbol == null
                ? userHook?.ReturnType ?? default
                : new CSharpSymbolDefinition(initializerBinding.TypeSymbol);
            var type = hasExplicitType
                ? explicitType
                : initializerType;
            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            diagnosticsBag.AddRange(CreateStateBindingDiagnostics(
                    stateDeclaration,
                    bindingKind,
                    type,
                    initializerBinding));
            {
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AddDuplicateComponentMemberDiagnostics(stateDeclaration, name, diagnosticsBuilder);
                if (userHook == null)
                {
                    AkburaSemanticModel.AddCSharpBindingDiagnostics(
                        stateDeclaration,
                        stateDeclaration.Initializer.Expression.ToFullString().Trim(),
                        initializerBinding,
                        diagnosticsBuilder);
                }

                diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            }
            var diagnostics = SetSemanticDiagnostics(stateDeclaration, diagnosticsBag);

            var symbolInfo = AkburaSymbolInfo.Success(new StateSymbol(
                stateDeclaration,
                type,
                initializerType,
                userHook,
                hasExplicitType,
                bindingKind));
            CacheBoundStateDeclaration(
                stateDeclaration,
                symbolInfo,
                initializerBinding,
                bindingKind,
                userHook,
                diagnostics);
            return symbolInfo;
        }

        private AkburaSymbolInfo ResolveParam(ParamDeclarationSyntax paramDeclaration)
        {
            var name = paramDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var hasExplicitType = paramDeclaration.Type != null;
            var explicitType = hasExplicitType
                ? ResolveExplicitParamType(paramDeclaration)
                : default;
            var defaultValueBinding = BindParamDefaultValueExpression(
                paramDeclaration,
                explicitType.Symbol as ITypeSymbol);
            var defaultValueType = defaultValueBinding.TypeSymbol == null
                ? default
                : new CSharpSymbolDefinition(defaultValueBinding.TypeSymbol);
            var type = hasExplicitType
                ? explicitType
                : defaultValueType;
            var bindingKind = AkburaSemanticModel.GetParamBindingKind(paramDeclaration);

            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            {
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AddDuplicateComponentMemberDiagnostics(paramDeclaration, name, diagnosticsBuilder);
                if (paramDeclaration.DefaultValue != null)
                {
                    AkburaSemanticModel.AddCSharpBindingDiagnostics(
                        paramDeclaration,
                        paramDeclaration.DefaultValue.ToFullString().Trim(),
                        defaultValueBinding,
                        diagnosticsBuilder);
                }

                diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            }

            var diagnostics = SetSemanticDiagnostics(paramDeclaration, diagnosticsBag);

            var symbolInfo = AkburaSymbolInfo.Success(new ParamSymbol(
                paramDeclaration,
                type,
                defaultValueType,
                hasExplicitType,
                bindingKind));
            CacheBoundParamDeclaration(
                paramDeclaration,
                symbolInfo,
                defaultValueBinding,
                diagnostics);
            return symbolInfo;
        }

        private AkburaSymbolInfo ResolveInject(InjectDeclarationSyntax injectDeclaration)
        {
            var name = injectDeclaration.Name.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(name))
            {
                return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
            }

            var type = ResolveInjectType(injectDeclaration);

            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            {
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AddDuplicateComponentMemberDiagnostics(injectDeclaration, name, diagnosticsBuilder);
                diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            }
            var diagnostics = SetSemanticDiagnostics(injectDeclaration, diagnosticsBag);

            var symbolInfo = AkburaSymbolInfo.Success(new InjectSymbol(injectDeclaration, type));
            SetCachedBoundNode(injectDeclaration, new BoundInjectDeclaration(
                injectDeclaration,
                GetBinder(injectDeclaration, BinderUsage.Expression),
                symbolInfo,
                diagnostics));
            return symbolInfo;
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

            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            {
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AddDuplicateComponentMemberDiagnostics(commandDeclaration, name, diagnosticsBuilder);
                AddDuplicateCommandParameterDiagnostics(commandDeclaration, diagnosticsBuilder);
                diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            }
            var diagnostics = SetSemanticDiagnostics(commandDeclaration, diagnosticsBag);

            var symbolInfo = AkburaSymbolInfo.Success(new CommandSymbol(
                commandDeclaration,
                returnType,
                resultType,
                parameters,
                isVoid,
                isAsyncLike,
                hasResult));
            SetCachedBoundNode(commandDeclaration, new BoundCommandDeclaration(
                commandDeclaration,
                GetBinder(commandDeclaration, BinderUsage.Expression),
                symbolInfo,
                diagnostics));
            return symbolInfo;
        }

        private ImmutableArray<BoundNode> CreateBoundChildrenForComponent(AkburaDocumentSyntax document)
        {
            using var builder = ImmutableArrayBuilder<BoundNode>.Rent();
            foreach (var member in document.Members)
            {
                switch (member.Kind)
                {
                    case AkburaSyntaxKind.StateDeclarationSyntax:
                    case AkburaSyntaxKind.ParamDeclarationSyntax:
                    case AkburaSyntaxKind.InjectDeclarationSyntax:
                    case AkburaSyntaxKind.CommandDeclarationSyntax:
                    case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                    case AkburaSyntaxKind.MarkupRootSyntax:
                    case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                    case AkburaSyntaxKind.CSharpStatementSyntax:
                        builder.Add(BindingSession.BindSemanticSyntax(member));
                        break;
                }
            }

            return builder.ToImmutable();
        }

        private BoundNode CreateAndCacheBoundStateDeclaration(
            StateDeclarationSyntax stateDeclaration,
            AkburaSymbolInfo symbolInfo)
        {
            var bindingKind = AkburaSemanticModel.GetStateBindingKind(stateDeclaration.Initializer);
            var targetType = symbolInfo.Symbol is IStateSymbol { HasExplicitType: true } stateSymbol
                && bindingKind == StateBindingKind.None
                ? stateSymbol.Type.Symbol as ITypeSymbol
                : null;
            var initializerBinding = BindStateInitializerExpression(stateDeclaration, targetType);
            var userHook = ResolveStateUserHook(stateDeclaration);
            return CacheBoundStateDeclaration(
                stateDeclaration,
                symbolInfo,
                initializerBinding,
                bindingKind,
                userHook,
                GetCachedSemanticDiagnostics(stateDeclaration));
        }

        private BoundNode CacheBoundStateDeclaration(
            StateDeclarationSyntax stateDeclaration,
            AkburaSymbolInfo symbolInfo,
            CSharpBindingResult initializerBinding,
            StateBindingKind bindingKind,
            IUserHookSymbol? userHook,
            ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        {
            var binder = GetBinder(stateDeclaration, BinderUsage.Expression);
            var initializer = new BoundStateInitializer(
                stateDeclaration.Initializer,
                binder,
                initializerBinding,
                bindingKind,
                userHook,
                diagnostics);
            SetCachedBoundNode(stateDeclaration.Initializer, initializer);

            var declaration = new BoundStateDeclaration(
                stateDeclaration,
                binder,
                symbolInfo,
                diagnostics,
                ImmutableArray.Create<BoundNode>(initializer));
            SetCachedBoundNode(stateDeclaration, declaration);
            return declaration;
        }

        private BoundNode CreateAndCacheBoundParamDeclaration(
            ParamDeclarationSyntax paramDeclaration,
            AkburaSymbolInfo symbolInfo)
        {
            var targetType = symbolInfo.Symbol is IParamSymbol { HasExplicitType: true } paramSymbol
                ? paramSymbol.Type.Symbol as ITypeSymbol
                : null;
            var defaultValueBinding = BindParamDefaultValueExpression(paramDeclaration, targetType);
            return CacheBoundParamDeclaration(
                paramDeclaration,
                symbolInfo,
                defaultValueBinding,
                GetCachedSemanticDiagnostics(paramDeclaration));
        }

        private BoundNode CacheBoundParamDeclaration(
            ParamDeclarationSyntax paramDeclaration,
            AkburaSymbolInfo symbolInfo,
            CSharpBindingResult defaultValueBinding,
            ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        {
            var binder = GetBinder(paramDeclaration, BinderUsage.Expression);
            ImmutableArray<BoundNode> children;
            if (paramDeclaration.DefaultValue == null)
            {
                children = ImmutableArray<BoundNode>.Empty;
            }
            else
            {
                var defaultValue = new BoundParamDefaultValue(
                    paramDeclaration.DefaultValue,
                    binder,
                    defaultValueBinding,
                    diagnostics);
                SetCachedBoundNode(paramDeclaration.DefaultValue, defaultValue);
                children = ImmutableArray.Create<BoundNode>(defaultValue);
            }

            var declaration = new BoundParamDeclaration(
                paramDeclaration,
                binder,
                symbolInfo,
                diagnostics,
                children);
            SetCachedBoundNode(paramDeclaration, declaration);
            return declaration;
        }

        private BoundNode CreateAndCacheBoundUseEffectDeclaration(
            UseEffectDeclarationSyntax useEffectDeclaration,
            AkburaSymbolInfo symbolInfo)
        {
            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            _ = CreateUseEffectDependencies(
                useEffectDeclaration,
                diagnosticsBag,
                out var dependencyNodes);

            return CacheBoundUseEffectDeclaration(
                useEffectDeclaration,
                symbolInfo,
                dependencyNodes,
                GetCachedSemanticDiagnostics(useEffectDeclaration));
        }

        private BoundNode CacheBoundUseEffectDeclaration(
            UseEffectDeclarationSyntax useEffectDeclaration,
            AkburaSymbolInfo symbolInfo,
            ImmutableArray<BoundNode> dependencyNodes,
            ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        {
            var binder = GetBinder(useEffectDeclaration, BinderUsage.Expression);
            using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
            childrenBuilder.AddRange(dependencyNodes);
            childrenBuilder.Add(CreateBoundUseEffectBody(useEffectDeclaration.Body));
            foreach (var tail in useEffectDeclaration.Tails)
            {
                childrenBuilder.Add(CreateBoundUseEffectBody(tail.Body));
            }

            var declaration = new BoundUseEffectDeclaration(
                useEffectDeclaration,
                binder,
                symbolInfo,
                diagnostics,
                childrenBuilder.ToImmutable());
            SetCachedBoundNode(useEffectDeclaration, declaration);
            return declaration;
        }

        private BoundUseEffectBody CreateBoundUseEffectBody(CSharpBlockSyntax body)
        {
            var binder = GetBinder(body, BinderUsage.Expression);
            using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
            foreach (var token in body.Tokens)
            {
                if (token.Kind is
                    AkburaSyntaxKind.MarkupRootSyntax or
                    AkburaSyntaxKind.CSharpStatementSyntax)
                {
                    childrenBuilder.Add(BindingSession.BindSemanticSyntax(token));
                }
            }

            var boundBody = new BoundUseEffectBody(
                body,
                binder,
                children: childrenBuilder.ToImmutable());
            SetCachedBoundNode(body, boundBody);
            return boundBody;
        }

        private AkburaSymbolInfo ResolveUseEffect(UseEffectDeclarationSyntax useEffectDeclaration)
        {
            var placeholderInfo = AkburaSymbolInfo.Success(new UseEffectSymbol(
                useEffectDeclaration,
                ImmutableArray<UseEffectDependency>.Empty));
            SetCachedSymbolInfo(useEffectDeclaration, placeholderInfo);

            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            var dependencies = CreateUseEffectDependencies(
                useEffectDeclaration,
                diagnosticsBag,
                out var dependencyNodes);

            var diagnostics = SetSemanticDiagnostics(useEffectDeclaration, diagnosticsBag);

            var symbolInfo = AkburaSymbolInfo.Success(new UseEffectSymbol(
                useEffectDeclaration,
                dependencies));
            CacheBoundUseEffectDeclaration(
                useEffectDeclaration,
                symbolInfo,
                dependencyNodes,
                diagnostics);
            return symbolInfo;
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

            var binding = BindCSharpType(csharpType);
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

            var binding = BindCSharpType(csharpType);
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

            var binding = BindCSharpType(csharpType);
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

            var binding = BindCSharpType(csharpType);
            var typeSymbol = binding.TypeSymbol;
            return typeSymbol == null ? default : new CSharpSymbolDefinition(typeSymbol);
        }

        private ImmutableArray<ICommandParameterSymbol> CreateCommandParameters(
            CommandDeclarationSyntax commandDeclaration)
        {
            var csharpParameters = AkburaSemanticModel.GetCSharpParameterList(commandDeclaration.Parameters);
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
            var binding = BindCSharpType(typeSyntax);
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

        private CSharpBindingResult BindParamDefaultValueExpression(
            ParamDeclarationSyntax paramDeclaration,
            ITypeSymbol? targetType = null)
        {
            var defaultValue = paramDeclaration.DefaultValue;
            if (defaultValue == null)
            {
                return CSharpBindingResult.Empty;
            }

            var csharpExpression = defaultValue.GetRawCSharpExpression();
            if (csharpExpression == null)
            {
                return CSharpBindingResult.Empty;
            }

            return BindCSharpExpression(
                csharpExpression,
                targetType: targetType);
        }

        private CSharpBindingResult BindStateInitializerExpression(
            StateDeclarationSyntax stateDeclaration,
            ITypeSymbol? targetType = null)
        {
            var csharpExpression = stateDeclaration.Initializer.Expression.GetRawCSharpExpression();
            if (csharpExpression == null)
            {
                return CSharpBindingResult.Empty;
            }

            return BindCSharpExpression(
                csharpExpression,
                stateDeclaration,
                isBindingPath: AkburaSemanticModel.IsStateBindingPath(csharpExpression),
                targetType: targetType);
        }

        private IUserHookSymbol? ResolveStateUserHook(StateDeclarationSyntax stateDeclaration)
        {
            if (!AkburaSemanticModel.TryGetStateUserHookInvocation(stateDeclaration, out var invocationName))
            {
                return null;
            }

            return ResolveUserHookInvocation(invocationName);
        }

        private ImmutableArray<UseEffectDependency> CreateUseEffectDependencies(
            UseEffectDeclarationSyntax useEffectDeclaration,
            BindingDiagnosticBag diagnostics,
            out ImmutableArray<BoundNode> dependencyNodes)
        {
            var argumentList = AkburaSemanticModel.GetCSharpArgumentList(useEffectDeclaration.Arguments);
            if (argumentList == null || argumentList.Arguments.Count == 0)
            {
                dependencyNodes = ImmutableArray<BoundNode>.Empty;
                return ImmutableArray<UseEffectDependency>.Empty;
            }

            using var builder = ImmutableArrayBuilder<UseEffectDependency>.Rent(argumentList.Arguments.Count);
            using var boundBuilder = ImmutableArrayBuilder<BoundNode>.Rent(argumentList.Arguments.Count);
            var binder = GetBinder(useEffectDeclaration.Arguments, BinderUsage.Expression);
            foreach (var argument in argumentList.Arguments)
            {
                var expression = argument.Expression;
                var expressionText = expression.ToFullString().Trim();
                if (string.IsNullOrWhiteSpace(expressionText))
                {
                    continue;
                }

                var binding = BindMarkupAttributeExpression(
                    useEffectDeclaration,
                    expression);
                using (var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent())
                {
                    AkburaSemanticModel.AddCSharpBindingDiagnostics(
                            useEffectDeclaration,
                            expressionText,
                            binding,
                            diagnosticsBuilder);
                    diagnostics.AddRange(diagnosticsBuilder.ToImmutable());
                }
                var csharpDefinition = binding.Symbol == null
                    ? default
                    : new CSharpSymbolDefinition(binding.Symbol);

                var dependency = new UseEffectDependency(
                    expressionText,
                    ResolveUseEffectDependencyAkburaSymbol(expression),
                    csharpDefinition);
                builder.Add(dependency);
                boundBuilder.Add(new BoundUseEffectDependency(
                    useEffectDeclaration.Arguments,
                    binder,
                    dependency,
                    binding));
            }

            dependencyNodes = boundBuilder.ToImmutable();
            return builder.ToImmutable();
        }

        private AkburaSymbol? ResolveUseEffectDependencyAkburaSymbol(CSharp.ExpressionSyntax expression)
        {
            return AkburaSemanticModel.TryGetUseEffectDependencyRootName(expression, out var rootName)
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
                            return GetSymbolInfo(stateDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.ParamDeclarationSyntax:
                        var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                        if (paramDeclaration.Name.Identifier.ValueText == name)
                        {
                            return GetSymbolInfo(paramDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.InjectDeclarationSyntax:
                        var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                        if (injectDeclaration.Name.Identifier.ValueText == name)
                        {
                            return GetSymbolInfo(injectDeclaration).Symbol;
                        }

                        break;

                    case AkburaSyntaxKind.CommandDeclarationSyntax:
                        var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                        if (commandDeclaration.Name.Identifier.ValueText == name)
                        {
                            return GetSymbolInfo(commandDeclaration).Symbol;
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

        private ImmutableArray<AkburaSemanticDiagnostic> CreateComponentDeclarationDiagnostics(
            AkburaDocumentSyntax document,
            string componentName,
            AkburaComponentTypeInfo componentTypeInfo)
        {
            using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            builder.AddRange(CreateUserHookDuplicateComponentMemberDiagnostics(document));
            if (!componentTypeInfo.HasValidBaseType)
            {
                var actualBaseType = componentTypeInfo.BaseType?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) ??
                    componentTypeInfo.DeclaredType?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat) ??
                    "<missing>";
                var expectedBaseType = componentTypeInfo.AkburaControlType?.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat) ??
                    "global::Akbura.AkburaControl";
                builder.Add(new AkburaSemanticDiagnostic(
                    document,
                    ErrorCodes.AKBURA_SEMANTIC_ComponentBaseTypeInvalid,
                    [componentName, actualBaseType, expectedBaseType]));
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
            var csharpParameters = AkburaSemanticModel.GetCSharpParameterList(commandDeclaration.Parameters);
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
