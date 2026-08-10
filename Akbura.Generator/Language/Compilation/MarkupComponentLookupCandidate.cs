using Akbura.Language.Symbols;

namespace Akbura.Language;

internal readonly struct MarkupComponentLookupCandidate
{
    public MarkupComponentLookupCandidate(
        string displayName,
        IMarkupComponentSymbol symbol)
    {
        DisplayName = displayName;
        Symbol = symbol;
    }

    public string DisplayName { get; }

    public IMarkupComponentSymbol Symbol { get; }
}
