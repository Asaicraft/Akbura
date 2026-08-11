using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
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
    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        return textView.Properties.GetOrCreateSingletonProperty(
            static () => new AkburaCompletionCommitManager());
    }
}

internal sealed class AkburaCompletionCommitManager :
    IAsyncCompletionCommitManager
{
    private static readonly char[] CommitCharacters =
        ['>', '=', ' ', '\t', '\n'];

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

        using var edit = buffer.CreateEdit();
        if (!edit.Replace(
                applicableSpan.Span,
                completion.InsertText))
        {
            return CommitResult.Unhandled;
        }

        var appliedSnapshot = edit.Apply();
        if (ReferenceEquals(session.TextView.TextBuffer, buffer))
        {
            var caretPosition = applicableSpan.Start.Position +
                completion.InsertText.Length -
                completion.CaretOffsetFromEnd;
            session.TextView.Caret.MoveTo(
                new SnapshotPoint(appliedSnapshot, caretPosition));
        }

        var suppressTypedCharacter =
            completion.CaretOffsetFromEnd > 0 &&
            typedChar is '=' or ' ';
        return suppressTypedCharacter
            ? new CommitResult(
                isHandled: true,
                CommitBehavior.SuppressFurtherTypeCharCommandHandlers)
            : CommitResult.Handled;
    }
}
