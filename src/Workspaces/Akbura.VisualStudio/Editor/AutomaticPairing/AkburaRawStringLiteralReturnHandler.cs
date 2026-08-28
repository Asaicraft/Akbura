using Akbura.Workspaces;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor.AutomaticPairing;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaRawStringLiteralReturnHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaRawStringLiteralReturnHandler :
    IChainedCommandHandler<ReturnKeyCommandArgs>
{
    private static readonly TimeSpan ParseBudget =
        TimeSpan.FromMilliseconds(40);

    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaRawStringLiteralReturnHandler(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(nameof(parserService));
    }

    public string DisplayName =>
        "Akbura raw string literal Return formatting";

    public CommandState GetCommandState(
        ReturnKeyCommandArgs args,
        Func<CommandState> nextCommandHandler)
    {
        return nextCommandHandler();
    }

    public void ExecuteCommand(
        ReturnKeyCommandArgs args,
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

        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var caretPoint = args.TextView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(
                caretPoint.Snapshot.TextBuffer,
                args.SubjectBuffer) ||
            !TryGetDelimiterSpans(
                manager,
                snapshot,
                caretPoint.Position,
                out var opening,
                out var closing,
                out var quoteCount) ||
            snapshot.GetLineFromPosition(opening.Start.Position)
                .LineNumber !=
            snapshot.GetLineFromPosition(closing.Start.Position)
                .LineNumber)
        {
            nextCommandHandler();
            return;
        }

        var caret = caretPoint.Position;
        var contentStart = opening.End.Position;
        var contentEnd = closing.Start.Position;
        if (caret < contentStart || caret > contentEnd)
        {
            nextCommandHandler();
            return;
        }

        var content = snapshot.GetText(
            contentStart,
            contentEnd - contentStart);
        if (content.IndexOfAny(new[] { '\r', '\n' }) >= 0)
        {
            nextCommandHandler();
            return;
        }

        var line = snapshot.GetLineFromPosition(
            opening.Start.Position);
        var baseIndentation = GetLeadingWhitespace(
            snapshot,
            line.Start.Position,
            opening.Start.Position);
        var innerIndentation = baseIndentation +
            CreateSingleIndentation(args.TextView.Options);
        var newLine = args.TextView.Options.GetOptionValue(
            DefaultOptions.NewLineCharacterOptionId);
        if (string.IsNullOrEmpty(newLine))
        {
            newLine = Environment.NewLine;
        }

        var relativeCaret = caret - contentStart;
        string replacement;
        int newCaret;
        if (content.Length == 0)
        {
            replacement =
                newLine +
                innerIndentation +
                newLine +
                baseIndentation;
            newCaret = contentStart +
                newLine.Length +
                innerIndentation.Length;
        }
        else
        {
            var before = content.Substring(0, relativeCaret);
            var after = content.Substring(relativeCaret);
            var caretPrefix =
                newLine +
                innerIndentation +
                before +
                newLine +
                innerIndentation;
            replacement =
                caretPrefix +
                after +
                newLine +
                baseIndentation;
            newCaret = contentStart + caretPrefix.Length;
        }

        using var edit = args.SubjectBuffer.CreateEdit();
        if (!edit.Replace(
                Span.FromBounds(contentStart, contentEnd),
                replacement))
        {
            nextCommandHandler();
            return;
        }

        var applied = edit.Apply();
        var closingStart = contentStart + replacement.Length;
        manager.SetSession(
            AkburaDynamicDelimiterSession.Create(
                AkburaDynamicDelimiterKind.RawStringQuotes,
                applied,
                new Span(opening.Start.Position, quoteCount),
                new Span(closingStart, quoteCount),
                '"',
                '"',
                quoteCount));
        if (ReferenceEquals(
                args.TextView.TextBuffer,
                args.SubjectBuffer))
        {
            args.TextView.Caret.MoveTo(
                new SnapshotPoint(applied, newCaret));
        }
    }

    private bool TryGetDelimiterSpans(
        AkburaDynamicDelimiterSessionManager manager,
        ITextSnapshot snapshot,
        int caret,
        out SnapshotSpan opening,
        out SnapshotSpan closing,
        out int quoteCount)
    {
        if (manager.TryGetSession(
                AkburaDynamicDelimiterKind.RawStringQuotes,
                out var session) &&
            session.TryGetSpans(
                snapshot,
                out opening,
                out closing))
        {
            quoteCount = opening.Length;
            return true;
        }

        try
        {
            using var budget = new CancellationTokenSource(ParseBudget);
            var document = _parserService.GetSyntacticDocument(
                snapshot,
                budget.Token);
            if (document.TryGetRawStringInfo(
                    caret,
                    out var info,
                    budget.Token) &&
                info.HasClosingDelimiter)
            {
                opening = new SnapshotSpan(
                    snapshot,
                    new Span(
                        info.OpeningSpan.Start,
                        info.OpeningSpan.Length));
                closing = new SnapshotSpan(
                    snapshot,
                    new Span(
                        info.ClosingSpan.Start,
                        info.ClosingSpan.Length));
                quoteCount = info.QuoteCount;
                return true;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Raw-string Return analysis failed: " + exception);
        }

        opening = default;
        closing = default;
        quoteCount = default;
        return false;
    }

    private static string GetLeadingWhitespace(
        ITextSnapshot snapshot,
        int lineStart,
        int limit)
    {
        var end = lineStart;
        while (end < limit && snapshot[end] is ' ' or '\t')
        {
            end++;
        }

        return snapshot.GetText(lineStart, end - lineStart);
    }

    private static string CreateSingleIndentation(
        IEditorOptions options)
    {
        var indentationSize = Math.Max(
            0,
            options.GetOptionValue(DefaultOptions.IndentSizeOptionId));
        if (indentationSize == 0)
        {
            return string.Empty;
        }

        if (options.GetOptionValue(
                DefaultOptions.ConvertTabsToSpacesOptionId))
        {
            return new string(' ', indentationSize);
        }

        var tabSize = Math.Max(
            1,
            options.GetOptionValue(DefaultOptions.TabSizeOptionId));
        var tabCount = indentationSize / tabSize;
        var spaceCount = indentationSize % tabSize;
        return new string('\t', tabCount) +
            new string(' ', spaceCount);
    }
}
