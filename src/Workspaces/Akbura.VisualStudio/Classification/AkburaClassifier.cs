using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using System.Collections.Immutable;

namespace Akbura.VisualStudio.Classification;

internal sealed class AkburaClassifier : IClassifier
{
    private readonly AkburaTextBufferContext _bufferContext;

    private readonly AkburaClassificationTypeMap _typeMap;

    public AkburaClassifier(
        AkburaTextBufferContext bufferContext,
        AkburaClassificationTypeMap typeMap)
    {
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(
                nameof(bufferContext));

        _typeMap = typeMap ??
            throw new ArgumentNullException(
                nameof(typeMap));

        _bufferContext.Changed +=
            OnBufferContextChanged;
    }

    public event EventHandler<ClassificationChangedEventArgs>?
        ClassificationChanged;

    public IList<ClassificationSpan> GetClassificationSpans(
        SnapshotSpan requestedSpan)
    {
        if (requestedSpan.Length == 0)
        {
            return Array.Empty<ClassificationSpan>();
        }

        try
        {
            if (!_bufferContext.TryGetPublishedClassificationState(
                    requestedSpan.Snapshot,
                    out var state))
            {
                return Array.Empty<ClassificationSpan>();
            }

            var result =
                new Dictionary<Span, ClassificationSpan>();
            var occupiedRanges = new List<Span>();

            /*
             * A fast syntactic publication must not erase the last semantic
             * colors from every unchanged token. Translate the previous
             * semantic result to the requested snapshot and retain a token
             * only while its text is identical. The fresh syntactic result
             * then fills new and edited spans until the next semantic pass.
             */
            if (!state.IncludesSemanticClassifications &&
                _bufferContext.TryGetPublishedState(
                    requestedSpan.Snapshot,
                    out var semanticState))
            {
                AddClassificationSpans(
                    semanticState,
                    requestedSpan,
                    requireUnchangedText: true,
                    overwriteExisting: false,
                    result,
                    occupiedRanges);
            }

            AddClassificationSpans(
                state,
                requestedSpan,
                requireUnchangedText: false,
                overwriteExisting:
                    state.IncludesSemanticClassifications,
                result,
                occupiedRanges);

            if (result.Count == 0)
            {
                return Array.Empty<ClassificationSpan>();
            }

            var ordered = result.Values.ToList();
            ordered.Sort(CompareClassificationSpans);
            return ordered;
        }
        catch (ArgumentException exception)
        {
            /*
             * Snapshot translation can fail only when the editor version
             * chain is no longer compatible. It must not break editing.
             */
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Classification,
                $"Akbura span translation failed: " +
                $"{exception}");

            return Array.Empty<ClassificationSpan>();
        }
        catch (Exception exception)
        {
            /*
             * A classifier failure must never break an editor command.
             */
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Classification,
                $"Akbura classification failed: " +
                $"{exception}");

