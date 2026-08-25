using Akbura.Workspaces;
using Microsoft.VisualStudio.Commanding;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Editor.Commanding.Commands;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor;

[Export(typeof(ICommandHandler))]
[Name(nameof(AkburaAutoClosingTagCommandHandler))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaAutoClosingTagCommandHandler :
    IChainedCommandHandler<TypeCharCommandArgs>
{
    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaAutoClosingTagCommandHandler(
        AkburaParserService parserService)
    {
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            "Command handler created.");
    }

    public string DisplayName =>
        "Akbura automatic closing tag";

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
        var typedCharacter = args.TypedChar;

        nextCommandHandler();

        if (args.TextView.IsClosed ||
            typedCharacter is not ('>' or '/'))
        {
            return;
        }

        var snapshot = args.SubjectBuffer.CurrentSnapshot;
        var caret = args.TextView.Caret.Position.BufferPosition;
        if (!ReferenceEquals(
                caret.Snapshot.TextBuffer,
                args.SubjectBuffer))
        {
            return;
        }

        var caretPosition = caret.Position;
        var trackingMode = typedCharacter == '/'
            ? PointTrackingMode.Negative
            : PointTrackingMode.Positive;
        var trackingPoint = snapshot.CreateTrackingPoint(
            caretPosition,
            trackingMode);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"'{typedCharacter}' received: " +
            $"position={caretPosition}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

#pragma warning disable VSSDK007 // The command API is synchronous; the task catches all failures and is deliberately detached.
        if (typedCharacter == '>')
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(
                    async () => await InsertClosingTagAsync(
                        args,
                        snapshot,
                        caretPosition,
                        trackingPoint))
                .FileAndForget("Akbura/AutoClosingTag");

            return;
        }

        ThreadHelper.JoinableTaskFactory.RunAsync(
                async () => await InsertSlashCompletionAsync(
                    args,
                    snapshot,
                    caretPosition,
                    trackingPoint))
            .FileAndForget("Akbura/SlashCompletion");
