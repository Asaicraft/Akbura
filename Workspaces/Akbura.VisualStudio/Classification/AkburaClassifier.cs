using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.VisualStudio.Classification;

internal sealed class AkburaClassifier : IClassifier
{
    private readonly AkburaTextBufferContext
        _bufferContext;

    private readonly AkburaClassificationTypeMap
        _typeMap;

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
            if (!_bufferContext.TryGetPublishedState(
                    requestedSpan.Snapshot,
                    out var state))
            {
                return Array.Empty<ClassificationSpan>();
            }

            var parsedRequestSpan =
                TranslateRequestedSpan(
                    requestedSpan,
                    state.Snapshot);

            if (parsedRequestSpan.Length == 0)
            {
                return Array.Empty<ClassificationSpan>();
            }

            var classifications =
                state.Classifications;

            if (classifications.IsDefaultOrEmpty)
            {
                return Array.Empty<ClassificationSpan>();
            }

            var firstIndex =
                FindFirstCandidate(
                    classifications,
                    parsedRequestSpan.Start.Position);

            var result =
                new List<ClassificationSpan>();

            for (var index = firstIndex;
                 index < classifications.Length;
                 index++)
            {
                var classification =
                    classifications[index];

                if (classification.Span.Start >=
                    parsedRequestSpan.End.Position)
                {
                    break;
                }

                if (classification.Span.End <=
                    parsedRequestSpan.Start.Position)
                {
                    continue;
                }

                if (classification.Span.Start < 0 ||
                    classification.Span.End >
                        state.Snapshot.Length ||
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

                var intersection =
                    currentClassificationSpan.Intersection(
                        requestedSpan);

                if (intersection == null ||
                    intersection.Value.Length == 0)
                {
                    continue;
                }

                result.Add(
                    new ClassificationSpan(
                        intersection.Value,
                        _typeMap.Get(
                            classification.Kind)));
            }

            return result;
        }
        catch (ArgumentException exception)
        {
            /*
             * Snapshot translation can fail only when the editor version
             * chain is no longer compatible. It must not break editing.
             */
            Debug.WriteLine(
                $"Akbura span translation failed: " +
                $"{exception}");

            return Array.Empty<ClassificationSpan>();
        }
        catch (Exception exception)
        {
            /*
             * A classifier failure must never break an editor command.
             */
            Debug.WriteLine(
                $"Akbura classification failed: " +
                $"{exception}");

            return Array.Empty<ClassificationSpan>();
        }
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