using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Completion;

[Export(typeof(IAsyncCompletionCommitManagerProvider))]
[Name(nameof(AkburaCompletionCommitManagerProvider))]
[ContentType(AkburaContentTypeNames.Akbura)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class AkburaCompletionCommitManagerProvider :
    IAsyncCompletionCommitManagerProvider
{
    private readonly IAsyncCompletionBroker _completionBroker;

    [ImportingConstructor]
    public AkburaCompletionCommitManagerProvider(
        IAsyncCompletionBroker completionBroker)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
    }

    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        return textView.Properties.GetOrCreateSingletonProperty(
            () => new AkburaCompletionCommitManager(
                _completionBroker));
    }
}

internal sealed class AkburaCompletionCommitManager :
    IAsyncCompletionCommitManager
{
    private static readonly char[] CommitCharacters =
        ['>', '=', ' ', '\t', '\n'];

    private readonly IAsyncCompletionBroker _completionBroker;

    public AkburaCompletionCommitManager(
        IAsyncCompletionBroker completionBroker)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
    }

    public IEnumerable<char> PotentialCommitCharacters =>
        CommitCharacters;

    public bool ShouldCommitCompletion(
        IAsyncCompletionSession session,
        SnapshotPoint location,
        char typedChar,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        return CommitCharacters.Contains(typedChar);
    }

    public CommitResult TryCommit(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!item.Properties.TryGetProperty(
                AkburaCompletionProperties.CoreItem,
                out AkburaCompletionItem completion))
        {
            return CommitResult.Unhandled;
        }

        var currentSnapshot = buffer.CurrentSnapshot;
        SnapshotSpan applicableSpan;
        try
        {
            applicableSpan = session.ApplicableToSpan.GetSpan(
                currentSnapshot);
        }
        catch (ArgumentException)
        {
            return CommitResult.Unhandled;
        }

        var triggerNextCompletion =
            completion.TriggerCompletionAfterInsert &&
            typedChar == ' ';
        var replacementText = triggerNextCompletion
            ? completion.InsertText + typedChar
            : completion.InsertText;

        using var edit = buffer.CreateEdit();
        if (!edit.Replace(
                applicableSpan.Span,
                replacementText))
        {
            return CommitResult.Unhandled;
        }

        var appliedSnapshot = edit.Apply();
        if (ReferenceEquals(session.TextView.TextBuffer, buffer))
        {
            var caretPosition = applicableSpan.Start.Position +
                replacementText.Length -
                completion.CaretOffsetFromEnd;
            session.TextView.Caret.MoveTo(
                new SnapshotPoint(appliedSnapshot, caretPosition));

            if (triggerNextCompletion)
            {
                TriggerNextCompletion(
                    session,
                    currentSnapshot,
                    appliedSnapshot,
                    caretPosition,
                    typedChar);
            }
        }

        var suppressTypedCharacter =
            triggerNextCompletion ||
            (completion.CaretOffsetFromEnd > 0 &&
             typedChar is '=' or ' ');
        return suppressTypedCharacter
            ? new CommitResult(
                isHandled: true,
                CommitBehavior.SuppressFurtherTypeCharCommandHandlers)
            : CommitResult.Handled;
    }

    private void TriggerNextCompletion(
        IAsyncCompletionSession currentSession,
        ITextSnapshot snapshotBeforeCommit,
        ITextSnapshot snapshotAfterCommit,
        int caretPosition,
        char typedChar)
    {
        var textView = currentSession.TextView;
        var trackingPoint = snapshotAfterCommit.CreateTrackingPoint(
            caretPosition,
            PointTrackingMode.Positive);
        var trigger = new CompletionTrigger(
            CompletionTriggerReason.Insertion,
            snapshotBeforeCommit,
            typedChar);

        currentSession.Dismiss();
#pragma warning disable VSSDK007 // The commit API is synchronous; the task is deliberately detached after handling all work.
        ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await Task.Yield();
            await ThreadHelper.JoinableTaskFactory
                .SwitchToMainThreadAsync();

            if (textView.IsClosed)
            {
                return;
            }

            var location = trackingPoint.GetPoint(
                textView.TextBuffer.CurrentSnapshot);
            var nextSession = _completionBroker.TriggerCompletion(
                textView,
                trigger,
                location,
                CancellationToken.None);
            nextSession?.OpenOrUpdate(
                trigger,
                location,
                CancellationToken.None);
        }).FileAndForget("Akbura/Completion/TriggerMembers");
#pragma warning restore VSSDK007
    }
}
