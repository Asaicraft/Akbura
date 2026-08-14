using Akbura.Language.Symbols;

namespace Akbura.Language;

internal readonly struct AkcssApplyLookupCandidate
{
    public AkcssApplyLookupCandidate(
        string displayText,
        string insertText,
        string sourceModule,
        int priority,
        IAkcssSymbol symbol)
    {
        DisplayText = displayText;
        InsertText = insertText;
        SourceModule = sourceModule;
        Priority = priority;
        Symbol = symbol;
    }

    public string DisplayText { get; }

    public string InsertText { get; }

    public string SourceModule { get; }

    public int Priority { get; }

    public IAkcssSymbol Symbol { get; }

    public bool IsUtility => Symbol is ITailwindUtilitySymbol;
}
