using Akbura.Language.Symbols;
using Akbura.Pools;
using System.Collections.Generic;
using System.Collections.Immutable;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Binder;

internal sealed class LookupResult
{
    private static readonly ObjectPool<LookupResult> s_pool = new(static () => new LookupResult(), size: 32);

    private List<AkburaSymbol>? _candidateSymbols;

    private LookupResult()
    {
        CandidateReason = CandidateReason.NotFound;
    }

    public AkburaSymbol? Symbol { get; private set; }

    public CandidateReason CandidateReason { get; private set; }

    public bool IsClear =>
        Symbol == null &&
        (_candidateSymbols == null || _candidateSymbols.Count == 0) &&
        CandidateReason == CandidateReason.NotFound;

    public bool IsGood => Symbol != null && CandidateReason == CandidateReason.None;

    public bool IsComplete => !IsClear;

    public static LookupResult GetInstance()
    {
        var result = s_pool.Allocate();
        result.Clear();
        return result;
    }

    public void SetFrom(AkburaSymbolInfo symbolInfo)
    {
        if (symbolInfo.Symbol != null)
        {
            SetSymbol(symbolInfo.Symbol);
            return;
        }

        if (!symbolInfo.CandidateSymbols.IsDefaultOrEmpty)
        {
            AddCandidates(symbolInfo.CandidateSymbols, symbolInfo.CandidateReason);
            return;
        }

        if (symbolInfo.CandidateReason != CandidateReason.NotFound)
        {
            CandidateReason = symbolInfo.CandidateReason;
        }
    }

    public void SetSymbol(AkburaSymbol symbol)
    {
        Symbol = symbol;
        CandidateReason = CandidateReason.None;
        _candidateSymbols?.Clear();
    }

    public void AddCandidate(
        AkburaSymbol symbol,
        CandidateReason candidateReason = CandidateReason.Ambiguous)
    {
        (_candidateSymbols ??= new List<AkburaSymbol>()).Add(symbol);
        CandidateReason = candidateReason;
    }

    public void AddCandidates(
        ImmutableArray<AkburaSymbol> symbols,
        CandidateReason candidateReason)
    {
        if (symbols.IsDefaultOrEmpty)
        {
            return;
        }

        _candidateSymbols ??= new List<AkburaSymbol>(symbols.Length);
        foreach (var symbol in symbols)
        {
            _candidateSymbols.Add(symbol);
        }

        CandidateReason = candidateReason;
    }

    public AkburaSymbolInfo ToSymbolInfo(CandidateReason emptyReason = CandidateReason.NotFound)
    {
        if (Symbol != null)
        {
            return AkburaSymbolInfo.Success(Symbol);
        }

        if (_candidateSymbols != null && _candidateSymbols.Count > 0)
        {
            return AkburaSymbolInfo.Candidates(_candidateSymbols.ToImmutableArray(), CandidateReason);
        }

        return AkburaSymbolInfo.None(IsClear ? emptyReason : CandidateReason);
    }

    public AkburaSymbolInfo ToSymbolInfoAndFree(CandidateReason emptyReason = CandidateReason.NotFound)
    {
        var symbolInfo = ToSymbolInfo(emptyReason);
        Free();
        return symbolInfo;
    }

    public void Free()
    {
        Clear();
        s_pool.Free(this);
    }

    private void Clear()
    {
        Symbol = null;
        CandidateReason = CandidateReason.NotFound;
        _candidateSymbols?.Clear();
    }
}
