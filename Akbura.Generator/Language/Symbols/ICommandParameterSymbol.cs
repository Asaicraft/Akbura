using Microsoft.CodeAnalysis;

namespace Akbura.Language.Symbols;

internal interface ICommandParameterSymbol : ISymbol
{
    int Ordinal { get; }

    CSharpSymbolDefinition Type { get; }

    IParameterSymbol? CSharpParameter { get; }
}
