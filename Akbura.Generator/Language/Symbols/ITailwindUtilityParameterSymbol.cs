using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.Symbols;

internal interface ITailwindUtilityParameterSymbol : ISymbol
{
    AkcssUtilityParameterSyntax DeclarationSyntax { get; }

    int Ordinal { get; }

    CSharpSymbolDefinition Type { get; }

    IParameterSymbol? CSharpParameter { get; }
}
