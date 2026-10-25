using Akbura.Language.Symbols;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

/// <summary>
/// Shares a lazy lookup within one sequential metadata operation initialization pass.
/// The context is passed between initializers and is never retained by the operations.
/// </summary>
internal sealed class MetadataAkcssLookupContext
{
    private readonly ImmutableArray<IAkcssSymbol> _symbols;
    private IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>>? _lookup;

    public MetadataAkcssLookupContext(ImmutableArray<IAkcssSymbol> symbols)
    {
        _symbols = symbols;
    }

    public bool TryGetCandidates(string metadataName, out ImmutableArray<IAkcssSymbol> candidates)
    {
        _lookup ??= CreateSymbolLookup(_symbols);
        return _lookup.TryGetValue(metadataName, out candidates);
    }

    private static IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> CreateSymbolLookup(
        ImmutableArray<IAkcssSymbol> symbols)
    {
        var grouped = new Dictionary<string, List<IAkcssSymbol>>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!grouped.TryGetValue(symbol.MetadataName, out var candidates))
            {
                candidates = [];
                grouped.Add(symbol.MetadataName, candidates);
            }

            candidates.Add(symbol);
        }

        var result = new Dictionary<string, ImmutableArray<IAkcssSymbol>>(
            grouped.Count,
            StringComparer.Ordinal);
        foreach (var pair in grouped)
        {
            result.Add(pair.Key, ImmutableArray.CreateRange(pair.Value));
        }

        return result;
    }
}
