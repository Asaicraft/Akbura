using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IMarkupComponentSymbol : ISymbol
{
    INamedTypeSymbol? ComponentType { get; }

    MarkupContentModel ContentModel { get; }

    ImmutableArray<MarkupChildContent> Children { get; }

    ImmutableArray<IParamSymbol> Parameters { get; }

    ImmutableArray<ICommandSymbol> Commands { get; }
}
