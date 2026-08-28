using Microsoft.VisualStudio.Text;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

internal enum AkburaDynamicDelimiterKind
{
    RawStringQuotes,
    InterpolatedStringBraces,
}

internal sealed class AkburaDynamicDelimiterSession
{
    private ITrackingSpan _openingSpan;
    private ITrackingSpan _closingSpan;

    private AkburaDynamicDelimiterSession(
        AkburaDynamicDelimiterKind kind,
        ITextSnapshot snapshot,
        Span openingSpan,
        Span closingSpan,
        char openingDelimiterCharacter,
        char closingDelimiterCharacter,
        int requiredDelimiterLength,
        int outerLiteralDelimiterCount)
    {
        Kind = kind;
        OpeningDelimiterCharacter = openingDelimiterCharacter;
        ClosingDelimiterCharacter = closingDelimiterCharacter;
        RequiredDelimiterLength = requiredDelimiterLength;
        OuterLiteralDelimiterCount = outerLiteralDelimiterCount;
        _openingSpan = snapshot.CreateTrackingSpan(
            openingSpan,
            SpanTrackingMode.EdgeExclusive);
        _closingSpan = snapshot.CreateTrackingSpan(
            closingSpan,
            SpanTrackingMode.EdgeExclusive);
    }

    public AkburaDynamicDelimiterKind Kind { get; }

    public char OpeningDelimiterCharacter { get; }

    public char ClosingDelimiterCharacter { get; }

    public int RequiredDelimiterLength { get; private set; }

    public int OuterLiteralDelimiterCount { get; private set; }

    public static AkburaDynamicDelimiterSession Create(
        AkburaDynamicDelimiterKind kind,
        ITextSnapshot snapshot,
        Span openingSpan,
        Span closingSpan,
        char openingDelimiterCharacter,
        char closingDelimiterCharacter,
        int requiredDelimiterLength,
        int outerLiteralDelimiterCount = 0)
    {
        return new AkburaDynamicDelimiterSession(
            kind,
            snapshot,
            openingSpan,
            closingSpan,
            openingDelimiterCharacter,
            closingDelimiterCharacter,
            requiredDelimiterLength,
            outerLiteralDelimiterCount);
    }

    public bool TryGetSpans(
        ITextSnapshot snapshot,
        out SnapshotSpan openingSpan,
        out SnapshotSpan closingSpan)
    {
        openingSpan = _openingSpan.GetSpan(snapshot);
        closingSpan = _closingSpan.GetSpan(snapshot);
        return IsExpectedDelimiter(
                openingSpan,
                OpeningDelimiterCharacter) &&
            IsExpectedDelimiter(
                closingSpan,
                ClosingDelimiterCharacter) &&
            openingSpan.End.Position <= closingSpan.Start.Position;
    }

    public bool ContainsCaret(
        ITextSnapshot snapshot,
        int position)
    {
        return TryGetSpans(
                snapshot,
                out var opening,
                out var closing) &&
            opening.Start.Position <= position &&
            position <= closing.End.Position;
    }

    public bool IsBodyEmpty(ITextSnapshot snapshot)
    {
        return TryGetSpans(
                snapshot,
                out var opening,
                out var closing) &&
            opening.End.Position == closing.Start.Position;
    }

    public void Update(
        ITextSnapshot snapshot,
        Span openingSpan,
        Span closingSpan,
        int requiredDelimiterLength,
        int outerLiteralDelimiterCount)
    {
        RequiredDelimiterLength = requiredDelimiterLength;
        OuterLiteralDelimiterCount = outerLiteralDelimiterCount;
        _openingSpan = snapshot.CreateTrackingSpan(
            openingSpan,
            SpanTrackingMode.EdgeExclusive);
        _closingSpan = snapshot.CreateTrackingSpan(
            closingSpan,
            SpanTrackingMode.EdgeExclusive);
    }

    private static bool IsExpectedDelimiter(
        SnapshotSpan span,
        char delimiterCharacter)
    {
        if (span.Length == 0)
        {
            return false;
        }

        for (var index = 0; index < span.Length; index++)
        {
            if (span.Snapshot[span.Start.Position + index] !=
                delimiterCharacter)
            {
                return false;
            }
        }

        return true;
    }
}
