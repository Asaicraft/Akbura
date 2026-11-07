using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Akbura.AvaloniaEdit.Highlighting;

internal sealed class WorkspaceColorizer(AkburaCodeEditor editor) : DocumentColorizingTransformer
{
    private TextDocument? _buffer;
    private ImmutableArray<AkburaClassifiedSpan> _spans = [];

    public void SetSpans(TextDocument buffer, ImmutableArray<AkburaClassifiedSpan> spans)
    {
        _buffer = buffer;
        _spans = spans;
        editor.TextArea.TextView.Redraw();
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
