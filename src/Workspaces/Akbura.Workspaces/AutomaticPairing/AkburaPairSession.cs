using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.AutomaticPairing;

public sealed record AkburaPairSession(
    AkburaPairSessionKind Kind,
    TextSpan OpeningSpan,
    TextSpan ClosingSpan,
    string OpeningText,
    string ClosingText,
    int RequiredDelimiterLength,
    int OuterLiteralDelimiterCount);
