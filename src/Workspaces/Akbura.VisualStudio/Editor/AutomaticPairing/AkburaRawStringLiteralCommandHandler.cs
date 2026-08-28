using Akbura.Workspaces;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaRawStringLiteralCommandHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaRawStringLiteralCommandHandler :
    IChainedCommandHandler<TypeCharCommandArgs>
{
    private static readonly TimeSpan ParseBudget =
        TimeSpan.FromMilliseconds(40);

    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaRawStringLiteralCommandHandler(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
    }

    public string DisplayName =>
        "Akbura raw string literal delimiter completion";

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

        var typedCharacter = args.TypedChar;
        var beforeSnapshot = args.SubjectBuffer.CurrentSnapshot;
        var beforeCaret = GetCaretPosition(
            args.TextView,
            args.SubjectBuffer,
            beforeSnapshot);

        if (typedCharacter == '"' &&
            beforeCaret >= 0 &&
            TryOvertypeClosingQuote(
                args.TextView,
                manager,
                beforeSnapshot,
                beforeCaret))
        {
            return;
        }

        var hasRawInfo = false;
        var rawInfo = default(AkburaRawStringInfo);
        if (typedCharacter == '"' && beforeCaret >= 0)
        {
            hasRawInfo = TryGetRawStringInfo(
                beforeSnapshot,
                beforeCaret,
                out rawInfo);
        }

        nextCommandHandler();

        if (typedCharacter != '"' || args.TextView.IsClosed)
        {
            return;
        }

        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var caret = GetCaretPosition(
            args.TextView,
            args.SubjectBuffer,
            snapshot);
        if (caret <= 0 ||
            snapshot[caret - 1] != '"' ||
            snapshot.Length - beforeSnapshot.Length != 1)
        {
            return;
        }

        var typedPosition = caret - 1;
        if (TryGrowTrackedRawDelimiter(
                args,
                manager,
                snapshot,
                typedPosition))
        {
            return;
        }

        if (hasRawInfo &&
            rawInfo.IsAtEndOfOpeningDelimiter &&
            rawInfo.HasClosingDelimiter &&
            rawInfo.OpeningSpan.End == beforeCaret)
        {
            GrowParsedRawDelimiter(
                args,
                manager,
                snapshot,
                rawInfo,
                typedPosition);
            return;
        }

        CreateInitialRawDelimiter(
            args,
            manager,
            snapshot,
            caret);
    }

    private bool TryGetRawStringInfo(
        ITextSnapshot snapshot,
        int position,
        out AkburaRawStringInfo info)
    {
        try
        {
            using var budget = new CancellationTokenSource(ParseBudget);
            var document = _parserService.GetSyntacticDocument(
                snapshot,
                budget.Token);
            return document.TryGetRawStringInfo(
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
                "Raw-string pair analysis failed: " + exception);
            info = default;
            return false;
        }
    }

    private static bool TryOvertypeClosingQuote(
        ITextView textView,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret)
    {
        if (!manager.TryGetSession(
                AkburaDynamicDelimiterKind.RawStringQuotes,
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

    private static bool TryGrowTrackedRawDelimiter(
        TypeCharCommandArgs args,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int typedPosition)
    {
        if (!manager.TryGetSession(
                AkburaDynamicDelimiterKind.RawStringQuotes,
                out var session) ||
            !session.TryGetSpans(
                snapshot,
                out var opening,
                out var closing) ||
            typedPosition != opening.End.Position)
        {
            return false;
        }

        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Insert(closing.End.Position, "\""))
        {
            return false;
        }

        var applied = edit.Apply();
        session.Update(
            applied,
            new Span(opening.Start.Position, opening.Length + 1),
            new Span(closing.Start.Position, closing.Length + 1),
            session.RequiredDelimiterLength + 1,
            outerLiteralDelimiterCount: 0);
        return true;
    }

    private static void GrowParsedRawDelimiter(
        TypeCharCommandArgs args,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        AkburaRawStringInfo info,
        int typedPosition)
    {
        if (typedPosition != info.OpeningSpan.End ||
            info.ClosingSpan.Start < typedPosition)
        {
            return;
        }

        var closingStart = info.ClosingSpan.Start + 1;
        var closingEnd = info.ClosingSpan.End + 1;
        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Insert(closingEnd, "\""))
        {
            return;
        }

        var applied = edit.Apply();
        manager.SetSession(
            AkburaDynamicDelimiterSession.Create(
                AkburaDynamicDelimiterKind.RawStringQuotes,
                applied,
                new Span(
                    info.OpeningSpan.Start,
                    info.QuoteCount + 1),
                new Span(
                    closingStart,
                    info.QuoteCount + 1),
                '"',
                '"',
                info.QuoteCount + 1));
    }

    private void CreateInitialRawDelimiter(
        TypeCharCommandArgs args,
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret)
    {
        if (!TryGetRawStringInfo(snapshot, caret, out var info) ||
            !info.IsAtEndOfOpeningDelimiter ||
            info.HasClosingDelimiter ||
            info.QuoteCount != 3)
        {
            return;
        }

        var closingText = new string('"', info.QuoteCount);
        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Insert(caret, closingText))
        {
            return;
        }

        var applied = edit.Apply();
        manager.SetSession(
            AkburaDynamicDelimiterSession.Create(
                AkburaDynamicDelimiterKind.RawStringQuotes,
                applied,
                new Span(
                    info.OpeningSpan.Start,
                    info.QuoteCount),
                new Span(caret, info.QuoteCount),
                '"',
                '"',
                info.QuoteCount));

        if (ReferenceEquals(
                args.TextView.TextBuffer,
                args.SubjectBuffer))
        {
            args.TextView.Caret.MoveTo(
                new SnapshotPoint(applied, caret));
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
