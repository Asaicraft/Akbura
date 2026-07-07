using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class AkcssStyleBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public AkcssStyleBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InAkcss |
                (declaration.Kind == DeclarationKind.AkcssUtility
                    ? AkburaBinderFlags.InAkcssUtility
                    : AkburaBinderFlags.InAkcssStyle))
    {
    }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration?.Kind != DeclarationKind.AkcssUtility)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        return GetDeclaredSymbols();
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkcssAssignmentSyntax =>
                BindAkcssPropertySetter(Unsafe.As<AkcssAssignmentSyntax>(syntax)),
            AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                BindAkcssIf(Unsafe.As<AkcssIfDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                BindAkcssApply(Unsafe.As<AkcssApplyDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                BindAkcssIntercept(Unsafe.As<AkcssInterceptDirectiveSyntax>(syntax)),
            _ => base.BindOperationSyntax(syntax),
        };
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkcssStyleRuleSyntax or
                AkburaSyntaxKind.AkcssUtilityDeclarationSyntax =>
                SemanticModel.CreateBoundAkcssSyntax(syntax),
            AkburaSyntaxKind.AkcssAssignmentSyntax or
                AkburaSyntaxKind.AkcssIfDirectiveSyntax or
                AkburaSyntaxKind.AkcssApplyDirectiveSyntax or
                AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                BindOperationSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }

    private BoundNode BindAkcssPropertySetter(AkcssAssignmentSyntax assignment)
    {
        return BindAkcssOperation(
            assignment,
            static (semanticModel, syntax, containingSymbol) =>
                semanticModel.CreateBoundAkcssPropertySetterCore(
                    Unsafe.As<AkcssAssignmentSyntax>(syntax),
                    containingSymbol));
    }

    private BoundNode BindAkcssIf(AkcssIfDirectiveSyntax ifDirective)
    {
        return BindAkcssOperation(
            ifDirective,
            static (semanticModel, syntax, containingSymbol) =>
                semanticModel.CreateBoundAkcssIfCore(
                    Unsafe.As<AkcssIfDirectiveSyntax>(syntax),
                    containingSymbol));
    }

    private BoundNode BindAkcssApply(AkcssApplyDirectiveSyntax applyDirective)
    {
        return BindAkcssOperation(
            applyDirective,
            static (semanticModel, syntax, containingSymbol) =>
                semanticModel.CreateBoundAkcssApplyCore(
                    Unsafe.As<AkcssApplyDirectiveSyntax>(syntax),
                    containingSymbol));
    }

    private BoundNode BindAkcssIntercept(AkcssInterceptDirectiveSyntax interceptDirective)
    {
        if (!TryGetContainingAkcssSymbol(interceptDirective, out var containingSymbol))
        {
            return CreateMissingContainingAkcssSymbolBoundNode(interceptDirective);
        }

        if (SemanticModel.TryGetCachedBoundNode(interceptDirective, out var cachedBoundNode))
        {
            return cachedBoundNode;
        }

        return SemanticModel.CreateBoundAkcssInterceptCore(interceptDirective, containingSymbol);
    }

    private BoundNode BindAkcssOperation(
        AkcssBodyMemberSyntax member,
        Func<AkburaSemanticModel, AkburaSyntax, IAkcssSymbol, BoundNode> bindCore)
    {
        if (!TryGetContainingAkcssSymbol(member, out var containingSymbol))
        {
            return CreateMissingContainingAkcssSymbolBoundNode(member);
        }

        if (SemanticModel.TryGetCachedBoundNode(member, out var cachedBoundNode))
        {
            return cachedBoundNode;
        }

        if (SemanticModel.TrySuppressAkcssOperationDueToIntercept(member, containingSymbol))
        {
            return new BoundDeclaration(
                member,
                this,
                AkburaSymbolInfo.None(AkburaCandidateReason.None));
        }

        return bindCore(SemanticModel, member, containingSymbol);
    }

    private bool TryGetContainingAkcssSymbol(
        AkburaSyntax syntax,
        out IAkcssSymbol containingSymbol)
    {
        containingSymbol = SemanticModel.GetContainingAkcssSymbol(syntax)!;
        return containingSymbol != null;
    }

    private BoundDeclaration CreateMissingContainingAkcssSymbolBoundNode(AkburaSyntax syntax)
    {
        SemanticModel.SetSemanticDiagnostics(
            syntax,
            ImmutableArray<AkburaSemanticDiagnostic>.Empty);
        return new BoundDeclaration(
            syntax,
            this,
            AkburaSymbolInfo.None(AkburaCandidateReason.NotFound));
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
        if (Declaration?.Kind != DeclarationKind.AkcssUtility)
        {
            return;
        }

        var symbol = FindDeclaredSymbol(GetDeclaredSymbolsForScope(Declaration.Syntax), name);
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

        symbols = SemanticModel.DeclarationSymbols.GetTailwindUtilityParameters(Declaration!);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
