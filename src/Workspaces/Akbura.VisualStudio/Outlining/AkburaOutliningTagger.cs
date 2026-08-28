using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using System.Runtime.CompilerServices;

namespace Akbura.VisualStudio.Outlining;

internal sealed class AkburaOutliningTagger :
    ITagger<IOutliningRegionTag>
{
    private static readonly TimeSpan ParseWaitTimeout =
        TimeSpan.FromMilliseconds(100);

    private readonly ITextBuffer _buffer;

    private readonly AkburaParserService _parserService;

    private readonly ConditionalWeakTable<
        ITextSnapshot,
        object> _scheduledRefreshes = new();

    public AkburaOutliningTagger(
        ITextBuffer buffer,
        AkburaParserService parserService)
    {
        _buffer = buffer ??
            throw new ArgumentNullException(
                nameof(buffer));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public event EventHandler<SnapshotSpanEventArgs>?
        TagsChanged;

    public IEnumerable<ITagSpan<IOutliningRegionTag>> GetTags(
        NormalizedSnapshotSpanCollection spans)
    {
        if (spans == null || spans.Count == 0)
        {
            return [];
        }

        var snapshot = spans[0].Snapshot;
        var task = _parserService
            .GetSyntacticDocumentAsync(snapshot);

        if (!TryGetCompletedDocument(
                task,
                out var document))
        {
            ScheduleRefresh(
                snapshot,
                task);
            return [];
        }

        var result =
            new List<ITagSpan<IOutliningRegionTag>>();

        foreach (var region in document.OutliningRegions)
        {
            if (region.Span.Start < 0 ||
                region.Span.End > snapshot.Length ||
                region.Span.Length == 0)
            {
                continue;
            }

            var snapshotSpan = new SnapshotSpan(
                snapshot,
                Span.FromBounds(
                    region.Span.Start,
                    region.Span.End));
            if (!IntersectsAny(
                    snapshotSpan,
                    spans))
            {
                continue;
            }

            result.Add(
                new TagSpan<IOutliningRegionTag>(
                    snapshotSpan,
                    new OutliningRegionTag(
                        isDefaultCollapsed: false,
                        isImplementation: false,
                        collapsedForm: region.CollapsedText,
                        collapsedHintForm:
                            snapshotSpan.GetText())));
        }

        return result;
    }

    private static bool TryGetCompletedDocument(
        Task<AkburaSyntacticDocument> task,
        out AkburaSyntacticDocument document)
    {
#pragma warning disable VSTHRD002 // Deliberately bounded to 100 ms to avoid editor flicker.
        try
        {
            if (!task.IsCompleted &&
                !task.Wait(ParseWaitTimeout))
            {
                document = null!;
                return false;
            }

            if (task.Status != TaskStatus.RanToCompletion)
            {
                document = null!;
                return false;
            }

            document = task.Result;
            return true;
        }
        catch (AggregateException)
        {
            document = null!;
            return false;
        }
#pragma warning restore VSTHRD002
    }

    private void ScheduleRefresh(
        ITextSnapshot snapshot,
        Task<AkburaSyntacticDocument> task)
    {
        _scheduledRefreshes.GetValue(
            snapshot,
            snapshotKey =>
            {
                _ = task.ContinueWith(
                    static (_, state) =>
                        ((AkburaOutliningTagger)state!)
                            .RaiseTagsChanged(),
                    this,
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                return new object();
            });
    }

    private void RaiseTagsChanged()
    {
        var snapshot = _buffer.CurrentSnapshot;
        TagsChanged?.Invoke(
            this,
            new SnapshotSpanEventArgs(
                new SnapshotSpan(
                    snapshot,
                    0,
                    snapshot.Length)));
    }

    private static bool IntersectsAny(
        SnapshotSpan region,
        NormalizedSnapshotSpanCollection spans)
    {
        foreach (var span in spans)
        {
            if (region.IntersectsWith(span))
            {
                return true;
            }
        }

        return false;
    }
}
