using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.AutomaticPairing;

internal readonly struct AkburaRawStringInfo
{
    public AkburaRawStringInfo(
        int dollarCount,
        int quoteCount,
        TextSpan openingSpan,
        TextSpan closingSpan,
        int caretPosition)
    {
        DollarCount = dollarCount;
        QuoteCount = quoteCount;
        OpeningSpan = openingSpan;
        ClosingSpan = closingSpan;
        CaretPosition = caretPosition;
    }

    public int DollarCount { get; }

    public int QuoteCount { get; }

    public TextSpan OpeningSpan { get; }

    public TextSpan ClosingSpan { get; }

    public int CaretPosition { get; }

    public bool IsInterpolated => DollarCount != 0;

    public bool HasClosingDelimiter => ClosingSpan.Length == QuoteCount;

    public bool IsAtEndOfOpeningDelimiter =>
        CaretPosition == OpeningSpan.End;
}
