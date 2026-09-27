using Akbura.Workspaces;
using Akbura.Workspaces.AutomaticPairing;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

internal sealed class AkburaPendingMarkupAutoCloseState
{
    private static readonly object PropertyKey = new();

    private readonly ITextView _textView;
    private readonly ITextBuffer _subjectBuffer;
    private readonly string _parentElementName;
    private readonly ITrackingSpan _parentEndTagSpan;
    private ITrackingPoint? _openingPoint;
    private ITrackingPoint? _generatedClosingAnglePoint;
    private readonly AkburaMarkupAutoCloseSessionLifecycle _lifecycle = new();

    public AkburaPendingMarkupAutoCloseState(
        ITextView textView,
        ITextBuffer subjectBuffer,
        AkburaMarkupTagPairContext context,
        ITextSnapshot snapshot)
    {
        _textView = textView ??
            throw new ArgumentNullException(nameof(textView));
        _subjectBuffer = subjectBuffer ??
            throw new ArgumentNullException(nameof(subjectBuffer));
        _parentElementName = context.ParentElementName;
        _parentEndTagSpan = snapshot.CreateTrackingSpan(
            new Span(
                context.ParentEndTagSpan.Start,
                context.ParentEndTagSpan.Length),
            SpanTrackingMode.EdgeExclusive);
    }

    public void TrackPairPoints(
        ITrackingPoint openingPoint,
        ITrackingPoint generatedClosingAnglePoint)
    {
        _openingPoint = openingPoint ??
            throw new ArgumentNullException(nameof(openingPoint));
        _generatedClosingAnglePoint = generatedClosingAnglePoint ??
            throw new ArgumentNullException(nameof(generatedClosingAnglePoint));
    }

    public bool IsPending => _lifecycle.IsPending;

