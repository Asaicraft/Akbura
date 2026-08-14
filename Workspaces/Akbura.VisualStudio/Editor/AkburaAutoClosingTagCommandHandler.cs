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
        nextCommandHandler();
        if (args.TypedChar != '>' ||
            args.TextView.IsClosed)
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
        var trackingPoint = snapshot.CreateTrackingPoint(
            caretPosition,
            PointTrackingMode.Positive);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.AutoClosingTag,
            $"'>' received: " +
            $"position={caretPosition}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

#pragma warning disable VSSDK007 // The command API is synchronous; the task catches all failures and is deliberately detached.
        ThreadHelper.JoinableTaskFactory.RunAsync(
            async () => await InsertClosingTagAsync(
                args,
                snapshot,
                caretPosition,
                trackingPoint))
            .FileAndForget("Akbura/AutoClosingTag");
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
