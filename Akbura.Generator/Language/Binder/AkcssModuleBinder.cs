using Akbura.Language.Declarations;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class AkcssModuleBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public AkcssModuleBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
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
                SemanticModel.CreateBoundAkcssSyntax(syntax),
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
        if (Declaration == null)
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

        symbols = CreateSymbolsForDeclarations(
            Declaration!.Children,
            AkburaDeclarationKind.AkcssStyle,
            AkburaDeclarationKind.AkcssUtility);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
