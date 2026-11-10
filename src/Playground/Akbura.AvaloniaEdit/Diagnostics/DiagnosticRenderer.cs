using Akbura.AvaloniaEdit.Highlighting;
using Akbura.Language.Syntax;
using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace Akbura.AvaloniaEdit.Diagnostics;

internal sealed class DiagnosticRenderer(AkburaCodeEditor editor) : IBackgroundRenderer
{
    private TextDocument? _buffer;
    private ImmutableArray<AkburaDiagnosticSpan> _issues = [];
    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(TextDocument buffer, ImmutableArray<AkburaDiagnosticSpan> issues)
    {
        if (ReferenceEquals(_buffer, buffer) && _issues.SequenceEqual(issues))
        {
            return;
        }

        _buffer = buffer;
        _issues = issues;
        editor.TextArea.TextView.InvalidateLayer(Layer);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!ReferenceEquals(_buffer, editor.Document) || !textView.VisualLinesValid)
        {
            return;
        }

        foreach (var diagnostic in _issues)
        {
            if (diagnostic.Severity is not (AkburaDiagnosticSeverity.Error or AkburaDiagnosticSeverity.Warning) ||
                diagnostic.Span.End > _buffer.TextLength)
            {
                continue;
            }

            var segment = new TextSegment { StartOffset = diagnostic.Span.Start, Length = diagnostic.Span.Length };

            var pen =
                new Pen(EditorBrushes.Get(
                    editor,
                    diagnostic.Severity == AkburaDiagnosticSeverity.Error ? "Error" : "Warning"),
                    1);

            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, segment))
            {
                var right = Math.Max(rect.Right, rect.Left + 4);
                for (var x = rect.Left; x < right; x += 4)
                {
                    drawingContext.DrawLine(pen, new Point(x, rect.Bottom - 1), new Point(Math.Min(x + 2, right), rect.Bottom - 3));
                    drawingContext.DrawLine(pen, new Point(Math.Min(x + 2, right), rect.Bottom - 3), new Point(Math.Min(x + 4, right), rect.Bottom - 1));
                }
            }
        }
    }
}
