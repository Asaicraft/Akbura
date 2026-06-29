using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Declarations;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
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
using AkburaOperation = Akbura.Language.Operations.IOperation;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynEventSymbol = Microsoft.CodeAnalysis.IEventSymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language;

internal sealed partial class AkburaSemanticModel
{
    private readonly Dictionary<AkburaSyntax, AkburaSymbolInfo> _symbolInfoCache = new();
    private readonly Dictionary<AkburaSyntax, AkburaOperation?> _operationCache = new();
    private readonly Dictionary<AkburaSyntax, BoundNode> _boundNodeCache = new();
    private readonly Dictionary<AkburaSyntax, ImmutableArray<AkburaSemanticDiagnostic>> _semanticDiagnosticsCache = new();
    private readonly Dictionary<AkburaSyntax, MemberSemanticModel> _memberSemanticModelCache = new();
    private readonly SemanticBindingCache _bindingCache;
    private readonly BindingSession _bindingSession;
    private readonly AkburaOperationFactory _operationFactory;
    private readonly AkburaDeclarationSymbolTable _declarationSymbols;

    public AkburaSemanticModel(AkburaCompilation compilation, AkburaSyntaxTree syntaxTree)
    {
        Compilation = compilation ?? throw new ArgumentNullException(nameof(compilation));
        SyntaxTree = syntaxTree ?? throw new ArgumentNullException(nameof(syntaxTree));
        _bindingCache = new SemanticBindingCache(
            _symbolInfoCache,
            _operationCache,
            _boundNodeCache,
            _semanticDiagnosticsCache);
        _bindingSession = new BindingSession(this);
        _operationFactory = new AkburaOperationFactory(CreateCSharpOperationSymbolMapper);
        _declarationSymbols = new AkburaDeclarationSymbolTable(this);
    }

    public AkburaCompilation Compilation { get; }

    public AkburaSyntaxTree SyntaxTree { get; }

