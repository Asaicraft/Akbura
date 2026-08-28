using Akbura.Workspaces;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.BraceCompletion;
using Microsoft.VisualStudio.Text.Editor;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

internal sealed class AkburaBraceCompletionSession :
    IBraceCompletionSession
{
    private ITrackingPoint _openingPoint;
    private ITrackingPoint _closingPoint;
    private bool _started;
    private bool _finished;

    public AkburaBraceCompletionSession(
        ITextView textView,
        SnapshotPoint openingPoint,
        char openingBrace,
        char closingBrace)
    {
        TextView = textView ??
            throw new ArgumentNullException(nameof(textView));
        SubjectBuffer = openingPoint.Snapshot.TextBuffer;
        OpeningBrace = openingBrace;
        ClosingBrace = closingBrace;
        _openingPoint = openingPoint.Snapshot.CreateTrackingPoint(
            openingPoint.Position,
            PointTrackingMode.Negative);
        _closingPoint = openingPoint.Snapshot.CreateTrackingPoint(
            openingPoint.Position,
            PointTrackingMode.Positive);
    }

    public ITrackingPoint OpeningPoint => _openingPoint;

    public ITrackingPoint ClosingPoint => _closingPoint;

    public ITextView TextView { get; }

    public ITextBuffer SubjectBuffer { get; }

    public char OpeningBrace { get; }

    public char ClosingBrace { get; }

    public void Start()
    {
        if (_started || _finished || TextView.IsClosed)
        {
            return;
        }

        var snapshot = SubjectBuffer.CurrentSnapshot;
        var trackedOpeningPosition = _openingPoint
            .GetPoint(snapshot)
            .Position;
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"Brace session starting: " +
            $"trackedPosition={trackedOpeningPosition}, " +
            $"snapshot={snapshot.Version.VersionNumber}, " +
            $"length={snapshot.Length}.");

        if (!TryGetOpeningPosition(
                snapshot,
                trackedOpeningPosition,
                out var openingPosition))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace session invalidated: opening character was not found " +
                "at or immediately before the tracked point.");
            Invalidate();
            return;
        }

        var closingPosition = openingPosition + 1;
        using var edit = SubjectBuffer.CreateEdit();
        if (!edit.Insert(
                closingPosition,
                ClosingBrace.ToString()))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Brace session invalidated: closing character insertion failed.");
            Invalidate();
            return;
        }

        var applied = edit.Apply();
        _openingPoint = applied.CreateTrackingPoint(
            openingPosition,
            PointTrackingMode.Negative);
        _closingPoint = applied.CreateTrackingPoint(
            closingPosition + 1,
            PointTrackingMode.Positive);
        _started = true;

        MoveCaretTo(applied, closingPosition);
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"Brace session started: " +
            $"openingPosition={openingPosition}, " +
            $"closingPosition={closingPosition}, " +
            $"snapshot={applied.Version.VersionNumber}.");
    }

    public void Finish()
    {
        Invalidate();
    }

    public void PreOverType(out bool handledCommand)
    {
        handledCommand = TryMoveAcrossClosingBrace();
    }

    public void PostOverType()
    {
    }

    public void PreTab(out bool handledCommand)
    {
        handledCommand = TryMoveAcrossClosingBrace(
            allowWhitespace: true);
    }

    public void PostTab()
    {
    }

    public void PreBackspace(out bool handledCommand)
    {
        handledCommand = false;
        if (!TryGetPairPositions(
                out var snapshot,
                out var openingPosition,
                out var closingPosition) ||
            !TryGetCaretPosition(snapshot, out var caretPosition) ||
            caretPosition != openingPosition + 1 ||
            caretPosition != closingPosition)
        {
            return;
        }

        using var edit = SubjectBuffer.CreateEdit();
        if (!edit.Delete(new Span(openingPosition, 2)))
        {
            return;
        }

        var applied = edit.Apply();
        MoveCaretTo(applied, openingPosition);
        handledCommand = true;
        Invalidate();
    }

    public void PostBackspace()
    {
    }

    public void PreDelete(out bool handledCommand)
    {
        handledCommand = false;
    }

    public void PostDelete()
    {
    }

    public void PreReturn(out bool handledCommand)
    {
        handledCommand = false;
    }

    public void PostReturn()
    {
    }

    private bool TryMoveAcrossClosingBrace(
        bool allowWhitespace = false)
    {
        if (!TryGetPairPositions(
                out var snapshot,
                out _,
                out var closingPosition) ||
            !TryGetCaretPosition(snapshot, out var caretPosition) ||
            caretPosition > closingPosition)
        {
            return false;
        }

        if (allowWhitespace)
        {
            for (var position = caretPosition;
                 position < closingPosition;
                 position++)
            {
                if (!char.IsWhiteSpace(snapshot[position]))
                {
                    return false;
                }
            }
        }
        else if (caretPosition != closingPosition)
        {
            return false;
        }

        MoveCaretTo(snapshot, closingPosition + 1);
        return true;
    }

    private bool TryGetOpeningPosition(
        ITextSnapshot snapshot,
        int trackedPosition,
        out int openingPosition)
    {
        if (trackedPosition < snapshot.Length &&
            snapshot[trackedPosition] == OpeningBrace)
        {
            openingPosition = trackedPosition;
            return true;
        }

        if (trackedPosition > 0 &&
            snapshot[trackedPosition - 1] == OpeningBrace)
        {
            openingPosition = trackedPosition - 1;
            return true;
        }

        openingPosition = default;
        return false;
    }

    private bool TryGetPairPositions(
        out ITextSnapshot snapshot,
        out int openingPosition,
        out int closingPosition)
    {
        snapshot = SubjectBuffer.CurrentSnapshot;
        openingPosition = -1;
        closingPosition = -1;
        if (!_started || _finished)
        {
            return false;
        }

        openingPosition = _openingPoint
            .GetPoint(snapshot)
            .Position;
        closingPosition = _closingPoint
            .GetPoint(snapshot)
            .Position - 1;
        if (openingPosition < 0 ||
            closingPosition <= openingPosition ||
            closingPosition >= snapshot.Length ||
            snapshot[openingPosition] != OpeningBrace ||
            snapshot[closingPosition] != ClosingBrace)
        {
            Invalidate();
            return false;
        }

        return true;
    }

    private bool TryGetCaretPosition(
        ITextSnapshot snapshot,
        out int position)
    {
        var caret = TextView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(
                caret.Snapshot.TextBuffer,
                SubjectBuffer))
        {
            position = default;
            return false;
        }

        position = caret.TranslateTo(
                snapshot,
                PointTrackingMode.Positive)
            .Position;
        return true;
    }

    private void MoveCaretTo(
        ITextSnapshot snapshot,
        int position)
    {
        if (!TextView.IsClosed &&
            ReferenceEquals(TextView.TextBuffer, SubjectBuffer))
        {
            TextView.Caret.MoveTo(
                new SnapshotPoint(snapshot, position));
        }
    }

    private void Invalidate()
    {
        _started = false;
        _finished = true;
    }
}
