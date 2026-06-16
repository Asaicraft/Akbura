using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IMarkupComponentSymbol : ISymbol
{
    INamedTypeSymbol? ComponentType { get; }

    MarkupContentModel ContentModel { get; }

    ImmutableArray<MarkupChildContent> Children { get; }

    ImmutableArray<IMarkupAttributeOperation> AttributeOperations { get; }

    IAkburaComponentSymbol? AkburaComponent { get; }
}