#pragma warning restore VSSDK007
    }

    private async Task InsertClosingTagAsync(
        TypeCharCommandArgs args,
        ITextSnapshot snapshot,
        int caretPosition,
        ITrackingPoint trackingPoint)
    {
        try
        {
            var document = await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);
            var closingTag = document
                .GetAutoClosingTagText(caretPosition);
            if (closingTag == null)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    "No closing tag was " +
                    "produced by the syntactic document.");
                return;
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                $"Parsed closing tag " +
                $"'{closingTag}'.");

            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            if (args.TextView.IsClosed)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    "Text view was closed " +
                    "before insertion.");
                return;
            }

            var currentSnapshot =
                args.SubjectBuffer.CurrentSnapshot;
            var insertionPosition = trackingPoint
                .GetPoint(currentSnapshot)
                .Position;

            if (StartsWith(
                    currentSnapshot,
                    insertionPosition,
                    closingTag))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    "Matching closing tag " +
                    "already exists.");
                return;
            }

            var currentCaret = args.TextView.Caret
                .Position.BufferPosition;
            var restoreCaret = ReferenceEquals(
                    currentCaret.Snapshot.TextBuffer,
                    args.SubjectBuffer) &&
                currentCaret.Position == insertionPosition;

            using var edit = args.SubjectBuffer.CreateEdit();
            if (!edit.Insert(
                    insertionPosition,
                    closingTag))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                    "Subject buffer rejected " +
                    "the insertion.");
                return;
            }

            var appliedSnapshot = edit.Apply();
            if (restoreCaret &&
                ReferenceEquals(
                    args.TextView.TextBuffer,
                    args.SubjectBuffer))
            {
                args.TextView.Caret.MoveTo(
                    new SnapshotPoint(
                        appliedSnapshot,
                        insertionPosition));
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                $"Inserted '{closingTag}' " +
                $"at {insertionPosition}.");
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
                "Automatic closing tag failed: " +
                exception);
        }
    }

    private async Task InsertSlashCompletionAsync(
        TypeCharCommandArgs args,
        ITextSnapshot snapshot,
        int caretPosition,
        ITrackingPoint trackingPoint)
    {
        try
        {
            var document = await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);
            if (!document.TryGetSlashCompletionEdit(
                    caretPosition,
                    out var completionEdit))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics
                        .Category.AutoClosingTag,
                    "No slash completion was produced " +
                    "by the syntactic document.");
                return;
            }

            var indentationLevel =
                document.GetSlashCompletionIndentationLevel(
                    caretPosition);

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics
                    .Category.AutoClosingTag,
                $"Parsed slash completion " +
                $"'{completionEdit.InsertionText}', " +
                $"overtype={completionEdit.OvertypeLength}, " +
                $"indentation=" +
                $"{indentationLevel?.ToString() ?? "unchanged"}.");

            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            if (args.TextView.IsClosed)
            {
                return;
            }

            var currentSnapshot =
                args.SubjectBuffer.CurrentSnapshot;
            var insertionPosition = trackingPoint
                .GetPoint(currentSnapshot)
                .Position;
            var currentCaret = args.TextView.Caret
                .Position
                .BufferPosition;

            if (!ReferenceEquals(
                    currentCaret.Snapshot.TextBuffer,
                    args.SubjectBuffer) ||
                currentCaret.Position != insertionPosition ||
                insertionPosition == 0 ||
                currentSnapshot[insertionPosition - 1] != '/')
            {
                return;
            }

            if (completionEdit.CompletesClosingTag &&
                (insertionPosition < 2 ||
                 currentSnapshot[insertionPosition - 2] != '<'))
            {
                return;
            }

            if (completionEdit.OvertypeLength != 0 &&
                (completionEdit.OvertypeLength != 1 ||
                 insertionPosition >= currentSnapshot.Length ||
                 currentSnapshot[insertionPosition] != '>'))
            {
                return;
            }

            var completionAlreadyPresent =
                completionEdit.InsertionText.Length == 0 ||
                StartsWith(
                    currentSnapshot,
                    insertionPosition,
                    completionEdit.InsertionText);
            var indentationSpan = default(Span);
            var indentationText = string.Empty;
            var hasIndentationEdit =
                completionEdit.CompletesClosingTag &&
                indentationLevel is { } level &&
                TryGetClosingTagIndentationEdit(
                    currentSnapshot,
                    insertionPosition,
                    level,
                    args.TextView.Options,
                    out indentationSpan,
                    out indentationText);

            var indentationDelta = 0;
            var appliedSnapshot = currentSnapshot;
            if (!completionAlreadyPresent || hasIndentationEdit)
            {
                using var edit =
                    args.SubjectBuffer.CreateEdit();

                if (hasIndentationEdit)
                {
                    if (!edit.Replace(
                            indentationSpan,
                            indentationText))
                    {
                        return;
                    }

                    indentationDelta =
                        indentationText.Length -
                        indentationSpan.Length;
                }

                if (!completionAlreadyPresent &&
                    !edit.Insert(
                        insertionPosition,
                        completionEdit.InsertionText))
                {
                    return;
                }

                appliedSnapshot = edit.Apply();
            }

            if (ReferenceEquals(
                    args.TextView.TextBuffer,
                    args.SubjectBuffer))
            {
                args.TextView.Caret.MoveTo(
                    new SnapshotPoint(
                        appliedSnapshot,
                        insertionPosition +
                        indentationDelta +
                        completionEdit.InsertionText.Length +
                        completionEdit.OvertypeLength));
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics
                    .Category.AutoClosingTag,
                $"Applied slash completion at " +
                $"{insertionPosition}; " +
                $"inserted='{completionEdit.InsertionText}', " +
                $"overtype={completionEdit.OvertypeLength}, " +
                $"indentationChanged={hasIndentationEdit}.");
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics
                    .Category.AutoClosingTag,
                "Automatic slash completion failed: " +
                exception);
        }
    }
    private static bool TryGetClosingTagIndentationEdit(
        ITextSnapshot snapshot,
        int insertionPosition,
        int indentationLevel,
        IEditorOptions options,
        out Span indentationSpan,
        out string indentationText)
    {
        indentationSpan = default;
        indentationText = string.Empty;

        var closingTagStart = insertionPosition - 2;
        if (closingTagStart < 0 ||
            snapshot[closingTagStart] != '<')
        {
            return false;
        }

        var line = snapshot.GetLineFromPosition(
            closingTagStart);
        var lineStart = line.Start.Position;
        for (var position = lineStart;
             position < closingTagStart;
             position++)
        {
            if (snapshot[position] is not (' ' or '\t'))
            {
                return false;
            }
        }

        var desiredIndentation = CreateIndentation(
            options,
            indentationLevel);
        var existingLength =
            closingTagStart - lineStart;
        if (existingLength == desiredIndentation.Length &&
            string.Equals(
                snapshot.GetText(
                    lineStart,
                    existingLength),
                desiredIndentation,
                StringComparison.Ordinal))
        {
            return false;
        }

        indentationSpan = new Span(
            lineStart,
            existingLength);
        indentationText = desiredIndentation;
        return true;
    }

    private static string CreateIndentation(
        IEditorOptions options,
        int indentationLevel)
    {
        var indentationSize = Math.Max(
            0,
            options.GetOptionValue(
                DefaultOptions.IndentSizeOptionId));
        var width = Math.Max(
            0,
            indentationLevel) *
            indentationSize;
        if (width == 0)
        {
            return string.Empty;
        }

        if (options.GetOptionValue(
                DefaultOptions
                    .ConvertTabsToSpacesOptionId))
        {
            return new string(' ', width);
        }

        var tabSize = Math.Max(
            1,
            options.GetOptionValue(
                DefaultOptions.TabSizeOptionId));
        var tabCount = width / tabSize;
        var spaceCount = width % tabSize;
        return new string('\t', tabCount) +
            new string(' ', spaceCount);
    }

    private static bool StartsWith(
        ITextSnapshot snapshot,
        int position,
        string value)
    {
        return position >= 0 &&
            position + value.Length <= snapshot.Length &&
            string.Equals(
                snapshot.GetText(position, value.Length),
                value,
                StringComparison.Ordinal);
    }
}