    public AkburaSymbolInfo GetSymbolInfo(AkburaSyntax syntax)
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
                AkburaSyntaxKind.UseEffectDeclarationSyntax or
                AkburaSyntaxKind.InlineAkcssBlockSyntax or
                AkburaSyntaxKind.AkcssStyleRuleSyntax or
                AkburaSyntaxKind.AkcssUtilityDeclarationSyntax => GetDeclarationSymbolInfo(syntax),
            AkburaSyntaxKind.MarkupElementSyntax => ResolveMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax => ResolveMarkupProperty(Unsafe.As<MarkupAttributeSyntax>(syntax)),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        });
    }

    public AkburaSymbol? GetDeclaredSymbol(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        if (Compilation.DeclarationTable.TryGetDeclaration(syntax, out var declaration))
        {
            return _declarationSymbols.GetSymbolInfo(declaration).Symbol;
        }

        ValidateSyntaxTreeOwnership(syntax);
        return null;
    }

    public ImmutableArray<AkburaSemanticDiagnostic> GetSemanticDiagnostics(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);
        return _bindingCache.GetDiagnostics(syntax, () =>
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
                AkburaSyntaxKind.UseEffectDeclarationSyntax or
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
                    BindingSession.BindSemanticSyntax(syntax));
            }

            if (syntax.Kind is AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax or
                AkburaSyntaxKind.AkcssAssignmentSyntax or
                AkburaSyntaxKind.AkcssIfDirectiveSyntax or
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
            {
                return CreateSemanticDiagnosticsFromBoundTree(
                    BindingSession.BindOperationSyntax(syntax));
            }
            else if (syntax.Kind == AkburaSyntaxKind.CSharpStatementSyntax)
            {
                var csharpStatement = Unsafe.As<CSharpStatementSyntax>(syntax);
                SetSemanticDiagnosticsIfAbsent(
                    csharpStatement,
                    CreateCSharpStatementUserHookDiagnostics(csharpStatement));
            }

            return _semanticDiagnosticsCache.TryGetValue(syntax, out var diagnostics)
                ? diagnostics
                : [];
        });
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateComponentSemanticDiagnostics(
        AkburaDocumentSyntax document)
    {
        return CreateSemanticDiagnosticsFromBoundTree(
            BindingSession.BindSemanticSyntax(document));
    }

    private static ImmutableArray<AkburaSemanticDiagnostic> CreateSemanticDiagnosticsFromBoundTree(
        BoundNode boundNode)
    {
        var bag = new BindingDiagnosticBag();
        AddBoundTreeDiagnostics(boundNode, bag);
        return bag.ToSemanticDiagnostics();
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
                return _operationCache.TryGetValue(syntax, out var cachedOperation)
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
        return _semanticDiagnosticsCache.TryGetValue(syntax, out var diagnostics)
            ? diagnostics
            : ImmutableArray<AkburaSemanticDiagnostic>.Empty;
    }

    internal bool TryGetCachedBoundNode(AkburaSyntax syntax, out BoundNode boundNode)
    {
        return _boundNodeCache.TryGetValue(syntax, out boundNode!);
    }

    internal BinderType GetBinder(AkburaSyntax syntax)
    {
        return _bindingSession.GetBinder(syntax);
    }

    internal BinderType GetBinder(AkburaSyntax syntax, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, usage);
    }

    internal BinderType GetBinder(AkburaSyntax syntax, int position, BinderUsage usage)
    {
        return _bindingSession.GetBinder(syntax, position, usage);
    }

    internal MemberSemanticModel GetMemberSemanticModel(AkburaSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        ValidateSyntaxTreeOwnership(syntax);

        var scope = GetMemberSemanticModelScope(syntax);
        if (_memberSemanticModelCache.TryGetValue(scope, out var memberModel))
        {
            return memberModel;
        }

        memberModel = new ComponentMemberSemanticModel(this, scope);
        _memberSemanticModelCache.Add(scope, memberModel);
        return memberModel;
    }

    internal BindingSession BindingSession
    {
        get
        {
            return _bindingSession;
        }
    }

    internal AkburaDeclarationSymbolTable DeclarationSymbols => _declarationSymbols;

    internal AkburaSymbolInfo CreateDeclarationSymbolInfo(AkburaDeclaration declaration)
    {
        if (declaration.SyntaxTree != null &&
            !ReferenceEquals(declaration.Syntax.Root, SyntaxTree.GetRoot()))
        {
            var containingModel = Compilation.GetSemanticModel(declaration.SyntaxTree);
            var symbol = containingModel.GetDeclaredSymbol(declaration.Syntax);
            return symbol == null
                ? AkburaSymbolInfo.None(AkburaCandidateReason.NotFound)
                : AkburaSymbolInfo.Success(symbol);
        }

        return declaration.Kind switch
        {
            AkburaDeclarationKind.Component or
                AkburaDeclarationKind.State or
                AkburaDeclarationKind.Parameter or
                AkburaDeclarationKind.InjectedService or
                AkburaDeclarationKind.Command or
                AkburaDeclarationKind.UseEffect =>
                GetMemberSemanticModel(declaration.Syntax).GetSymbolInfo(declaration.Syntax),
            AkburaDeclarationKind.AkcssModule when declaration.Syntax.Kind == AkburaSyntaxKind.InlineAkcssBlockSyntax =>
                ResolveInlineAkcssModule(Unsafe.As<InlineAkcssBlockSyntax>(declaration.Syntax)),
            AkburaDeclarationKind.AkcssModule when declaration.Syntax.Kind == AkburaSyntaxKind.AkcssDocumentSyntax =>
                ResolveExternalAkcssModule(declaration),
            AkburaDeclarationKind.AkcssStyle =>
                ResolveAkcssStyle(Unsafe.As<AkcssStyleRuleSyntax>(declaration.Syntax)),
            AkburaDeclarationKind.AkcssUtility =>
                ResolveTailwindUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(declaration.Syntax)),
            _ => AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax),
        };
    }

    private AkburaSymbolInfo GetDeclarationSymbolInfo(AkburaSyntax syntax)
    {
        return Compilation.DeclarationTable.TryGetDeclaration(syntax, out var declaration)
            ? _declarationSymbols.GetSymbolInfo(declaration)
            : GetMemberSemanticModel(syntax).GetSymbolInfo(syntax);
    }

    private AkburaDocumentSyntax GetMemberSemanticModelScope(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.AkburaDocumentSyntax)
            {
                return Unsafe.As<AkburaDocumentSyntax>(node);
            }
        }

        return SyntaxTree.GetRoot();
    }

    private AkburaSymbolInfo ResolveInlineAkcssModule(InlineAkcssBlockSyntax inlineAkcssBlock)
    {
        var componentInfo = GetSymbolInfo(SyntaxTree.GetRoot());
        if (_symbolInfoCache.TryGetValue(inlineAkcssBlock, out var cachedInfo))
        {
            return cachedInfo;
        }

        var containingComponent = componentInfo.Symbol as IAkburaComponentSymbol;
        var symbol = CreateInlineAkcssModuleSymbol(inlineAkcssBlock, containingComponent);
        var symbolInfo = AkburaSymbolInfo.Success(symbol);
        _symbolInfoCache[inlineAkcssBlock] = symbolInfo;
        SetSemanticDiagnostics(
            inlineAkcssBlock,
            CreateDuplicateAkcssSymbolDiagnostics(inlineAkcssBlock.Members));
        return symbolInfo;
    }

    private ImmutableArray<IAkcssModuleSymbol> CreateInlineAkcssModuleSymbols(
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
            _symbolInfoCache[inlineAkcssBlock] = AkburaSymbolInfo.Success(module);
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

    private AkburaSymbolInfo ResolveExternalAkcssModule(AkburaDeclaration declaration)
    {
        var document = Unsafe.As<AkcssDocumentSyntax>(declaration.Syntax);
        if (_symbolInfoCache.TryGetValue(document, out var cachedInfo))
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
        _symbolInfoCache[document] = symbolInfo;
        SetSemanticDiagnostics(
            document,
            CreateDuplicateAkcssSymbolDiagnostics(document.Members));
        return symbolInfo;
    }

    private static string? GetExternalAkcssPath(AkburaDeclaration declaration)
    {
        var syntaxTree = declaration.AkcssSyntaxTree;
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

    internal BoundNode CreateBoundAkcssSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.InlineAkcssBlockSyntax =>
                CreateBoundInlineAkcssModule(Unsafe.As<InlineAkcssBlockSyntax>(syntax)),
            AkburaSyntaxKind.AkcssDocumentSyntax =>
                CreateBoundExternalAkcssModule(Unsafe.As<AkcssDocumentSyntax>(syntax)),
            AkburaSyntaxKind.AkcssStyleRuleSyntax =>
                CreateBoundAkcssStyle(Unsafe.As<AkcssStyleRuleSyntax>(syntax)),
            AkburaSyntaxKind.AkcssUtilityDeclarationSyntax =>
                CreateBoundAkcssUtility(Unsafe.As<AkcssUtilityDeclarationSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                GetBinder(syntax, BinderUsage.Akcss),
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundAkcssModule CreateBoundInlineAkcssModule(InlineAkcssBlockSyntax inlineAkcssBlock)
    {
        var symbolInfo = GetDeclarationSymbolInfo(inlineAkcssBlock);
        var boundModule = CreateBoundAkcssModule(
            inlineAkcssBlock,
            inlineAkcssBlock.Members,
            symbolInfo);
        _boundNodeCache[inlineAkcssBlock] = boundModule;
        return boundModule;
    }

    private BoundAkcssModule CreateBoundExternalAkcssModule(AkcssDocumentSyntax document)
    {
        var symbolInfo = GetDeclarationSymbolInfo(document);
        var boundModule = CreateBoundAkcssModule(
            document,
            document.Members,
            symbolInfo);
        _boundNodeCache[document] = boundModule;
        return boundModule;
    }

    private BoundAkcssModule CreateBoundAkcssModule(
        AkburaSyntax syntax,
        Akbura.Language.Syntax.SyntaxList<AkcssTopLevelMemberSyntax> members,
        AkburaSymbolInfo symbolInfo)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    childrenBuilder.Add(BindingSession.BindSemanticSyntax(member));
                    break;

                case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                    foreach (var utility in Unsafe.As<AkcssUtilitiesSectionSyntax>(member).Utilities)
                    {
                        childrenBuilder.Add(BindingSession.BindSemanticSyntax(utility));
                    }

                    break;
            }
        }

        var boundModule = new BoundAkcssModule(
            syntax,
            GetBinder(syntax, BinderUsage.Akcss),
            symbolInfo,
            GetCachedSemanticDiagnostics(syntax),
            childrenBuilder.ToImmutable());
        return boundModule;
    }

    private BoundAkcssStyle CreateBoundAkcssStyle(AkcssStyleRuleSyntax styleRule)
    {
        var symbolInfo = GetDeclarationSymbolInfo(styleRule);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(CreateAkcssBoundOperations(styleRule.Members, symbol));
        var boundStyle = new BoundAkcssStyle(
            styleRule,
            GetBinder(styleRule, BinderUsage.Akcss),
            symbolInfo,
            GetCachedSemanticDiagnostics(styleRule),
            children);
        _boundNodeCache[styleRule] = boundStyle;
        return boundStyle;
    }

    private BoundAkcssUtility CreateBoundAkcssUtility(AkcssUtilityDeclarationSyntax utilityDeclaration)
    {
        var symbolInfo = GetDeclarationSymbolInfo(utilityDeclaration);
        var symbol = symbolInfo.Symbol as IAkcssSymbol;
        var children = symbol == null
            ? ImmutableArray<BoundNode>.Empty
            : CastBoundNodes(CreateAkcssBoundOperations(utilityDeclaration.Members, symbol));
        var boundUtility = new BoundAkcssUtility(
            utilityDeclaration,
            GetBinder(utilityDeclaration, BinderUsage.Akcss),
            symbolInfo,
            GetCachedSemanticDiagnostics(utilityDeclaration),
            children);
        _boundNodeCache[utilityDeclaration] = boundUtility;
        return boundUtility;
    }

    private static ImmutableArray<BoundNode> CastBoundNodes(ImmutableArray<BoundAkcssOperation> operations)
    {
        if (operations.IsDefaultOrEmpty)
        {
            return ImmutableArray<BoundNode>.Empty;
        }

        using var builder = ImmutableArrayBuilder<BoundNode>.Rent(operations.Length);
        foreach (var operation in operations)
        {
            builder.Add(operation);
        }

        return builder.ToImmutable();
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
            var diagnosticsBag = new BindingDiagnosticBag();
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
            var diagnosticsBag = new BindingDiagnosticBag();
            diagnosticsBag.Add(CreateAkcssSelectorTargetNotFoundDiagnostic(
                    utilityDeclaration,
                    utilityDeclaration.Selector.TargetType?.ToFullString().Trim() ?? string.Empty));
            diagnosticsBag.AddRange(CreateDuplicateAkcssSymbolDiagnostics(utilityDeclaration));
            SetSemanticDiagnostics(utilityDeclaration, diagnosticsBag);
            return AkburaSymbolInfo.None(AkburaCandidateReason.NotFound);
        }

        var symbol = CreateTailwindUtilitySymbolForAkcss(utilityDeclaration, targetType, includeOperations: true);

        var diagnostics = new BindingDiagnosticBag();
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
        using var interceptOperationsBuilder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (member.Kind != AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
            {
                continue;
            }

            var interceptDirective = Unsafe.As<AkcssInterceptDirectiveSyntax>(member);
            if (!_operationCache.TryGetValue(interceptDirective, out var cachedInterceptOperation) ||
                cachedInterceptOperation is not IAkcssInterceptOperation interceptOperation)
            {
                var interceptBoundNode = CreateBoundAkcssInterceptCore(interceptDirective, containingSymbol);
                _boundNodeCache[interceptDirective] = interceptBoundNode;
                interceptOperation = (IAkcssInterceptOperation)_operationFactory.CreateOperation(interceptBoundNode)!;
                _operationCache[interceptDirective] = interceptOperation;
            }

            interceptOperationsBuilder.Add(interceptOperation);
            if (!interceptOperation.InterceptType.IsDefault)
            {
                SetAkcssInterceptType(containingSymbol, interceptOperation.InterceptType);
            }
        }

        var interceptOperations = interceptOperationsBuilder.ToImmutable();
        if (containingSymbol.IsIntercepted)
        {
            foreach (var member in members)
            {
                if (member.Kind != AkburaSyntaxKind.AkcssInterceptDirectiveSyntax)
                {
                    SetAkcssInterceptIgnoredDiagnostics(member, containingSymbol);
                }
            }

            return interceptOperations;
        }

        using var builder = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssAssignmentSyntax:
                {
                    var assignment = Unsafe.As<AkcssAssignmentSyntax>(member);
                    if (!_operationCache.TryGetValue(assignment, out var cachedOperation) ||
                        cachedOperation is not IAkcssOperation operation)
                    {
                        var boundNode = CreateBoundAkcssPropertySetterCore(assignment, containingSymbol);
                        _boundNodeCache[assignment] = boundNode;
                        operation = (IAkcssOperation)_operationFactory.CreateOperation(boundNode)!;
                        _operationCache[assignment] = operation;
                    }

                    builder.Add(operation);
                    break;
                }

                case AkburaSyntaxKind.AkcssIfDirectiveSyntax:
                {
                    var ifDirective = Unsafe.As<AkcssIfDirectiveSyntax>(member);
                    if (!_operationCache.TryGetValue(ifDirective, out var cachedOperation) ||
                        cachedOperation is not IAkcssOperation ifOperation)
                    {
                        var boundNode = CreateBoundAkcssIfCore(ifDirective, containingSymbol);
                        _boundNodeCache[ifDirective] = boundNode;
                        ifOperation = (IAkcssOperation)_operationFactory.CreateOperation(boundNode)!;
                        _operationCache[ifDirective] = ifOperation;
                    }

                    builder.Add(ifOperation);
                    break;
                }

                case AkburaSyntaxKind.AkcssApplyDirectiveSyntax:
                {
                    var applyDirective = Unsafe.As<AkcssApplyDirectiveSyntax>(member);
                    if (!_operationCache.TryGetValue(applyDirective, out var cachedOperation) ||
                        cachedOperation is not IAkcssOperation applyOperation)
                    {
                        var boundNode = CreateBoundAkcssApplyCore(applyDirective, containingSymbol);
                        _boundNodeCache[applyDirective] = boundNode;
                        applyOperation = (IAkcssOperation)_operationFactory.CreateOperation(boundNode)!;
                        _operationCache[applyDirective] = applyOperation;
                    }

                    builder.Add(applyOperation);
                    break;
                }

                case AkburaSyntaxKind.AkcssInterceptDirectiveSyntax:
                {
                    var interceptDirective = Unsafe.As<AkcssInterceptDirectiveSyntax>(member);
                    var interceptOperation = (IAkcssInterceptOperation?)_operationCache[interceptDirective];
                    if (interceptOperation == null)
                    {
                        var boundNode = CreateBoundAkcssInterceptCore(interceptDirective, containingSymbol);
                        _boundNodeCache[interceptDirective] = boundNode;
                        interceptOperation = (IAkcssInterceptOperation)_operationFactory.CreateOperation(boundNode)!;
                    }

                    _operationCache[interceptDirective] = interceptOperation;
                    builder.Add(interceptOperation);
                    break;
                }
            }
        }

        return builder.ToImmutable();
    }

    private ImmutableArray<BoundAkcssOperation> CreateAkcssBoundOperations(
        Akbura.Language.Syntax.SyntaxList<AkcssBodyMemberSyntax> members,
        IAkcssSymbol containingSymbol)
    {
        using var builder = ImmutableArrayBuilder<BoundAkcssOperation>.Rent();
        foreach (var member in members)
        {
            if (_boundNodeCache.TryGetValue(member, out var cachedBoundNode) &&
                cachedBoundNode is BoundAkcssOperation cachedOperation)
            {
                builder.Add(cachedOperation);
                continue;
            }

            BoundNode? boundNode = member.Kind switch
            {
                AkburaSyntaxKind.AkcssAssignmentSyntax =>
                    CreateBoundAkcssPropertySetterCore(
                        Unsafe.As<AkcssAssignmentSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                    CreateBoundAkcssIfCore(
                        Unsafe.As<AkcssIfDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                    CreateBoundAkcssApplyCore(
                        Unsafe.As<AkcssApplyDirectiveSyntax>(member),
                        containingSymbol),
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                    CreateBoundAkcssInterceptCore(
                        Unsafe.As<AkcssInterceptDirectiveSyntax>(member),
                        containingSymbol),
                _ => null,
            };

            if (boundNode is BoundAkcssOperation operation)
            {
                _boundNodeCache[member] = operation;
                builder.Add(operation);
            }
        }

        return builder.ToImmutable();
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

    private void SetAkcssInterceptIgnoredDiagnostics(
        AkcssBodyMemberSyntax member,
        IAkcssSymbol containingSymbol)
    {
        SetSemanticDiagnostics(
            member,
            ImmutableArray.Create(CreateAkcssInterceptIgnoresMemberDiagnostic(member, containingSymbol)));
    }

    private static void SetAkcssInterceptType(
        IAkcssSymbol containingSymbol,
        CSharpSymbolDefinition interceptType)
    {
        switch (containingSymbol)
        {
            case AkcssStyleSymbol styleSymbol:
                styleSymbol.SetInterceptType(interceptType);
                break;
            case TailwindUtilitySymbol utilitySymbol:
                utilitySymbol.SetInterceptType(interceptType);
                break;
        }
    }

    internal BoundAkcssIf CreateBoundAkcssIfCore(
        AkcssIfDirectiveSyntax ifDirective,
        IAkcssSymbol containingSymbol)
    {
        var expression = ParseAkcssConditionExpression(ifDirective);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : BindAkcssExpression(expression, containingSymbol);
        var conditionType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var operations = CreateAkcssBoundOperations(ifDirective.Members, containingSymbol);

        var diagnosticsBag = new BindingDiagnosticBag();
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddAkcssExpressionDiagnostics(
                ifDirective,
                ifDirective.Condition.ToFullString().Trim(),
                binding,
                diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }
        var diagnostics = SetSemanticDiagnostics(ifDirective, diagnosticsBag);

        return new BoundAkcssIf(
            ifDirective,
            GetBinder(ifDirective, BinderUsage.Akcss),
            containingSymbol,
            conditionType,
            binding.OperationDefinition,
            operations,
            diagnostics,
            hasErrors: expression == null ||
                diagnostics.Length > 0 ||
                operations.Any(static operation => operation.HasErrors));
    }

    internal BoundAkcssApply CreateBoundAkcssApplyCore(
        AkcssApplyDirectiveSyntax applyDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();
        using var itemsBuilder = ImmutableArrayBuilder<string>.Rent();
        using var symbolsBuilder = ImmutableArrayBuilder<IAkcssSymbol>.Rent();

        foreach (var item in GetAkcssApplyItems(applyDirective))
        {
            itemsBuilder.Add(item);
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            var resolved = ResolveAkcssApplyItem(
                    applyDirective,
                    item,
                    containingSymbol,
                    diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
            if (resolved != null)
            {
                symbolsBuilder.Add(resolved);
            }
        }

        var diagnostics = SetSemanticDiagnostics(applyDirective, diagnosticsBag);

        return new BoundAkcssApply(
            applyDirective,
            GetBinder(applyDirective, BinderUsage.Akcss),
            containingSymbol,
            itemsBuilder.ToImmutable(),
            symbolsBuilder.ToImmutable(),
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    internal BoundAkcssIntercept CreateBoundAkcssInterceptCore(
        AkcssInterceptDirectiveSyntax interceptDirective,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();
        var interceptType = default(CSharpSymbolDefinition);

        CSharp.TypeSyntax typeSyntax;
        try
        {
            typeSyntax = interceptDirective.Type.ToCSharp();
        }
        catch (InvalidOperationException)
        {
            typeSyntax = CSharpSyntaxFactory.ParseTypeName(interceptDirective.Type.ToFullString());
        }

        var binding = BindCSharpType(typeSyntax, GetAkcssCSharpUsingDirectives(containingSymbol));
        if (binding.TypeSymbol is not INamedTypeSymbol namedType)
        {
            diagnosticsBag.Add(CreateAkcssInterceptTypeNotFoundDiagnostic(
                interceptDirective,
                interceptDirective.Type.ToFullString().Trim()));
        }
        else if (!IsAkcssInterceptRuntimeType(namedType, containingSymbol, out var expectedBaseType))
        {
            diagnosticsBag.Add(CreateAkcssInterceptTypeInvalidDiagnostic(
                interceptDirective,
                namedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                expectedBaseType));
        }
        else
        {
            interceptType = new CSharpSymbolDefinition(namedType);
        }

        var diagnostics = SetSemanticDiagnostics(interceptDirective, diagnosticsBag);

        return new BoundAkcssIntercept(
            interceptDirective,
            GetBinder(interceptDirective, BinderUsage.Akcss),
            containingSymbol,
            interceptType,
            diagnostics,
            hasErrors: diagnostics.Length > 0);
    }

    internal BoundAkcssPropertySetter CreateBoundAkcssPropertySetterCore(
        AkcssAssignmentSyntax assignment,
        IAkcssSymbol containingSymbol)
    {
        var diagnosticsBag = new BindingDiagnosticBag();

        var property = ResolveAkcssPropertyWithDiagnostics(
            assignment,
            containingSymbol,
            diagnosticsBag);
        var expression = ParseAkcssAssignmentExpression(assignment);
        var binding = expression == null
            ? CSharpBindingResult.Empty
            : BindAkcssExpression(expression, containingSymbol);

        var valueType = binding.TypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(binding.TypeSymbol);
        var valueOperation = binding.OperationDefinition;
        var valueKind = AkcssPropertyValueKind.CSharpExpression;
        var requiresBrushConversion = false;
        object? convertedValue = null;
        var activeBinding = binding;
        var hasExpectedTypeBinding = false;

        if (property?.Type.Symbol is ITypeSymbol expectedType && expression != null)
        {
            if (IsAkcssColorPropertyType(expectedType))
            {
                if (TryGetAkcssColorIdentifierText(expression, out var colorName))
                {
                    if (TryBindAvaloniaNamedColor(colorName, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorName,
                            property.Name));
                    }
                }
                else if (TryGetAkcssColorStringLiteralText(expression, out var colorText))
                {
                    if (ColorParser.TryParse(colorText, out var color))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = color;
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        if (TryGetAvaloniaColorType(out var colorType))
                        {
                            valueType = new CSharpSymbolDefinition(colorType);
                        }
                    }
                    else if (TryBindAvaloniaNamedColor(colorText, containingSymbol, out var namedColorBinding))
                    {
                        valueKind = AkcssPropertyValueKind.ColorLiteral;
                        convertedValue = new CSharpSymbolDefinition(namedColorBinding.Symbol!);
                        valueOperation = namedColorBinding.OperationDefinition;
                        activeBinding = namedColorBinding;
                        requiresBrushConversion = IsAvaloniaBrushType(expectedType);
                        valueType = namedColorBinding.TypeSymbol == null
                            ? valueType
                            : new CSharpSymbolDefinition(namedColorBinding.TypeSymbol);
                    }
                    else
                    {
                        valueKind = AkcssPropertyValueKind.Error;
                        diagnosticsBag.Add(CreateAkcssInvalidColorDiagnostic(
                            assignment,
                            colorText,
                            property.Name));
                    }
                }
                else if (binding.TypeSymbol != null &&
                         IsAvaloniaColorType(binding.TypeSymbol) &&
                         IsAvaloniaBrushType(expectedType))
                {
                    requiresBrushConversion = true;
                }
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                TryBindExpectedTypeStaticMember(
                    expression,
                    expectedType,
                    containingSymbol,
                    out var expectedTypeMemberBinding))
            {
                convertedValue = expectedTypeMemberBinding.Symbol == null
                    ? null
                    : new CSharpSymbolDefinition(expectedTypeMemberBinding.Symbol);
                valueOperation = expectedTypeMemberBinding.OperationDefinition;
                activeBinding = expectedTypeMemberBinding;
                hasExpectedTypeBinding = true;
                valueType = expectedTypeMemberBinding.TypeSymbol == null
                    ? valueType
                    : new CSharpSymbolDefinition(expectedTypeMemberBinding.TypeSymbol);
            }
            else if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                     TryAcceptExpectedTypeCastExpression(expression, expectedType, containingSymbol))
            {
                valueType = new CSharpSymbolDefinition(expectedType);
                activeBinding = CSharpBindingResult.Empty;
                hasExpectedTypeBinding = true;
            }

            var isThicknessPropertyType = IsAvaloniaThicknessType(expectedType);
            var isThicknessTuple = false;
            object? thickness = null;
            if (isThicknessPropertyType &&
                TryCreateAkcssThicknessValue(
                    expression,
                    assignment.Expression.ToFullString(),
                    out thickness,
                    out isThicknessTuple))
            {
                valueKind = AkcssPropertyValueKind.ThicknessTuple;
                convertedValue = thickness;
                valueType = new CSharpSymbolDefinition(expectedType);
                activeBinding = CSharpBindingResult.Empty;
            }
            else if (isThicknessPropertyType && isThicknessTuple)
            {
                valueKind = AkcssPropertyValueKind.Error;
                diagnosticsBag.Add(CreateAkcssInvalidThicknessDiagnostic(
                    assignment,
                    assignment.Expression.ToFullString().Trim(),
                    property.Name));
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                TryCreateAkcssAmxInvocationValue(expression, out var amxInvocation))
            {
                valueKind = AkcssPropertyValueKind.AmxInvocation;
                convertedValue = amxInvocation;
            }

            if (valueKind == AkcssPropertyValueKind.CSharpExpression &&
                !hasExpectedTypeBinding &&
                !requiresBrushConversion)
            {
                activeBinding = BindAkcssExpression(
                    expression,
                    containingSymbol,
                    expectedType);
                valueOperation = activeBinding.OperationDefinition;
                valueType = activeBinding.TypeSymbol == null
                    ? valueType
                    : new CSharpSymbolDefinition(activeBinding.TypeSymbol);
            }
        }

        if (valueKind != AkcssPropertyValueKind.Error)
        {
            using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            AddAkcssExpressionDiagnostics(
                    assignment,
                    activeBinding,
                    diagnosticsBuilder);
            AddAkcssValueConversionDiagnostics(
                    assignment,
                    property,
                    valueKind,
                    requiresBrushConversion,
                    activeBinding,
                    diagnosticsBuilder);
            diagnosticsBag.AddRange(diagnosticsBuilder.ToImmutable());
        }

        var diagnostics = SetSemanticDiagnostics(assignment, diagnosticsBag);

        return new BoundAkcssPropertySetter(
            assignment,
            GetBinder(assignment, BinderUsage.Akcss),
            containingSymbol,
            property,
            valueType,
            valueOperation,
            valueKind,
            requiresBrushConversion,
            convertedValue,
            diagnostics,
            property == null || valueKind == AkcssPropertyValueKind.Error || diagnostics.Length > 0);
    }

    private static ImmutableArray<string> GetAkcssApplyItems(AkcssApplyDirectiveSyntax applyDirective)
    {
        var text = applyDirective.Items.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return ImmutableArray<string>.Empty;
        }

        return text
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .ToImmutableArray();
    }

    private void AddAkcssExpressionDiagnostics(
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

    private void AddAkcssExpressionDiagnostics(
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

    private void AddAkcssValueConversionDiagnostics(
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

    private IAkcssSymbol? ResolveAkcssApplyItem(
        AkcssApplyDirectiveSyntax applyDirective,
        string item,
        IAkcssSymbol containingSymbol,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var (name, argumentCount) = ParseAkcssApplyItem(item);
        if (name.Length == 0)
        {
            diagnosticsBuilder.Add(CreateAkcssApplyItemNotFoundDiagnostic(applyDirective, item));
            return null;
        }

        var localCandidates = FindAkcssApplyCandidates(
            GetContainingAkcssLayer(containingSymbol.DeclarationSyntax),
            name,
            argumentCount);
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
            var candidates = FindAkcssApplyCandidates(layer, name, argumentCount);
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

    private static (string Name, int ArgumentCount) ParseAkcssApplyItem(string item)
    {
        var text = item.Trim();
        if (text.Length == 0)
        {
            return (string.Empty, 0);
        }

        var firstDash = text.IndexOf('-');
        if (firstDash < 0)
        {
            return (text, 0);
        }

        var name = text[..firstDash];
        var argumentCount = text[(firstDash + 1)..]
            .Split(['-'], StringSplitOptions.RemoveEmptyEntries)
            .Length;
        return (name, argumentCount);
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
            var matches = Compilation.AkcssSyntaxTrees
                .Where(tree => string.Equals(tree.LogicalName, importName, StringComparison.Ordinal))
                .ToImmutableArray();
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

    private AkburaPropertySymbol? ResolveAkcssPropertyWithDiagnostics(
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
                avaloniaProperty),
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
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

    private static CSharp.ExpressionSyntax? ParseAkcssAssignmentExpression(AkcssAssignmentSyntax assignment)
    {
        try
        {
            return CSharpSyntaxFactory.ParseExpression(assignment.Expression.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static CSharp.ExpressionSyntax? ParseAkcssConditionExpression(AkcssIfDirectiveSyntax ifDirective)
    {
        try
        {
            return CSharpSyntaxFactory.ParseExpression(ifDirective.Condition.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static bool TryGetAkcssColorIdentifierText(
        CSharp.ExpressionSyntax expression,
        out string text)
    {
        text = expression is CSharp.IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : string.Empty;

        return text.Length > 0;
    }

    private static bool TryGetAkcssColorStringLiteralText(
        CSharp.ExpressionSyntax expression,
        out string text)
    {
        text = expression is CSharp.LiteralExpressionSyntax literal &&
            literal.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression)
                ? literal.Token.ValueText
                : string.Empty;

        return text.Length > 0;
    }

    private bool TryBindAvaloniaNamedColor(
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

    private bool TryBindExpectedTypeStaticMember(
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

    private bool TryAcceptExpectedTypeCastExpression(
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

    private static bool TryCreateAkcssThicknessValue(
        CSharp.ExpressionSyntax expression,
        string rawText,
        out object? thickness,
        out bool isThicknessTuple)
    {
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

    private bool TryCreateAkcssAmxInvocationValue(
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

    private bool IsAkcssColorPropertyType(ITypeSymbol type)
    {
        return IsAvaloniaColorType(type) || IsAvaloniaBrushType(type);
    }

    private bool IsAvaloniaColorType(ITypeSymbol type)
    {
        return TryGetAvaloniaColorType(out var colorType) &&
            IsSameType(type, colorType);
    }

    private bool IsAvaloniaBrushType(ITypeSymbol type)
    {
        return TryGetAvaloniaBrushType(out var brushType) &&
            IsAssignableTo(type, brushType);
    }

    private bool IsAvaloniaThicknessType(ITypeSymbol type)
    {
        return TryGetAvaloniaThicknessType(out var thicknessType) &&
            IsSameType(type, thicknessType);
    }

    private bool TryGetAvaloniaColorType(out INamedTypeSymbol colorType)
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

    private AkburaSemanticDiagnostic CreateAkcssInvalidColorDiagnostic(
        AkcssAssignmentSyntax syntax,
        string colorText,
        string propertyName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInvalidColor,
            [colorText, propertyName]);
    }

    private AkburaSemanticDiagnostic CreateAkcssInvalidThicknessDiagnostic(
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

    private AkburaSemanticDiagnostic CreateAkcssInterceptTypeNotFoundDiagnostic(
        AkcssInterceptDirectiveSyntax syntax,
        string typeName)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_AkcssInterceptTypeNotFound,
            [typeName]);
    }

    private AkburaSemanticDiagnostic CreateAkcssInterceptTypeInvalidDiagnostic(
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

    private bool IsAkcssInterceptRuntimeType(
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

    private static CSharp.ParameterListSyntax? GetCSharpParameterList(
        CSharpParameterListSyntax parameterListSyntax)
    {
        return parameterListSyntax.Parameters.Node is GreenSyntaxToken.CSharpRawToken rawToken
            ? rawToken.RawNode as CSharp.ParameterListSyntax
            : null;
    }

    private static CSharp.ArgumentListSyntax? GetCSharpArgumentList(
        CSharpArgumentListSyntax argumentListSyntax)
    {
        return argumentListSyntax.Parameters.Node is GreenSyntaxToken.CSharpRawToken rawToken
            ? rawToken.RawNode as CSharp.ArgumentListSyntax
            : null;
    }

    private static bool TryGetUseEffectDependencyRootName(
        CSharp.ExpressionSyntax expression,
        out string rootName)
    {
        expression = UnwrapUseEffectDependencyExpression(expression);
        switch (expression)
        {
            case CSharp.IdentifierNameSyntax identifierName:
                rootName = identifierName.Identifier.ValueText;
                return !string.IsNullOrWhiteSpace(rootName);

            case CSharp.MemberAccessExpressionSyntax memberAccess:
                return TryGetUseEffectDependencyRootName(memberAccess.Expression, out rootName);

            case CSharp.ConditionalAccessExpressionSyntax conditionalAccess:
                return TryGetUseEffectDependencyRootName(conditionalAccess.Expression, out rootName);

            case CSharp.ElementAccessExpressionSyntax elementAccess:
                return TryGetUseEffectDependencyRootName(elementAccess.Expression, out rootName);

            case CSharp.InvocationExpressionSyntax invocation:
                return TryGetUseEffectDependencyRootName(invocation.Expression, out rootName);

            default:
                rootName = string.Empty;
                return false;
        }
    }

    private static CSharp.ExpressionSyntax UnwrapUseEffectDependencyExpression(
        CSharp.ExpressionSyntax expression)
    {
        while (expression is CSharp.ParenthesizedExpressionSyntax parenthesized)
        {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private IUserHookSymbol? ResolveUserHookInvocation(string invocationName)
    {
        var candidates = GetUserHookTypeNameCandidates(invocationName);
        foreach (var candidate in candidates)
        {
            foreach (var metadataName in GetUserHookMetadataNameCandidates(candidate))
            {
                var type = Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName);
                if (type != null &&
                    TryCreateUserHookSymbol(invocationName, type, out var symbol))
                {
                    return symbol;
                }
            }
        }

        foreach (var type in GetAllNamedTypes(Compilation.CSharpCompilation.Assembly.GlobalNamespace))
        {
            if (candidates.Contains(type.Name, StringComparer.Ordinal) &&
                TryCreateUserHookSymbol(invocationName, type, out var symbol))
            {
                return symbol;
            }
        }

        return null;
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateCSharpStatementUserHookDiagnostics(
        CSharpStatementSyntax statement)
    {
        using var builder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var rawNode = TryParseCSharpRawNode(statement.Tokens.ToFullString());
        if (rawNode == null)
        {
            return ImmutableArray<AkburaSemanticDiagnostic>.Empty;
        }

        foreach (var invocation in rawNode.DescendantNodesAndSelf().OfType<CSharp.InvocationExpressionSyntax>())
        {
            if (TryGetUserHookInvocationName(invocation, out var invocationName) &&
                ResolveUserHookInvocation(invocationName) != null)
            {
                builder.Add(CreateUserHookMustBeTopLevelDiagnostic(statement, invocationName));
            }
        }

        return builder.ToImmutable();
    }

    private static Microsoft.CodeAnalysis.SyntaxNode? TryParseCSharpRawNode(string text)
    {
        try
        {
            return CSharpSyntaxFactory.ParseStatement(text);
        }
        catch (ArgumentException)
        {
            try
            {
                return CSharpSyntaxFactory.ParseExpression(text);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }
    }

    private static bool TryGetStateUserHookInvocation(
        StateDeclarationSyntax stateDeclaration,
        out string invocationName)
    {
        invocationName = string.Empty;
        CSharp.ExpressionSyntax expression;
        try
        {
            expression = CSharpSyntaxFactory.ParseExpression(
                stateDeclaration.Initializer.Expression.ToFullString());
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (expression is not CSharp.InvocationExpressionSyntax invocation ||
            !TryGetUserHookInvocationName(invocation, out invocationName))
        {
            return false;
        }

        return true;
    }

    private static bool TryGetUserHookInvocationName(
        CSharp.InvocationExpressionSyntax invocation,
        out string invocationName)
    {
        invocationName = invocation.Expression is CSharp.IdentifierNameSyntax identifier
            ? identifier.Identifier.ValueText
            : string.Empty;
        return invocationName.StartsWith("use", StringComparison.Ordinal) &&
            invocationName.Length > 3;
    }

    private static ImmutableArray<string> GetUserHookTypeNameCandidates(string invocationName)
    {
        var suffix = invocationName.StartsWith("use", StringComparison.Ordinal)
            ? invocationName[3..]
            : invocationName;
        if (suffix.Length == 0)
        {
            return ImmutableArray<string>.Empty;
        }

        var typeStem = "Use" + char.ToUpperInvariant(suffix[0]) + suffix[1..];
        return ImmutableArray.Create(typeStem + "Hook", typeStem);
    }

    private IEnumerable<string> GetUserHookMetadataNameCandidates(string typeName)
    {
        foreach (var @namespace in GetAkburaUsingNamespaces())
        {
            yield return @namespace + "." + typeName;
        }

        var currentNamespace = GetAkburaNamespaceText(SyntaxTree.GetRoot(), SyntaxTree);
        if (currentNamespace.Length > 0)
        {
            yield return currentNamespace + "." + typeName;
        }

        yield return typeName;
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamespaceSymbol @namespace)
    {
        foreach (var type in @namespace.GetTypeMembers())
        {
            foreach (var nestedType in GetAllNamedTypes(type))
            {
                yield return nestedType;
            }
        }

        foreach (var nestedNamespace in @namespace.GetNamespaceMembers())
        {
            foreach (var type in GetAllNamedTypes(nestedNamespace))
            {
                yield return type;
            }
        }
    }

    private static IEnumerable<INamedTypeSymbol> GetAllNamedTypes(INamedTypeSymbol type)
    {
        yield return type;
        foreach (var nestedType in type.GetTypeMembers())
        {
            foreach (var candidate in GetAllNamedTypes(nestedType))
            {
                yield return candidate;
            }
        }
    }

    private static bool TryCreateUserHookSymbol(
        string invocationName,
        INamedTypeSymbol type,
        out IUserHookSymbol symbol)
    {
        symbol = null!;
        if (!type.Name.StartsWith("Use", StringComparison.Ordinal) ||
            !HasUserHookAttribute(type))
        {
            return false;
        }

        var useHookMethod = type.GetMembers("UseHook")
            .OfType<IMethodSymbol>()
            .FirstOrDefault(static method =>
                method.DeclaredAccessibility == Accessibility.Public &&
                method.MethodKind == MethodKind.Ordinary);
        if (useHookMethod == null)
        {
            return false;
        }

        symbol = new UserHookSymbol(invocationName, type, useHookMethod);
        return true;
    }

    private static bool HasUserHookAttribute(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes())
        {
            var attributeClass = attribute.AttributeClass;
            if (attributeClass == null)
            {
                continue;
            }

            if (attributeClass.Name is "UserHook" or "UserHookAttribute")
            {
                return true;
            }
        }

        return false;
    }

    private static StateBindingKind GetStateBindingKind(StateInitializerSyntax initializer)
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

    private static ParamBindingKind GetParamBindingKind(ParamDeclarationSyntax paramDeclaration)
    {
        return paramDeclaration.BindingKeyword.Kind switch
        {
            Akbura.Language.Syntax.SyntaxKind.BindToken => ParamBindingKind.Bind,
            Akbura.Language.Syntax.SyntaxKind.OutToken => ParamBindingKind.Out,
            _ => ParamBindingKind.Default,
        };
    }

    private ImmutableArray<AkburaSemanticDiagnostic> CreateStateBindingDiagnostics(
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

    private static bool IsStateBindingPath(CSharp.ExpressionSyntax expression)
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

    private AkburaSemanticDiagnostic CreateUserHookMustBeTopLevelDiagnostic(
        CSharpStatementSyntax statement,
        string invocationName)
    {
        return new AkburaSemanticDiagnostic(
            statement,
            ErrorCodes.AKBURA_SEMANTIC_UserHookMustBeTopLevel,
            [invocationName],
            AkburaDiagnosticSeverity.Error);
    }

    internal BoundNode CreateBoundMarkupSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax =>
                CreateBoundMarkupRoot(Unsafe.As<MarkupRootSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementSyntax =>
                CreateBoundMarkupComponent(Unsafe.As<MarkupElementSyntax>(syntax)),
            AkburaSyntaxKind.MarkupElementContentSyntax or
                AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax =>
                CreateBoundMarkupContent(Unsafe.As<MarkupContentSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                GetBinder(syntax, BinderUsage.Markup),
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundMarkupRoot CreateBoundMarkupRoot(MarkupRootSyntax markupRoot)
    {
        var element = BindingSession.BindSemanticSyntax(markupRoot.Element);
        var symbolInfo = element.SymbolInfo;
        var boundRoot = new BoundMarkupRoot(
            markupRoot,
            GetBinder(markupRoot, BinderUsage.Markup),
            symbolInfo,
            GetCachedSemanticDiagnostics(markupRoot),
            ImmutableArray.Create(element));
        _boundNodeCache[markupRoot] = boundRoot;
        return boundRoot;
    }

    private BoundMarkupComponent CreateBoundMarkupComponent(MarkupElementSyntax markupElement)
    {
        var symbolInfo = GetSymbolInfo(markupElement);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();

        if (markupElement.StartTag != null)
        {
            foreach (var attribute in markupElement.StartTag.Attributes)
            {
                childrenBuilder.Add(BindingSession.BindOperationSyntax(attribute));
            }
        }

        foreach (var content in markupElement.Body)
        {
            childrenBuilder.Add(BindingSession.BindSemanticSyntax(content));
        }

        var contentSetter = CreateBoundMarkupContentSetter(markupElement, componentSymbol);
        if (contentSetter != null)
        {
            childrenBuilder.Add(contentSetter);
        }

        var boundComponent = new BoundMarkupComponent(
            markupElement,
            GetBinder(markupElement, BinderUsage.Markup),
            symbolInfo,
            GetCachedSemanticDiagnostics(markupElement),
            childrenBuilder.ToImmutable());
        _boundNodeCache[markupElement] = boundComponent;
        return boundComponent;
    }

    private BoundMarkupContentSetter? CreateBoundMarkupContentSetter(
        MarkupElementSyntax markupElement,
        IMarkupComponentSymbol? componentSymbol)
    {
        if (componentSymbol == null ||
            componentSymbol.ContentModel.IsDefault ||
            HasElementContent(markupElement) ||
            !TryCreateMarkupContentValueExpression(
                markupElement,
                out var expression,
                out var literalValue,
                out var isSynthesizedString,
                out var hasText,
                out var diagnosticSyntax))
        {
            return null;
        }

        var property = CreateMarkupContentPropertySymbol(componentSymbol);
        var targetType = GetMarkupContentTargetType(componentSymbol.ContentModel);
        var binding = BindMarkupAttributeExpression(diagnosticSyntax, expression, targetType);
        var valueTypeSymbol = binding.TypeSymbol ??
            binding.Conversion.SourceType ??
            binding.OperationDefinition.Type;
        if (valueTypeSymbol == null &&
            !isSynthesizedString &&
            !hasText &&
            diagnosticSyntax.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
        {
            var directBinding = BindMarkupAttributeExpression(
                diagnosticSyntax,
                expression);
            valueTypeSymbol = directBinding.TypeSymbol ??
                directBinding.OperationDefinition.Type;
        }

        var valueType = valueTypeSymbol == null
            ? default
            : new CSharpSymbolDefinition(valueTypeSymbol);

        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        AddMarkupExpressionDiagnostics(
            markupElement,
            expression.ToFullString(),
            binding,
            diagnosticsBuilder);
        AddMarkupContentValueDiagnostics(
            diagnosticSyntax,
            componentSymbol.ContentModel,
            binding,
            hasText,
            diagnosticsBuilder);
        var diagnostics = diagnosticsBuilder.ToImmutable();

        return new BoundMarkupContentSetter(
            markupElement,
            GetBinder(markupElement, BinderUsage.Markup),
            componentSymbol,
            property,
            componentSymbol.ContentModel,
            componentSymbol.Children,
            valueType,
            binding.OperationDefinition,
            literalValue,
            isSynthesizedString,
            diagnostics,
            property == null || diagnostics.Length > 0);
    }

    private AkburaPropertySymbol? CreateMarkupContentPropertySymbol(IMarkupComponentSymbol componentSymbol)
    {
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
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            new CSharpSymbolDefinition(contentProperty),
            containingSymbol: componentSymbol,
            isImplicitlyDeclared: true);
    }

    private static ITypeSymbol? GetMarkupContentTargetType(MarkupContentModel contentModel)
    {
        return contentModel.AllowedChildType.Symbol is ITypeSymbol type &&
            type.SpecialType != SpecialType.System_Object
                ? type
                : null;
    }

    private static bool HasElementContent(MarkupElementSyntax markupElement)
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

    private static bool TryCreateMarkupContentValueExpression(
        MarkupElementSyntax markupElement,
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

        CSharp.ExpressionSyntax? singleExpression = null;
        var expressionCount = 0;
        var literalBuilder = new System.Text.StringBuilder();
        var interpolatedBuilder = new System.Text.StringBuilder();

        foreach (var content in markupElement.Body)
        {
            if (content.Kind == AkburaSyntaxKind.MarkupTextLiteralSyntax)
            {
                var textLiteral = Unsafe.As<MarkupTextLiteralSyntax>(content);
                var text = textLiteral.ToFullString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    continue;
                }

                diagnosticSyntax ??= textLiteral;
                hasText = true;
                literalBuilder.Append(text);
                interpolatedBuilder.Append(EscapeInterpolatedStringText(text));
            }
            else if (content.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
            {
                var inlineExpression = Unsafe.As<MarkupInlineExpressionSyntax>(content);
                var parsedExpression = ParseInlineExpression(inlineExpression.Expression);
                var expressionText = parsedExpression?.ToFullString() ??
                    inlineExpression.Expression.Expression.ToFullString();

                diagnosticSyntax ??= inlineExpression;
                expressionCount++;
                singleExpression ??= parsedExpression ?? CSharpSyntaxFactory.ParseExpression(expressionText);
                interpolatedBuilder.Append('{').Append(expressionText).Append('}');
            }
        }

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
            literalValue = literalBuilder.ToString();
            expression = CSharpSyntaxFactory.LiteralExpression(
                Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
                CSharpSyntaxFactory.Literal(literalValue));
            return true;
        }

        isSynthesizedString = true;
        expression = CSharpSyntaxFactory.ParseExpression("$@\"" + interpolatedBuilder + "\"");
        return true;
    }

    private static string EscapeInterpolatedStringText(string text)
    {
        return text
            .Replace("{", "{{")
            .Replace("}", "}}")
            .Replace("\"", "\"\"");
    }

    private void AddMarkupContentValueDiagnostics(
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

    private BoundMarkupContent CreateBoundMarkupContent(MarkupContentSyntax content)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        AkburaSymbolInfo symbolInfo = AkburaSymbolInfo.None(AkburaCandidateReason.None);
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default;

        if (content.Kind == AkburaSyntaxKind.MarkupElementContentSyntax)
        {
            var elementContent = Unsafe.As<MarkupElementContentSyntax>(content);
            var element = BindingSession.BindSemanticSyntax(elementContent.Element);
            symbolInfo = element.SymbolInfo;
            childrenBuilder.Add(element);
        }
        else if (content.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
        {
            var inlineExpression = Unsafe.As<MarkupInlineExpressionSyntax>(content);
            var expression = ParseInlineExpression(inlineExpression.Expression);
            if (expression != null)
            {
                var binding = BindMarkupAttributeExpression(inlineExpression, expression);
                using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
                AddMarkupExpressionDiagnostics(
                    inlineExpression,
                    inlineExpression.Expression.ToFullString().Trim(),
                    binding,
                    diagnosticsBuilder);
                diagnostics = diagnosticsBuilder.ToImmutable();
                SetSemanticDiagnostics(inlineExpression, diagnostics);
            }
        }

        var boundContent = new BoundMarkupContent(
            content,
            GetBinder(content, BinderUsage.Markup),
            symbolInfo,
            diagnostics.IsDefault ? GetCachedSemanticDiagnostics(content) : diagnostics,
            childrenBuilder.ToImmutable());
        _boundNodeCache[content] = boundContent;
        return boundContent;
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
            var contentModel = CreateMarkupContentModel(namedType);
            var children = CreateMarkupChildren(markupElement, contentModel, out var diagnostics);
            SetSemanticDiagnostics(markupElement, diagnostics);

            var symbol = new MarkupComponentSymbol(
                componentNameText,
                new CSharpSymbolDefinition(namedType),
                contentModel,
                children);
            _symbolInfoCache[markupElement] = AkburaSymbolInfo.Success(symbol);
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

                SetSemanticDiagnostics(markupElement, ImmutableArray<AkburaSemanticDiagnostic>.Empty);
                var usageSymbol = new MarkupComponentSymbol(
                    componentNameText,
                    componentSymbol.CSharpDefinition,
                    componentSymbol.ContentModel,
                    children: ImmutableArray<MarkupChildContent>.Empty,
                    akburaComponent: componentSymbol);
                _symbolInfoCache[markupElement] = AkburaSymbolInfo.Success(usageSymbol);
                usageSymbol.SetAttributeOperations(CreateMarkupAttributeOperations(markupElement));

                symbol = usageSymbol;
                return true;
            }
        }

        symbol = null!;
        return false;
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

    private string GetAkburaComponentMetadataName(AkburaSyntaxTree syntaxTree)
    {
        var componentName = syntaxTree.ComponentName;
        if (componentName.Length == 0)
        {
            return string.Empty;
        }

        var namespaceText = GetAkburaNamespaceText(syntaxTree.GetRoot(), syntaxTree);
        return namespaceText.Length == 0
            ? componentName
            : namespaceText + "." + componentName;
    }

    private string GetAkburaNamespaceText(AkburaDocumentSyntax root, AkburaSyntaxTree? syntaxTree = null)
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
        var parameter = FindComponentParameter(componentSymbol, propertyName);
        var command = FindComponentCommand(componentSymbol, propertyName);
        RoslynPropertySymbol? clrProperty = null;
        RoslynFieldSymbol? avaloniaProperty = null;
        RoslynEventSymbol? clrEvent = null;
        RoslynFieldSymbol? avaloniaRoutedEvent = null;

        if (componentType != null)
        {
            clrProperty = FindPublicClrProperty(componentType, propertyName);
            avaloniaProperty = FindAvaloniaPropertyField(componentType, propertyName);
            clrEvent = FindPublicClrEvent(componentType, propertyName);
            avaloniaRoutedEvent = FindAvaloniaRoutedEventField(componentType, propertyName);
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
                    HasInaccessibleMarkupMember(componentType, propertyName)
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
            propertyName,
            GetMarkupPropertyType(parameter, command, clrProperty, avaloniaProperty),
            avaloniaProperty == null ? default : new CSharpSymbolDefinition(avaloniaProperty),
            clrProperty == null ? default : new CSharpSymbolDefinition(clrProperty),
            parameter,
            command,
            containingSymbol: componentSymbol));
    }

    private static string GetMarkupPropertyName(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax => Unsafe.As<MarkupPlainAttributeSyntax>(markupAttribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax => Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    private static MarkupElementSyntax? GetContainingMarkupElement(MarkupAttributeSyntax markupAttribute)
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
        var componentName = componentSymbol.CSharpDefinition.IsDefault
            ? componentSymbol.Name
            : componentSymbol.CSharpDefinition.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyNotFound,
            [propertyName, componentName]);
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

    private static RoslynPropertySymbol? FindPublicClrProperty(
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
        RoslynFieldSymbol? avaloniaProperty)
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

    private MarkupContentModel CreateMarkupContentModel(INamedTypeSymbol componentType)
    {
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

    private ImmutableArray<MarkupChildContent> CreateMarkupChildren(
        MarkupElementSyntax markupElement,
        MarkupContentModel contentModel,
        out ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        using var childrenBuilder = ImmutableArrayBuilder<MarkupChildContent>.Rent();
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        var hasValueText = false;
        var inlineExpressionCount = 0;

        foreach (var childSyntax in markupElement.Body)
        {
            if (childSyntax.Kind == AkburaSyntaxKind.MarkupTextLiteralSyntax &&
                !string.IsNullOrWhiteSpace(childSyntax.ToFullString()))
            {
                hasValueText = true;
            }
            else if (childSyntax.Kind == AkburaSyntaxKind.MarkupInlineExpressionSyntax)
            {
                inlineExpressionCount++;
            }
        }

        var validateInlineExpressionContent = !hasValueText && inlineExpressionCount == 1;

        foreach (var childSyntax in markupElement.Body)
        {
            switch (childSyntax)
            {
                case MarkupElementContentSyntax elementContent:
                    AddElementChild(elementContent, contentModel, childrenBuilder, diagnosticsBuilder);
                    break;

                case MarkupTextLiteralSyntax textLiteral:
                    AddTextChild(textLiteral, contentModel, childrenBuilder, diagnosticsBuilder);
                    break;

                case MarkupInlineExpressionSyntax inlineExpression:
                    AddExpressionChild(
                        inlineExpression,
                        contentModel,
                        validateInlineExpressionContent,
                        childrenBuilder,
                        diagnosticsBuilder);
                    break;
            }
        }

        if (!hasValueText &&
            inlineExpressionCount > 1 &&
            !HasElementContent(markupElement) &&
            TryCreateMarkupContentValueExpression(
                markupElement,
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

    private void AddElementChild(
        MarkupElementContentSyntax elementContent,
        MarkupContentModel contentModel,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var symbolInfo = GetSymbolInfo(elementContent.Element);
        var componentSymbol = symbolInfo.Symbol as IMarkupComponentSymbol;
        var childType = componentSymbol?.CSharpDefinition ?? default;

        childrenBuilder.Add(new MarkupChildContent(
            elementContent,
            MarkupChildKind.Element,
            childType,
            componentSymbol));

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
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var text = textLiteral.ToFullString();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        var stringType = Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String);
        var textType = new CSharpSymbolDefinition(stringType);
        childrenBuilder.Add(new MarkupChildContent(
            textLiteral,
            MarkupChildKind.Text,
            textType,
            text: text));

        if (!contentModel.AllowsText)
        {
            diagnosticsBuilder.Add(CreateInvalidMarkupChildDiagnostic(
                textLiteral,
                textType,
                contentModel));
        }
    }

    private void AddExpressionChild(
        MarkupInlineExpressionSyntax inlineExpression,
        MarkupContentModel contentModel,
        bool validateContentType,
        ImmutableArrayBuilder<MarkupChildContent> childrenBuilder,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
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
            expressionType));

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

    private CSharpBindingResult BindCSharpType(
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

        return BindingSession
            .GetCSharpProbeBinder(SyntaxTree.GetRoot(), BinderUsage.Type)
            .BindFieldType(compilationUnit);
    }

    private CSharpBindingResult BindCSharpExpression(
        CSharp.ExpressionSyntax expressionSyntax,
        StateDeclarationSyntax? scopeStateDeclaration = null,
        bool isBindingPath = true,
        ITypeSymbol? targetType = null)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        if (scopeStateDeclaration != null)
        {
            foreach (var field in CreateStateProbeFieldsBefore(scopeStateDeclaration))
            {
                membersBuilder.Add(field);
            }
        }

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);

        var binder = BindingSession.GetCSharpProbeBinder(
            (AkburaSyntax?)scopeStateDeclaration ?? SyntaxTree.GetRoot(),
            BinderUsage.Expression);
        var binding = binder.BindReturnExpression(compilationUnit, isBindingPath);
        return ApplyExpectedTypeConversion(binding, binder, targetType);
    }

    private CSharpBindingResult BindAkcssExpression(
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

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetCSharpUsingDirectives()
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

    private ImmutableArray<CSharp.UsingDirectiveSyntax> GetAkcssCSharpUsingDirectives(IAkcssSymbol containingSymbol)
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

    private ImmutableArray<CSharp.ExternAliasDirectiveSyntax> GetCSharpExternAliases()
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
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SealedKeyword)));
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

    private void ValidateSyntaxTreeOwnership(AkburaSyntax syntax)
    {
        if (!ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()))
        {
            throw new ArgumentException("Syntax node is not part of this semantic model syntax tree.", nameof(syntax));
        }
    }

    private void ValidateBoundSyntaxOwnership(AkburaSyntax syntax)
    {
        if (ReferenceEquals(syntax.Root, SyntaxTree.GetRoot()) ||
            Compilation.DeclarationTable.TryGetDeclaration(syntax.Root, out _))
        {
            return;
        }

        throw new ArgumentException("Syntax node is not part of this semantic model syntax tree or compilation declarations.", nameof(syntax));
    }

    internal void SetSemanticDiagnostics(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        _semanticDiagnosticsCache[syntax] = diagnostics.IsDefault
            ? ImmutableArray<AkburaSemanticDiagnostic>.Empty
            : diagnostics;
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
        return semanticDiagnostics;
    }

    private void SetSemanticDiagnosticsIfAbsent(
        AkburaSyntax syntax,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
    {
        if (!_semanticDiagnosticsCache.ContainsKey(syntax))
        {
            SetSemanticDiagnostics(syntax, diagnostics);
        }
    }

    private static void AddCSharpBindingDiagnostics(
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
