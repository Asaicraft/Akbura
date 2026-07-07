using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class ComponentBinder : Binder
{
    private ImmutableArray<ISymbol> _lazyDeclaredSymbols;

    public ComponentBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        Declaration declaration,
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

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.AkburaDocumentSyntax or
                AkburaSyntaxKind.StateDeclarationSyntax or
                AkburaSyntaxKind.ParamDeclarationSyntax or
                AkburaSyntaxKind.InjectDeclarationSyntax or
                AkburaSyntaxKind.CommandDeclarationSyntax or
                AkburaSyntaxKind.UseEffectDeclarationSyntax =>
                SemanticModel.GetMemberSemanticModel(syntax).BindSemanticSyntax(syntax),
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

        symbols = SemanticModel.DeclarationSymbols.GetDeclaredSymbols(
            Declaration!,
            DeclarationKind.State,
            DeclarationKind.Parameter,
            DeclarationKind.InjectedService,
            DeclarationKind.Command,
            DeclarationKind.UseEffect,
            DeclarationKind.UserHook);

        ImmutableInterlocked.InterlockedInitialize(ref _lazyDeclaredSymbols, symbols);
        return _lazyDeclaredSymbols;
    }
}
