using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal static class BinderBoundNodeExtensions
{
    public static BoundNode WrapWithDeclaredSymbolsIfAny(
        this Binder binder,
        AkburaSyntax scopeDesignator,
        BoundNode body)
    {
        var declaredSymbols = binder.GetDeclaredSymbolsForScope(scopeDesignator);
        return declaredSymbols.IsEmpty
            ? body
            : new BoundBlock(
                scopeDesignator,
                binder,
                declaredSymbols,
                ImmutableArray.Create(body));
    }
}
