using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.AutomaticPairing;

internal readonly struct AkburaInterpolationInfo
{
    public AkburaInterpolationInfo(
        int dollarCount,
        bool isRaw,
        TextSpan openingSpan,
        TextSpan closingSpan,
        int caretPosition)
    {
        DollarCount = dollarCount;
        IsRaw = isRaw;
        OpeningSpan = openingSpan;
        ClosingSpan = closingSpan;
        CaretPosition = caretPosition;
    }

    public int DollarCount { get; }

    public int RequiredBraceCount => IsRaw ? DollarCount : 1;

    public bool IsRaw { get; }

    public TextSpan OpeningSpan { get; }

    public TextSpan ClosingSpan { get; }

    public int CaretPosition { get; }

    public bool HasClosingDelimiter =>
        ClosingSpan.Length == RequiredBraceCount;

    public bool IsAtEndOfOpeningDelimiter =>
        CaretPosition == OpeningSpan.End;
}
