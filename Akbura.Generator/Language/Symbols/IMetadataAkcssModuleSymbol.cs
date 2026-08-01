using Microsoft.CodeAnalysis;

namespace Akbura.Language.Symbols;

internal interface IMetadataAkcssModuleSymbol : IAkcssModuleSymbol
{
    INamedTypeSymbol RuntimeModuleType { get; }

    string SourcePath { get; }

    int FormatVersion { get; }
}
