using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binding;

internal sealed class ComponentBinder : Binder
{
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

        return CreateSymbolsForDeclarations(
            Declaration.Children,
            AkburaDeclarationKind.State,
            AkburaDeclarationKind.Parameter,
            AkburaDeclarationKind.InjectedService,
            AkburaDeclarationKind.Command,
            AkburaDeclarationKind.UseEffect,
            AkburaDeclarationKind.UserHook);
    }

    protected override AkburaSymbolInfo LookupSymbolInSingleBinder(
        string name,
        BinderLookupOptions options,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration == null)
        {
            return AkburaSymbolInfo.None(CandidateReason.NotFound);
        }

        var symbols = GetDeclaredSymbolsForScope(Declaration.Syntax);
        var symbol = FindDeclaredSymbol(symbols, name);
        return symbol == null
            ? AkburaSymbolInfo.None(CandidateReason.NotFound)
            : AkburaSymbolInfo.Success(symbol);
    }
}
