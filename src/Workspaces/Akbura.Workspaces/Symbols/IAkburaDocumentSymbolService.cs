using System.Collections.Immutable;

namespace Akbura.Workspaces.Symbols;

public interface IAkburaDocumentSymbolService
{
    ImmutableArray<AkburaDocumentSymbol> GetSymbols(
        AkburaSyntacticDocument document,
        CancellationToken cancellationToken = default);

    ImmutableArray<AkburaDocumentSymbol> GetSymbols(
        AkburaDocumentContext context,
        CancellationToken cancellationToken = default);
}
