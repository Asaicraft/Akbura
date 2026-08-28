using Microsoft.CodeAnalysis;

namespace Akbura.Language;

internal readonly struct AkcssExpectedValueLookupCandidate
{
    public AkcssExpectedValueLookupCandidate(
        string displayText,
        string insertText,
        string typeDisplay,
        ISymbol symbol)
    {
        DisplayText = displayText;
        InsertText = insertText;
        TypeDisplay = typeDisplay;
        Symbol = symbol;
    }

    public string DisplayText { get; }

    public string InsertText { get; }

    public string TypeDisplay { get; }

    public ISymbol Symbol { get; }
}
