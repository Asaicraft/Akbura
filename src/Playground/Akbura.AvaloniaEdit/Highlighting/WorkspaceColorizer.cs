using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Akbura.AvaloniaEdit.Highlighting;

internal sealed class WorkspaceColorizer(AkburaCodeEditor editor) : DocumentColorizingTransformer
{
    private TextDocument? _buffer;
    private ImmutableArray<AkburaClassifiedSpan> _spans = [];

    // These spans are presentation only: after an edit they are mapped, not reclassified.
    internal ImmutableArray<AkburaClassifiedSpan> Spans => _spans;
    internal bool HasPresentation => ReferenceEquals(_buffer, editor.Document) && !_spans.IsEmpty;

    public void SetSpans(TextDocument buffer, ImmutableArray<AkburaClassifiedSpan> spans)
    {
        _buffer = buffer;
        _spans = spans.Where(span => span.Span.Length > 0 && span.Span.End <= buffer.TextLength).ToImmutableArray();
        editor.TextArea.TextView.Redraw();
    }

    public void TrackChange(TextDocument buffer, DocumentChangeEventArgs change)
    {
        if (!ReferenceEquals(_buffer, buffer) || _spans.IsEmpty) return;

        var removedEnd = change.Offset + change.RemovalLength;
        var delta = change.InsertionLength - change.RemovalLength;
        var mapped = ImmutableArray.CreateBuilder<AkburaClassifiedSpan>(_spans.Length);
        foreach (var classified in _spans)
        {
            var span = classified.Span;
            if (span.End <= change.Offset)
            {
                mapped.Add(classified);
            }
            else if (span.Start >= removedEnd)
            {
                mapped.Add(classified with { Span = new TextSpan(span.Start + delta, span.Length) });
            }
            else if (span.Start <= change.Offset && removedEnd <= span.End && change.RemovalLength < span.Length)
            {
                // A surviving token keeps its provisional color across an internal edit,
                // including the inserted text. Boundary insertions stay outside via the cases above.
                mapped.Add(classified with { Span = new TextSpan(span.Start, span.Length + delta) });
            }
            else
            {
                // An edit crossing token boundaries retains only the untouched fragments.
                // Deleted tokens and their replacement text do not inherit an unrelated color.
                if (span.Start < change.Offset)
                {
                    mapped.Add(classified with { Span = TextSpan.FromBounds(span.Start, change.Offset) });
                }
                if (span.End > removedEnd)
                {
                    mapped.Add(classified with { Span = TextSpan.FromBounds(removedEnd + delta, span.End + delta) });
                }
            }
        }
        _spans = mapped.ToImmutable();
        editor.TextArea.TextView.Redraw();
    }

    public void Clear()
    {
        _buffer = null;
        _spans = [];
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        if (!ReferenceEquals(_buffer, editor.Document))
        {
            return;
        }

        foreach (var classified in _spans)
        {
            var start = Math.Max(line.Offset, classified.Span.Start);
            var end = Math.Min(line.EndOffset, classified.Span.End);

            if (start >= end)
            {
                continue;
            }

            var brush = EditorBrushes.Get(editor, classified.Kind.ToString());
            ChangeLinePart(start, end, element => element.TextRunProperties.SetForegroundBrush(brush));
        }
    }
}
