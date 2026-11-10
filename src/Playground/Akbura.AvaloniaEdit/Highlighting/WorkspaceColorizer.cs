using Akbura.Collections;
using Akbura.Pools;
using Akbura.AvaloniaEdit.Infrastructure;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Akbura.AvaloniaEdit.Highlighting;

internal sealed class WorkspaceColorizer(AkburaCodeEditor editor) : DocumentColorizingTransformer
{
    private readonly ArrayElement<IImmutableBrush?>[] _lineBrushes =
        new ArrayElement<IImmutableBrush?>[(int)AkburaClassificationKind.LastKind + 1];
    private TextDocument? _buffer;
    private readonly TextSegmentCollection<ClassifiedSegment> _segments = [];
    private ImmutableArray<AkburaClassifiedSpan> _snapshot = [];

#if DEBUG
    internal EditorPerformance? Performance
    {
        get; init;
    }
#endif

    // Only inspection/publication materializes this snapshot; rendering and edits use the tree.
    internal ImmutableArray<AkburaClassifiedSpan> Spans
    {
        get
        {
            if (!_snapshot.IsDefault)
            {
                return _snapshot;
            }
            var ordered = ArrayBuilder<ClassifiedSegment>.GetInstance(_segments.Count);
            try
            {
                foreach (var segment in _segments)
                {
                    ordered.Add(segment);
                }
                ordered.Sort(SegmentOrderComparer.Instance);
                return _snapshot = CreateSpans(ordered);
            }
            finally
            {
                ordered.Free();
            }
        }
    }

    internal bool HasPresentation => ReferenceEquals(_buffer, editor.Document) && _segments.Count > 0;

    public void SetSpans(TextDocument buffer, ImmutableArray<AkburaClassifiedSpan> spans)
    {
        using var incomingBuilder = ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent(spans.Length);
        foreach (var span in spans)
        {
            if (span.Span.Length > 0 && span.Span.End <= buffer.TextLength)
            {
                incomingBuilder.Add(span);
            }
        }
        var incoming = incomingBuilder.ToImmutable();
        var old = Spans;
        var sameBuffer = ReferenceEquals(_buffer, buffer);
        if (sameBuffer && old.AsSpan().SequenceEqual(incoming.AsSpan()))
        {
            return;
        }

        var start = buffer.TextLength;
        var end = 0;
        for (var i = 0; i < Math.Max(old.Length, incoming.Length); i++)
        {
            if (i < old.Length && i < incoming.Length && old[i] == incoming[i])
            {
                continue;
            }

            if (i < old.Length)
            {
                start = Math.Min(start, old[i].Span.Start);
                end = Math.Max(end, old[i].Span.End);
            }
            if (i < incoming.Length)
            {
                start = Math.Min(start, incoming[i].Span.Start);
                end = Math.Max(end, incoming[i].Span.End);
            }
        }
        _buffer = buffer;
        _segments.Clear();
        for (var i = 0; i < incoming.Length; i++)
        {
            _segments.Add(new ClassifiedSegment(incoming[i].Span, incoming[i].Kind, i));
        }

        _snapshot = incoming;
#if DEBUG
        Performance?.Count("PresentationRebuild");
#endif
        if (!sameBuffer)
        {
#if DEBUG
            Performance?.Count("FullRedraw");
#endif
            editor.TextArea.TextView.Redraw();
        }
        else if (end > start)
        {
#if DEBUG
            Performance?.Count("RangeRedraw");
#endif
            start = Math.Min(start, buffer.TextLength);
            editor.TextArea.TextView.Redraw(start, Math.Max(0, Math.Min(end, buffer.TextLength) - start));
        }
    }