            return Array.Empty<ClassificationSpan>();
        }
    }

    private void AddClassificationSpans(
        AkburaClassifiedBufferState state,
        SnapshotSpan requestedSpan,
        bool requireUnchangedText,
        bool overwriteExisting,
        Dictionary<Span, ClassificationSpan> result,
        List<Span> occupiedRanges)
    {
        var parsedRequestSpan =
            TranslateRequestedSpan(
                requestedSpan,
                state.Snapshot);

        if (parsedRequestSpan.Length == 0 ||
            state.Classifications.IsDefaultOrEmpty)
        {
            return;
        }

        var classifications = state.Classifications;
        var firstIndex = FindFirstCandidate(
            classifications,
            parsedRequestSpan.Start.Position);

        for (var index = firstIndex;
             index < classifications.Length;
             index++)
        {
            var classification = classifications[index];

            if (classification.Span.Start >=
                parsedRequestSpan.End.Position)
            {
                break;
            }

            if (classification.Span.End <=
                    parsedRequestSpan.Start.Position ||
                classification.Span.Start < 0 ||
                classification.Span.End > state.Snapshot.Length ||
                classification.Span.Length == 0)
            {
                continue;
            }

            var parsedClassificationSpan =
                new SnapshotSpan(
                    state.Snapshot,
                    Span.FromBounds(
                        classification.Span.Start,
                        classification.Span.End));

            var currentClassificationSpan =
                TranslateClassificationSpan(
                    parsedClassificationSpan,
                    requestedSpan.Snapshot);

            if (currentClassificationSpan.Length == 0 ||
                (requireUnchangedText &&
                 (!HasStableBoundaries(
                      parsedClassificationSpan,
                      currentClassificationSpan) ||
                  !HasSameText(
                      parsedClassificationSpan,
                      currentClassificationSpan))))
            {
                continue;
            }

            var intersection =
                currentClassificationSpan.Intersection(
                    requestedSpan);

            if (intersection == null ||
                intersection.Value.Length == 0)
            {
                continue;
            }

            var key = currentClassificationSpan.Span;
            if (!overwriteExisting &&
                OverlapsAny(occupiedRanges, key))
            {
                continue;
            }

            var isNewSpan = !result.ContainsKey(key);
            result[key] =
                new ClassificationSpan(
                    intersection.Value,
                    _typeMap.Get(
                        classification.Kind));

            if (isNewSpan)
            {
                AddOccupiedRange(occupiedRanges, key);
            }
        }
    }

    private static bool HasStableBoundaries(
        SnapshotSpan previousSpan,
        SnapshotSpan currentSpan)
    {
        if (IsSameSnapshotVersion(
                previousSpan.Snapshot,
                currentSpan.Snapshot))
        {
            return true;
        }

        var inclusiveSpan = previousSpan.TranslateTo(
            currentSpan.Snapshot,
            SpanTrackingMode.EdgeInclusive);

        return inclusiveSpan.Span == currentSpan.Span;
    }

    private static bool OverlapsAny(
        List<Span> occupiedRanges,
        Span span)
    {
        var low = 0;
        var high = occupiedRanges.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (occupiedRanges[middle].End <= span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < occupiedRanges.Count &&
               occupiedRanges[low].Start < span.End;
    }

    private static void AddOccupiedRange(
        List<Span> occupiedRanges,
        Span span)
    {
        var low = 0;
        var high = occupiedRanges.Count;

        while (low < high)
        {
            var middle = low + ((high - low) / 2);
            if (occupiedRanges[middle].End < span.Start)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var index = low;
        var start = span.Start;
        var end = span.End;

        while (index < occupiedRanges.Count &&
               occupiedRanges[index].Start <= end)
        {
            start = Math.Min(start, occupiedRanges[index].Start);
            end = Math.Max(end, occupiedRanges[index].End);
            occupiedRanges.RemoveAt(index);
        }

        occupiedRanges.Insert(
            index,
            Span.FromBounds(start, end));
    }

    private static bool HasSameText(
        SnapshotSpan previousSpan,
        SnapshotSpan currentSpan)
    {
        if (previousSpan.Length != currentSpan.Length)
        {
            return false;
        }

        for (var offset = 0;
             offset < previousSpan.Length;
             offset++)
        {
            if (previousSpan.Snapshot[
                    previousSpan.Start.Position + offset] !=
                currentSpan.Snapshot[
                    currentSpan.Start.Position + offset])
            {
                return false;
            }
        }

        return true;
    }

    private static int CompareClassificationSpans(
        ClassificationSpan left,
        ClassificationSpan right)
    {
        var start = left.Span.Start.Position.CompareTo(
            right.Span.Start.Position);

        return start != 0
            ? start
            : left.Span.Length.CompareTo(right.Span.Length);
    }

    private static SnapshotSpan TranslateRequestedSpan(
        SnapshotSpan requestedSpan,
        ITextSnapshot parsedSnapshot)
    {
        if (IsSameSnapshotVersion(
                requestedSpan.Snapshot,
                parsedSnapshot))
        {
            return new SnapshotSpan(
                parsedSnapshot,
                requestedSpan.Span);
        }

        /*
         * Translate the editor request back to the parsed snapshot.
         * EdgeInclusive includes tokens touching an edit boundary.
         */
        return requestedSpan.TranslateTo(
            parsedSnapshot,
            SpanTrackingMode.EdgeInclusive);
    }

    private static SnapshotSpan TranslateClassificationSpan(
        SnapshotSpan parsedSpan,
        ITextSnapshot requestedSnapshot)
    {
        if (IsSameSnapshotVersion(
                parsedSpan.Snapshot,
                requestedSnapshot))
        {
            return new SnapshotSpan(
                requestedSnapshot,
                parsedSpan.Span);
        }

        /*
         * Translate a ready classification forward to the editor snapshot.
         * EdgeExclusive prevents inserted text from inheriting an adjacent
         * token classification.
         */
        return parsedSpan.TranslateTo(
            requestedSnapshot,
            SpanTrackingMode.EdgeExclusive);
    }

    private static int FindFirstCandidate(
        ImmutableArray<AkburaClassifiedSpan> classifications,
        int position)
    {
        var low = 0;
        var high = classifications.Length;

        /*
         * Find the first classification whose start is not less than the
         * requested position.
         */
        while (low < high)
        {
            var middle =
                low + ((high - low) / 2);

            if (classifications[middle].Span.Start <
                position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        /*
         * The preceding span may start before the request and still overlap
         * it, so include one previous candidate.
         */
        if (low > 0)
        {
            low--;
        }

        while (low < classifications.Length &&
               classifications[low].Span.End <=
                   position)
        {
            low++;
        }

        return low;
    }

    private static bool IsSameSnapshotVersion(
        ITextSnapshot left,
        ITextSnapshot right)
    {
        return ReferenceEquals(
                   left.TextBuffer,
                   right.TextBuffer) &&
               left.Version.VersionNumber ==
                   right.Version.VersionNumber;
    }

    private void OnBufferContextChanged(
        object sender,
        AkburaBufferChangedEventArgs e)
    {
        ClassificationChanged?.Invoke(
            this,
            new ClassificationChangedEventArgs(
                e.Span));
    }
}
