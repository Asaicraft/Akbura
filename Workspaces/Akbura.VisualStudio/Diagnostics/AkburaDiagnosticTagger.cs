using Akbura.Language.Syntax;
using Akbura.VisualStudio.Editor;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Tagging;
using System.Diagnostics;

namespace Akbura.VisualStudio.Diagnostics;

internal sealed class AkburaDiagnosticTagger :
    ITagger<IErrorTag>
{
    private readonly AkburaTextBufferContext _bufferContext;

    private readonly AkburaDiagnosticTableDataSource _tableDataSource;

    public AkburaDiagnosticTagger(
        ITextBuffer buffer,
        AkburaTextBufferContext bufferContext,
        AkburaDiagnosticTableDataSource tableDataSource)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        _bufferContext = bufferContext ??
            throw new ArgumentNullException(
                nameof(bufferContext));

        _tableDataSource = tableDataSource ??
            throw new ArgumentNullException(
                nameof(tableDataSource));

        _bufferContext.Changed += OnBufferContextChanged;
        _bufferContext.Disposed += OnBufferContextDisposed;

        _tableDataSource.Update(
            _bufferContext,
            buffer.CurrentSnapshot);
    }

    public event EventHandler<SnapshotSpanEventArgs>? TagsChanged;

    public IEnumerable<ITagSpan<IErrorTag>> GetTags(
        NormalizedSnapshotSpanCollection spans)
    {
        if (spans == null || spans.Count == 0)
        {
            return [];
        }

        try
        {
            var requestedSnapshot = spans[0].Snapshot;
            if (!_bufferContext.TryGetPublishedClassificationState(
                    requestedSnapshot,
                    out var state) ||
                state.Diagnostics.IsDefaultOrEmpty)
            {
                return [];
            }

            var result = new List<ITagSpan<IErrorTag>>();

            foreach (var diagnostic in state.Diagnostics)
            {
                var errorType = GetErrorType(
                    diagnostic.Severity);
                if (errorType == null ||
                    !TryCreateVisibleSpan(
                        state.Snapshot,
                        diagnostic.Span.Start,
                        diagnostic.Span.Length,
                        out var parsedSpan))
                {
                    continue;
                }

                var currentSpan = TranslateDiagnosticSpan(
                    parsedSpan,
                    requestedSnapshot);

                if (currentSpan.Length == 0 ||
                    !IntersectsAny(currentSpan, spans))
                {
                    continue;
                }

                result.Add(new TagSpan<IErrorTag>(
                    currentSpan,
                    new ErrorTag(
                        errorType,
                        diagnostic.Message)));
            }

            return result;
        }
        catch (ArgumentException exception)
        {
            Debug.WriteLine(
                $"Akbura diagnostic span translation failed: " +
                $"{exception}");

            return [];
        }
        catch (Exception exception)
        {
            // A tagger failure must never break an editor command.
            Debug.WriteLine(
                $"Akbura diagnostic tagging failed: " +
                $"{exception}");

            return [];
        }
    }

    private static string? GetErrorType(
        AkburaDiagnosticSeverity severity)
    {
        return severity switch
        {
            AkburaDiagnosticSeverity.Error =>
                PredefinedErrorTypeNames.SyntaxError,
            AkburaDiagnosticSeverity.Warning =>
                PredefinedErrorTypeNames.Warning,
            AkburaDiagnosticSeverity.Info =>
                PredefinedErrorTypeNames.Suggestion,
            _ => null,
        };
    }

    private static bool TryCreateVisibleSpan(
        ITextSnapshot snapshot,
        int start,
        int length,
        out SnapshotSpan span)
    {
        if (start < 0 ||
            length < 0 ||
            start > snapshot.Length ||
            length > snapshot.Length - start)
        {
            span = default;
            return false;
        }

        if (length == 0)
        {
            if (snapshot.Length == 0)
            {
                span = default;
                return false;
            }

            start = FindVisiblePosition(
                snapshot,
                start);

            length = 1;
        }

        span = new SnapshotSpan(
            snapshot,
            start,
            length);
        return true;
    }

    private static int FindVisiblePosition(
        ITextSnapshot snapshot,
        int position)
    {
        if (position < snapshot.Length &&
            !char.IsWhiteSpace(snapshot[position]))
        {
            return position;
        }

        var previous = Math.Min(
            position - 1,
            snapshot.Length - 1);
        while (previous >= 0 &&
               char.IsWhiteSpace(snapshot[previous]))
        {
            previous--;
        }

        if (previous >= 0)
        {
            return previous;
        }

        var next = Math.Max(0, position);
        while (next < snapshot.Length &&
               char.IsWhiteSpace(snapshot[next]))
        {
            next++;
        }

        return next < snapshot.Length
            ? next
            : 0;
    }

    private static SnapshotSpan TranslateDiagnosticSpan(
        SnapshotSpan parsedSpan,
        ITextSnapshot requestedSnapshot)
    {
        if (ReferenceEquals(
                parsedSpan.Snapshot.TextBuffer,
                requestedSnapshot.TextBuffer) &&
            parsedSpan.Snapshot.Version.VersionNumber ==
                requestedSnapshot.Version.VersionNumber)
        {
            return new SnapshotSpan(
                requestedSnapshot,
                parsedSpan.Span);
        }

        return parsedSpan.TranslateTo(
            requestedSnapshot,
            SpanTrackingMode.EdgeExclusive);
    }

    private static bool IntersectsAny(
        SnapshotSpan diagnosticSpan,
        NormalizedSnapshotSpanCollection requestedSpans)
    {
        foreach (var requestedSpan in requestedSpans)
        {
            if (diagnosticSpan.IntersectsWith(requestedSpan))
            {
                return true;
            }
        }

        return false;
    }

    private void OnBufferContextChanged(
        object sender,
        AkburaBufferChangedEventArgs e)
    {
        _tableDataSource.Update(
            _bufferContext,
            e.Span.Snapshot);

        TagsChanged?.Invoke(
            this,
            new SnapshotSpanEventArgs(e.Span));
    }

    private void OnBufferContextDisposed(
        object? sender,
        EventArgs e)
    {
        _tableDataSource.Remove(_bufferContext);
        _bufferContext.Changed -= OnBufferContextChanged;
        _bufferContext.Disposed -= OnBufferContextDisposed;
    }
}
