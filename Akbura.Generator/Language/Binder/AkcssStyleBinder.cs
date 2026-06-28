using Akbura.Language.Declarations;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class AkcssStyleBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public AkcssStyleBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InAkcss |
                (declaration.Kind == AkburaDeclarationKind.AkcssUtility
                    ? AkburaBinderFlags.InAkcssUtility
                    : AkburaBinderFlags.InAkcssStyle))
    {
    }

    public override ImmutableArray<ISymbol> GetDeclaredSymbolsForScope(AkburaSyntax scopeDesignator)
    {
        if (!OwnsScope(scopeDesignator) ||
            Declaration?.Kind != AkburaDeclarationKind.AkcssUtility)
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
                SemanticModel.CreateBoundAkcssPropertySetter(Unsafe.As<AkcssAssignmentSyntax>(syntax)),
            AkburaSyntaxKind.AkcssIfDirectiveSyntax =>
                SemanticModel.CreateBoundAkcssIf(Unsafe.As<AkcssIfDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssApplyDirectiveSyntax =>
                SemanticModel.CreateBoundAkcssApply(Unsafe.As<AkcssApplyDirectiveSyntax>(syntax)),
            AkburaSyntaxKind.AkcssInterceptDirectiveSyntax =>
                SemanticModel.CreateBoundAkcssIntercept(Unsafe.As<AkcssInterceptDirectiveSyntax>(syntax)),
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

    protected override void LookupSymbolsInSingleBinder(
        LookupResult result,
        string name,
        int arity,
        BinderLookupOptions options,
        Binder originalBinder,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration?.Kind != AkburaDeclarationKind.AkcssUtility)
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
