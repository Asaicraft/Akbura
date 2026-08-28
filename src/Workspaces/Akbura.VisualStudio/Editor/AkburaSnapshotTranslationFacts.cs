using Microsoft.VisualStudio.Text;

namespace Akbura.VisualStudio.Editor;

internal static class AkburaSnapshotTranslationFacts
{
    public static SnapshotPoint TranslatePoint(
        SnapshotPoint point,
        ITextSnapshot target)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (IsSameSnapshotVersion(point.Snapshot, target))
        {
            return new SnapshotPoint(target, point.Position);
        }

        return new SnapshotSpan(point, 0)
            .TranslateTo(target, SpanTrackingMode.EdgeInclusive)
            .Start;
    }

    public static SnapshotSpan TranslateSourceSpan(
        SnapshotSpan span,
        ITextSnapshot target)
    {
        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        if (IsSameSnapshotVersion(span.Snapshot, target))
        {
            return new SnapshotSpan(target, span.Span);
        }

        return span.TranslateTo(target, SpanTrackingMode.EdgeExclusive);
    }

    private static bool IsSameSnapshotVersion(
        ITextSnapshot left,
        ITextSnapshot right)
    {
        return ReferenceEquals(left.TextBuffer, right.TextBuffer) &&
            left.Version.VersionNumber == right.Version.VersionNumber;
    }
}
