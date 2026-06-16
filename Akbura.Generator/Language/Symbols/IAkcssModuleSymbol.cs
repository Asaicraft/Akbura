using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal interface IAkcssModuleSymbol : ISymbol
{
    bool IsInlined { get; }

    new IAkburaComponentSymbol? ContainingSymbol { get; }

    ImmutableArray<IAkcssSymbol> AkcssSymbols { get; }

    string? Path { get; }

    AkburaSyntax DeclaringSyntax { get; }
}
