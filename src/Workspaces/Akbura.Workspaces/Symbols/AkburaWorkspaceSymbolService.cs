using Akbura.Pools;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Symbols;

internal sealed class AkburaWorkspaceSymbolService :
    IAkburaWorkspaceSymbolService
{
    private const int MaximumResults = 512;

    private readonly IAkburaDocumentSymbolService _documents;

    public AkburaWorkspaceSymbolService(
        IAkburaDocumentSymbolService documents)
    {
        _documents = documents ??
            throw new ArgumentNullException(nameof(documents));
    }

    public ImmutableArray<AkburaWorkspaceSymbol> Search(
        AkburaSolutionSnapshot solution,
        string query,
        CancellationToken cancellationToken = default)
    {
        if (solution == null)
        {
            throw new ArgumentNullException(nameof(solution));
        }
        query ??= string.Empty;

        using var matches =
            ImmutableArrayBuilder<RankedSymbol>.Rent();
        var sourceOrder = 0;
        foreach (var project in solution.Projects.Values)
        {
            foreach (var document in project.Documents.Values)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var context = new AkburaDocumentContext(
                    solution,
                    project,
                    document);
                foreach (var symbol in _documents.GetSymbols(
                             context,
                             cancellationToken))
                {
                    AddSymbol(
                        symbol,
                        document.Uri,
                        containerName: null,
                        query,
                        ref sourceOrder,
                        matches,
                        cancellationToken);
                }
            }
        }

        using var result =
            ImmutableArrayBuilder<AkburaWorkspaceSymbol>.Rent(
                Math.Min(matches.Count, MaximumResults));
        foreach (var match in matches.AsEnumerable()
                     .OrderBy(static item => item.Rank)
                     .ThenBy(static item => item.Symbol.Name,
                         StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static item => item.SourceOrder)
                     .Take(MaximumResults))
        {
            result.Add(match.Symbol);
        }

        return result.ToImmutable();
    }

    private static void AddSymbol(
        AkburaDocumentSymbol symbol,
        Uri uri,
        string? containerName,
        string query,
        ref int sourceOrder,
        ImmutableArrayBuilder<RankedSymbol> matches,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var rank = GetMatchRank(symbol.Name, query);
        if (rank >= 0)
        {
            matches.Add(new RankedSymbol(
                new AkburaWorkspaceSymbol(
                    symbol.Name,
                    symbol.Detail,
                    containerName,
                    symbol.Kind,
                    uri,
                    symbol.SelectionSpan),
                rank,
                sourceOrder++));
        }

        foreach (var child in symbol.Children)
        {
            AddSymbol(
                child,
                uri,
                symbol.Name,
                query,
                ref sourceOrder,
                matches,
                cancellationToken);
        }
    }

    private static int GetMatchRank(string name, string query)
    {
        if (query.Length == 0)
        {
            return 4;
        }

        if (name.StartsWith(query, StringComparison.Ordinal))
        {
            return 0;
        }

        if (name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
        {
            return 1;
        }

        if (IsCamelCaseMatch(name, query))
        {
            return 2;
        }

        return IsSubsequence(name, query) ? 3 : -1;
    }

    private static bool IsCamelCaseMatch(string name, string query)
    {
        var queryIndex = 0;
        for (var index = 0;
             index < name.Length && queryIndex < query.Length;
             index++)
        {
            if ((index == 0 || char.IsUpper(name[index])) &&
                char.ToUpperInvariant(name[index]) ==
                char.ToUpperInvariant(query[queryIndex]))
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }

    private static bool IsSubsequence(string name, string query)
    {
        var queryIndex = 0;
        foreach (var character in name)
        {
            if (queryIndex < query.Length &&
                char.ToUpperInvariant(character) ==
                char.ToUpperInvariant(query[queryIndex]))
            {
                queryIndex++;
            }
        }

        return queryIndex == query.Length;
    }

    private readonly record struct RankedSymbol(
        AkburaWorkspaceSymbol Symbol,
        int Rank,
        int SourceOrder);
}