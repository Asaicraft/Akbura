using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssModuleBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public AkcssModuleBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            DeclarationFacts.GetSyntax(declaration),
            flags | AkburaBinderFlags.InAkcss)
    {
    }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration == null)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        return GetDeclaredSymbols();
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.InlineAkcssBlockSyntax or
                AkburaSyntaxKind.AkcssDocumentSyntax =>
                BindAkcssModuleSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    private BoundNode BindAkcssModuleSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.InlineAkcssBlockSyntax =>
                BindInlineModule(Unsafe.As<InlineAkcssBlockSyntax>(syntax)),
            AkburaSyntaxKind.AkcssDocumentSyntax =>
                BindExternalModule(Unsafe.As<AkcssDocumentSyntax>(syntax)),
            _ => new BoundDeclaration(
                syntax,
                this,
                AkburaSymbolInfo.None(AkburaCandidateReason.UnsupportedSyntax)),
        };
    }

    private BoundAkcssModule BindInlineModule(InlineAkcssBlockSyntax inlineAkcssBlock)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(inlineAkcssBlock);
        var boundModule = BindModule(
            inlineAkcssBlock,
            inlineAkcssBlock.Members,
            symbolInfo);
        SemanticModel.SetCachedBoundNode(inlineAkcssBlock, boundModule);
        return boundModule;
    }

    private BoundAkcssModule BindExternalModule(AkcssDocumentSyntax document)
    {
        var symbolInfo = SemanticModel.GetDeclarationSymbolInfo(document);
        var boundModule = BindModule(
            document,
            document.Members,
            symbolInfo);
        SemanticModel.SetCachedBoundNode(document, boundModule);
        return boundModule;
    }

    private BoundAkcssModule BindModule(
        AkburaSyntax syntax,
        SyntaxList<AkcssTopLevelMemberSyntax> members,
        AkburaSymbolInfo symbolInfo)
    {
        using var childrenBuilder = ImmutableArrayBuilder<BoundNode>.Rent();
        foreach (var member in members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                    childrenBuilder.Add(SemanticModel.BindingSession.BindSemanticSyntax(member));
                    break;

                case AkburaSyntaxKind.AkcssUtilitiesSectionSyntax:
                    foreach (var utility in Unsafe.As<AkcssUtilitiesSectionSyntax>(member).Utilities)
                    {
                        childrenBuilder.Add(SemanticModel.BindingSession.BindSemanticSyntax(utility));
                    }

                    break;
            }
        }

        return new BoundAkcssModule(
            syntax,
            this,
            symbolInfo,
            SemanticModel.GetCachedSemanticDiagnostics(syntax),
            childrenBuilder.ToImmutable());
    }

    protected override void LookupSymbolsInSingleBinder(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration == null)
        {
            return;
        }

        var symbol = FindDeclaredSymbol(GetDeclaredSymbolsForScope(DeclarationFacts.GetSyntax(Declaration)), name);
        if (symbol != null)
        {
            result.SetSymbol(symbol);
        }
    }

    private ImmutableArray<ISymbol> GetDeclaredSymbols()
    {
        var symbols = _lazyDeclaredSymbols;
        if (!symbols.IsDefault)
        {
            return symbols;
        }

        symbols = SemanticModel.DeclarationSymbols.GetDeclaredSymbols(
            Declaration!,
            DeclarationKind.AkcssStyle,
            DeclarationKind.AkcssUtility);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
