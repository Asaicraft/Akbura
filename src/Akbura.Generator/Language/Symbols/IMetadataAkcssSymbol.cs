using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IMetadataAkcssSymbol : IAkcssSymbol
{
    IMetadataAkcssModuleSymbol MetadataModule { get; }

    INamedTypeSymbol MetadataCarrierType { get; }

    int RuntimeStyleIndex { get; }

    bool HasErrors { get; }

    ImmutableArray<string> ObservedProperties { get; }

    ImmutableArray<AttributeData> OperationAttributes { get; }

    void SetOperations(ImmutableArray<Akbura.Language.Operations.IAkcssOperation> operations);
}