    public void TrackChange(TextDocument buffer, DocumentChangeEventArgs change)
    {
        if (!ReferenceEquals(_buffer, buffer) || _segments.Count == 0)
        {
            return;
        }

        var removedEnd = change.Offset + change.RemovalLength;
        var delta = change.InsertionLength - change.RemovalLength;
        var affected = ArrayBuilder<ClassifiedSegment>.GetInstance();
        var replacements = ArrayBuilder<ClassifiedSegment>.GetInstance();
        try
        {
            foreach (var segment in _segments.FindOverlappingSegments(change.Offset, change.RemovalLength))
            {
                if (segment.EndOffset > change.Offset && segment.StartOffset < removedEnd ||
                    change.RemovalLength == 0 && segment.StartOffset < change.Offset && segment.EndOffset > change.Offset)
                {
                    affected.Add(segment);
                }
            }
            foreach (var segment in affected)
            {
                var start = segment.StartOffset;
                var end = segment.EndOffset;
                _segments.Remove(segment);
                if (start <= change.Offset && removedEnd <= end && change.RemovalLength < end - start)
                {
                    replacements.Add(new(new TextSpan(start, end - start + delta), segment.Kind, segment.Order));
                }
                else
                {
                    if (start < change.Offset)
                    {
                        replacements.Add(new(TextSpan.FromBounds(start, change.Offset), segment.Kind, segment.Order));
                    }
                    if (end > removedEnd)
                    {
                        replacements.Add(new(TextSpan.FromBounds(removedEnd + delta, end + delta), segment.Kind, segment.Order));
                    }
                }
            }
            // The disconnected tree shifts unaffected segments once. Only intersections
            // need custom provisional fragments, and document changes invalidate edited lines.
            _segments.UpdateOffsets(new OffsetChangeMapEntry(change.Offset, change.RemovalLength, change.InsertionLength));
            foreach (var segment in replacements)
            {
                if (segment.Length > 0)
                {
                    _segments.Add(segment);
                }
            }
#if DEBUG
            Performance?.Count("PresentationRemapped", affected.Count);
#endif
        }
        finally
        {
            replacements.Free();
            affected.Free();
        }
        _snapshot = default;
        // TextDocument already invalidates changed lines and tracks surviving visual lines.
    }

    public void Clear()
    {
        _buffer = null;
        _segments.Clear();
        _snapshot = [];
    }

    internal void InvalidateAppearance()
    {
#if DEBUG
        Performance?.Count("FullRedraw");
#endif
        editor.TextArea.TextView.Redraw();
    }

    private ArrayBuilder<ClassifiedSegment> GetLineSegments(int start, int length)
    {
        var segments = ArrayBuilder<ClassifiedSegment>.GetInstance();
        try
        {
            foreach (var segment in _segments.FindOverlappingSegments(start, length))
            {
                if (segment.StartOffset < start + length && segment.EndOffset > start)
                {
                    segments.Add(segment);
                }
            }
            segments.Sort(SegmentOrderComparer.Instance);
#if DEBUG
            Performance?.Count("RenderedSpanVisits", segments.Count);
#endif
            return segments;
        }
        catch
        {
            segments.Free();
            throw;
        }
    }

    private static ImmutableArray<AkburaClassifiedSpan> CreateSpans(ArrayBuilder<ClassifiedSegment> segments)
    {
        using var spans = ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent(segments.Count);
        foreach (var segment in segments)
        {
            spans.Add(new(new TextSpan(segment.StartOffset, segment.Length), segment.Kind));
        }
        return spans.ToImmutable();
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!ReferenceEquals(_buffer, editor.Document))
        {
            return;
        }

        // Reuse storage, but resolve resources afresh for each line.
        Array.Clear(_lineBrushes);
        var segments = GetLineSegments(line.Offset, line.Length);
        try
        {
            foreach (var classified in segments)
            {
                var start = Math.Max(line.Offset, classified.StartOffset);
                var end = Math.Min(line.EndOffset, classified.EndOffset);
                if (start >= end)
                {
                    continue;
                }
                var brush = _lineBrushes[(int)classified.Kind].Value ??=
                    EditorBrushes.Get(editor, classified.Kind.ToString());
                ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
            }
        }
        finally
        {
            segments.Free();
        }
    }

    private sealed class SegmentOrderComparer : IComparer<ClassifiedSegment>
    {
        internal static readonly SegmentOrderComparer Instance = new();

        public int Compare(ClassifiedSegment? left, ClassifiedSegment? right)
        {
            var order = left!.Order.CompareTo(right!.Order);
            return order != 0 ? order : left.StartOffset.CompareTo(right.StartOffset);
        }
    }

    private sealed class ClassifiedSegment : TextSegment
    {
        internal ClassifiedSegment(TextSpan span, AkburaClassificationKind kind, int order)
        {
            StartOffset = span.Start;
            Length = span.Length;
            Kind = kind;
            Order = order;
        }

        internal AkburaClassificationKind Kind
        {
            get;
        }

        internal int Order
        {
            get;
        }
    }
}
