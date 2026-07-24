using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynMethodSymbol = Microsoft.CodeAnalysis.IMethodSymbol;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynEventSymbol = Microsoft.CodeAnalysis.IEventSymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using BinderType = Akbura.Language.Binder.Binder;
using System.Diagnostics;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel : IOperationFactoryContext
{
    private readonly SemanticBindingCache _bindingCache;
    private readonly BindingSession _bindingSession;
    private readonly IOperationFactory _operationFactory;
    private readonly DeclarationSymbolTable _declarationSymbols;

    protected AkburaSemanticModel(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        _bindingCache = new SemanticBindingCache();
        _bindingSession = new BindingSession(this);
        _operationFactory = new AkburaOperationFactory(CreateCSharpOperationSymbolMapper);
        _declarationSymbols = new DeclarationSymbolTable(this);
    }

    protected AkburaSemanticModel(AkburaSemanticModel semanticModel)
    {
        if (semanticModel == null)
        {
            throw new ArgumentNullException(nameof(semanticModel));
        }

        Compilation = semanticModel.Compilation;
        SyntaxTree = semanticModel.SyntaxTree;
        _bindingCache = semanticModel._bindingCache;
        _bindingSession = semanticModel._bindingSession;
        _operationFactory = semanticModel._operationFactory;
        _declarationSymbols = semanticModel._declarationSymbols;
    }

    public AkburaCompilation Compilation { get; }

    public AkburaSyntaxTree SyntaxTree { get; }

    public virtual AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
    {
        return GetSymbolInfoCore(syntax);
    }

    protected AkburaSymbolInfo GetSyntaxTreeSymbolInfo(AkburaSyntax syntax)
    {
        return GetSymbolInfoCore(syntax);
    }

    private AkburaSymbolInfo GetSymbolInfoCore(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        return _bindingCache.GetSymbolInfo(syntax, () => syntax.Kind switch
        {
            AkburaSyntaxKind.AkburaDocumentSyntax or
                AkburaSyntaxKind.StateDeclarationSyntax or
                AkburaSyntaxKind.ParamDeclarationSyntax or
                AkburaSyntaxKind.InjectDeclarationSyntax or
                AkburaSyntaxKind.CommandDeclarationSyntax or
                AkburaSyntaxKind.InlineAkcssBlockSyntax or
                AkburaSyntaxKind.AkcssStyleRuleSyntax or
                AkburaSyntaxKind.AkcssUtilityDeclarationSyntax => GetDeclarationSymbolInfo(syntax),
            AkburaSyntaxKind.MarkupElementSyntax => ResolveMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax => ResolveMarkupProperty(Unsafe.As<MarkupAttributeSyntax>(syntax)),
            AkburaSyntaxKind.CSharpStatementSyntax =>
                BindingSession.BindSemanticSyntax(syntax).SymbolInfo,
            AkburaSyntaxKind.AkcssAssignmentSyntax =>
                BindingSession.BindOperationSyntax(syntax).SymbolInfo,
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        });
    }

    public AkburaSymbol? GetDeclaredSymbol(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (Compilation.TryGetDeclaration(syntax, out var declaration))
        {
            return _declarationSymbols.GetSymbolInfo(declaration).Symbol;
        }

        ValidateSyntaxTreeOwnership(syntax);
        if (syntax is MarkupAttributeSyntax markupAttribute &&
            IsMarkupNameDirective(markupAttribute))
        {
            return GetSymbolInfo(markupAttribute).Symbol;
        }

        return null;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetSemanticDiagnostics(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);
        return _bindingCache.GetAggregatedDiagnostics(
            syntax,
            () => GetSemanticDiagnosticsCore(syntax));
    }

    private ImmutableArray<AkburaSemanticDiagnostic> GetSemanticDiagnosticsCore(AkburaSyntax syntax)
    {
        _ = GetSymbolInfo(syntax);
        if (syntax.Kind == AkburaSyntaxKind.AkburaDocumentSyntax)
        {
            return CreateComponentSemanticDiagnostics(Unsafe.As<AkburaDocumentSyntax>(syntax));
        }

        if (syntax.Kind is AkburaSyntaxKind.StateDeclarationSyntax or
            AkburaSyntaxKind.ParamDeclarationSyntax or
            AkburaSyntaxKind.InjectDeclarationSyntax or
            AkburaSyntaxKind.CommandDeclarationSyntax or
            AkburaSyntaxKind.MarkupRootSyntax or
            AkburaSyntaxKind.MarkupElementSyntax or
            AkburaSyntaxKind.MarkupElementContentSyntax or
            AkburaSyntaxKind.MarkupInlineExpressionSyntax or
            AkburaSyntaxKind.MarkupTextLiteralSyntax or
            AkburaSyntaxKind.InlineAkcssBlockSyntax or
            AkburaSyntaxKind.AkcssStyleRuleSyntax or
            AkburaSyntaxKind.AkcssUtilityDeclarationSyntax)
        {
            return CreateSemanticDiagnosticsFromBoundTree(
                BindingSession.BindSemanticSyntax(syntax),
                GetCachedSemanticDiagnostics(syntax));
        }

        if (syntax.Kind is AkburaSyntaxKind.MarkupPlainAttributeSyntax or
            AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax or
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
            AkburaSyntaxKind.TailwindFlagAttributeSyntax or
            AkburaSyntaxKind.TailwindFullAttributeSyntax or
            AkburaSyntaxKind.AkcssAssignmentSyntax or
            AkburaSyntaxKind.AkcssIfDirectiveSyntax or
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
        {
            return CreateSemanticDiagnosticsFromBoundTree(
                BindingSession.BindOperationSyntax(syntax),
                GetCachedSemanticDiagnostics(syntax));
        }

        if (syntax.Kind == AkburaSyntaxKind.CSharpStatementSyntax)
        {
            return CreateSemanticDiagnosticsFromBoundTree(
                BindingSession.BindSemanticSyntax(syntax),
                GetCachedSemanticDiagnostics(syntax));
        }

        return _bindingCache.TryGetDiagnostics(syntax, out var diagnostics)
            ? diagnostics
            : [];
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateComponentSemanticDiagnostics(
        AkburaDocumentSyntax document)
    {
        return CreateSemanticDiagnosticsFromBoundTree(
            BindingSession.BindSemanticSyntax(document),
            GetCachedSemanticDiagnostics(document));
    }

    private static ImmutableArray<AkburaSemanticDiagnostic> CreateSemanticDiagnosticsFromBoundTree(
        BoundNode boundNode,
        ImmutableArray<AkburaSemanticDiagnostic> additionalDiagnostics = default)
    {
        var bag = BindingDiagnosticBag.GetInstance();
        bag.AddRange(additionalDiagnostics);
        AddBoundTreeDiagnostics(boundNode, bag);
        var diagnostics = bag.ToSemanticDiagnostics();
        bag.Free();
        return diagnostics;
    }

    private static void AddBoundTreeDiagnostics(
        BoundNode boundNode,
        BindingDiagnosticBag bag)
    {
        bag.AddRange(boundNode.Diagnostics);
        foreach (var child in boundNode.Children)
        {
            AddBoundTreeDiagnostics(child, bag);
        }
    }

    public AkburaOperation? GetOperation(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        return _bindingCache.GetOperation(
            syntax,
            () =>
            {
                var boundNode = _bindingSession.BindOperationSyntax(syntax);
                return _bindingCache.TryGetOperation(syntax, out var cachedOperation)
                    ? cachedOperation
                    : _operationFactory.CreateOperation(boundNode);
            });
    }

    internal BoundNode GetBoundNode(
        AkburaSyntax syntax,
        Func<BoundNode> bind)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (bind == null)
        {
            throw new ArgumentNullException(nameof(bind));
        }

        ValidateBoundSyntaxOwnership(syntax);
        return _bindingCache.GetBoundNode(syntax, bind);
    }

    internal ImmutableArray<AkburaSemanticDiagnostic> GetCachedSemanticDiagnostics(AkburaSyntax syntax)
    {
        return _bindingCache.TryGetDiagnostics(syntax, out var diagnostics)
            ? diagnostics
            : ImmutableArray<AkburaSemanticDiagnostic>.Empty;
    }

    internal bool TryGetCachedBoundNode(AkburaSyntax syntax, out BoundNode boundNode)
    {
        return _bindingCache.TryGetBoundNode(syntax, out boundNode!);
    }

    internal bool TryGetCachedSymbolInfo(AkburaSyntax syntax, out AkburaSymbolInfo symbolInfo)
    {
        return _bindingCache.TryGetSymbolInfo(syntax, out symbolInfo);
    }

    internal bool TryGetCachedOperation(AkburaSyntax syntax, out AkburaOperation? operation)
    {
        return _bindingCache.TryGetOperation(syntax, out operation);
    }

    internal void SetCachedSymbolInfo(AkburaSyntax syntax, AkburaSymbolInfo symbolInfo)
    {
        _bindingCache.SetSymbolInfo(syntax, symbolInfo);
    }

    internal TBoundNode SetCachedBoundNode<TBoundNode>(
        AkburaSyntax syntax,
        TBoundNode boundNode)
        where TBoundNode : BoundNode
    {
        _bindingCache.SetBoundNode(syntax, boundNode);
        return boundNode;
    }

    internal void SetCachedOperation(AkburaSyntax syntax, AkburaOperation? operation)
    {
        _bindingCache.SetOperation(syntax, operation);
    }

    bool IOperationFactoryContext.TryGetCachedOperation(
        AkburaSyntax syntax,
        out AkburaOperation? operation)
    {
        return TryGetCachedOperation(syntax, out operation);
    }

    void IOperationFactoryContext.SetCachedBoundNode(
        AkburaSyntax syntax,
        BoundNode boundNode)
    {
        SetCachedBoundNode(syntax, boundNode);
    }

    void IOperationFactoryContext.SetCachedOperation(
        AkburaSyntax syntax,
        AkburaOperation? operation)
    {
        SetCachedOperation(syntax, operation);
    }

    internal BinderType GetBinder(AkburaSyntax syntax)
    {
        return _bindingSession.GetBinder(syntax);
    }

    internal BinderType GetBinder(AkburaSyntax syntax, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, usage);
    }

    BinderType IOperationFactoryContext.GetBinder(
        AkburaSyntax syntax,
        BinderUsage usage)
    {
        return GetBinder(syntax, usage);
    }

    void IOperationFactoryContext.SetAkcssInterceptIgnoredDiagnostics(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol)
    {
        SetAkcssInterceptIgnoredDiagnostics(member, containingSymbol);
    }

    internal BinderType GetBinder(AkburaSyntax syntax, int position, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, position, usage);
    }

    internal virtual MemberSemanticModel GetMemberSemanticModel(AkburaSyntax syntax)
        => throw new NotSupportedException(
            "Only syntax-tree semantic models can dispatch to member semantic models.");

    internal BindingSession BindingSession
    {
        get
        {
            return _bindingSession;
        }
    }

    internal DeclarationSymbolTable DeclarationSymbols => _declarationSymbols;

    internal AkburaSymbolInfo CreateDeclarationSymbolInfo(Declaration declaration)
    {
        if (declaration is not SingleDeclaration singleDeclaration)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var syntax = singleDeclaration.Syntax;
        if (declaration is SingleSyntaxDeclaration syntaxDeclaration &&
            syntaxDeclaration.SyntaxTree != null &&
            !ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()))
        {
            var containingModel = Compilation.GetSemanticModel(syntaxDeclaration.SyntaxTree);
            var symbol = containingModel.GetDeclaredSymbol(syntax);
            return symbol == null
                ? AkburaSymbolInfo.None(AkburaCandidateReason.NotFound)
                : AkburaSymbolInfo.Success(symbol);
        }

        return declaration.Kind switch
        {
            DeclarationKind.Component or
                DeclarationKind.State or
                DeclarationKind.Parameter or
                DeclarationKind.InjectedService or
                DeclarationKind.Command =>
                GetMemberSemanticModel(syntax).GetSymbolInfo(syntax),
            DeclarationKind.AkcssModule when syntax.Kind == AkburaSyntaxKind.InlineAkcssBlockSyntax =>
                ResolveInlineAkcssModule(Unsafe.As<InlineAkcssBlockSyntax>(syntax)),
            DeclarationKind.AkcssModule when syntax.Kind == AkburaSyntaxKind.AkcssDocumentSyntax =>
                ResolveExternalAkcssModule(declaration),
            DeclarationKind.AkcssStyle =>
                ResolveAkcssStyle(Unsafe.As<AkcssStyleRuleSyntax>(syntax)),
            DeclarationKind.AkcssUtility =>
                ResolveTailwindUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax)),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        };
    }

    internal AkburaSymbolInfo GetDeclarationSymbolInfo(AkburaSyntax syntax)
    {
        return Compilation.TryGetDeclaration(syntax, out var declaration)
            ? _declarationSymbols.GetSymbolInfo(declaration)
            : GetMemberSemanticModel(syntax).GetSymbolInfo(syntax);
    }

    private AkburaSymbolInfo ResolveInlineAkcssModule(InlineAkcssBlockSyntax inlineAkcssBlock)
    {
        var componentInfo = GetSymbolInfo(SyntaxTree.GetRoot());
        if (_bindingCache.TryGetSymbolInfo(inlineAkcssBlock, out var cachedInfo))
        {
            return cachedInfo;
        }

        var containingComponent = componentInfo.Symbol as IAkburaComponentSymbol;
        var symbol = CreateInlineAkcssModuleSymbol(inlineAkcssBlock, containingComponent);
        var symbolInfo = AkburaSymbolInfo.Success(symbol);
        SetCachedSymbolInfo(inlineAkcssBlock, symbolInfo);
        SetSemanticDiagnostics(
            inlineAkcssBlock,
            CreateDuplicateAkcssSymbolDiagnostics(inlineAkcssBlock.Members));
        return symbolInfo;
    }

    internal ImmutableArray<IAkcssModuleSymbol> CreateInlineAkcssModuleSymbols(
        ReadOnlySpan<InlineAkcssBlockSyntax> inlineAkcssBlocks,
        IAkburaComponentSymbol containingComponent)
    {
        if (inlineAkcssBlocks.Length == 0)
        {
            return ImmutableArray<IAkcssModuleSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<IAkcssModuleSymbol>.Rent(inlineAkcssBlocks.Length);
        foreach (var inlineAkcssBlock in inlineAkcssBlocks)
        {
            var module = CreateInlineAkcssModuleSymbol(inlineAkcssBlock, containingComponent);
            SetCachedSymbolInfo(inlineAkcssBlock, AkburaSymbolInfo.Success(module));
            SetSemanticDiagnostics(
                inlineAkcssBlock,
                CreateDuplicateAkcssSymbolDiagnostics(inlineAkcssBlock.Members));
            builder.Add(module);
        }

        return builder.ToImmutable();
    }

    private IAkcssModuleSymbol CreateInlineAkcssModuleSymbol(
        InlineAkcssBlockSyntax inlineAkcssBlock,
        IAkburaComponentSymbol? containingComponent)
    {
        return new AkcssModuleSymbol(
            inlineAkcssBlock,
            isInlined: true,
            containingSymbol: containingComponent,
            CreateAkcssSymbols(inlineAkcssBlock.Members),
            path: null);
    }

    private AkburaSymbolInfo ResolveExternalAkcssModule(Declaration declaration)
    {
        var document = Unsafe.As<AkcssDocumentSyntax>(DeclarationFacts.GetSyntax(declaration));
        if (_bindingCache.TryGetSymbolInfo(document, out var cachedInfo))
        {
            return cachedInfo;
        }

        var symbol = new AkcssModuleSymbol(
            document,
            isInlined: false,
            containingSymbol: null,
            CreateAkcssSymbols(document.Members),
            GetExternalAkcssPath(declaration));
        var symbolInfo = AkburaSymbolInfo.Success(symbol);
        SetCachedSymbolInfo(document, symbolInfo);
        SetSemanticDiagnostics(
            document,
            CreateDuplicateAkcssSymbolDiagnostics(document.Members));
        return symbolInfo;
    }

    private static string? GetExternalAkcssPath(Declaration declaration)
    {
        var syntaxTree = DeclarationFacts.GetAkcssSyntaxTree(declaration);
        if (syntaxTree == null)
        {
            return string.IsNullOrWhiteSpace(declaration.Name)
                ? null
                : declaration.Name;
        }

        return !string.IsNullOrWhiteSpace(syntaxTree.LogicalName)
            ? syntaxTree.LogicalName
            : syntaxTree.FilePath;
    }

    private ImmutableArray<IAkcssSymbol> CreateAkcssSymbols(
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        using var builder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    var styleRule = Unsafe.As<AkcssStyleRuleSyntax>(member);
                    if (GetDeclarationSymbolInfo(styleRule).Symbol is IAkcssSymbol styleSymbol)
                    {
                        builder.Add(styleSymbol);
                    }

                    break;

                case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                    var utilitiesSection = Unsafe.As<AkcssUtilitiesSectionSyntax>(member);
                    foreach (var utilityDeclaration in utilitiesSection.Utilities)
                    {
                        if (GetDeclarationSymbolInfo(utilityDeclaration).Symbol is IAkcssSymbol utilitySymbol)
                        {
                            builder.Add(utilitySymbol);
                        }
                    }

                    break;
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateDuplicateAkcssSymbolDiagnostics(
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            foreach (var symbolSyntax in EnumerateAkcssSymbolSyntaxes(member))
            {
                AddDuplicateAkcssSymbolDiagnostics(symbolSyntax, seen, builder);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateDuplicateAkcssSymbolDiagnostics(
        AkburaSyntax symbolSyntax)
    {
        if (!TryGetAkcssSymbolKey(symbolSyntax, out var symbolKey, out var displayName))
        {
            return ImmutableArray<AkburaSemanticDiagnostic>.Empty;
        }

        var seen = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var candidate in EnumerateContainingAkcssSymbolSyntaxes(symbolSyntax))
        {
            if (candidate.Position >= symbolSyntax.Position)
            {
                break;
            }

            if (TryGetAkcssSymbolKey(candidate, out var candidateKey, out var candidateDisplayName))
            {
                if (!seen.ContainsKey(candidateKey))
                {
                    seen.Add(candidateKey, candidateDisplayName);
                }
            }
        }

        return seen.ContainsKey(symbolKey)
            ? ImmutableArray.Create(CreateDuplicateAkcssSymbolDiagnostic(symbolSyntax, displayName))
            : ImmutableArray<AkburaSemanticDiagnostic>.Empty;
    }

    private void AddDuplicateAkcssSymbolDiagnostics(
        AkburaSyntax syntax,
        Dictionary<string, string> seen,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (!TryGetAkcssSymbolKey(syntax, out var key, out var displayName))
        {
            return;
        }

        if (seen.ContainsKey(key))
        {
            diagnosticsBuilder.Add(CreateDuplicateAkcssSymbolDiagnostic(syntax, displayName));
            return;
        }

        seen.Add(key, displayName);
    }

    private IEnumerable<AkburaSyntax> EnumerateContainingAkcssSymbolSyntaxes(AkburaSyntax syntax)
    {
        for (var node = syntax.Parent; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                    foreach (var member in Unsafe.As<InlineAkcssBlockSyntax>(node).Members)
                    {
                        foreach (var symbolSyntax in EnumerateAkcssSymbolSyntaxes(member))
                        {
                            yield return symbolSyntax;
                        }
                    }

                    yield break;

                case AkburaSyntaxKind.AkcssDocumentSyntax:
                    foreach (var member in Unsafe.As<AkcssDocumentSyntax>(node).Members)
                    {
                        foreach (var symbolSyntax in EnumerateAkcssSymbolSyntaxes(member))
                        {
                            yield return symbolSyntax;
                        }
                    }

                    yield break;
            }
        }
    }

    private static IEnumerable<AkburaSyntax> EnumerateAkcssSymbolSyntaxes(AkcssTopLevelMemberSyntax member)
    {
        switch (member.Kind)
        {
            case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                yield return Unsafe.As<AkcssStyleRuleSyntax>(member);
                break;

            case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                foreach (var utility in Unsafe.As<AkcssUtilitiesSectionSyntax>(member).Utilities)
                {
                    yield return utility;
                }

                break;
        }
    }

    private static bool TryGetAkcssSymbolKey(
        AkburaSyntax syntax,
        out string key,
        out string displayName)
    {
        switch (syntax.Kind)
        {
            case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                {
                    var selector = Unsafe.As<AkcssStyleRuleSyntax>(syntax).Selector.ToFullString().Trim();
                    if (selector.Length == 0)
                    {
                        key = string.Empty;
                        displayName = string.Empty;
                        return false;
                    }

                    key = "style:" + selector;
                    displayName = selector;
                    return true;
                }

            case AkburaSyntaxKind.AkcssUtilityDeclarationSyntax:
                {
                    var utility = Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax);
                    var name = utility.Selector.Name.Identifier.ValueText;
                    if (string.IsNullOrWhiteSpace(name))
                    {
                        key = string.Empty;
                        displayName = string.Empty;
                        return false;
                    }

                    var targetType = utility.Selector.TargetType?.ToFullString().Trim() ?? string.Empty;
                    var arity = utility.Selector.Parameters.Count;
                    key = "utility:" + targetType + ":" + name + "/" + arity;
                    displayName = targetType.Length == 0
                        ? name + "/" + arity
                        : targetType + "." + name + "/" + arity;
                    return true;
                }

            default:
                key = string.Empty;
                displayName = string.Empty;
                return false;
        }
    }

    private static AkburaSemanticDiagnostic CreateDuplicateAkcssSymbolDiagnostic(
        AkburaSyntax syntax,
        string displayName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_DuplicateAkcssSymbol,
            [displayName]);
    }

    private AkburaSymbolInfo ResolveAkcssStyle(
        AkcssStyleRuleSyntax styleRule)
    {
        var name = styleRule.Selector.Name?.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name) &&
            styleRule.Selector.TargetType == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (!TryResolveAkcssTargetType(styleRule.Selector.TargetType, out var targetType))
        {
            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            diagnosticsBag.Add(CreateAkcssSelectorTargetNotFoundDiagnostic(
                    styleRule,
                    styleRule.Selector.TargetType?.ToFullString().Trim() ?? string.Empty));
            diagnosticsBag.AddRange(CreateDuplicateAkcssSymbolDiagnostics(styleRule));
            SetSemanticDiagnostics(styleRule, diagnosticsBag);
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        var symbol = CreateAkcssStyleSymbol(styleRule, targetType, includeOperations: true);

        SetSemanticDiagnosticsIfAbsent(styleRule, CreateDuplicateAkcssSymbolDiagnostics(styleRule));

        return AkburaSymbolInfo.Success(symbol);
    }

    private AkburaSymbolInfo ResolveTailwindUtility(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var name = utilityDeclaration.Selector.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (!TryResolveAkcssTargetType(utilityDeclaration.Selector.TargetType, out var targetType))
        {
            var diagnosticsBag = BindingDiagnosticBag.GetInstance();
            diagnosticsBag.Add(CreateAkcssSelectorTargetNotFoundDiagnostic(
                    utilityDeclaration,
                    utilityDeclaration.Selector.TargetType?.ToFullString().Trim() ?? string.Empty));
            diagnosticsBag.AddRange(CreateDuplicateAkcssSymbolDiagnostics(utilityDeclaration));
            SetSemanticDiagnostics(utilityDeclaration, diagnosticsBag);
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        var symbol = CreateTailwindUtilitySymbolForAkcss(utilityDeclaration, targetType, includeOperations: true);

        var diagnostics = BindingDiagnosticBag.GetInstance();
        diagnostics.AddRange(CreateDuplicateAkcssSymbolDiagnostics(utilityDeclaration));
        SetSemanticDiagnostics(utilityDeclaration, diagnostics);

        return AkburaSymbolInfo.Success(symbol);
    }

    private AkcssStyleSymbol CreateAkcssStyleSymbol(
        AkcssStyleRuleSyntax styleRule,
        CSharpSymbolDefinition targetType,
        bool includeOperations)
    {
        var symbol = new AkcssStyleSymbol(
            styleRule,
            targetType,
            ImmutableArray<IAkcssOperation>.Empty);
        if (includeOperations)
        {
            symbol.SetOperations(CreateAkcssOperations(styleRule.Members, symbol));
        }

        return symbol;
    }

    private TailwindUtilitySymbol CreateTailwindUtilitySymbolForAkcss(
        AkcssUtilityDeclarationSyntax utilityDeclaration,
        CSharpSymbolDefinition targetType,
        bool includeOperations)
    {
        var symbol = new TailwindUtilitySymbol(
            utilityDeclaration,
            targetType,
            CreateTailwindUtilityParameters(utilityDeclaration),
            ImmutableArray<IAkcssOperation>.Empty);
        if (includeOperations)
        {
            symbol.SetOperations(CreateAkcssOperations(utilityDeclaration.Members, symbol));
        }

        return symbol;
    }

    internal ImmutableArray<ITailwindUtilityParameterSymbol> CreateTailwindUtilityParameters(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var csharpParameters = BindTailwindUtilityCSharpParameters(utilityDeclaration);
        using var builder = ImmutableArrayBuilder<ITailwindUtilityParameterSymbol>.Rent();

        for (var index = 0; index < utilityDeclaration.Selector.Parameters.Count; index++)
        {
            var parameter = utilityDeclaration.Selector.Parameters[index];
            var csharpParameter = index < csharpParameters.Length
                ? csharpParameters[index]
                : null;
            var type = csharpParameter?.Type == null
                ? ResolveTailwindUtilityParameterType(parameter)
                : new CSharpSymbolDefinition(csharpParameter.Type);

            builder.Add(new TailwindUtilityParameterSymbol(
                parameter,
                index,
                type,
                csharpParameter));
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IParameterSymbol> BindTailwindUtilityCSharpParameters(
        AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        using var parametersBuilder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in utilityDeclaration.Selector.Parameters)
        {
            var parameterName = parameter.ParamName.Identifier.ValueText;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                return ImmutableArray<IParameterSymbol>.Empty;
            }

            try
            {
                parametersBuilder.Add(CSharpSyntaxFactory.Parameter(
                        CSharpSyntaxFactory.Identifier(parameterName))
                    .WithType(parameter.Type.ToCSharp()));
            }
            catch (InvalidOperationException)
            {
                return ImmutableArray<IParameterSymbol>.Empty;
            }
        }

        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(
                    CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaUtility")
            .WithParameterList(CSharpSyntaxFactory.ParameterList(
                CSharpSyntaxFactory.SeparatedList(parametersBuilder.ToImmutable())))
            .WithBody(CSharpSyntaxFactory.Block());

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(method));

        var compilationUnit = CreateCSharpProbeCompilationUnit(
            probeClass,
            GetAkcssCSharpUsingDirectives(utilityDeclaration));

        var parseOptions = Compilation.CSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options as CSharpParseOptions ??
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        var syntaxTree = CSharpSyntaxTree.Create(compilationUnit, parseOptions);
        var probeCompilation = Compilation.CSharpCompilation.AddSyntaxTrees(syntaxTree);
        var semanticModel = probeCompilation.GetSemanticModel(syntaxTree);
        var probeMethod = syntaxTree
            .GetCompilationUnitRoot()
            .DescendantNodes()
            .OfType<CSharp.MethodDeclarationSyntax>()
            .Single();

        return semanticModel.GetDeclaredSymbol(probeMethod)?.Parameters ??
            ImmutableArray<IParameterSymbol>.Empty;
    }

    private CSharpSymbolDefinition ResolveTailwindUtilityParameterType(
        AkcssUtilityParameterSyntax parameter)
    {
        try
        {
            var binding = BindCSharpType(
                parameter.Type.ToCSharp(),
                GetAkcssCSharpUsingDirectives(parameter));
            return binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol);
        }
        catch (InvalidOperationException)
        {
            return default;
        }
    }

    internal ImmutableArray<IAkcssOperation> CreateAkcssOperations(
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        return _operationFactory.CreateAkcssOperations(members, containingSymbol, this);
    }

    internal IAkcssSymbol? GetContainingAkcssSymbol(AkburaSyntax syntax)
    {
        if (TryGetContainingAkcssSymbolFromAncestors(syntax, out var symbol))
        {
            return symbol;
        }

        var root = SyntaxTree.GetRoot();
        if (!ReferenceEquals(syntax.Root, root) &&
            root.FullSpan.Contains(syntax.FullSpan))
        {
            var rebound = root.FindNode(syntax.FullSpan, getInnermostNodeForTie: true);
            if (!ReferenceEquals(rebound, syntax) &&
                TryGetContainingAkcssSymbolFromAncestors(rebound, out symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    private bool TryGetContainingAkcssSymbolFromAncestors(
        AkburaSyntax syntax,
        out IAkcssSymbol? symbol)
    {
        for (var node = syntax.Parent; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    symbol = GetSymbolInfo(Unsafe.As<AkcssStyleRuleSyntax>(node)).Symbol as IAkcssSymbol;
                    return symbol != null;
                case AkburaSyntaxKind.AkcssUtilityDeclarationSyntax:
                    symbol = GetSymbolInfo(Unsafe.As<AkcssUtilityDeclarationSyntax>(node)).Symbol as IAkcssSymbol;
                    return symbol != null;
            }
        }

        symbol = null;
        return false;
    }

    internal bool TrySuppressAkcssOperationDueToIntercept(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol)
    {
        if (!containingSymbol.IsIntercepted)
        {
            return false;
        }

        SetAkcssInterceptIgnoredDiagnostics(member, containingSymbol);
        return true;
    }

    internal void SetAkcssInterceptIgnoredDiagnostics(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol)
    {
        SetSemanticDiagnostics(
            member,
            [CreateAkcssInterceptIgnoresMemberDiagnostic(member, containingSymbol)]);
    }

    internal void AddAkcssExpressionDiagnostics(
        AkcssAssignmentSyntax assignment,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddAkcssExpressionDiagnostics(
            assignment,
            assignment.Expression.ToFullString().Trim(),
            binding,
            diagnosticsBuilder);
    }

    internal void AddAkcssExpressionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (binding.Diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var diagnostic in binding.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                diagnosticsBuilder.Add(CreateAkcssExpressionErrorDiagnostic(
                    syntax,
                    expressionText,
                    diagnostic));
            }
        }
    }

    internal void AddAkcssValueConversionDiagnostics(
        AkcssAssignmentSyntax assignment,
        AkburaPropertySymbol? property,
        AkcssPropertyValueKind valueKind,
        bool requiresBrushConversion,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property?.Type.Symbol is not ITypeSymbol targetType ||
            requiresBrushConversion ||
            binding.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) ||
            valueKind is AkcssPropertyValueKind.ColorLiteral or AkcssPropertyValueKind.ThicknessTuple ||
            IsImplicitExpectedConversion(binding))
        {
            return;
        }

        if (binding.Conversion.TargetType != null)
        {
            if (binding.Conversion.SourceType is ITypeSymbol conversionSourceType)
            {
                if (IsSameType(conversionSourceType, targetType))
                {
                    return;
                }

                diagnosticsBuilder.Add(CreateAkcssValueCannotConvertDiagnostic(
                    assignment,
                    property.Name,
                    conversionSourceType,
                    targetType));
            }

            return;
        }

        if (binding.TypeSymbol is not ITypeSymbol sourceType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateAkcssValueCannotConvertDiagnostic(
            assignment,
            property.Name,
            sourceType,
            targetType));
    }

    private static bool IsImplicitExpectedConversion(CSharpBindingResult binding)
    {
        return binding.Conversion.TargetType != null &&
               binding.Conversion.IsImplicit;
    }

    internal IAkcssSymbol? ResolveAkcssApplyItem(
        AkcssApplyDirectiveSyntax applyDirective,
        string item,
        IAkcssSymbol containingSymbol,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var text = item.Trim();
        if (text.Length == 0)
        {
            diagnosticsBuilder.Add(CreateAkcssApplyItemNotFoundDiagnostic(applyDirective, item));
            return null;
        }

        var localCandidates = FindAkcssApplyItemCandidates(
            GetContainingAkcssLayer(containingSymbol.DeclarationSyntax),
            text);
        if (localCandidates.Length > 1)
        {
            diagnosticsBuilder.Add(CreateAkcssApplyItemAmbiguousDiagnostic(applyDirective, item));
            return null;
        }

        if (localCandidates.Length == 1)
        {
            return localCandidates[0];
        }

        foreach (var layer in GetImportedAkcssSymbolLayers(containingSymbol.DeclarationSyntax, diagnosticsBuilder))
        {
            var candidates = FindAkcssApplyItemCandidates(layer, text);
            if (candidates.Length > 1)
            {
                diagnosticsBuilder.Add(CreateAkcssApplyItemAmbiguousDiagnostic(applyDirective, item));
                return null;
            }

            if (candidates.Length == 1)
            {
                return candidates[0];
            }
        }

        diagnosticsBuilder.Add(CreateAkcssApplyItemNotFoundDiagnostic(applyDirective, item));
        return null;
    }

    private ImmutableArray<IAkcssSymbol> FindAkcssApplyItemCandidates(
        ImmutableArray<IAkcssSymbol> layer,
        string item)
    {
        var exactCandidates = FindAkcssApplyCandidates(layer, item, argumentCount: 0);
        if (!exactCandidates.IsDefaultOrEmpty)
        {
            return exactCandidates;
        }

        var argumentCount = 1;
        for (var dashIndex = item.LastIndexOf('-');
             dashIndex > 0;
             dashIndex = item.LastIndexOf('-', dashIndex - 1), argumentCount++)
        {
            var candidates = FindAkcssApplyCandidates(
                layer,
                item[..dashIndex],
                argumentCount);
            if (!candidates.IsDefaultOrEmpty)
            {
                return candidates;
            }
        }

        return ImmutableArray<IAkcssSymbol>.Empty;
    }

    private ImmutableArray<IAkcssSymbol> FindAkcssApplyCandidates(
        ImmutableArray<IAkcssSymbol> layer,
        string name,
        int argumentCount)
    {
        using var builder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var symbol in layer)
        {
            if (symbol is ITailwindUtilitySymbol utility)
            {
                if (utility.Name == name &&
                    utility.Parameters.Length == argumentCount)
                {
                    builder.Add(symbol);
                }

                continue;
            }

            if (symbol is AkcssStyleSymbol style &&
                style.ClassName == name &&
                argumentCount == 0)
            {
                builder.Add(symbol);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IAkcssSymbol> GetContainingAkcssLayer(AkburaSyntax syntax)
    {
        var members = GetContainingAkcssTopLevelMembers(syntax);
        return members.Count == 0
            ? ImmutableArray<IAkcssSymbol>.Empty
            : CreateAkcssLookupSymbols(members);
    }

    private ImmutableArray<ImmutableArray<IAkcssSymbol>> GetImportedAkcssSymbolLayers(
        AkburaSyntax syntax,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var layersBuilder = ImmutableArrayBuilder<ImmutableArray<IAkcssSymbol>>.Rent();
        foreach (var importName in GetAkcssImportNames(syntax))
        {
            var matches = Compilation.GetAkcssSyntaxTreesByLogicalName(importName);
            if (matches.Length == 0)
            {
                diagnosticsBuilder.Add(CreateAkcssImportNotFoundDiagnostic(importName));
                continue;
            }

            using var layerBuilder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
            foreach (var tree in matches)
            {
                foreach (var symbol in CreateAkcssLookupSymbols(tree.GetRoot().Members))
                {
                    layerBuilder.Add(symbol);
                }
            }

            layersBuilder.Add(layerBuilder.ToImmutable());
        }

        return layersBuilder.ToImmutable();
    }

    private ImmutableArray<IAkcssSymbol> CreateAkcssLookupSymbols(
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members)
    {
        using var builder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    var styleRule = Unsafe.As<AkcssStyleRuleSyntax>(member);
                    if (TryResolveAkcssTargetType(styleRule.Selector.TargetType, out var styleTargetType))
                    {
                        builder.Add(CreateAkcssStyleSymbol(styleRule, styleTargetType, includeOperations: false));
                    }

                    break;

                case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                    var utilitiesSection = Unsafe.As<AkcssUtilitiesSectionSyntax>(member);
                    foreach (var utilityDeclaration in utilitiesSection.Utilities)
                    {
                        if (TryResolveAkcssTargetType(utilityDeclaration.Selector.TargetType, out var utilityTargetType))
                        {
                            builder.Add(CreateTailwindUtilitySymbolForAkcss(
                                utilityDeclaration,
                                utilityTargetType,
                                includeOperations: false));
                        }
                    }

                    break;
            }
        }

        return builder.ToImmutable();
    }

    internal AkburaPropertySymbol? ResolveAkcssPropertyWithDiagnostics(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol,
        BindingDiagnosticBag diagnostics)
    {
        AkburaPropertySymbol? property = null;
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        property = ResolveAkcssProperty(
                assignment,
                containingSymbol,
                diagnosticsBuilder);
        diagnostics.AddRange(diagnosticsBuilder.ToImmutable());
        return property;
    }

    private AkburaPropertySymbol? ResolveAkcssProperty(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var propertyReference = assignment.PropertyName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(propertyReference) ||
            !TryGetAkcssPropertyOwner(
                assignment,
                containingSymbol,
                propertyReference,
                out var ownerType,
                out var propertyName))
        {
            return null;
        }

        var clrProperty = FindPublicClrProperty(ownerType, propertyName);
        var avaloniaProperty = FindAvaloniaPropertyField(ownerType, propertyName);
        if (avaloniaProperty == null &&
            propertyReference.LastIndexOf('.') > 0)
        {
            avaloniaProperty = FindExactAvaloniaPropertyField(ownerType, propertyName);
        }

        if (TryCreateAttachedPropertySymbol(
                ownerType,
                propertyName,
                containingSymbol.TargetType.Symbol as ITypeSymbol,
                SymbolLanguage.Akcss,
                containingSymbol,
                out var attachedProperty))
        {
            return attachedProperty;
        }

        if (clrProperty == null && avaloniaProperty == null)
        {
            diagnosticsBuilder.Add(HasInaccessiblePropertyMember(ownerType, propertyName)
                ? CreateInaccessibleMemberDiagnostic(assignment, propertyName, ownerType)
                : CreateAkcssPropertyNotFoundDiagnostic(
                    assignment,
                    propertyName,
                    ownerType));
            return null;
        }

        return new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(
                parameter: null,
                command: null,
                clrProperty,
                avaloniaProperty,
                attachedProperty: null),
            avaloniaPropertyDefinition: avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrPropertyDefinition: clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            language: SymbolLanguage.Akcss,
            containingSymbol: containingSymbol);
    }

    private bool TryGetAkcssPropertyOwner(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol,
        string propertyReference,
        out INamedTypeSymbol ownerType,
        out string propertyName)
    {
        var lastDot = propertyReference.LastIndexOf('.');
        if (lastDot > 0 && lastDot < propertyReference.Length - 1)
        {
            propertyName = propertyReference[(lastDot + 1)..].Trim();
            var ownerText = propertyReference[..lastDot].Trim();
            if (string.IsNullOrWhiteSpace(propertyName) ||
                string.IsNullOrWhiteSpace(ownerText))
            {
                ownerType = null!;
                return false;
            }

            try
            {
                var binding = BindCSharpType(
                    CSharpSyntaxFactory.ParseTypeName(ownerText),
                    GetAkcssCSharpUsingDirectives(assignment));
                if (binding.TypeSymbol is INamedTypeSymbol boundOwner)
                {
                    ownerType = boundOwner;
                    return true;
                }
            }
            catch (ArgumentException)
            {
            }

            ownerType = null!;
            return false;
        }

        propertyName = propertyReference;
        return TryGetAkcssPropertyOwner(containingSymbol, out ownerType);
    }

    private bool TryGetAkcssPropertyOwner(
        IAkcssSymbol containingSymbol,
        out INamedTypeSymbol ownerType)
    {
        if (containingSymbol.TargetType.Symbol is INamedTypeSymbol targetType)
        {
            ownerType = targetType;
            return true;
        }

        return TryGetDefaultAkcssStyleTargetType(out ownerType);
    }

    private bool TryGetDefaultAkcssStyleTargetType(out INamedTypeSymbol targetType)
    {
        targetType = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "Avalonia.Controls.Primitives.TemplatedControl")!;
        return targetType != null;
    }

    internal bool TryBindAvaloniaNamedColor(
        string colorName,
        IAkcssSymbol containingSymbol,
        out CSharpBindingResult binding)
    {
        binding = CSharpBindingResult.Empty;
        if (string.IsNullOrWhiteSpace(colorName) ||
            !IsValidCSharpIdentifier(colorName))
        {
            return false;
        }

        var expression = CSharpSyntaxFactory.ParseExpression(
            "global::Avalonia.Media.Colors." + colorName);
        binding = BindAkcssExpression(expression, containingSymbol);
        return binding.Symbol is RoslynPropertySymbol property &&
            property.ContainingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            "global::Avalonia.Media.Colors" &&
            binding.TypeSymbol != null &&
            IsAvaloniaColorType(binding.TypeSymbol);
    }

    internal bool TryBindExpectedTypeStaticMember(
        CSharp.ExpressionSyntax expression,
        ITypeSymbol expectedType,
        IAkcssSymbol containingSymbol,
        out CSharpBindingResult binding)
    {
        binding = CSharpBindingResult.Empty;
        if (!TryGetExpectedTypeMemberName(expression, expectedType, out var memberName) ||
            !IsValidCSharpIdentifier(memberName) ||
            !TryGetStaticMemberOwnerType(expectedType, out var ownerType))
        {
            return false;
        }

        foreach (var memberExpressionText in GetExpectedTypeStaticMemberExpressionCandidates(ownerType, memberName))
        {
            var memberExpression = CSharpSyntaxFactory.ParseExpression(memberExpressionText);
            var candidateBinding = BindAkcssExpression(memberExpression, containingSymbol);
            if (candidateBinding.Symbol != null &&
                candidateBinding.TypeSymbol != null &&
                IsSameType(candidateBinding.TypeSymbol, ownerType))
            {
                binding = candidateBinding;
                return true;
            }
        }

        return false;
    }

    internal bool TryAcceptExpectedTypeCastExpression(
        CSharp.ExpressionSyntax expression,
        ITypeSymbol expectedType,
        IAkcssSymbol containingSymbol)
    {
        if (expression is not CSharp.CastExpressionSyntax castExpression ||
            !TryGetStaticMemberOwnerType(expectedType, out var ownerType))
        {
            return false;
        }

        var typeBinding = BindCSharpType(
            castExpression.Type,
            GetAkcssCSharpUsingDirectives(containingSymbol));
        return typeBinding.TypeSymbol != null &&
            IsSameType(typeBinding.TypeSymbol, ownerType);
    }

    internal static bool TryGetExpectedTypeMemberName(
        CSharp.ExpressionSyntax expression,
        ITypeSymbol expectedType,
        out string memberName)
    {
        memberName = string.Empty;
        switch (expression)
        {
            case CSharp.IdentifierNameSyntax identifier:
                memberName = identifier.Identifier.ValueText;
                return memberName.Length > 0;

            case CSharp.MemberAccessExpressionSyntax
            {
                Expression: CSharp.IdentifierNameSyntax receiver,
                Name: CSharp.IdentifierNameSyntax name,
            } when receiver.Identifier.ValueText == expectedType.Name:
                memberName = name.Identifier.ValueText;
                return memberName.Length > 0;

            default:
                return false;
        }
    }

    private static bool TryGetStaticMemberOwnerType(
        ITypeSymbol expectedType,
        out INamedTypeSymbol ownerType)
    {
        if (expectedType is INamedTypeSymbol
            {
                OriginalDefinition.SpecialType: SpecialType.System_Nullable_T,
                TypeArguments.Length: 1,
            } nullableType &&
            nullableType.TypeArguments[0] is INamedTypeSymbol nullableArgument)
        {
            ownerType = nullableArgument;
            return true;
        }

        if (expectedType is INamedTypeSymbol namedType)
        {
            ownerType = namedType;
            return true;
        }

        ownerType = null!;
        return false;
    }

    private IEnumerable<string> GetExpectedTypeStaticMemberExpressionCandidates(
        INamedTypeSymbol ownerType,
        string memberName)
    {
        var ownerTypeText = ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        yield return ownerTypeText + "." + memberName;
        yield return ownerType.Name + "." + memberName;

        if (TryGetCompanionStaticMemberOwnerType(ownerType, out var companionOwnerType))
        {
            yield return companionOwnerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                "." + memberName;
            yield return companionOwnerType.Name + "." + memberName;
        }
    }

    private bool TryGetCompanionStaticMemberOwnerType(
        INamedTypeSymbol ownerType,
        out INamedTypeSymbol companionOwnerType)
    {
        companionOwnerType = null!;
        var namespaceText = ownerType.ContainingNamespace.IsGlobalNamespace
            ? string.Empty
            : ownerType.ContainingNamespace.ToDisplayString();
        var metadataName = string.IsNullOrEmpty(namespaceText)
            ? ownerType.Name + "s"
            : namespaceText + "." + ownerType.Name + "s";

        companionOwnerType = Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName)!;
        return companionOwnerType != null;
    }

    private static bool IsValidCSharpIdentifier(string text)
    {
        if (text.Length == 0 ||
            !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(text[0]))
        {
            return false;
        }

        for (var i = 1; i < text.Length; i++)
        {
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(text[i]))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool TryCreateAkcssThicknessValue(
        CSharp.ExpressionSyntax expression,
        string rawText,
        out object? thickness,
        out bool isThicknessTuple)
    {
        if (TryParseAkcssDouble(expression, out var uniformValue))
        {
            thickness = new AkcssThicknessValue(
                uniformValue,
                uniformValue,
                uniformValue,
                uniformValue);
            isThicknessTuple = false;
            return true;
        }

        if (expression is CSharp.TupleExpressionSyntax tupleExpression)
        {
            isThicknessTuple = true;
            if (TryCreateAkcssThicknessValue(tupleExpression, out thickness))
            {
                return true;
            }

            var tupleText = rawText.Trim();
            return tupleText.StartsWith("(", StringComparison.Ordinal) &&
                tupleText.EndsWith(")", StringComparison.Ordinal) &&
                tupleText.IndexOf(':') >= 0 &&
                TryCreateNamedAkcssThicknessValue(tupleText[1..^1], out thickness);
        }

        var text = rawText.Trim();
        if (!text.StartsWith("(", StringComparison.Ordinal) ||
            !text.EndsWith(")", StringComparison.Ordinal) ||
            text.IndexOf(':') < 0)
        {
            thickness = default;
            isThicknessTuple = false;
            return false;
        }

        isThicknessTuple = true;
        return TryCreateNamedAkcssThicknessValue(text[1..^1], out thickness);
    }

    private static bool TryCreateAkcssThicknessValue(
        CSharp.TupleExpressionSyntax tupleExpression,
        out object? thickness)
    {
        thickness = null;
        var arguments = tupleExpression.Arguments;

        if (arguments.Count == 2 &&
            arguments[0].NameColon == null &&
            arguments[1].NameColon == null)
        {
            return TryCreateAkcssThicknessValue(
                arguments[0].Expression,
                arguments[1].Expression,
                arguments[0].Expression,
                arguments[1].Expression,
                out thickness);
        }

        if (arguments.Count == 4 &&
            arguments.All(static argument => argument.NameColon == null))
        {
            return TryCreateAkcssThicknessValue(
                arguments[0].Expression,
                arguments[1].Expression,
                arguments[2].Expression,
                arguments[3].Expression,
                out thickness);
        }

        var zero = CreateAkcssZeroExpression();
        var left = zero;
        var top = zero;
        var right = zero;
        var bottom = zero;
        foreach (var argument in arguments)
        {
            if (argument.NameColon == null)
            {
                return false;
            }

            switch (argument.NameColon.Name.Identifier.ValueText)
            {
                case "left":
                    left = argument.Expression;
                    break;
                case "top":
                    top = argument.Expression;
                    break;
                case "right":
                    right = argument.Expression;
                    break;
                case "bottom":
                    bottom = argument.Expression;
                    break;
                case "horizontal":
                    left = right = argument.Expression;
                    break;
                case "vertical":
                    top = bottom = argument.Expression;
                    break;
                default:
                    return false;
            }
        }

        return arguments.Count > 0 &&
            TryCreateAkcssThicknessValue(left, top, right, bottom, out thickness);
    }

    private static bool TryCreateNamedAkcssThicknessValue(
        string text,
        out object? thickness)
    {
        thickness = null;
        var zero = CreateAkcssZeroExpression();
        var left = zero;
        var top = zero;
        var right = zero;
        var bottom = zero;

        foreach (var component in text.Split(','))
        {
            var separatorIndex = component.IndexOf(':');
            if (separatorIndex <= 0 ||
                separatorIndex == component.Length - 1)
            {
                return false;
            }

            CSharp.ExpressionSyntax expression;
            try
            {
                expression = CSharpSyntaxFactory.ParseExpression(
                    component[(separatorIndex + 1)..].Trim());
            }
            catch (ArgumentException)
            {
                return false;
            }

            switch (component[..separatorIndex].Trim())
            {
                case "left":
                    left = expression;
                    break;
                case "top":
                    top = expression;
                    break;
                case "right":
                    right = expression;
                    break;
                case "bottom":
                    bottom = expression;
                    break;
                case "horizontal":
                    left = right = expression;
                    break;
                case "vertical":
                    top = bottom = expression;
                    break;
                default:
                    return false;
            }
        }

        return TryCreateAkcssThicknessValue(left, top, right, bottom, out thickness);
    }

    private static bool TryCreateAkcssThicknessValue(
        CSharp.ExpressionSyntax left,
        CSharp.ExpressionSyntax top,
        CSharp.ExpressionSyntax right,
        CSharp.ExpressionSyntax bottom,
        out object thickness)
    {
        if (TryParseAkcssDouble(left, out var leftValue) &&
            TryParseAkcssDouble(top, out var topValue) &&
            TryParseAkcssDouble(right, out var rightValue) &&
            TryParseAkcssDouble(bottom, out var bottomValue))
        {
            thickness = new AkcssThicknessValue(leftValue, topValue, rightValue, bottomValue);
            return true;
        }

        thickness = new AkcssThicknessExpressionValue(left, top, right, bottom);
        return true;
    }

    private static CSharp.ExpressionSyntax CreateAkcssZeroExpression()
    {
        return CSharpSyntaxFactory.LiteralExpression(
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression,
            CSharpSyntaxFactory.Literal(0));
    }

    private static bool TryParseAkcssDouble(
        CSharp.ExpressionSyntax expression,
        out double value)
    {
        return double.TryParse(
            expression.ToString(),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    internal bool TryCreateAkcssAmxInvocationValue(
        CSharp.ExpressionSyntax expression,
        out AkcssAmxInvocationValue value)
    {
        foreach (var invocation in expression
                     .DescendantNodesAndSelf()
                     .OfType<CSharp.InvocationExpressionSyntax>())
        {
            if (!TryGetAkcssAmxInvocationKind(invocation.Expression, out var kind, out var genericName))
            {
                continue;
            }

            var typeArgument = default(CSharpSymbolDefinition);
            if (genericName?.TypeArgumentList.Arguments.Count == 1)
            {
                var binding = BindCSharpType(genericName.TypeArgumentList.Arguments[0]);
                if (binding.TypeSymbol != null)
                {
                    typeArgument = new CSharpSymbolDefinition(binding.TypeSymbol);
                }
            }

            value = new AkcssAmxInvocationValue(
                kind,
                typeArgument,
                invocation.ArgumentList.Arguments.Select(static argument => argument.Expression).ToImmutableArray(),
                methodSymbol: null);
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetAkcssAmxInvocationKind(
        CSharp.ExpressionSyntax expression,
        out AkcssAmxInvocationKind kind,
        out CSharp.GenericNameSyntax? genericName)
    {
        kind = AkcssAmxInvocationKind.None;
        genericName = null;

        if (expression is not CSharp.MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Expression.ToString() != "Amx")
        {
            return false;
        }

        var methodName = memberAccess.Name switch
        {
            CSharp.GenericNameSyntax name => name.Identifier.ValueText,
            CSharp.IdentifierNameSyntax name => name.Identifier.ValueText,
            _ => string.Empty,
        };

        genericName = memberAccess.Name as CSharp.GenericNameSyntax;
        kind = methodName switch
        {
            "Extend" => AkcssAmxInvocationKind.Extend,
            "StaticResource" => AkcssAmxInvocationKind.StaticResource,
            "DynamicResource" => AkcssAmxInvocationKind.DynamicResource,
            _ => AkcssAmxInvocationKind.None,
        };

        return kind != AkcssAmxInvocationKind.None;
    }

    internal bool IsAkcssColorPropertyType(ITypeSymbol type)
    {
        return IsAvaloniaColorType(type) || IsAvaloniaBrushType(type);
    }

    internal bool IsAvaloniaColorType(ITypeSymbol type)
    {
        return TryGetAvaloniaColorType(out var colorType) &&
            IsSameType(type, colorType);
    }

    internal bool IsAvaloniaBrushType(ITypeSymbol type)
    {
        return TryGetAvaloniaBrushType(out var brushType) &&
            IsAssignableTo(type, brushType);
    }

    internal bool IsAvaloniaThicknessType(ITypeSymbol type)
    {
        return TryGetAvaloniaThicknessType(out var thicknessType) &&
            IsSameType(type, thicknessType);
    }

    internal bool TryGetAvaloniaColorType(out INamedTypeSymbol colorType)
    {
        colorType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Media.Color")!;
        return colorType != null;
    }

    private bool TryGetAvaloniaBrushType(out INamedTypeSymbol brushType)
    {
        brushType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Media.IBrush")!;
        return brushType != null;
    }

    private bool TryGetAvaloniaThicknessType(out INamedTypeSymbol thicknessType)
    {
        thicknessType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Thickness")!;
        return thicknessType != null;
    }

    private AkburaSemanticDiagnostic CreateAkcssPropertyNotFoundDiagnostic(
        AkcssAssignmentSyntax syntax,
        string propertyName,
        INamedTypeSymbol ownerType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssPropertyNotFound,
            [propertyName, ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]);
    }

    internal AkburaSemanticDiagnostic CreateAkcssInvalidColorDiagnostic(
        AkcssAssignmentSyntax syntax,
        string colorText,
        string propertyName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidColor,
            [colorText, propertyName]);
    }

    internal AkburaSemanticDiagnostic CreateAkcssInvalidThicknessDiagnostic(
        AkcssAssignmentSyntax syntax,
        string tupleText,
        string propertyName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidThickness,
            [tupleText, propertyName]);
    }

    private AkburaSemanticDiagnostic CreateAkcssExpressionErrorDiagnostic(
        AkburaSyntax syntax,
        string expressionText,
        Diagnostic diagnostic)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssExpressionError,
            [expressionText, diagnostic.GetMessage()]);
    }

    private static AkburaSemanticDiagnostic CreateAkcssValueCannotConvertDiagnostic(
        AkcssAssignmentSyntax syntax,
        string propertyName,
        ITypeSymbol sourceType,
        ITypeSymbol targetType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssValueCannotConvert,
            [
                propertyName,
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
            ]);
    }

    private AkburaSemanticDiagnostic CreateAkcssApplyItemNotFoundDiagnostic(
        AkcssApplyDirectiveSyntax syntax,
        string item)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemNotFound,
            [item]);
    }

    private AkburaSemanticDiagnostic CreateAkcssApplyItemAmbiguousDiagnostic(
        AkcssApplyDirectiveSyntax syntax,
        string item)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssApplyItemAmbiguous,
            [item]);
    }

    internal AkburaSemanticDiagnostic CreateAkcssInterceptTypeNotFoundDiagnostic(
        AkcssInterceptDirectiveSyntax syntax,
        string typeName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptTypeNotFound,
            [typeName]);
    }

    internal AkburaSemanticDiagnostic CreateAkcssInterceptTypeInvalidDiagnostic(
        AkcssInterceptDirectiveSyntax syntax,
        string typeName,
        string expectedBaseType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptTypeInvalid,
            [typeName, expectedBaseType]);
    }

    private AkburaSemanticDiagnostic CreateAkcssInterceptIgnoresMemberDiagnostic(
        AkcssBodyMemberSyntax syntax,
        IAkcssSymbol containingSymbol)
    {
        var memberText = syntax.ToFullString().Trim();
        var styleName = string.IsNullOrEmpty(containingSymbol.ClassName)
            ? containingSymbol.MetadataName
            : containingSymbol.ClassName;
        var interceptText = containingSymbol.InterceptType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptIgnoresMember,
            [memberText, styleName, interceptText],
            AkburaDiagnosticSeverity.Warning);
    }

    private AkburaSemanticDiagnostic CreateAkcssSelectorTargetNotFoundDiagnostic(
        AkburaSyntax syntax,
        string targetTypeName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssSelectorTargetNotFound,
            [targetTypeName]);
    }

    internal bool TryResolveAkcssTargetType(
        CSharpTypeSyntax? targetTypeSyntax,
        out CSharpSymbolDefinition targetType)
    {
        targetType = default;
        if (targetTypeSyntax == null)
        {
            return true;
        }

        var targetTypeName = targetTypeSyntax.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(targetTypeName))
        {
            return false;
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = targetTypeSyntax.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            csharpType = CSharpSyntaxFactory.ParseTypeName(targetTypeName);
        }

        var binding = BindCSharpType(csharpType, GetAkcssCSharpUsingDirectives(targetTypeSyntax));
        if (binding.TypeSymbol is INamedTypeSymbol boundType &&
            IsAvaloniaControlTargetType(boundType))
        {
            targetType = new CSharpSymbolDefinition(boundType);
            return true;
        }

        var avaloniaType = Compilation.CSharpCompilation.GetTypeByMetadataName(
            "Avalonia.Controls." + targetTypeName);
        if (avaloniaType != null &&
            IsAvaloniaControlTargetType(avaloniaType))
        {
            targetType = new CSharpSymbolDefinition(avaloniaType);
            return true;
        }

        return false;
    }

    private bool IsAvaloniaControlTargetType(INamedTypeSymbol type)
    {
        return TryGetAvaloniaControlType(out var controlType) &&
            IsAssignableTo(type, controlType);
    }

    internal bool IsAkcssInterceptRuntimeType(
        INamedTypeSymbol type,
        IAkcssSymbol containingSymbol,
        out string expectedBaseType)
    {
        expectedBaseType = containingSymbol is ITailwindUtilitySymbol
            ? "Akbura.Akcss.AkcssUtility"
            : "Akbura.Akcss.AkcssClass";

        var runtimeType = Compilation.CSharpCompilation.GetTypeByMetadataName(expectedBaseType);
        return runtimeType != null &&
            IsAssignableTo(type, runtimeType);
    }

    internal static CSharp.ParameterListSyntax? GetCSharpParameterList(
        CSharpParameterListSyntax parameterListSyntax)
    {
        return parameterListSyntax.GetRawCSharpParameterList();
    }

    internal static CSharp.ArgumentListSyntax? GetCSharpArgumentList(
        CSharpArgumentListSyntax argumentListSyntax)
    {
        return argumentListSyntax.GetRawCSharpArgumentList();
    }

    internal static StateBindingKind GetStateBindingKind(StateInitializerSyntax initializer)
    {
        if (initializer.Kind != AkburaSyntaxKind.BindableStateInitializer)
        {
            return StateBindingKind.None;
        }

        var bindableInitializer = Unsafe.As<BindableStateInitializerSyntax>(initializer);
        return bindableInitializer.BindingKeyword.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.InToken => StateBindingKind.In,
            Akbura.Language.Syntax.SyntaxKind.OutToken => StateBindingKind.Out,
            Akbura.Language.Syntax.SyntaxKind.BindToken => StateBindingKind.Bind,
            _ => StateBindingKind.None,
        };
    }

    internal static ParamBindingKind GetParamBindingKind(ParamDeclarationSyntax paramDeclaration)
    {
        return paramDeclaration.BindingKeyword.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.BindToken => ParamBindingKind.Bind,
            Akbura.Language.Syntax.SyntaxKind.OutToken => ParamBindingKind.Out,
            _ => ParamBindingKind.Default,
        };
    }

    internal ImmutableArray<AkburaSemanticDiagnostic> CreateStateBindingDiagnostics(
        StateDeclarationSyntax stateDeclaration,
        StateBindingKind bindingKind,
        CSharpSymbolDefinition stateType,
        CSharpBindingResult initializerBinding)
    {
        if (bindingKind == StateBindingKind.None)
        {
            return ImmutableArray<AkburaSemanticDiagnostic>.Empty;
        }

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        if (!initializerBinding.IsBindingPath)
        {
            diagnosticsBuilder.Add(CreateStateBindingExpressionExpectedDiagnostic(stateDeclaration));
            return diagnosticsBuilder.ToImmutable();
        }

        if (RequiresWritableStateBindingTarget(bindingKind) &&
            !IsWritableStateBindingTarget(initializerBinding.Symbol))
        {
            diagnosticsBuilder.Add(CreateStateBindingTargetNotWritableDiagnostic(stateDeclaration));
        }

        if (RequiresObservableStateBindingSource(bindingKind) &&
            !CanObserveStateBindingSource(initializerBinding, stateType))
        {
            diagnosticsBuilder.Add(CreateStateBindingSourceNotObservableDiagnostic(
                stateDeclaration,
                stateType,
                initializerBinding));
        }

        return diagnosticsBuilder.ToImmutable();
    }

    private static bool RequiresWritableStateBindingTarget(StateBindingKind bindingKind)
    {
        return bindingKind is StateBindingKind.Bind or StateBindingKind.In;
    }

    private static bool RequiresObservableStateBindingSource(StateBindingKind bindingKind)
    {
        return bindingKind is StateBindingKind.Bind or StateBindingKind.Out;
    }

    private static bool IsWritableStateBindingTarget(RoslynSymbol? symbol)
    {
        return symbol switch
        {
            RoslynPropertySymbol property => property.SetMethod?.DeclaredAccessibility == Accessibility.Public,
            RoslynFieldSymbol field => !field.IsReadOnly && !field.IsConst,
            null => true,
            _ => false,
        };
    }

    private bool CanObserveStateBindingSource(
        CSharpBindingResult binding,
        CSharpSymbolDefinition stateType)
    {
        if (binding.TypeSymbol != null &&
            TryGetIObservableElementType(binding.TypeSymbol, out var observableElementType) &&
            (stateType.Symbol is not ITypeSymbol expectedType ||
             IsSameType(observableElementType, expectedType)))
        {
            return true;
        }

        var containingType = GetBindingSourceContainingType(binding.Symbol) ??
            binding.ReceiverType as INamedTypeSymbol;

        if (containingType != null &&
            ImplementsINotifyPropertyChanged(containingType))
        {
            return true;
        }

        return binding.Symbol is RoslynFieldSymbol or RoslynPropertySymbol &&
            binding.ReceiverType == null &&
            binding.TypeSymbol != null &&
            ImplementsINotifyPropertyChanged(binding.TypeSymbol);
    }

    internal static bool IsStateBindingPath(CSharp.ExpressionSyntax expression)
    {
        return expression switch
        {
            CSharp.IdentifierNameSyntax => true,
            CSharp.ThisExpressionSyntax => true,
            CSharp.BaseExpressionSyntax => true,
            CSharp.ParenthesizedExpressionSyntax parenthesized => IsStateBindingPath(parenthesized.Expression),
            CSharp.MemberAccessExpressionSyntax memberAccess => IsStateBindingPath(memberAccess.Expression),
            CSharp.ElementAccessExpressionSyntax elementAccess => IsStateBindingPath(elementAccess.Expression),
            _ => false,
        };
    }

    private static INamedTypeSymbol? GetBindingSourceContainingType(RoslynSymbol? symbol)
    {
        return symbol switch
        {
            RoslynPropertySymbol property => property.ContainingType,
            RoslynFieldSymbol field => field.ContainingType,
            _ => null,
        };
    }

    private static bool ImplementsINotifyPropertyChanged(ITypeSymbol type)
    {
        foreach (var @interface in type.AllInterfaces)
        {
            if (@interface.Name == "INotifyPropertyChanged" &&
                @interface.ContainingNamespace.ToDisplayString() == "System.ComponentModel")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetIObservableElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            IsIObservableOfT(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (IsIObservableOfT(@interface))
            {
                elementType = @interface.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsIObservableOfT(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        return original.Name == "IObservable" &&
            original.Arity == 1 &&
            original.ContainingNamespace.ToDisplayString() == "System";
    }

    private AkburaSemanticDiagnostic CreateStateBindingSourceNotObservableDiagnostic(
        StateDeclarationSyntax stateDeclaration,
        CSharpSymbolDefinition stateType,
        CSharpBindingResult binding)
    {
        var sourceText = stateDeclaration.Initializer.Expression.ToFullString().Trim();
        var stateTypeText = stateType.IsDefault
            ? "state type"
            : stateType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        var ownerText = GetBindingSourceContainingType(binding.Symbol)?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ??
            binding.ReceiverType?.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat) ??
            "source object";

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingSourceNotObservable,
            [sourceText, stateTypeText, ownerText],
            AkburaDiagnosticSeverity.Warning);
    }

    private AkburaSemanticDiagnostic CreateStateBindingExpressionExpectedDiagnostic(
        StateDeclarationSyntax stateDeclaration)
    {
        var expressionText = stateDeclaration.Initializer.Expression.ToFullString().Trim();

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingExpressionExpected,
            [expressionText],
            AkburaDiagnosticSeverity.Error);
    }

    private AkburaSemanticDiagnostic CreateStateBindingTargetNotWritableDiagnostic(
        StateDeclarationSyntax stateDeclaration)
    {
        var targetText = stateDeclaration.Initializer.Expression.ToFullString().Trim();

        return new AkburaSemanticDiagnostic(
            stateDeclaration,
            ErrorCodes.AKBURA_SEMANTIC_StateBindingTargetNotWritable,
            [targetText],
            AkburaDiagnosticSeverity.Error);
    }

    internal AkburaPropertySymbol? CreateMarkupContentPropertySymbol(IMarkupComponentSymbol componentSymbol)
    {
        if (componentSymbol.ContentModel.ContentParameter is { } contentParameter)
        {
            return new PropertySymbol(
                contentParameter.Name,
                contentParameter.Type,
                parameter: contentParameter,
                containingSymbol: componentSymbol,
                isImplicitlyDeclared: true);
        }

        if (componentSymbol.ContentModel.ContentProperty.Symbol is not RoslynPropertySymbol contentProperty)
        {
            return null;
        }

        var avaloniaProperty = componentSymbol.ComponentType == null
            ? null
            : FindAvaloniaPropertyField(componentSymbol.ComponentType, contentProperty.Name);

        return new PropertySymbol(
            contentProperty.Name,
            componentSymbol.ContentModel.AllowedChildType,
            avaloniaPropertyDefinition: avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrPropertyDefinition: new CSharpSymbolDefinition(contentProperty),
            containingSymbol: componentSymbol,
            isImplicitlyDeclared: true);
    }

    internal static ITypeSymbol? GetMarkupContentTargetType(MarkupContentModel contentModel)
    {
        return contentModel.AllowedChildType.Symbol is ITypeSymbol type &&
            type.SpecialType != SpecialType.System_Object
                ? type
                : null;
    }

    internal static bool HasElementContent(MarkupElementSyntax markupElement)
    {
        foreach (var content in markupElement.Body)
        {
            if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool TryCreateMarkupContentValueExpression(
        MarkupElementSyntax markupElement,
        MarkupWhitespaceMode whitespaceMode,
        out CSharp.ExpressionSyntax expression,
        out string? literalValue,
        out bool isSynthesizedString,
        out bool hasText,
        out MarkupContentSyntax diagnosticSyntax)
    {
        expression = null!;
        literalValue = null;
        isSynthesizedString = false;
        hasText = false;
        diagnosticSyntax = null!;

        var normalizer = new MarkupWhitespaceNormalizer(whitespaceMode);

        CSharp.ExpressionSyntax? singleExpression = null;
        var expressionCount = 0;

        foreach (var content in markupElement.Body)
        {
            switch (content.Kind)
            {
                case AkburaSyntaxKind.MarkupTextLiteralSyntax:
                    {
                        var textLiteral =
                            Unsafe.As<MarkupTextLiteralSyntax>(content);

                        normalizer.AppendText(
                            textLiteral.ToFullString());

                        if (normalizer.HasText)
                        {
                            diagnosticSyntax ??= textLiteral;
                        }

                        break;
                    }

                case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
                    {
                        var inlineExpression =
                            Unsafe.As<MarkupInlineExpressionSyntax>(content);

                        var parsedExpression =
                            ParseInlineExpression(inlineExpression.Expression);

                        var expressionText =
                            parsedExpression?.ToFullString() ??
                            inlineExpression.Expression.Expression
                                .ToFullString();

                        diagnosticSyntax ??= inlineExpression;
                        expressionCount++;
                        singleExpression ??=
                            parsedExpression ??
                            CSharpSyntaxFactory.ParseExpression(
                                expressionText);

                        normalizer.AppendExpression(expressionText);
                        break;
                    }
            }
        }

        hasText = normalizer.HasText;

        if (diagnosticSyntax == null)
        {
            return false;
        }

        if (!hasText && expressionCount == 1)
        {
            expression = singleExpression!;
            return true;
        }

        if (expressionCount == 0)
        {
            literalValue = normalizer.LiteralText;

            if (literalValue.Length == 0)
            {
                return false;
            }

            expression = CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind
                    .StringLiteralExpression,
                CSharpSyntaxFactory.Literal(literalValue));

            return true;
        }

        isSynthesizedString = true;

        expression = CSharpSyntaxFactory.ParseExpression(
            "$@\"" +
            normalizer.InterpolatedText +
            "\"");

        return true;
    }

    private static string EscapeInterpolatedStringText(string text)
    {
        return text
            .Replace("{", "{{")
            .Replace("}", "}}")
            .Replace("\"", "\"\"");
    }

    internal void AddMarkupContentValueDiagnostics(
        MarkupContentSyntax syntax,
        MarkupContentModel contentModel,
        CSharpBindingResult binding,
        bool hasText,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (hasText && !contentModel.AllowsText)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                syntax,
                new CSharpSymbolDefinition(Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String)),
                contentModel));
            return;
        }

        if (binding.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            return;
        }

        if (contentModel.AllowedChildType.Symbol is not ITypeSymbol targetType)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                syntax,
                binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
                contentModel));
            return;
        }

        if (targetType.SpecialType == SpecialType.System_Object)
        {
            return;
        }

        var conversion = binding.Conversion;
        if (conversion.TargetType != null)
        {
            var conversionSourceType = conversion.SourceType ?? binding.TypeSymbol as ITypeSymbol;
            if (conversion.IsImplicit ||
                conversionSourceType == null ||
                IsSameType(conversionSourceType, targetType))
            {
                return;
            }

            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                syntax,
                new CSharpSymbolDefinition(conversionSourceType),
                contentModel));
            return;
        }

        if (binding.TypeSymbol is not ITypeSymbol sourceType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
            syntax,
            new CSharpSymbolDefinition(sourceType),
            contentModel));
    }

    private AkburaSymbolInfo ResolveMarkupComponent(MarkupElementSyntax markupElement)
    {
        var startTag = markupElement.StartTag;
        if (startTag == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentName = startTag.Name;
        var componentNameText = componentName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(componentNameText))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (TryResolveMarkupPropertyElement(
                markupElement,
                componentNameText,
                out var propertyElementSymbolInfo))
        {
            return propertyElementSymbolInfo;
        }

        CSharp.TypeSyntax csharpType;
        try
        {
            csharpType = componentName.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (TryResolveAkburaMarkupComponent(markupElement, componentNameText, out var akburaComponentSymbol))
        {
            return AkburaSymbolInfo.Success(akburaComponentSymbol);
        }

        var binding = BindCSharpType(csharpType);
        if (binding.TypeSymbol is INamedTypeSymbol namedType &&
            namedType.TypeKind != TypeKind.Error)
        {
            var contentModel = CreateMarkupContentModel(namedType, markupElement);
            var symbol = new MarkupComponentSymbol(
                componentNameText,
                new CSharpSymbolDefinition(namedType),
                contentModel);
            SetCachedSymbolInfo(markupElement, AkburaSymbolInfo.Success(symbol));

            var children = CreateMarkupChildren(
                markupElement,
                contentModel,
                out var diagnostics,
                namedType);
            SetSemanticDiagnostics(markupElement, diagnostics);
            symbol.SetChildren(children);
            symbol.SetAttributeOperations(CreateMarkupAttributeOperations(markupElement));

            return AkburaSymbolInfo.Success(symbol);
        }

        SetSemanticDiagnostics(markupElement, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        var candidates = CreateMarkupComponentCandidates(componentNameText, binding.CandidateSymbols);
        if (candidates.Length > 0)
        {
            return AkburaSymbolInfo.Candidates(candidates, binding.CandidateReason);
        }

        return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
    }

    private bool TryResolveMarkupPropertyElement(
        MarkupElementSyntax markupElement,
        string elementName,
        out AkburaSymbolInfo symbolInfo)
    {
        symbolInfo = default;
        var separator = elementName.LastIndexOf('.');
        if (separator <= 0 || separator == elementName.Length - 1)
        {
            return false;
        }

        var containingElement = GetParentMarkupElement(markupElement);
        if (containingElement == null ||
            GetSymbolInfo(containingElement).Symbol is not IMarkupComponentSymbol containingComponent)
        {
            return false;
        }

        var ownerName = elementName[..separator];
        var propertyName = elementName[(separator + 1)..].Trim();
        if (containingComponent.AkburaComponent is { } akburaComponent &&
            IsMarkupPropertyElementOwner(akburaComponent, ownerName))
        {
            var parameter = FindComponentParameter(containingComponent, propertyName);
            if (parameter != null)
            {
                var parameterProperty = new PropertySymbol(
                    propertyName,
                    parameter.Type,
                    parameter: parameter,
                    containingSymbol: containingComponent);
                SetSemanticDiagnosticsIfAbsent(
                    markupElement,
                    ImmutableArray<AkburaSemanticDiagnostic>.Empty);
                symbolInfo = AkburaSymbolInfo.Success(parameterProperty);
                return true;
            }

            if (containingComponent.ComponentType == null)
            {
                SetSemanticDiagnostics(
                    markupElement,
                    ImmutableArray.Create(
                        CreateMarkupPropertyNotFoundDiagnostic(
                            markupElement,
                            propertyName,
                            containingComponent)));
                symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
                return true;
            }
        }

        if (containingComponent.ComponentType is not { } componentType ||
            !IsMarkupPropertyElementOwner(componentType, ownerName))
        {
            return false;
        }

        var clrProperty = FindPublicClrProperty(componentType, propertyName);
        var avaloniaProperty = FindAvaloniaPropertyField(componentType, propertyName);
        if (clrProperty == null && avaloniaProperty == null)
        {
            SetSemanticDiagnostics(
                markupElement,
                ImmutableArray.Create(HasInaccessiblePropertyMember(componentType, propertyName)
                    ? CreateInaccessibleMemberDiagnostic(markupElement, propertyName, componentType)
                    : CreateMarkupPropertyNotFoundDiagnostic(markupElement, propertyName, componentType)));
            symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
            return true;
        }

        var property = new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(
                parameter: null,
                command: null,
                clrProperty,
                avaloniaProperty,
                attachedProperty: null),
            avaloniaPropertyDefinition: avaloniaProperty == null
                ? default
                : new CSharpSymbolDefinition(avaloniaProperty),
            clrPropertyDefinition: clrProperty == null
                ? default
                : new CSharpSymbolDefinition(clrProperty),
            containingSymbol: containingComponent);
        SetSemanticDiagnosticsIfAbsent(markupElement, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        symbolInfo = AkburaSymbolInfo.Success(property);
        return true;
    }

    internal MarkupContentModel CreateMarkupPropertyElementContentModel(
        AkburaPropertySymbol property)
    {
        if (property.Parameter is { } parameter)
        {
            return CreateAkburaParameterContentModel(parameter);
        }

        if (property.Type.Symbol is not ITypeSymbol propertyType)
        {
            return default;
        }

        if (property.ClrPropertyDefinition.Symbol is RoslynPropertySymbol clrProperty &&
            BindingSession.MarkupTemplateContent.IsTemplateContentProperty(clrProperty) &&
            Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control") is { } controlType)
        {
            return new MarkupContentModel(
                property.CSharpDefinition,
                new CSharpSymbolDefinition(controlType),
                isCollection: false,
                allowsText: false);
        }

        if (TryGetIListElementType(propertyType, out var elementType))
        {
            return new MarkupContentModel(
                property.CSharpDefinition,
                new CSharpSymbolDefinition(elementType),
                isCollection: true,
                allowsText: AllowsTextContent(elementType));
        }

        return new MarkupContentModel(
            property.CSharpDefinition,
            property.Type,
            isCollection: false,
            allowsText: AllowsTextContent(propertyType));
    }

    internal static MarkupElementSyntax? GetParentMarkupElement(
        MarkupElementSyntax markupElement)
    {
        for (var parent = markupElement.Parent; parent != null; parent = parent.Parent)
        {
            if (parent.Kind == AkburaSyntaxKind.MarkupElementSyntax)
            {
                return Unsafe.As<MarkupElementSyntax>(parent);
            }
        }

        return null;
    }

    internal static bool IsMarkupPropertyElementOwner(
        INamedTypeSymbol componentType,
        string ownerName)
    {
        var simpleName = GetSimpleMarkupPropertyElementOwnerName(ownerName);

        for (var current = componentType; current != null; current = current.BaseType)
        {
            if (string.Equals(current.Name, simpleName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            if (string.Equals(@interface.Name, simpleName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsMarkupPropertyElementOwner(
        IAkburaComponentSymbol component,
        string ownerName)
    {
        return string.Equals(
            component.Name,
            GetSimpleMarkupPropertyElementOwnerName(ownerName),
            StringComparison.Ordinal);
    }

    private static string GetSimpleMarkupPropertyElementOwnerName(string ownerName)
    {
        var simpleName = ownerName.Trim();
        var aliasSeparator = simpleName.LastIndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator >= 0)
        {
            simpleName = simpleName[(aliasSeparator + 2)..];
        }

        var namespaceSeparator = simpleName.LastIndexOf('.');
        if (namespaceSeparator >= 0)
        {
            simpleName = simpleName[(namespaceSeparator + 1)..];
        }

        var genericStart = simpleName.IndexOfAny(['{', '<']);
        return genericStart < 0
            ? simpleName
            : simpleName[..genericStart];
    }

    private bool TryResolveAkburaMarkupComponent(
        MarkupElementSyntax markupElement,
        string componentNameText,
        out IMarkupComponentSymbol symbol)
    {
        foreach (var candidateMetadataName in GetAkburaComponentCandidateMetadataNames(markupElement.StartTag!.Name))
        {
            foreach (var syntaxTree in Compilation.SyntaxTrees)
            {
                if (ReferenceEquals(syntaxTree, SyntaxTree))
                {
                    continue;
                }

                var metadataName = GetAkburaComponentMetadataName(syntaxTree);
                if (metadataName.Length == 0 ||
                    metadataName != candidateMetadataName)
                {
                    continue;
                }

                var componentSemanticModel = Compilation.GetSemanticModel(syntaxTree);
                if (componentSemanticModel.GetSymbolInfo(syntaxTree.GetRoot()).Symbol is not IAkburaComponentSymbol componentSymbol)
                {
                    continue;
                }

                symbol = CreateAkburaMarkupComponentUsage(
                    markupElement,
                    componentNameText,
                    componentSymbol);
                return true;
            }

            foreach (var componentSymbol in
                     Compilation.GetReferencedComponentSymbols(candidateMetadataName))
            {
                symbol = CreateAkburaMarkupComponentUsage(
                    markupElement,
                    componentNameText,
                    componentSymbol);
                return true;
            }
        }

        symbol = null!;
        return false;
    }

    private IMarkupComponentSymbol CreateAkburaMarkupComponentUsage(
        MarkupElementSyntax markupElement,
        string componentNameText,
        IAkburaComponentSymbol componentSymbol)
    {
        var contentModel = componentSymbol.ComponentType is { } componentType
            ? CreateMarkupContentModel(componentType, markupElement)
            : default;
        if (contentModel.IsDefault)
        {
            contentModel = componentSymbol.ContentModel.IsDefault
                ? CreateAkburaParameterContentModel(componentSymbol.Parameters)
                : componentSymbol.ContentModel;
        }
        var usageSymbol = new MarkupComponentSymbol(
            componentNameText,
            componentSymbol.CSharpDefinition,
            contentModel,
            children: ImmutableArray<MarkupChildContent>.Empty,
            akburaComponent: componentSymbol);
        SetCachedSymbolInfo(markupElement, AkburaSymbolInfo.Success(usageSymbol));
        var children = CreateMarkupChildren(
            markupElement,
            contentModel,
            out var diagnostics,
            componentSymbol.ComponentType);
        SetSemanticDiagnostics(markupElement, diagnostics);
        usageSymbol.SetChildren(children);
        usageSymbol.SetAttributeOperations(CreateMarkupAttributeOperations(markupElement));

        return usageSymbol;
    }

    private ImmutableArray<IMarkupAttributeOperation> CreateMarkupAttributeOperations(
        MarkupElementSyntax markupElement)
    {
        if (markupElement.StartTag == null)
        {
            return ImmutableArray<IMarkupAttributeOperation>.Empty;
        }

        using var builder = ImmutableArrayBuilder<IMarkupAttributeOperation>.Rent();
        foreach (var attribute in markupElement.StartTag.Attributes)
        {
            if (GetOperation(attribute) is IMarkupAttributeOperation operation)
            {
                builder.Add(operation);
            }
        }

        return builder.ToImmutable();
    }

    private IEnumerable<string> GetAkburaComponentCandidateMetadataNames(
        MarkupComponentNameSyntax componentName)
    {
        var nameText = componentName.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(nameText))
        {
            yield break;
        }

        if (nameText.StartsWith("global::", StringComparison.Ordinal))
        {
            yield return nameText["global::".Length..];
            yield break;
        }

        if (nameText.IndexOf("::", StringComparison.Ordinal) >= 0)
        {
            yield break;
        }

        if (nameText.IndexOf(".", StringComparison.Ordinal) >= 0)
        {
            yield return nameText;
            yield break;
        }

        foreach (var @namespace in GetAkburaUsingNamespaces())
        {
            yield return @namespace + "." + nameText;
        }

        var currentNamespace = GetAkburaNamespaceText(SyntaxTree.GetRoot(), SyntaxTree);
        if (currentNamespace.Length > 0)
        {
            yield return currentNamespace + "." + nameText;
        }

        yield return nameText;
    }

    private ImmutableArray<string> GetAkburaUsingNamespaces()
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.UsingDirectiveSyntax)
            {
                continue;
            }

            var usingDirective = Unsafe.As<UsingDirectiveSyntax>(member);
            if (usingDirective.Alias != null ||
                usingDirective.StaticKeyword.RawKind != 0 ||
                IsAkcssUsingDirective(usingDirective))
            {
                continue;
            }

            var namespaceText = usingDirective.Name.ToFullString().Trim();
            if (namespaceText.Length > 0)
            {
                builder.Add(namespaceText);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<IParamSymbol> CreateAkburaComponentParameters(AkburaSyntaxTree syntaxTree)
    {
        using var builder = ImmutableArrayBuilder<IParamSymbol>.Rent();
        var semanticModel = Compilation.GetSemanticModel(syntaxTree);

        foreach (var member in syntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.ParamDeclarationSyntax)
            {
                continue;
            }

            var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
            if (semanticModel.GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol)
            {
                builder.Add(paramSymbol);
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<ICommandSymbol> CreateAkburaComponentCommands(AkburaSyntaxTree syntaxTree)
    {
        using var builder = ImmutableArrayBuilder<ICommandSymbol>.Rent();
        var semanticModel = Compilation.GetSemanticModel(syntaxTree);

        foreach (var member in syntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.CommandDeclarationSyntax)
            {
                continue;
            }

            var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
            if (semanticModel.GetSymbolInfo(commandDeclaration).Symbol is ICommandSymbol commandSymbol)
            {
                builder.Add(commandSymbol);
            }
        }

        return builder.ToImmutable();
    }

    internal string GetAkburaComponentMetadataName(AkburaSyntaxTree syntaxTree)
    {
        var componentName = syntaxTree.ComponentName;
        if (Compilation.TryGetReferencedComponentDeclaration(syntaxTree, out var declaration) &&
            declaration.MetadataName is { Length: > 0 } metadataName)
        {
            return metadataName.StartsWith("global::", StringComparison.Ordinal)
                ? metadataName["global::".Length..]
                : metadataName;
        }

        if (componentName.Length == 0)
        {
            return string.Empty;
        }

        var namespaceText = GetAkburaNamespaceText(syntaxTree.GetRoot(), syntaxTree);
        return namespaceText.Length == 0
            ? componentName
            : namespaceText + "." + componentName;
    }

    internal string GetAkburaNamespaceText(AkburaDocumentSyntax root, AkburaSyntaxTree? syntaxTree = null)
    {
        foreach (var member in root.Members)
        {
            if (member.Kind == AkburaSyntaxKind.NamespaceDeclarationSyntax)
            {
                return Unsafe.As<NamespaceDeclarationSyntax>(member).Name.ToFullString().Trim();
            }
        }

        return syntaxTree == null
            ? string.Empty
            : GetDefaultAkburaNamespaceText(syntaxTree);
    }

    private string GetDefaultAkburaNamespaceText(AkburaSyntaxTree syntaxTree)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        AddNamespaceSegments(builder, Compilation.RootNamespace);

        var directory = GetAkburaRelativeDirectory(syntaxTree);
        AddNamespaceSegments(builder, directory);

        return string.Join(".", builder.ToImmutable());
    }

    private string GetAkburaRelativeDirectory(AkburaSyntaxTree syntaxTree)
    {
        if (string.IsNullOrWhiteSpace(syntaxTree.FilePath))
        {
            return string.Empty;
        }

        var filePath = syntaxTree.FilePath;
        if (Path.IsPathRooted(filePath))
        {
            if (string.IsNullOrWhiteSpace(Compilation.ProjectDirectory))
            {
                return string.Empty;
            }

            var projectDirectory = Path.GetFullPath(Compilation.ProjectDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullFilePath = Path.GetFullPath(filePath);
            var prefix = projectDirectory + Path.DirectorySeparatorChar;
            if (fullFilePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                filePath = fullFilePath[prefix.Length..];
            }
            else
            {
                return string.Empty;
            }
        }

        return Path.GetDirectoryName(filePath) ?? string.Empty;
    }

    internal bool TryGetMarkupElementReferenceType(
        MarkupElementSyntax markupElement,
        out CSharpSymbolDefinition type)
    {
        if (TryGetCachedSymbolInfo(markupElement, out var cachedSymbolInfo) &&
            cachedSymbolInfo.Symbol is IMarkupComponentSymbol cachedComponent &&
            !cachedComponent.CSharpDefinition.IsDefault)
        {
            type = cachedComponent.CSharpDefinition;
            return true;
        }

        var startTag = markupElement.StartTag;
        if (startTag == null)
        {
            type = default;
            return false;
        }

        var componentNameText = startTag.Name.ToFullString().Trim();
        if (componentNameText.Length > 0 &&
            TryResolveAkburaMarkupComponent(
                markupElement,
                componentNameText,
                out var akburaComponent) &&
            !akburaComponent.CSharpDefinition.IsDefault)
        {
            type = akburaComponent.CSharpDefinition;
            return true;
        }

        try
        {
            var binding = BindCSharpType(startTag.Name.ToCSharp());
            if (binding.TypeSymbol is INamedTypeSymbol { TypeKind: not TypeKind.Error } namedType)
            {
                type = new CSharpSymbolDefinition(namedType);
                return true;
            }
        }
        catch (InvalidOperationException)
        {
        }

        type = default;
        return false;
    }

    private static void AddNamespaceSegments(
        ImmutableArrayBuilder<string> builder,
        string namespaceText)
    {
        if (string.IsNullOrWhiteSpace(namespaceText))
        {
            return;
        }

        var normalized = namespaceText
            .Replace(Path.DirectorySeparatorChar, '.')
            .Replace(Path.AltDirectorySeparatorChar, '.');
        foreach (var segment in normalized.Split(['.'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = segment.Trim();
            if (trimmed.Length > 0)
            {
                builder.Add(trimmed);
            }
        }
    }

    private AkburaSymbolInfo ResolveMarkupProperty(MarkupAttributeSyntax markupAttribute)
    {
        var propertyName = GetMarkupPropertyName(markupAttribute);
        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        if (IsMarkupNameDirective(markupAttribute))
        {
            return ResolveMarkupNameDirective(Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute));
        }

        if (IsMarkupDataTypeDirective(markupAttribute))
        {
            SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.Success(new PropertySymbol(
                "DataType",
                new CSharpSymbolDefinition(Compilation.CSharpCompilation.GetTypeByMetadataName("System.Type")!)));
        }

        if (IsMarkupItemNameDirective(markupAttribute))
        {
            SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.Success(new PropertySymbol(
                "ItemName",
                new CSharpSymbolDefinition(
                    Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String))));
        }

        if (IsMarkupWhitespaceDirective(markupAttribute))
        {
            var stringType = Compilation.CSharpCompilation.GetSpecialType(
                SpecialType.System_String);

            SetSemanticDiagnosticsIfAbsent(
                markupAttribute,
                []);

            return AkburaSymbolInfo.Success(
                new PropertySymbol(
                    "space",
                    new CSharpSymbolDefinition(stringType),
                    isImplicitlyDeclared: true));
        }

        var markupElement = GetContainingMarkupElement(markupAttribute);
        if (markupElement == null)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var componentSymbolInfo = GetSymbolInfo(markupElement);
        if (componentSymbolInfo.Symbol is not IMarkupComponentSymbol componentSymbol)
        {
            SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.None(componentSymbolInfo.CandidateReason);
        }

        var componentType = componentSymbol.ComponentType;
        if (markupAttribute.Kind == AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax &&
            componentType != null)
        {
            var attachedAttribute = Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute);
            if (TryResolveMarkupAttachedProperty(
                    attachedAttribute,
                    componentType,
                    componentSymbol,
                    out var attachedPropertyInfo))
            {
                return attachedPropertyInfo;
            }
        }

        if (componentType != null &&
            TryResolveMarkupAttachedProperty(
                markupAttribute,
                propertyName,
                componentType,
                componentSymbol,
                out var legacyAttachedPropertyInfo))
        {
            return legacyAttachedPropertyInfo;
        }

        var memberName = GetMarkupMemberLookupName(propertyName);
        var parameter = FindComponentParameter(componentSymbol, propertyName);
        var command = FindComponentCommand(componentSymbol, propertyName);
        RoslynPropertySymbol? clrProperty = null;
        RoslynFieldSymbol? avaloniaProperty = null;
        RoslynEventSymbol? clrEvent = null;
        RoslynFieldSymbol? avaloniaRoutedEvent = null;

        if (componentType != null)
        {
            clrProperty = FindPublicClrProperty(componentType, memberName);
            avaloniaProperty = FindAvaloniaPropertyField(componentType, memberName);
            clrEvent = FindPublicClrEvent(componentType, memberName);
            avaloniaRoutedEvent = FindAvaloniaRoutedEventField(componentType, memberName);
        }

        if (parameter == null &&
            command == null &&
            clrProperty == null &&
            avaloniaProperty == null &&
            clrEvent == null &&
            avaloniaRoutedEvent == null)
        {
            SetSemanticDiagnostics(
                markupAttribute,
                ImmutableArray.Create(componentType != null &&
                    HasInaccessibleMarkupMember(componentType, memberName)
                        ? CreateInaccessibleMemberDiagnostic(markupAttribute, propertyName, componentType)
                        : CreateMarkupPropertyNotFoundDiagnostic(
                            markupAttribute,
                            propertyName,
                            componentSymbol)));

            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        if (parameter == null &&
            command == null &&
            clrProperty == null &&
            avaloniaProperty == null &&
            (clrEvent != null || avaloniaRoutedEvent != null))
        {
            SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

            return AkburaSymbolInfo.Success(new RoutedEventSymbol(
                propertyName,
                GetMarkupEventHandlerType(clrEvent, avaloniaRoutedEvent),
                GetMarkupEventArgsType(clrEvent, avaloniaRoutedEvent),
                avaloniaRoutedEvent == null ? default : new CSharpSymbolDefinition(avaloniaRoutedEvent),
                clrEvent == null ? default : new CSharpSymbolDefinition(clrEvent),
                containingSymbol: componentSymbol));
        }

        SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);

        return AkburaSymbolInfo.Success(new PropertySymbol(
            memberName,
            GetMarkupPropertyType(
                parameter,
                command,
                clrProperty,
                avaloniaProperty,
                attachedProperty: null),
            avaloniaPropertyDefinition: avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrPropertyDefinition: clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            parameter: parameter,
            command: command,
            containingSymbol: componentSymbol));
    }

    private AkburaSymbolInfo ResolveMarkupNameDirective(
        MarkupAttachedPropertyAttributeSyntax markupAttribute)
    {
        MarkupNameDeclaration? declaration = null;
        for (var binder = BindingSession.GetOperationBinder(markupAttribute);
             binder != null;
             binder = binder.Next)
        {
            if (binder is MarkupBinder markupBinder &&
                markupBinder.TryGetDeclaredNameDeclaration(markupAttribute, out declaration))
            {
                break;
            }
        }

        if (declaration == null)
        {
            SetSemanticDiagnostics(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var diagnostic = declaration.CreateDiagnostic();
        SetSemanticDiagnostics(
            markupAttribute,
            diagnostic == null
                ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
                : ImmutableArray.Create(diagnostic));

        if (declaration.Failure == MarkupNameDeclarationFailure.Duplicate)
        {
            var originalSymbol = declaration.OriginalDeclaration?.GetOrCreateSymbol(this);
            return originalSymbol == null
                ? AkburaSymbolInfo.None(AkburaCandidateReason.Ambiguous)
                : AkburaSymbolInfo.Candidates(
                    ImmutableArray.Create<AkburaSymbol>(originalSymbol),
                    AkburaCandidateReason.Ambiguous);
        }

        if (!declaration.IsValid)
        {
            return AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax);
        }

        var symbol = declaration.GetOrCreateSymbol(this);
        return symbol == null
            ? AkburaSymbolInfo.None(AkburaCandidateReason.NotFound)
            : AkburaSymbolInfo.Success(symbol);
    }

    private bool TryResolveMarkupAttachedProperty(
        MarkupAttachedPropertyAttributeSyntax markupAttribute,
        INamedTypeSymbol componentType,
        IMarkupComponentSymbol componentSymbol,
        out AkburaSymbolInfo symbolInfo)
    {
        symbolInfo = default;
        var propertyName = markupAttribute.Name.Identifier.ValueText;
        if (!TryBindAttachedPropertyOwner(markupAttribute.OwnerType, out var ownerType))
        {
            return false;
        }

        if (!TryCreateAttachedPropertySymbol(
                ownerType,
                propertyName,
                componentType,
                SymbolLanguage.Markup,
                componentSymbol,
                out var property))
        {
            SetSemanticDiagnostics(
                markupAttribute,
                ImmutableArray.Create(HasInaccessiblePropertyMember(ownerType, propertyName)
                    ? CreateInaccessibleMemberDiagnostic(markupAttribute, propertyName, ownerType)
                    : CreateMarkupPropertyNotFoundDiagnostic(markupAttribute, propertyName, ownerType)));
            symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
            return true;
        }

        SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        symbolInfo = AkburaSymbolInfo.Success(property);
        return true;
    }

    private bool TryResolveMarkupAttachedProperty(
        MarkupAttributeSyntax markupAttribute,
        string propertyReference,
        INamedTypeSymbol componentType,
        IMarkupComponentSymbol componentSymbol,
        out AkburaSymbolInfo symbolInfo)
    {
        symbolInfo = default;
        if (!TrySplitAttachedPropertyReference(
                propertyReference,
                out var ownerText,
                out var propertyName))
        {
            return false;
        }

        if (!TryBindAttachedPropertyOwner(
                ownerText,
                GetCSharpUsingDirectives(),
                out var ownerType))
        {
            return false;
        }

        if (!TryCreateAttachedPropertySymbol(
                ownerType,
                propertyName,
                componentType,
                SymbolLanguage.Markup,
                componentSymbol,
                out var property))
        {
            SetSemanticDiagnostics(
                markupAttribute,
                ImmutableArray.Create(HasInaccessiblePropertyMember(ownerType, propertyName)
                    ? CreateInaccessibleMemberDiagnostic(markupAttribute, propertyName, ownerType)
                    : CreateMarkupPropertyNotFoundDiagnostic(markupAttribute, propertyName, ownerType)));
            symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
            return true;
        }

        SetSemanticDiagnosticsIfAbsent(markupAttribute, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        symbolInfo = AkburaSymbolInfo.Success(property);
        return true;
    }

    private bool TryBindAttachedPropertyOwner(
        MarkupComponentNameSyntax ownerSyntax,
        out INamedTypeSymbol ownerType)
    {
        ownerType = null!;
        var binding = BindCSharpType(
            ownerSyntax.ToCSharp(),
            GetCSharpUsingDirectives());
        if (binding.TypeSymbol is INamedTypeSymbol namedType)
        {
            ownerType = namedType;
            return true;
        }

        return false;
    }

    private bool TryBindAttachedPropertyOwner(
        string ownerText,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives,
        out INamedTypeSymbol ownerType)
    {
        ownerType = null!;
        try
        {
            var binding = BindCSharpType(
                CSharpSyntaxFactory.ParseTypeName(ownerText),
                usingDirectives);
            if (binding.TypeSymbol is INamedTypeSymbol namedType)
            {
                ownerType = namedType;
                return true;
            }
        }
        catch (ArgumentException)
        {
        }

        return false;
    }

    private bool TryCreateAttachedPropertySymbol(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol? appliedTargetType,
        SymbolLanguage language,
        AkburaSymbol? containingSymbol,
        out PropertySymbol property)
    {
        property = null!;
        var attachedProperty = FindPublicAttachedPropertyField(ownerType, propertyName);
        if (attachedProperty == null)
        {
            return false;
        }

        TryGetAttachedPropertyValueType(attachedProperty.Type, out var attachedValueType);
        var getter = FindPublicAttachedAccessor(
            ownerType,
            conventionalName: "Get" + propertyName,
            fallbackName: "Get",
            minimumParameterCount: 1,
            attachedValueType,
            isSetter: false);
        var setter = FindPublicAttachedAccessor(
            ownerType,
            conventionalName: "Set" + propertyName,
            fallbackName: "Set",
            minimumParameterCount: 2,
            attachedValueType,
            isSetter: true);
        var attachedTargetType = GetAttachedPropertyTargetType(getter, setter);
        if (attachedTargetType != null &&
            appliedTargetType != null &&
            !IsAssignableTo(appliedTargetType, attachedTargetType))
        {
            return false;
        }

        property = new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(
                parameter: null,
                command: null,
                clrProperty: null,
                avaloniaProperty: IsAvaloniaPropertyType(attachedProperty.Type) ? attachedProperty : null,
                attachedProperty),
            avaloniaPropertyDefinition: IsAvaloniaPropertyType(attachedProperty.Type)
                ? new CSharpSymbolDefinition(attachedProperty)
                : default,
            attachedPropertyDefinition: new CSharpSymbolDefinition(attachedProperty),
            attachedGetterDefinition: getter == null ? default : new CSharpSymbolDefinition(getter),
            attachedSetterDefinition: setter == null ? default : new CSharpSymbolDefinition(setter),
            attachedTargetType: attachedTargetType == null ? default : new CSharpSymbolDefinition(attachedTargetType),
            language: language,
            containingSymbol: containingSymbol);
        return true;
    }

    private static bool TrySplitAttachedPropertyReference(
        string propertyReference,
        out string ownerText,
        out string propertyName)
    {
        var lastDot = propertyReference.LastIndexOf('.');
        if (lastDot <= 0 ||
            lastDot >= propertyReference.Length - 1)
        {
            ownerText = string.Empty;
            propertyName = string.Empty;
            return false;
        }

        ownerText = propertyReference[..lastDot].Trim();
        propertyName = propertyReference[(lastDot + 1)..].Trim();
        return ownerText.Length > 0 && propertyName.Length > 0;
    }

    private static string GetMarkupMemberLookupName(string propertyName)
    {
        return string.Equals(propertyName, "class", StringComparison.Ordinal)
            ? "Classes"
            : propertyName;
    }

    internal static string GetMarkupPropertyName(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax => Unsafe.As<MarkupPlainAttributeSyntax>(markupAttribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax => Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax => Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    internal static MarkupElementSyntax? GetContainingMarkupElement(MarkupAttributeSyntax markupAttribute)
    {
        for (var node = markupAttribute.Parent; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.MarkupElementSyntax)
            {
                return Unsafe.As<MarkupElementSyntax>(node);
            }
        }

        return null;
    }

    private static IParamSymbol? FindComponentParameter(
        IMarkupComponentSymbol componentSymbol,
        string propertyName)
    {
        var akburaComponent = componentSymbol.AkburaComponent;
        if (akburaComponent == null)
        {
            return null;
        }

        foreach (var parameter in akburaComponent.Parameters)
        {
            if (parameter.Name == propertyName)
            {
                return parameter;
            }
        }

        return null;
    }

    private static ICommandSymbol? FindComponentCommand(
        IMarkupComponentSymbol componentSymbol,
        string propertyName)
    {
        var akburaComponent = componentSymbol.AkburaComponent;
        if (akburaComponent == null)
        {
            return null;
        }

        foreach (var command in akburaComponent.Commands)
        {
            if (command.Name == propertyName)
            {
                return command;
            }
        }

        return null;
    }

    private AkburaSemanticDiagnostic CreateMarkupPropertyNotFoundDiagnostic(
        MarkupAttributeSyntax syntax,
        string propertyName,
        IMarkupComponentSymbol componentSymbol)
    {
        return CreateMarkupPropertyNotFoundDiagnostic(
            (AkburaSyntax)syntax,
            propertyName,
            componentSymbol);
    }

    private AkburaSemanticDiagnostic CreateMarkupPropertyNotFoundDiagnostic(
        AkburaSyntax syntax,
        string propertyName,
        IMarkupComponentSymbol componentSymbol)
    {
        var componentName = componentSymbol.CSharpDefinition.IsDefault
            ? componentSymbol.Name
            : componentSymbol.CSharpDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound,
            [propertyName, componentName]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupPropertyNotFoundDiagnostic(
        AkburaSyntax syntax,
        string propertyName,
        INamedTypeSymbol ownerType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound,
            [propertyName, ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]);
    }

    private bool HasInaccessibleMarkupMember(
        INamedTypeSymbol componentType,
        string memberName)
    {
        return HasInaccessiblePropertyMember(componentType, memberName) ||
            HasInaccessibleClrEvent(componentType, memberName) ||
            HasInaccessibleAvaloniaRoutedEventField(componentType, memberName);
    }

    private bool HasInaccessiblePropertyMember(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        return HasInaccessibleClrProperty(componentType, propertyName) ||
            HasInaccessibleAttachedPropertyField(componentType, propertyName) ||
            HasInaccessibleAvaloniaPropertyField(componentType, propertyName);
    }

    private bool HasInaccessibleClrProperty(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility != Accessibility.Public)
                {
                    return true;
                }
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            foreach (var property in @interface.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility != Accessibility.Public)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasInaccessibleAvaloniaPropertyField(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        return HasInaccessibleAvaloniaPropertyFieldName(componentType, propertyName + "Property") ||
            HasInaccessibleAvaloniaPropertyFieldName(componentType, propertyName);
    }

    private bool HasInaccessibleAvaloniaPropertyFieldName(
        INamedTypeSymbol componentType,
        string fieldName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(fieldName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility != Accessibility.Public &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasInaccessibleAttachedPropertyField(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        foreach (var fieldName in GetAttachedPropertyFieldNames(propertyName))
        {
            for (var current = componentType; current != null; current = current.BaseType)
            {
                foreach (var field in current.GetMembers(fieldName).OfType<RoslynFieldSymbol>())
                {
                    if (field.IsStatic &&
                        field.DeclaredAccessibility != Accessibility.Public &&
                        IsAttachedPropertyType(field.Type))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static bool HasInaccessibleClrEvent(
        INamedTypeSymbol componentType,
        string eventName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var @event in current.GetMembers(eventName).OfType<RoslynEventSymbol>())
            {
                if (!@event.IsStatic &&
                    @event.DeclaredAccessibility != Accessibility.Public)
                {
                    return true;
                }
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            foreach (var @event in @interface.GetMembers(eventName).OfType<RoslynEventSymbol>())
            {
                if (!@event.IsStatic &&
                    @event.DeclaredAccessibility != Accessibility.Public)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool HasInaccessibleAvaloniaRoutedEventField(
        INamedTypeSymbol componentType,
        string eventName)
    {
        var routedEventName = eventName + "Event";
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(routedEventName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility != Accessibility.Public &&
                    IsAvaloniaRoutedEventType(field.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static AkburaSemanticDiagnostic CreateInaccessibleMemberDiagnostic(
        AkburaSyntax syntax,
        string memberName,
        INamedTypeSymbol ownerType)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_InaccessibleMember,
            [memberName, ownerType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)]);
    }

    internal static RoslynPropertySymbol? FindPublicClrProperty(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            foreach (var property in @interface.GetMembers(propertyName).OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }
        }

        return null;
    }

    private RoslynFieldSymbol? FindAvaloniaPropertyField(
        INamedTypeSymbol componentType,
        string propertyName)
    {
        var avaloniaPropertyName = propertyName + "Property";
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(avaloniaPropertyName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private RoslynFieldSymbol? FindExactAvaloniaPropertyField(
        INamedTypeSymbol componentType,
        string propertyFieldName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(propertyFieldName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private RoslynFieldSymbol? FindPublicAttachedPropertyField(
        INamedTypeSymbol ownerType,
        string propertyName)
    {
        foreach (var fieldName in GetAttachedPropertyFieldNames(propertyName))
        {
            for (var current = ownerType; current != null; current = current.BaseType)
            {
                foreach (var field in current.GetMembers(fieldName).OfType<RoslynFieldSymbol>())
                {
                    if (field.IsStatic &&
                        field.DeclaredAccessibility == Accessibility.Public &&
                        IsAttachedPropertyType(field.Type))
                    {
                        return field;
                    }
                }
            }
        }

        return null;
    }

    private static ImmutableArray<string> GetAttachedPropertyFieldNames(string propertyName)
    {
        return propertyName.EndsWith("Property", StringComparison.Ordinal)
            ? ImmutableArray.Create(propertyName)
            : ImmutableArray.Create(propertyName + "Property", propertyName);
    }

    private static RoslynMethodSymbol? FindPublicAttachedAccessor(
        INamedTypeSymbol ownerType,
        string conventionalName,
        string fallbackName,
        int minimumParameterCount,
        ITypeSymbol? attachedValueType,
        bool isSetter)
    {
        return FindPublicAttachedAccessor(
                ownerType,
                conventionalName,
                minimumParameterCount,
                attachedValueType,
                isSetter) ??
            FindPublicAttachedAccessor(
                ownerType,
                fallbackName,
                minimumParameterCount,
                attachedValueType,
                isSetter);
    }

    private static RoslynMethodSymbol? FindPublicAttachedAccessor(
        INamedTypeSymbol ownerType,
        string methodName,
        int minimumParameterCount,
        ITypeSymbol? attachedValueType,
        bool isSetter)
    {
        for (var current = ownerType; current != null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers(methodName).OfType<RoslynMethodSymbol>())
            {
                if (IsAttachedAccessorCandidate(
                        method,
                        minimumParameterCount,
                        attachedValueType,
                        isSetter))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static bool IsAttachedAccessorCandidate(
        RoslynMethodSymbol method,
        int minimumParameterCount,
        ITypeSymbol? attachedValueType,
        bool isSetter)
    {
        if (!method.IsStatic ||
            method.DeclaredAccessibility != Accessibility.Public ||
            method.Arity != 0 ||
            method.Parameters.Length != minimumParameterCount ||
            method.Parameters.Any(static parameter => parameter.RefKind != RefKind.None))
        {
            return false;
        }

        if (attachedValueType == null)
        {
            return true;
        }

        return isSetter
            ? method.ReturnsVoid &&
              SymbolEqualityComparer.Default.Equals(method.Parameters[1].Type, attachedValueType)
            : SymbolEqualityComparer.Default.Equals(method.ReturnType, attachedValueType);
    }

    private static ITypeSymbol? GetAttachedPropertyTargetType(
        RoslynMethodSymbol? getter,
        RoslynMethodSymbol? setter)
    {
        if (setter is { Parameters.Length: > 0 })
        {
            return setter.Parameters[0].Type;
        }

        if (getter is { Parameters.Length: > 0 })
        {
            return getter.Parameters[0].Type;
        }

        return null;
    }

    private static RoslynEventSymbol? FindPublicClrEvent(
        INamedTypeSymbol componentType,
        string eventName)
    {
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var @event in current.GetMembers(eventName).OfType<RoslynEventSymbol>())
            {
                if (!@event.IsStatic &&
                    @event.DeclaredAccessibility == Accessibility.Public)
                {
                    return @event;
                }
            }
        }

        foreach (var @interface in componentType.AllInterfaces)
        {
            foreach (var @event in @interface.GetMembers(eventName).OfType<RoslynEventSymbol>())
            {
                if (!@event.IsStatic &&
                    @event.DeclaredAccessibility == Accessibility.Public)
                {
                    return @event;
                }
            }
        }

        return null;
    }

    private RoslynFieldSymbol? FindAvaloniaRoutedEventField(
        INamedTypeSymbol componentType,
        string eventName)
    {
        var routedEventName = eventName + "Event";
        for (var current = componentType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers(routedEventName).OfType<RoslynFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.DeclaredAccessibility == Accessibility.Public &&
                    IsAvaloniaRoutedEventType(field.Type))
                {
                    return field;
                }
            }
        }

        return null;
    }

    private bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        return TryGetAvaloniaPropertyType(out var avaloniaPropertyType) &&
            IsAssignableTo(type, avaloniaPropertyType);
    }

    private bool IsAttachedPropertyType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        for (var current = namedType; current != null; current = current.BaseType)
        {
            if (current.Name == "AttachedProperty" &&
                current.TypeArguments.Length == 1 &&
                current.TypeArguments[0].TypeKind != TypeKind.Error)
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAvaloniaPropertyType(out INamedTypeSymbol avaloniaPropertyType)
    {
        avaloniaPropertyType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.AvaloniaProperty")!;
        return avaloniaPropertyType != null;
    }

    private bool IsAvaloniaRoutedEventType(ITypeSymbol type)
    {
        return TryGetAvaloniaRoutedEventType(out var routedEventType) &&
            IsAssignableTo(type, routedEventType);
    }

    private bool TryGetAvaloniaRoutedEventType(out INamedTypeSymbol routedEventType)
    {
        routedEventType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Interactivity.RoutedEvent")!;
        return routedEventType != null;
    }

    private CSharpSymbolDefinition GetMarkupEventHandlerType(
        RoslynEventSymbol? clrEvent,
        RoslynFieldSymbol? avaloniaRoutedEvent)
    {
        if (clrEvent?.Type is { TypeKind: not TypeKind.Error } eventType)
        {
            return new CSharpSymbolDefinition(eventType);
        }

        if (GetMarkupEventArgsType(clrEvent, avaloniaRoutedEvent).Symbol is ITypeSymbol eventArgsType &&
            Compilation.CSharpCompilation.GetTypeByMetadataName("System.EventHandler`1") is { } eventHandlerType)
        {
            return new CSharpSymbolDefinition(eventHandlerType.Construct(eventArgsType));
        }

        return default;
    }

    private static CSharpSymbolDefinition GetMarkupEventArgsType(
        RoslynEventSymbol? clrEvent,
        RoslynFieldSymbol? avaloniaRoutedEvent)
    {
        if (TryGetDelegateEventArgsType(clrEvent?.Type, out var delegateEventArgsType))
        {
            return new CSharpSymbolDefinition(delegateEventArgsType);
        }

        if (TryGetRoutedEventArgsType(avaloniaRoutedEvent?.Type, out var routedEventArgsType))
        {
            return new CSharpSymbolDefinition(routedEventArgsType);
        }

        return default;
    }

    private static bool TryGetDelegateEventArgsType(
        ITypeSymbol? eventType,
        out ITypeSymbol eventArgsType)
    {
        if (eventType is INamedTypeSymbol { DelegateInvokeMethod.Parameters.Length: >= 2 } delegateType)
        {
            eventArgsType = delegateType.DelegateInvokeMethod.Parameters[1].Type;
            return eventArgsType.TypeKind != TypeKind.Error;
        }

        eventArgsType = null!;
        return false;
    }

    private static bool TryGetRoutedEventArgsType(
        ITypeSymbol? routedEventType,
        out ITypeSymbol eventArgsType)
    {
        for (var current = routedEventType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "RoutedEvent" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia.Interactivity" &&
                current.TypeArguments.Length == 1 &&
                current.TypeArguments[0].TypeKind != TypeKind.Error)
            {
                eventArgsType = current.TypeArguments[0];
                return true;
            }
        }

        eventArgsType = null!;
        return false;
    }

    private static CSharpSymbolDefinition GetMarkupPropertyType(
        IParamSymbol? parameter,
        ICommandSymbol? command,
        RoslynPropertySymbol? clrProperty,
        RoslynFieldSymbol? avaloniaProperty,
        RoslynFieldSymbol? attachedProperty)
    {
        if (parameter != null)
        {
            return parameter.Type;
        }

        if (command != null)
        {
            return command.ReturnType;
        }

        if (clrProperty?.Type is { TypeKind: not TypeKind.Error } clrPropertyType)
        {
            return new CSharpSymbolDefinition(clrPropertyType);
        }

        if (avaloniaProperty != null &&
            TryGetAvaloniaPropertyValueType(avaloniaProperty.Type, out var avaloniaPropertyType))
        {
            return new CSharpSymbolDefinition(avaloniaPropertyType);
        }

        if (attachedProperty != null &&
            TryGetAttachedPropertyValueType(attachedProperty.Type, out var attachedPropertyType))
        {
            return new CSharpSymbolDefinition(attachedPropertyType);
        }

        return default;
    }

    private static bool TryGetAvaloniaPropertyValueType(
        ITypeSymbol propertyType,
        out ITypeSymbol valueType)
    {
        for (var current = propertyType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.ContainingNamespace.ToDisplayString() != "Avalonia")
            {
                continue;
            }

            if (current.Name is "StyledProperty" or "AttachedProperty" or "AvaloniaProperty" &&
                current.TypeArguments.Length == 1 &&
                current.TypeArguments[0].TypeKind != TypeKind.Error)
            {
                valueType = current.TypeArguments[0];
                return true;
            }

            if (current.Name == "DirectProperty" &&
                current.TypeArguments.Length == 2 &&
                current.TypeArguments[1].TypeKind != TypeKind.Error)
            {
                valueType = current.TypeArguments[1];
                return true;
            }
        }

        valueType = null!;
        return false;
    }

    private static bool TryGetAttachedPropertyValueType(
        ITypeSymbol propertyType,
        out ITypeSymbol valueType)
    {
        for (var current = propertyType as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "AttachedProperty" &&
                current.TypeArguments.Length == 1 &&
                current.TypeArguments[0].TypeKind != TypeKind.Error)
            {
                valueType = current.TypeArguments[0];
                return true;
            }
        }

        valueType = null!;
        return false;
    }

    internal MarkupContentModel CreateMarkupContentModel(
        INamedTypeSymbol componentType,
        MarkupElementSyntax? markupElement = null)
    {
        if (TryCreateTextBlockTextContentModel(componentType, markupElement, out var textBlockContentModel))
        {
            return textBlockContentModel;
        }

        if (TryGetAvaloniaControlType(out var controlType) &&
            IsAssignableTo(componentType, controlType))
        {
            var contentProperty = FindAvaloniaContentProperty(componentType);
            if (contentProperty == null)
            {
                return default;
            }

            var contentType = contentProperty.Type;
            if (TryGetIListElementType(contentType, out var itemType))
            {
                return new MarkupContentModel(
                    new CSharpSymbolDefinition(contentProperty),
                    new CSharpSymbolDefinition(itemType),
                    isCollection: true,
                    allowsText: AllowsTextContent(itemType));
            }

            return new MarkupContentModel(
                new CSharpSymbolDefinition(contentProperty),
                new CSharpSymbolDefinition(contentType),
                isCollection: false,
                allowsText: AllowsTextContent(contentType));
        }

        if (TryGetIListElementType(componentType, out var elementType))
        {
            return new MarkupContentModel(
                contentProperty: default,
                allowedChildType: new CSharpSymbolDefinition(elementType),
                isCollection: true,
                allowsText: AllowsTextContent(elementType));
        }

        return default;
    }

    internal MarkupContentModel CreateAkburaParameterContentModel(
        ImmutableArray<IParamSymbol> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (string.Equals(parameter.Name, "Content", StringComparison.Ordinal))
            {
                return CreateAkburaParameterContentModel(parameter);
            }
        }

        return default;
    }

    private MarkupContentModel CreateAkburaParameterContentModel(IParamSymbol parameter)
    {
        if (parameter.Type.Symbol is not ITypeSymbol parameterType)
        {
            return default;
        }

        if (TryGetIListElementType(parameterType, out var elementType))
        {
            return new MarkupContentModel(
                contentProperty: default,
                allowedChildType: new CSharpSymbolDefinition(elementType),
                isCollection: true,
                allowsText: AllowsTextContent(elementType),
                contentParameter: parameter);
        }

        return new MarkupContentModel(
            contentProperty: default,
            allowedChildType: parameter.Type,
            isCollection: false,
            allowsText: AllowsTextContent(parameterType),
            contentParameter: parameter);
    }

    private bool TryCreateTextBlockTextContentModel(
        INamedTypeSymbol componentType,
        MarkupElementSyntax? markupElement,
        out MarkupContentModel contentModel)
    {
        contentModel = default;
        if (markupElement == null ||
            HasElementContent(markupElement) ||
            !HasValueContent(markupElement) ||
            !IsAvaloniaTextBlockType(componentType))
        {
            return false;
        }

        var textProperty = FindPublicClrProperty(componentType, "Text");
        if (textProperty == null)
        {
            return false;
        }

        contentModel = new MarkupContentModel(
            new CSharpSymbolDefinition(textProperty),
            new CSharpSymbolDefinition(textProperty.Type),
            isCollection: false,
            allowsText: AllowsTextContent(textProperty.Type));
        return true;
    }

    private bool IsAvaloniaTextBlockType(INamedTypeSymbol componentType)
    {
        var textBlockType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.TextBlock");
        return textBlockType != null &&
            IsAssignableTo(componentType, textBlockType);
    }

    private static bool HasValueContent(MarkupElementSyntax markupElement)
    {
        foreach (var content in markupElement.Body)
        {
            if (content.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
            {
                return true;
            }

            if (content.Kind == AkburaSyntaxKind.MarkupTextLiteralSyntax &&
                !string.IsNullOrWhiteSpace(content.ToFullString()))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeTextFragment(
        string text,
        MarkupWhitespaceMode mode,
        bool trimStart,
        bool trimEnd)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        if (mode == MarkupWhitespaceMode.Preserve)
        {
            return text;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        var hasPendingWhitespace = false;

        foreach (var character in text)
        {
            if (IsMarkupWhitespace(character))
            {
                hasPendingWhitespace = true;
                continue;
            }

            if (hasPendingWhitespace)
            {
                if (builder.Length > 0 || !trimStart)
                {
                    builder.Append(' ');
                }

                hasPendingWhitespace = false;
            }

            builder.Append(character);
        }

        if (hasPendingWhitespace &&
            !trimEnd &&
            (builder.Length > 0 || !trimStart))
        {
            builder.Append(' ');
        }

        return builder.ToString();
    }

    private static bool IsMarkupWhitespace(char character)
    {
        return character is ' ' or '\t' or '\r' or '\n';
    }

    private static bool IsMarkupValueContent(
        MarkupContentSyntax content)
    {
        return content.Kind is
            AkburaSyntaxKind.MarkupElementContentSyntax or
            AkburaSyntaxKind.MarkupTextLiteralSyntax or
            AkburaSyntaxKind.MarkupInlineExpressionSyntax;
    }

    private static bool ContainsOnlyMarkupWhitespace(string text)
    {
        foreach (var character in text)
        {
            if (!IsMarkupWhitespace(character))
            {
                return false;
            }
        }

        return true;
    }

    private static string GetEffectiveTextFragment(
        MarkupTextLiteralSyntax textLiteral,
        MarkupContentModel contentModel,
        MarkupWhitespaceMode mode,
        bool trimStart,
        bool trimEnd)
    {
        var rawText = textLiteral.ToFullString();

        if (mode == MarkupWhitespaceMode.Default &&
            !contentModel.AllowsText &&
            ContainsOnlyMarkupWhitespace(rawText))
        {
            return string.Empty;
        }

        return NormalizeTextFragment(
            rawText,
            mode,
            trimStart,
            trimEnd);
    }

    internal ImmutableArray<MarkupChildContent> CreateMarkupChildren(
        MarkupElementSyntax markupElement,
        MarkupContentModel contentModel,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        INamedTypeSymbol? containingType = null)
    {
        using var childrenBuilder =
            ImmutableArrayBuilder<MarkupChildContent>.Rent();

        using var diagnosticsBuilder =
            ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        var whitespaceMode = BindingSession.MarkupWhitespace
            .GetEffectiveMode(markupElement);

        var valueContentCount = 0;

        foreach (var childSyntax in markupElement.Body)
        {
            if (IsMarkupPropertyElementContent(
                    childSyntax,
                    containingType))
            {
                continue;
            }

            if (IsMarkupValueContent(childSyntax))
            {
                valueContentCount++;
            }
        }

        var hasValueText = false;
        var hasValueElement = false;
        var inlineExpressionCount = 0;
        var valueContentIndex = 0;

        foreach (var childSyntax in markupElement.Body)
        {
            if (IsMarkupPropertyElementContent(
                    childSyntax,
                    containingType))
            {
                continue;
            }

            if (!IsMarkupValueContent(childSyntax))
            {
                continue;
            }

            var trimStart = valueContentIndex == 0;
            var trimEnd =
                valueContentIndex == valueContentCount - 1;

            switch (childSyntax.Kind)
            {
                case AkburaSyntaxKind.MarkupElementContentSyntax:
                    hasValueElement = true;
                    break;

                case AkburaSyntaxKind.MarkupTextLiteralSyntax:
                    {
                        var textLiteral =
                            Unsafe.As<MarkupTextLiteralSyntax>(
                                childSyntax);

                        var effectiveText = GetEffectiveTextFragment(
                            textLiteral,
                            contentModel,
                            whitespaceMode,
                            trimStart,
                            trimEnd);

                        if (effectiveText.Length > 0)
                        {
                            hasValueText = true;
                        }

                        break;
                    }

                case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
                    inlineExpressionCount++;
                    break;
            }

            valueContentIndex++;
        }

        var validateInlineExpressionContent =
            !hasValueText &&
            inlineExpressionCount == 1;

        valueContentIndex = 0;

        foreach (var childSyntax in markupElement.Body)
        {
            if (IsMarkupPropertyElementContent(
                    childSyntax,
                    containingType))
            {
                continue;
            }

            if (!IsMarkupValueContent(childSyntax))
            {
                continue;
            }

            var trimStart = valueContentIndex == 0;
            var trimEnd =
                valueContentIndex == valueContentCount - 1;

            switch (childSyntax.Kind)
            {
                case AkburaSyntaxKind.MarkupElementContentSyntax:
                    AddElementChild(
                        Unsafe.As<MarkupElementContentSyntax>(
                            childSyntax),
                        contentModel,
                        whitespaceMode,
                        childrenBuilder,
                        diagnosticsBuilder);
                    break;

                case AkburaSyntaxKind.MarkupTextLiteralSyntax:
                    AddTextChild(
                        Unsafe.As<MarkupTextLiteralSyntax>(
                            childSyntax),
                        contentModel,
                        whitespaceMode,
                        trimStart,
                        trimEnd,
                        childrenBuilder,
                        diagnosticsBuilder);
                    break;

                case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
                    AddExpressionChild(
                        Unsafe.As<MarkupInlineExpressionSyntax>(
                            childSyntax),
                        contentModel,
                        whitespaceMode,
                        validateInlineExpressionContent,
                        childrenBuilder,
                        diagnosticsBuilder);
                    break;
            }

            valueContentIndex++;
        }

        if (!hasValueText &&
            inlineExpressionCount > 1 &&
            !hasValueElement &&
            TryCreateMarkupContentValueExpression(
                markupElement,
                whitespaceMode,
                out var expression,
                out _,
                out _,
                out var synthesizedHasText,
                out var diagnosticSyntax))
        {
            var binding = BindMarkupAttributeExpression(
                diagnosticSyntax,
                expression,
                GetMarkupContentTargetType(contentModel));

            AddMarkupContentValueDiagnostics(
                diagnosticSyntax,
                contentModel,
                binding,
                synthesizedHasText,
                diagnosticsBuilder);
        }

        diagnostics = diagnosticsBuilder.ToImmutable();
        return childrenBuilder.ToImmutable();
    }

    private bool IsMarkupPropertyElementContent(
        MarkupContentSyntax content,
        INamedTypeSymbol? containingType)
    {
        if (content.Kind != AkburaSyntaxKind.MarkupElementContentSyntax)
        {
            return false;
        }

        var propertyElement = Unsafe.As<MarkupElementContentSyntax>(content).Element;
        if (GetSyntaxTreeSymbolInfo(propertyElement).Symbol is AkburaPropertySymbol)
        {
            return true;
        }

        if (containingType == null)
        {
            return false;
        }

        var elementName = propertyElement.StartTag?.Name.ToFullString().Trim();
        if (string.IsNullOrEmpty(elementName))
        {
            return false;
        }

        var separator = elementName!.LastIndexOf('.');
        return separator > 0 &&
            separator < elementName.Length - 1 &&
            IsMarkupPropertyElementOwner(
                containingType,
                elementName[..separator]);
    }

    private void AddElementChild(
        MarkupElementContentSyntax elementContent,
        MarkupContentModel contentModel,
        MarkupWhitespaceMode whitespaceMode,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic>
        diagnosticsBuilder)
    {
        var symbolInfo = GetSyntaxTreeSymbolInfo(elementContent.Element);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        var childType = componentSymbol?.CSharpDefinition ?? default;

        childrenBuilder.Add(new MarkupChildContent(
            elementContent,
            MarkupChildKind.Element,
            childType,
            componentSymbol,
            whitespaceMode: whitespaceMode));

        if (componentSymbol?.ComponentType == null)
        {
            return;
        }

        if (!IsAllowedMarkupChildType(componentSymbol.ComponentType, contentModel))
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                elementContent,
                componentSymbol.CSharpDefinition,
                contentModel));
        }
    }

    private void AddTextChild(
        MarkupTextLiteralSyntax textLiteral,
        MarkupContentModel contentModel,
        MarkupWhitespaceMode whitespaceMode,
        bool trimStart,
        bool trimEnd,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic>
        diagnosticsBuilder)
    {
        var rawText = textLiteral.ToFullString();

        var text = GetEffectiveTextFragment(
            textLiteral,
            contentModel,
            whitespaceMode,
            trimStart,
            trimEnd);

        if (text.Length == 0)
        {
            return;
        }

        var stringType = Compilation.CSharpCompilation
            .GetSpecialType(SpecialType.System_String);

        var textType =
            new CSharpSymbolDefinition(stringType);

        childrenBuilder.Add(
            new MarkupChildContent(
                textLiteral,
                MarkupChildKind.Text,
                textType,
                text: text,
                rawText: rawText,
                whitespaceMode: whitespaceMode));

        if (!contentModel.AllowsText)
        {
            diagnosticsBuilder.Add(
                CreateInvalidMarkupChildDiagnostic(
                    textLiteral,
                    textType,
                    contentModel));
        }
    }

    private void AddExpressionChild(
        MarkupInlineExpressionSyntax inlineExpression,
        MarkupContentModel contentModel,
        MarkupWhitespaceMode whitespaceMode,
        bool validateContentType,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic>
        diagnosticsBuilder)
    {
        var expression = ParseInlineExpression(inlineExpression.Expression);
        var binding = CSharpBindingResult.Empty;
        if (expression != null)
        {
            binding = BindMarkupAttributeExpression(
                inlineExpression,
                expression,
                validateContentType
                    ? GetMarkupContentTargetType(contentModel)
                    : null);
        }

        var expressionTypeSymbol = binding.TypeSymbol ??
            binding.Conversion.SourceType ??
            binding.OperationDefinition.Type;
        var expressionType = expressionTypeSymbol is ITypeSymbol typeSymbol
            ? new CSharpSymbolDefinition(typeSymbol)
            : default;
        childrenBuilder.Add(new MarkupChildContent(
            inlineExpression,
            MarkupChildKind.Expression,
            expressionType,
            whitespaceMode: whitespaceMode));

        if (expression != null)
        {
            var expressionText = inlineExpression.Expression.ToFullString().Trim();
            AddMarkupExpressionDiagnostics(
                inlineExpression,
                expressionText,
                binding,
                diagnosticsBuilder);
            if (validateContentType)
            {
                AddMarkupContentValueDiagnostics(
                    inlineExpression,
                    contentModel,
                    binding,
                    hasText: false,
                    diagnosticsBuilder);
            }
        }
        else if (!contentModel.AllowsChildren)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                inlineExpression,
                expressionType,
                contentModel));
        }
    }

    private AkburaSemanticDiagnostic CreateInvalidMarkupChildDiagnostic(
        MarkupContentSyntax syntax,
        CSharpSymbolDefinition childType,
        MarkupContentModel contentModel)
    {
        var childTypeText = childType.IsDefault
            ? "expression"
            : childType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var expectedTypeText = contentModel.AllowedChildType.IsDefault
            ? "no children"
            : contentModel.AllowedChildType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_InvalidMarkupChild,
            [childTypeText, expectedTypeText]);
    }

    private bool IsAllowedMarkupChildType(ITypeSymbol childType, MarkupContentModel contentModel)
    {
        return contentModel.AllowedChildType.Symbol is ITypeSymbol allowedType &&
            IsAssignableTo(childType, allowedType);
    }

    private bool AllowsTextContent(ITypeSymbol type)
    {
        return type.SpecialType is SpecialType.System_Object or SpecialType.System_String;
    }

    private RoslynPropertySymbol? FindAvaloniaContentProperty(INamedTypeSymbol type)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<RoslynPropertySymbol>())
            {
                if (property.IsStatic ||
                    property.DeclaredAccessibility != Accessibility.Public)
                {
                    continue;
                }

                if (HasAvaloniaContentAttribute(property))
                {
                    return property;
                }
            }
        }

        return null;
    }

    private bool HasAvaloniaContentAttribute(RoslynPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
                "global::Avalonia.Metadata.ContentAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private bool TryGetAvaloniaControlType(out INamedTypeSymbol controlType)
    {
        controlType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.Control")!;
        return controlType != null;
    }

    private bool TryGetIListElementType(ITypeSymbol type, out ITypeSymbol elementType)
    {
        if (type is INamedTypeSymbol namedType &&
            IsIListOfT(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (IsIListOfT(@interface))
            {
                elementType = @interface.TypeArguments[0];
                return true;
            }
        }

        if (IsNonGenericIList(type) ||
            type.AllInterfaces.Any(IsNonGenericIList))
        {
            elementType = Compilation.CSharpCompilation.GetSpecialType(
                SpecialType.System_Object);
            return true;
        }

        elementType = null!;
        return false;
    }

    private static bool IsIListOfT(INamedTypeSymbol type)
    {
        var original = type.OriginalDefinition;
        return original.Name == "IList" &&
            original.Arity == 1 &&
            original.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
    }

    private static bool IsNonGenericIList(ITypeSymbol type)
    {
        return type is INamedTypeSymbol namedType &&
            namedType.Name == "IList" &&
            namedType.Arity == 0 &&
            namedType.ContainingNamespace.ToDisplayString() == "System.Collections";
    }

    internal static bool IsAssignableTo(ITypeSymbol source, ITypeSymbol target)
    {
        if (target.SpecialType == SpecialType.System_Object ||
            IsSameType(source, target))
        {
            return true;
        }

        if (source is INamedTypeSymbol namedSource &&
            target is INamedTypeSymbol namedTarget)
        {
            for (var current = namedSource.BaseType; current != null; current = current.BaseType)
            {
                if (IsSameType(current, namedTarget))
                {
                    return true;
                }
            }
        }

        foreach (var @interface in source.AllInterfaces)
        {
            if (IsSameType(@interface, target))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsSameType(ITypeSymbol left, ITypeSymbol right)
    {
        return SymbolEqualityComparer.Default.Equals(left, right) ||
            left.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ==
            right.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal CSharpBindingResult BindCSharpType(
        CSharp.TypeSyntax typeSyntax,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives = default)
    {
        var field = CSharpSyntaxFactory.FieldDeclaration(
            CSharpSyntaxFactory.VariableDeclaration(typeSyntax)
                .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                    CSharpSyntaxFactory.VariableDeclarator("__akbura_value"))));

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(field));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass, usingDirectives);

        return new CSharpProbeBinder(this, BindingSession.RootBinder)
            .BindFieldType(compilationUnit);
    }

    internal CSharpBindingResult BindCSharpExpression(
        CSharp.ExpressionSyntax expressionSyntax,
        StateDeclarationSyntax? scopeStateDeclaration = null,
        bool isBindingPath = true,
        ITypeSymbol? targetType = null)
    {
        var scope = (AkburaSyntax?)scopeStateDeclaration ?? SyntaxTree.GetRoot();
        var binder = BindingSession.GetCSharpProbeBinder(scope, BinderUsage.Expression);
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(
                    Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        using var members = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        if (scopeStateDeclaration != null)
        {
            members.AddRange(CreateStateProbeFieldsBefore(scopeStateDeclaration));
        }

        members.Add(method);
        var compilationUnit = binder.CreateComponentProbeCompilationUnit(
            members.ToImmutable(),
            "__AkburaSemanticProbe");
        var binding = binder.BindReturnExpression(compilationUnit, isBindingPath);
        return ApplyExpectedTypeConversion(binding, binder, targetType);
    }

    internal CSharpBindingResult BindAkcssExpression(
        CSharp.ExpressionSyntax expressionSyntax,
        IAkcssSymbol containingSymbol,
        ITypeSymbol? targetType = null)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithParameterList(CreateAkcssExpressionParameterList(containingSymbol))
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(method));

        var compilationUnit = CreateCSharpProbeCompilationUnit(
            probeClass,
            GetAkcssCSharpUsingDirectives(containingSymbol));

        var binder = BindingSession.GetCSharpProbeBinder(
            containingSymbol.DeclarationSyntax,
            BinderUsage.Akcss);
        var binding = binder.BindReturnExpression(compilationUnit, isBindingPath: true);
        return ApplyExpectedTypeConversion(binding, binder, targetType);
    }

    private static CSharpBindingResult ApplyExpectedTypeConversion(
        CSharpBindingResult binding,
        CSharpProbeBinder binder,
        ITypeSymbol? targetType)
    {
        return targetType == null
            ? binding
            : binding.WithConversion(binder.ClassifyConversion(binding.TypeSymbol, targetType));
    }

    private CSharp.ParameterListSyntax CreateAkcssExpressionParameterList(IAkcssSymbol containingSymbol)
    {
        if (containingSymbol is not ITailwindUtilitySymbol utilitySymbol ||
            utilitySymbol.Parameters.Length == 0)
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var builder = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        foreach (var parameter in utilitySymbol.Parameters)
        {
            var parameterType = parameter.Type.Symbol == null
                ? CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword))
                : CSharpSyntaxFactory.ParseTypeName(
                    parameter.Type.Symbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            builder.Add(CSharpSyntaxFactory.Parameter(
                    CSharpSyntaxFactory.Identifier(parameter.Name))
                .WithType(parameterType));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(builder.ToImmutable()));
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateStateProbeFieldsBefore(
        StateDeclarationSyntax scopeStateDeclaration)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Position >= scopeStateDeclaration.Position)
            {
                break;
            }

            if (member.Kind != AkburaSyntaxKind.StateDeclarationSyntax)
            {
                continue;
            }

            var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
            if (TryCreateStateProbeField(stateDeclaration, out var field))
            {
                builder.Add(field);
            }
        }

        return builder.ToImmutable();
    }

    private bool TryCreateStateProbeField(
        StateDeclarationSyntax stateDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = stateDeclaration.Name.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        var type = GetStateProbeFieldType(stateDeclaration);
        if (type == null)
        {
            return false;
        }

        field = CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        CSharpSyntaxFactory.VariableDeclarator(
                            CSharpSyntaxFactory.Identifier(name)))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword)));

        return true;
    }

    private CSharp.TypeSyntax? GetStateProbeFieldType(StateDeclarationSyntax stateDeclaration)
    {
        if (stateDeclaration.Type != null)
        {
            try
            {
                return stateDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        if (GetSymbolInfo(stateDeclaration).Symbol is not IStateSymbol stateSymbol ||
            stateSymbol.Type.Symbol is not ITypeSymbol typeSymbol)
        {
            return null;
        }

        return CSharpSyntaxFactory.ParseTypeName(
            typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
    }

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> GetCSharpUsingDirectives()
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind == AkburaSyntaxKind.UsingDirectiveSyntax)
            {
                var usingDirective = Unsafe.As<UsingDirectiveSyntax>(member);
                if (IsAkcssUsingDirective(usingDirective))
                {
                    continue;
                }

                builder.Add(usingDirective.ToCSharp());
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssCSharpUsingDirectives()
        => GetAkcssCSharpUsingDirectives((AkburaSyntax?)null);

    internal ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssCSharpUsingDirectives(IAkcssSymbol containingSymbol)
        => GetAkcssCSharpUsingDirectives(containingSymbol.DeclarationSyntax);

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssCSharpUsingDirectives(AkburaSyntax? akcssSyntax)
    {
        using var builder = ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax>.Rent();
        foreach (var usingDirective in GetCSharpUsingDirectives())
        {
            builder.Add(usingDirective);
        }

        if (akcssSyntax != null)
        {
            foreach (var usingDirective in GetAkcssUsingDirectives(akcssSyntax))
            {
                if (IsAkcssUsingDirective(usingDirective))
                {
                    continue;
                }

                var name = usingDirective.Name.ToFullString().Trim();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    builder.Add(CSharpSyntaxFactory.UsingDirective(
                        CSharpSyntaxFactory.ParseName(name)));
                }
            }
        }

        AddAkcssImplicitUsing(builder, "Avalonia");
        AddAkcssImplicitUsing(builder, "Avalonia.Layout");
        AddAkcssImplicitUsing(builder, "Avalonia.Media");
        AddAkcssImplicitUsing(builder, "Akbura");
        return builder.ToImmutable();
    }

    private ImmutableArray<AkcssUsingDirectiveSyntax> GetAkcssUsingDirectives(AkburaSyntax syntax)
    {
        var members = GetContainingAkcssTopLevelMembers(syntax);
        if (members.Count == 0)
        {
            return ImmutableArray<AkcssUsingDirectiveSyntax>.Empty;
        }

        using var builder = ImmutableArrayBuilder<AkcssUsingDirectiveSyntax>.Rent();
        foreach (var member in members)
        {
            if (member.Kind == AkburaSyntaxKind.AkcssUsingDirectiveSyntax)
            {
                builder.Add(Unsafe.As<AkcssUsingDirectiveSyntax>(member));
            }
        }

        return builder.ToImmutable();
    }

    private Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> GetContainingAkcssTopLevelMembers(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                    return Unsafe.As<InlineAkcssBlockSyntax>(node).Members;
                case AkburaSyntaxKind.AkcssDocumentSyntax:
                    return Unsafe.As<AkcssDocumentSyntax>(node).Members;
            }
        }

        return default;
    }

    private ImmutableArray<string> GetAkcssImportNames(AkburaSyntax syntax)
    {
        using var builder = ImmutableArrayBuilder<string>.Rent();
        foreach (var usingDirective in GetAkcssUsingDirectives(syntax))
        {
            if (TryGetAkcssImportName(usingDirective, out var importName))
            {
                builder.Add(importName);
            }
        }

        return builder.ToImmutable();
    }

    private static bool IsAkcssUsingDirective(AkcssUsingDirectiveSyntax usingDirective)
        => TryGetAkcssImportName(usingDirective, out _);

    private static bool IsAkcssUsingDirective(UsingDirectiveSyntax usingDirective)
        => TryGetAkcssImportName(usingDirective, out _);

    private static bool TryGetAkcssImportName(
        AkcssUsingDirectiveSyntax usingDirective,
        out string importName)
    {
        importName = usingDirective.Name.ToFullString().Trim();
        return importName.EndsWith(".akcss", StringComparison.Ordinal);
    }

    private static bool TryGetAkcssImportName(
        UsingDirectiveSyntax usingDirective,
        out string importName)
    {
        importName = string.Empty;
        if (usingDirective.Alias != null ||
            usingDirective.StaticKeyword.RawKind != 0)
        {
            return false;
        }

        var name = usingDirective.Name.ToFullString().Trim();
        if (!name.EndsWith(".akcss", StringComparison.Ordinal))
        {
            return false;
        }

        importName = name;
        return true;
    }

    private static void AddAkcssImplicitUsing(
        ImmutableArrayBuilder<CSharp.UsingDirectiveSyntax> builder,
        string namespaceName)
    {
        builder.Add(CSharpSyntaxFactory.UsingDirective(
            CSharpSyntaxFactory.ParseName(namespaceName)));
    }

    internal ImmutableArray<CSharp.ExternAliasDirectiveSyntax> GetCSharpExternAliases()
    {
        using var builder = ImmutableArrayBuilder<CSharp.ExternAliasDirectiveSyntax>.Rent();
        var seenAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (var reference in Compilation.CSharpCompilation.References)
        {
            foreach (var alias in reference.Properties.Aliases)
            {
                if (string.IsNullOrWhiteSpace(alias) ||
                    alias == "global" ||
                    !seenAliases.Add(alias))
                {
                    continue;
                }

                builder.Add(CSharpSyntaxFactory.ExternAliasDirective(
                    CSharpSyntaxFactory.Identifier(alias)));
            }
        }

        return builder.ToImmutable();
    }

    private CSharp.FileScopedNamespaceDeclarationSyntax? GetCSharpNamespaceDeclaration()
    {
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind == AkburaSyntaxKind.NamespaceDeclarationSyntax)
            {
                return Unsafe.As<NamespaceDeclarationSyntax>(member).ToCSharp();
            }
        }

        var namespaceText = GetDefaultAkburaNamespaceText(SyntaxTree);
        return namespaceText.Length == 0
            ? null
            : CSharpSyntaxFactory.FileScopedNamespaceDeclaration(
                CSharpSyntaxFactory.ParseName(namespaceText));
    }

    private CSharp.CompilationUnitSyntax CreateCSharpProbeCompilationUnit(
        CSharp.MemberDeclarationSyntax member,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives = default)
    {
        return CreateCSharpProbeCompilationUnit(
            ImmutableArray.Create(member),
            usingDirectives);
    }

    private CSharp.CompilationUnitSyntax CreateCSharpProbeCompilationUnit(
        ImmutableArray<CSharp.MemberDeclarationSyntax> members,
        ImmutableArray<CSharp.UsingDirectiveSyntax> usingDirectives = default)
    {
        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        if (TryCreateCurrentComponentProbeType(out var componentType))
        {
            membersBuilder.Add(componentType);
        }

        foreach (var member in members)
        {
            membersBuilder.Add(member);
        }

        var compilationUnit = CSharpSyntaxFactory.CompilationUnit()
            .WithExterns(CSharpSyntaxFactory.List(GetCSharpExternAliases()))
            .WithUsings(CSharpSyntaxFactory.List(
                usingDirectives.IsDefault
                    ? GetCSharpUsingDirectives()
                    : usingDirectives));

        var namespaceDeclaration = GetCSharpNamespaceDeclaration();
        if (namespaceDeclaration != null)
        {
            return compilationUnit.WithMembers(
                CSharpSyntaxFactory.SingletonList<CSharp.MemberDeclarationSyntax>(
                    namespaceDeclaration.WithMembers(
                        CSharpSyntaxFactory.List(membersBuilder.ToImmutable()))));
        }

        return compilationUnit.WithMembers(
            CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));
    }

    private bool TryCreateCurrentComponentProbeType(
        out CSharp.ClassDeclarationSyntax componentType)
    {
        componentType = null!;

        var componentName = SyntaxTree.ComponentName;
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return false;
        }

        var metadataName = GetAkburaComponentMetadataName(SyntaxTree);
        if (metadataName.Length > 0 &&
            Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName) != null)
        {
            return false;
        }

        componentType = CSharpSyntaxFactory.ClassDeclaration(ToCSharpIdentifier(componentName))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)));
        var componentTypeInfo = AkburaComponentTypeResolver.Resolve(
            Compilation.CSharpCompilation,
            metadataName);
        if (componentTypeInfo.AkburaControlType != null)
        {
            var baseType = CSharpSyntaxFactory.ParseTypeName(
                componentTypeInfo.AkburaControlType.ToDisplayString(
                    SymbolDisplayFormat.FullyQualifiedFormat));
            componentType = componentType.WithBaseList(CSharpSyntaxFactory.BaseList(
                CSharpSyntaxFactory.SingletonSeparatedList<CSharp.BaseTypeSyntax>(
                    CSharpSyntaxFactory.SimpleBaseType(baseType))));
        }

        return true;
    }

    private static ImmutableArray<AkburaSymbol> CreateMarkupComponentCandidates(
        string componentName,
        ImmutableArray<RoslynSymbol> csharpCandidates)
    {
        if (csharpCandidates.IsDefaultOrEmpty)
        {
            return ImmutableArray<AkburaSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<AkburaSymbol>.Rent();
        var seenSymbols = new HashSet<RoslynSymbol>(SymbolEqualityComparer.Default);
        foreach (var candidate in csharpCandidates)
        {
            if (candidate is not INamedTypeSymbol namedType ||
                namedType.TypeKind == TypeKind.Error ||
                !seenSymbols.Add(namedType))
            {
                continue;
            }

            builder.Add(new MarkupComponentSymbol(
                componentName,
                new CSharpSymbolDefinition(namedType)));
        }

        return builder.ToImmutable();
    }

    [Conditional("DEBUG")]
    protected void ValidateSyntaxTreeOwnership(AkburaSyntax syntax)
    {
        if (!ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()))
        {
            throw new ArgumentException("Syntax node is not part of this semantic model syntax tree.", nameof(syntax));
        }
    }

    [Conditional("DEBUG")]
    private void ValidateBoundSyntaxOwnership(AkburaSyntax syntax)
    {
        if (ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()) ||
            Compilation.TryGetDeclaration(syntax.Root, out _))
        {
            return;
        }

        throw new ArgumentException("Syntax node is not part of this semantic model syntax tree or compilation declarations.", nameof(syntax));
    }

    internal void SetSemanticDiagnostics(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _bindingCache.SetDiagnostics(
            syntax,
            diagnostics.IsDefault
                ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
                : diagnostics);
    }

    internal ImmutableArray<AkburaSemanticDiagnostic> SetSemanticDiagnostics(
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (diagnostics == null)
        {
            throw new ArgumentNullException(nameof(diagnostics));
        }

        var semanticDiagnostics = diagnostics.ToSemanticDiagnostics();
        SetSemanticDiagnostics(syntax, semanticDiagnostics);
        diagnostics.Free();
        return semanticDiagnostics;
    }

    private void SetSemanticDiagnosticsIfAbsent(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        if (!_bindingCache.ContainsDiagnostics(syntax))
        {
            SetSemanticDiagnostics(syntax, diagnostics);
        }
    }

    internal static void AddCSharpBindingDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (!binding.Diagnostics.IsDefaultOrEmpty)
        {
            foreach (var diagnostic in binding.Diagnostics)
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    diagnosticsBuilder.Add(CreateCSharpExpressionErrorDiagnostic(
                        syntax,
                        expressionText,
                        diagnostic));
                }
            }
        }

        AddCSharpConversionDiagnostics(
            syntax,
            expressionText,
            binding,
            diagnosticsBuilder);
    }

    private static void AddCSharpConversionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var conversion = binding.Conversion;
        if (conversion.TargetType == null ||
            conversion.IsImplicit ||
            conversion.SourceType == null ||
            IsSameType(conversion.SourceType, conversion.TargetType))
        {
            return;
        }

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError,
            [
                expressionText,
                "Cannot implicitly convert type '" +
                conversion.SourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                "' to '" +
                conversion.TargetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                "'."
            ]));
    }

    private static AkburaSemanticDiagnostic CreateCSharpExpressionErrorDiagnostic(
        AkburaSyntax syntax,
        string expressionText,
        Diagnostic diagnostic)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_CSharpExpressionError,
            [expressionText, diagnostic.GetMessage()]);
    }
}
