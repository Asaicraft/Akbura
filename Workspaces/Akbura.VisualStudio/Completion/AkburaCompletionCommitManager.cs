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
    [
        ' ', '\t', '\n',
        '!', '"', '#', '$', '%', '&', '\'', '(', ')', '*', '+',
        ',', '-', '.', '/', ':', ';', '<', '=', '>', '?', '@',
        '[', '\\', ']', '^', '`', '{', '|', '}', '~',
    ];

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
        var computedItems = session.GetComputedItems(token);
        return !computedItems.UsesSoftSelection &&
            !computedItems.SuggestionItemSelected &&
            computedItems.SelectedItem?.CommitCharacters.Contains(
                typedChar) == true;
    }

    public CommitResult TryCommit(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        CompletionItem item,
        char typedChar,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (item.Properties.TryGetProperty(
                AkburaCompletionProperties.RoslynItem,
                out AkburaRoslynCompletionItemData roslynData))
        {
            return TryCommitRoslynCompletion(
                session,
                buffer,
                roslynData,
                typedChar,
                token);
        }

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

    private static CommitResult TryCommitRoslynCompletion(
        IAsyncCompletionSession session,
        ITextBuffer buffer,
        AkburaRoslynCompletionItemData data,
        char typedChar,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(
                data.State.HostSnapshot.TextBuffer,
                buffer))
        {
            return CommitResult.Unhandled;
        }

        var completionChange = ThreadHelper
            .JoinableTaskFactory
            .Run(async () => await data.State.Service
                .GetChangeAsync(
                    data.State.Document,
                    data.Item,
                    typedChar,
                    cancellationToken)
                .ConfigureAwait(false));
        var changes = completionChange.TextChanges.IsDefaultOrEmpty
            ? [completionChange.TextChange]
            : completionChange.TextChanges;
        var currentSnapshot = buffer.CurrentSnapshot;
        var mappedChanges = new List<MappedCompletionChange>(
            changes.Length);

        foreach (var change in changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!data.State.Projection.TryMapToHost(
                    change.Span,
                    out var hostSpan))
            {
                return CommitResult.Unhandled;
            }

            SnapshotSpan currentSpan;
            try
            {
                currentSpan = new SnapshotSpan(
                        data.State.HostSnapshot,
                        new Span(
                            hostSpan.Start,
                            hostSpan.Length))
                    .TranslateTo(
                        currentSnapshot,
                        SpanTrackingMode.EdgeInclusive);
            }
            catch (ArgumentException)
            {
                return CommitResult.Unhandled;
            }

            mappedChanges.Add(new MappedCompletionChange(
                currentSpan.Span,
                change.NewText ?? string.Empty));
        }

        int? caretPosition = null;
        if (completionChange.NewPosition is { } projectedPosition)
        {
            var relativePosition = projectedPosition -
                data.State.Projection.ProjectedSpan.Start;
            var projectedLengthAfterChanges =
                data.State.Projection.ProjectedSpan.Length;
            foreach (var change in changes)
            {
                projectedLengthAfterChanges +=
                    (change.NewText?.Length ?? 0) -
                    change.Span.Length;
            }

            if (relativePosition < 0 ||
                relativePosition > projectedLengthAfterChanges)
            {
                return CommitResult.Unhandled;
            }

            try
            {
                var currentHostStart = new SnapshotPoint(
                        data.State.HostSnapshot,
                        data.State.Projection.HostSpan.Start)
                    .TranslateTo(
                        currentSnapshot,
                        PointTrackingMode.Negative)
                    .Position;
                caretPosition = currentHostStart +
                    relativePosition;
            }
            catch (ArgumentException)
            {
                return CommitResult.Unhandled;
            }
        }

        using var edit = buffer.CreateEdit();
        foreach (var change in mappedChanges
                     .OrderByDescending(static change =>
                         change.Span.Start))
        {
            if (!edit.Replace(
                    change.Span,
                    change.NewText))
            {
                return CommitResult.Unhandled;
            }
        }

        var appliedSnapshot = edit.Apply();
        if (caretPosition is { } position &&
            ReferenceEquals(
                session.TextView.TextBuffer,
                buffer) &&
            position >= 0 &&
            position <= appliedSnapshot.Length)
        {
            session.TextView.Caret.MoveTo(
                new SnapshotPoint(
                    appliedSnapshot,
                    position));
        }

        return completionChange.IncludesCommitCharacter
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

    private readonly struct MappedCompletionChange
    {
        public MappedCompletionChange(
            Span span,
            string newText)
        {
            Span = span;
            NewText = newText;
        }

        public Span Span { get; }

        public string NewText { get; }
    }
}
