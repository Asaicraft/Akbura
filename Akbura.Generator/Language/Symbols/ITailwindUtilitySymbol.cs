using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface ITailwindUtilitySymbol : IAkcssSymbol
{
    ImmutableArray<ITailwindUtilityParameterSymbol> Parameters { get; }
}
