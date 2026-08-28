using System.Collections.Immutable;

namespace Akbura.Workspaces.Symbols;

public interface IAkburaWorkspaceSymbolService
{
    ImmutableArray<AkburaWorkspaceSymbol> Search(
        AkburaSolutionSnapshot solution,
        string query,
        CancellationToken cancellationToken = default);
}