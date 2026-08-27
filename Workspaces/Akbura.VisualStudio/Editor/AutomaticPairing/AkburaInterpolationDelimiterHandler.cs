using Akbura.Workspaces;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaInterpolationDelimiterHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaInterpolationDelimiterHandler :
    IChainedCommandHandler<TypeCharCommandArgs>
{
    private static readonly TimeSpan ParseBudget =
        TimeSpan.FromMilliseconds(40);

    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaInterpolationDelimiterHandler(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
    }

    public string DisplayName =>
        "Akbura interpolated string delimiter completion";

    public CommandState GetCommandState(
        TypeCharCommandArgs args,
        Func<CommandState> nextCommandHandler)
    {
        return nextCommandHandler();
    }

    public void ExecuteCommand(
        TypeCharCommandArgs args,
        Action nextCommandHandler,
        CommandExecutionContext executionContext)
    {
        if (args.TextView.IsClosed ||
            !args.TextView.Selection.IsEmpty ||
            args.TextView.GetMultiSelectionBroker()
                .HasMultipleSelections)
        {
            nextCommandHandler();
            return;
        }
        var manager = AkburaDynamicDelimiterSessionManager
            .GetOrCreate(args.TextView, args.SubjectBuffer);
        using var commandEdit = manager.BeginCommandEdit();

        var beforeSnapshot = args.SubjectBuffer.CurrentSnapshot;
        var beforeCaret = GetCaretPosition(
            args.TextView,
            args.SubjectBuffer,
            beforeSnapshot);
        if (beforeCaret >= 0 &&
            args.TypedChar == '}' &&
            TryOvertypeClosingBrace(
                args.TextView,
                manager,
                beforeSnapshot,
                beforeCaret))
        {
            return;
        }

        if (beforeCaret >= 0 &&
            args.TypedChar == '{' &&
            TryHandleEmptySessionBrace(
                args,
                manager,
                beforeSnapshot,
                beforeCaret))
        {
            return;
        }

        nextCommandHandler();

        if (args.TypedChar != '{' || args.TextView.IsClosed)
        {
            return;
        }

        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var caret = GetCaretPosition(
            args.TextView,
            args.SubjectBuffer,
            snapshot);
        if (caret <= 0 ||
            snapshot[caret - 1] != '{' ||
            snapshot.Length - beforeSnapshot.Length != 1 ||
            !TryGetInterpolationInfo(
                snapshot,
                caret,
                out var info) ||
            !info.IsAtEndOfOpeningDelimiter ||
            info.HasClosingDelimiter)
        {
            return;
        }

        var closingText = new string(
            '}',
            info.RequiredBraceCount);
        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Insert(caret, closingText))
        {
            return;
        }

        var applied = edit.Apply();
        manager.SetSession(
            AkburaDynamicDelimiterSession.Create(
                AkburaDynamicDelimiterKind.InterpolatedStringBraces,
                applied,
                new Span(
                    info.OpeningSpan.Start,
                    info.RequiredBraceCount),
                new Span(caret, info.RequiredBraceCount),
                '{',
                '}',
                info.RequiredBraceCount));

        if (ReferenceEquals(
                args.TextView.TextBuffer,
                args.SubjectBuffer))
        {
            args.TextView.Caret.MoveTo(
                new SnapshotPoint(applied, caret));
        }
    }

    private bool TryGetInterpolationInfo(
        ITextSnapshot snapshot,
        int position,
        out AkburaInterpolationInfo info)
    {
        try
        {
            using var budget = new CancellationTokenSource(ParseBudget);
            var document = _parserService.GetSyntacticDocument(
                snapshot,
                budget.Token);
            return document.TryGetInterpolationInfo(
                position,
                out info,
                budget.Token);
        }
        catch (OperationCanceledException)
        {
            info = default;
            return false;
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Interpolation pair analysis failed: " + exception);
            info = default;
            return false;
        }
    }

    private static bool TryOvertypeClosingBrace(
        ITextView textView,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret)
    {
        if (!manager.TryGetSession(
                AkburaDynamicDelimiterKind.InterpolatedStringBraces,
                out var session) ||
            !session.TryGetSpans(
                snapshot,
                out _,
                out var closing) ||
            caret < closing.Start.Position ||
            caret >= closing.End.Position)
        {
            return false;
        }

        textView.Caret.MoveTo(
            new SnapshotPoint(snapshot, caret + 1));
        return true;
    }

    private static bool TryHandleEmptySessionBrace(
        TypeCharCommandArgs args,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret)
    {
        if (!manager.TryGetSession(
                AkburaDynamicDelimiterKind.InterpolatedStringBraces,
                out var session) ||
            !session.TryGetSpans(
                snapshot,
                out var opening,
                out var closing) ||
            opening.End.Position != caret ||
            closing.Start.Position != caret)
        {
            return false;
        }

        if (session.RequiredDelimiterLength == 1 &&
            session.OuterLiteralDelimiterCount == 0)
        {
            using var escapeEdit = args.SubjectBuffer.CreateEdit();
            if (!escapeEdit.Replace(
                    closing.Span,
                    "{"))
            {
                return false;
            }

            var appliedEscape = escapeEdit.Apply();
            manager.RemoveSession(
                AkburaDynamicDelimiterKind.InterpolatedStringBraces);
            MoveCaret(args, appliedEscape, caret + 1);
            return true;
        }

        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Insert(caret, "{") ||
            !edit.Insert(closing.End.Position, "}"))
        {
            return false;
        }

        var applied = edit.Apply();
        session.Update(
            applied,
            new Span(
                opening.Start.Position,
                opening.Length + 1),
            new Span(
                closing.Start.Position + 1,
                closing.Length + 1),
            session.RequiredDelimiterLength,
            session.OuterLiteralDelimiterCount + 1);
        MoveCaret(args, applied, caret + 1);
        return true;
    }

    private static void MoveCaret(
        TypeCharCommandArgs args,
        ITextSnapshot snapshot,
        int position)
    {
        if (ReferenceEquals(
                args.TextView.TextBuffer,
                args.SubjectBuffer))
        {
            args.TextView.Caret.MoveTo(
                new SnapshotPoint(snapshot, position));
        }
    }

    private static int GetCaretPosition(
        ITextView textView,
        ITextBuffer subjectBuffer,
        ITextSnapshot snapshot)
    {
        var caret = textView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(
                caret.Snapshot.TextBuffer,
                subjectBuffer))
        {
            return -1;
        }

        return caret.TranslateTo(
                snapshot,
                PointTrackingMode.Positive)
            .Position;
    }
}
