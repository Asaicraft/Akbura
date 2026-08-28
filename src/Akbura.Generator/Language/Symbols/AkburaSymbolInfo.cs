using System.Collections.Immutable;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Symbols;

internal readonly struct AkburaSymbolInfo
{
    private AkburaSymbolInfo(
        AkburaSymbol? symbol,
        ImmutableArray<AkburaSymbol> candidateSymbols,
        CandidateReason candidateReason)
    {
        Symbol = symbol;
        CandidateSymbols = candidateSymbols.IsDefault
            ? ImmutableArray<AkburaSymbol>.Empty
            : candidateSymbols;
        CandidateReason = candidateReason;
    }

    public AkburaSymbol? Symbol { get; }

    public ImmutableArray<AkburaSymbol> CandidateSymbols { get; }

    public CandidateReason CandidateReason { get; }

    public static AkburaSymbolInfo Success(AkburaSymbol symbol)
    {
        return new AkburaSymbolInfo(symbol, ImmutableArray<AkburaSymbol>.Empty, CandidateReason.None);
    }

    public static AkburaSymbolInfo Candidates(
        ImmutableArray<AkburaSymbol> candidateSymbols,
        CandidateReason candidateReason)
    {
        return new AkburaSymbolInfo(null, candidateSymbols, candidateReason);
    }

    public static AkburaSymbolInfo None(CandidateReason candidateReason)
    {
        return new AkburaSymbolInfo(null, ImmutableArray<AkburaSymbol>.Empty, candidateReason);
    }
}
