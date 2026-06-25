using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

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
