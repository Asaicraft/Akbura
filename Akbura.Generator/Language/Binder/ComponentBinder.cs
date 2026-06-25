using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed class ComponentBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public ComponentBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InComponent)
    {
    }

    public string ComponentName => Declaration?.Name ?? string.Empty;

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

        var symbols = GetDeclaredSymbolsForScope(Declaration.Syntax);
        var symbol = FindDeclaredSymbol(symbols, name);
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
            AkburaDeclarationKind.State,
            AkburaDeclarationKind.Parameter,
            AkburaDeclarationKind.InjectedService,
            AkburaDeclarationKind.Command,
            AkburaDeclarationKind.UseEffect,
            AkburaDeclarationKind.UserHook);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
