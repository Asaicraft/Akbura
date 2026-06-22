using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binding;

internal sealed class AkcssStyleBinder : Binder
{
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
            Declaration?.Kind != AkburaDeclarationKind.AkcssUtility ||
            SemanticModel.GetSymbolInfo(Declaration.Syntax).Symbol is not ITailwindUtilitySymbol utility)
        {
            return base.GetDeclaredSymbolsForScope(scopeDesignator);
        }

        return ImmutableArray<ISymbol>.CastUp(utility.Parameters);
    }

    protected override AkburaSymbolInfo LookupSymbolInSingleBinder(
        string name,
        BinderLookupOptions options,
        AkburaSyntax syntax,
        BindingDiagnosticBag diagnostics)
    {
        if (Declaration?.Kind != AkburaDeclarationKind.AkcssUtility)
        {
            return AkburaSymbolInfo.None(CandidateReason.NotFound);
        }

        var symbol = FindDeclaredSymbol(GetDeclaredSymbolsForScope(Declaration.Syntax), name);
        return symbol == null
            ? AkburaSymbolInfo.None(CandidateReason.NotFound)
            : AkburaSymbolInfo.Success(symbol);
    }
}