    public static void BeginTypeCharCommand(
        ITextView textView,
        ITextBuffer subjectBuffer,
        char typedCharacter,
        ITextSnapshot snapshot,
        int caretPosition)
    {
        if (typedCharacter != '>' ||
            !textView.Properties.TryGetProperty(
                PropertyKey,
                out AkburaPendingMarkupAutoCloseState state))
        {
            return;
        }

        var isAtGeneratedClosingAngle =
            ReferenceEquals(textView, state._textView) &&
            ReferenceEquals(subjectBuffer, state._subjectBuffer) &&
            ReferenceEquals(snapshot.TextBuffer, subjectBuffer) &&
            state.IsAtTagEndForTypeCharCommand(
                snapshot,
                caretPosition,
                allowMissingGeneratedAngle:
                    state._lifecycle.IsMarkupContextArmed);
        if (state._lifecycle.TryBeginTypeCharCommand(
                isAtGeneratedClosingAngle))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Markup auto-close type-char command started at " +
                $"tracked tag end {caretPosition}.");
        }
    }

    public void Register()
    {
        if (_textView.Properties.TryGetProperty(
                PropertyKey,
                out AkburaPendingMarkupAutoCloseState existing))
        {
            existing.Cancel();
        }

        _textView.Properties.AddProperty(PropertyKey, this);
    }

    public bool PublishPending(ITextSnapshot snapshot)
    {
        if (!TryGetValidSpans(
                snapshot,
                out var openingPosition,
                out var closingAnglePosition,
                out var parentEndTagSpan) ||
            !_lifecycle.TryPublishPending(
                successfulAngleOvertype: true))
        {
            return false;
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"Markup auto-close pending: " +
            $"parent={_parentElementName}, " +
            $"closingAngle={closingAnglePosition}, " +
            $"parentEndTag=[{parentEndTagSpan.Start}..{parentEndTagSpan.End}), " +
            $"opening={openingPosition}.");
        return true;
    }

    public void Finish()
    {
        var snapshot = _subjectBuffer.CurrentSnapshot;
        var isCaretAtTagEnd = false;
        var caretPosition = -1;
        var closingAnglePosition = -1;
        var hasValidTagPair = false;
        if (!_textView.IsClosed)
        {
            var caret = _textView.Caret.Position.BufferPosition;
            caretPosition = caret.TranslateTo(
                snapshot,
                PointTrackingMode.Positive).Position;
            hasValidTagPair = ReferenceEquals(
                    caret.Snapshot.TextBuffer,
                    _subjectBuffer) &&
                TryGetValidSpans(
                    snapshot,
                    out _,
                    out closingAnglePosition,
                    out _);
            isCaretAtTagEnd = hasValidTagPair &&
                (caretPosition == closingAnglePosition ||
                 caretPosition == closingAnglePosition + 1);
        }

        if (_lifecycle.Finish(
                preserveMarkupContext: isCaretAtTagEnd))
        {
            var preservationReason = IsPending
                ? "pending auto-close"
                : _lifecycle.IsTypeCharCommandInProgress
                    ? "in-flight type-char command"
                    : "markup context at tag end";
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                $"Brace session finished: preserved " +
                $"{preservationReason}.");
            return;
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            "Brace session finished without markup context: " +
            $"viewClosed={_textView.IsClosed}, " +
            $"validTagPair={hasValidTagPair}, " +
            $"caret={caretPosition}, " +
            $"trackedClosingAngle={closingAnglePosition}.");
        RemoveFromView();
    }

    public void Cancel()
    {
        _lifecycle.Cancel();
        RemoveFromView();
    }

    public static AkburaMarkupTagPairContext? TryTake(
        ITextView textView,
        ITextBuffer subjectBuffer,
        char typedCharacter,
        ITextSnapshot snapshot,
        int caretPosition)
    {
        if (typedCharacter != '>')
        {
            return null;
        }

        if (!textView.Properties.TryGetProperty(
                PropertyKey,
                out AkburaPendingMarkupAutoCloseState state))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Markup auto-close consume: found=false, " +
                "reason=no-pending-state.");
            return null;
        }

        if (!state.IsValidForCommand(
                textView,
                subjectBuffer,
                snapshot,
                caretPosition))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Markup auto-close consume: found=false, " +
                "reason=stale-or-unrelated-session.");
            state.Cancel();
            return null;
        }

        var span = state._parentEndTagSpan.GetSpan(snapshot).Span;
        if (!state._lifecycle.TryConsume())
        {
            state.Cancel();
            return null;
        }

        state.RemoveFromView();
        var context = new AkburaMarkupTagPairContext(
            state._parentElementName,
            new Microsoft.CodeAnalysis.Text.TextSpan(
                span.Start,
                span.Length));
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"Markup auto-close consume: found=true, " +
            $"parent={context.ParentElementName}, " +
            $"parentEndTag=[{span.Start}..{span.End}).");
        return context;
    }

    private bool IsValidForCommand(
        ITextView textView,
        ITextBuffer subjectBuffer,
        ITextSnapshot snapshot,
        int caretPosition)
    {
        if (!ReferenceEquals(textView, _textView) ||
            !ReferenceEquals(subjectBuffer, _subjectBuffer) ||
            !ReferenceEquals(snapshot.TextBuffer, _subjectBuffer))
        {
            return false;
        }

        if (TryGetValidSpans(
                snapshot,
                out _,
                out var closingAnglePosition,
                out _))
        {
            return (caretPosition == closingAnglePosition &&
                caretPosition > 0 &&
                snapshot[caretPosition - 1] == '>') ||
                ((IsPending || _lifecycle.IsTypeCharCommandInProgress) &&
                caretPosition == closingAnglePosition + 1 &&
                snapshot[closingAnglePosition] == '>');
        }

        return _lifecycle.IsTypeCharCommandInProgress &&
            IsValidTypeCharCommandResult(snapshot, caretPosition);
    }

    private bool IsAtTagEndForTypeCharCommand(
        ITextSnapshot snapshot,
        int caretPosition,
        bool allowMissingGeneratedAngle)
    {
        var openingPoint = _openingPoint;
        var closingAnglePoint = _generatedClosingAnglePoint;
        if (openingPoint == null || closingAnglePoint == null)
        {
            return false;
        }

        var openingPosition = openingPoint.GetPoint(snapshot).Position;
        var closingAnglePosition = closingAnglePoint
            .GetPoint(snapshot)
            .Position;
        var parentEndTagSpan = _parentEndTagSpan.GetSpan(snapshot).Span;
        return ReferenceEquals(snapshot.TextBuffer, _subjectBuffer) &&
            caretPosition == closingAnglePosition &&
            openingPosition >= 0 &&
            openingPosition < snapshot.Length &&
            snapshot[openingPosition] == '<' &&
            parentEndTagSpan.Start >= 0 &&
            parentEndTagSpan.End <= snapshot.Length &&
            caretPosition <= parentEndTagSpan.Start &&
            (allowMissingGeneratedAngle ||
             closingAnglePosition < snapshot.Length &&
             snapshot[closingAnglePosition] == '>') &&
            string.Equals(
                snapshot.GetText(parentEndTagSpan),
                $"</{_parentElementName}>",
                StringComparison.Ordinal);
    }

    private bool IsValidTypeCharCommandResult(
        ITextSnapshot snapshot,
        int caretPosition)
    {
        var openingPoint = _openingPoint;
        if (openingPoint == null ||
            caretPosition <= 0 ||
            caretPosition > snapshot.Length ||
            snapshot[caretPosition - 1] != '>')
        {
            return false;
        }

        var openingPosition = openingPoint.GetPoint(snapshot).Position;
        var parentEndTagSpan = _parentEndTagSpan.GetSpan(snapshot).Span;
        return openingPosition >= 0 &&
            openingPosition < snapshot.Length &&
            snapshot[openingPosition] == '<' &&
            caretPosition > openingPosition &&
            caretPosition <= parentEndTagSpan.Start &&
            parentEndTagSpan.Start >= 0 &&
            parentEndTagSpan.End <= snapshot.Length &&
            string.Equals(
                snapshot.GetText(parentEndTagSpan),
                $"</{_parentElementName}>",
                StringComparison.Ordinal);
    }

    private bool TryGetValidSpans(
        ITextSnapshot snapshot,
        out int openingPosition,
        out int closingAnglePosition,
        out Span parentEndTagSpan)
    {
        var openingPoint = _openingPoint;
        var closingAnglePoint = _generatedClosingAnglePoint;
        if (openingPoint == null || closingAnglePoint == null)
        {
            openingPosition = default;
            closingAnglePosition = default;
            parentEndTagSpan = default;
            return false;
        }

        openingPosition = openingPoint.GetPoint(snapshot).Position;
        closingAnglePosition = closingAnglePoint.GetPoint(snapshot).Position;
        parentEndTagSpan = _parentEndTagSpan.GetSpan(snapshot).Span;

        return ReferenceEquals(snapshot.TextBuffer, _subjectBuffer) &&
            openingPosition >= 0 &&
            openingPosition < snapshot.Length &&
            snapshot[openingPosition] == '<' &&
            closingAnglePosition >= 0 &&
            closingAnglePosition < snapshot.Length &&
            snapshot[closingAnglePosition] == '>' &&
            parentEndTagSpan.Start >= 0 &&
            parentEndTagSpan.End <= snapshot.Length &&
            string.Equals(
                snapshot.GetText(parentEndTagSpan),
                $"</{_parentElementName}>",
                StringComparison.Ordinal);
    }

    private void RemoveFromView()
    {
        if (_textView.Properties.TryGetProperty(
                PropertyKey,
                out AkburaPendingMarkupAutoCloseState activeState) &&
            ReferenceEquals(activeState, this))
        {
            _textView.Properties.RemoveProperty(PropertyKey);
        }
    }
}
