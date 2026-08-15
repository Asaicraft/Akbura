using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using Microsoft.CodeAnalysis.Text;
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

    private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;

    [ImportingConstructor]
    public AkburaCompletionCommitManagerProvider(
        IAsyncCompletionBroker completionBroker,
        ITextUndoHistoryRegistry undoHistoryRegistry)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(
                nameof(undoHistoryRegistry));
    }

    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView)
    {
        if (textView == null)
        {
            throw new ArgumentNullException(nameof(textView));
        }

        return textView.Properties.GetOrCreateSingletonProperty(
            () => new AkburaCompletionCommitManager(
                _completionBroker,
                _undoHistoryRegistry));
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

    private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;

    public AkburaCompletionCommitManager(
        IAsyncCompletionBroker completionBroker,
        ITextUndoHistoryRegistry undoHistoryRegistry)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(
                nameof(undoHistoryRegistry));
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
            applicableSpan = item.ApplicableToSpan.TranslateTo(
                currentSnapshot,
                SpanTrackingMode.EdgeInclusive);
        }
        catch (ArgumentException)
        {
            return CommitResult.Unhandled;
        }

        var triggerNextCompletion =
            completion.TriggerCompletionAfterInsert &&
            (typedChar == ' ' ||
             completion.CaretOffsetFromEnd > 0 ||
             completion.Kind == AkburaCompletionKind.Property &&
             completion.InsertText.EndsWith(
                 " ",
                 StringComparison.Ordinal));
        var appendTypedCharacter =
            triggerNextCompletion &&
            typedChar == ' ' &&
            completion.CaretOffsetFromEnd == 0 &&
            !completion.InsertText.EndsWith(
                " ",
                StringComparison.Ordinal);
        var replacementText = appendTypedCharacter
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
                    typedChar == ' ' ? typedChar : ' ');
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

    private CommitResult TryCommitRoslynCompletion(
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

        var roslynChange = ThreadHelper
            .JoinableTaskFactory
            .Run(async () =>
            {
                var completionChange = await data.State.Service
                    .GetChangeAsync(
                        data.State.Document,
                        data.Item,
                        typedChar,
                        cancellationToken)
                    .ConfigureAwait(false);
                var projectedText = await data.State.Document
                    .GetTextAsync(cancellationToken)
                    .ConfigureAwait(false);
                return (completionChange, projectedText);
            });
        if (!AkburaCSharpCompletionChangeMapper.TryMapCompletionChange(
                SourceText.From(data.State.HostSnapshot.GetText()),
                roslynChange.projectedText,
                data.State.Projection,
                roslynChange.completionChange,
                out var mapped))
        {
            return CommitResult.Unhandled;
        }

        var currentSnapshot = buffer.CurrentSnapshot;
        var mappedChanges = new List<MappedCompletionChange>(
            mapped.Changes.Length);

        foreach (var change in mapped.Changes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SnapshotSpan currentSpan;
            try
            {
                currentSpan = new SnapshotSpan(
                        data.State.HostSnapshot,
                        new Span(
                            change.Span.Start,
                            change.Span.Length))
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
                change.NewText ?? string.Empty,
                IsImportChange(change, data.State.Projection)));
        }

        int? caretPosition = null;
        if (mapped.NewHostPosition is { } mappedHostPosition)
        {
            var originalActiveStartAfterChanges =
                GetActiveStartAfterChanges(
                    data.State.Projection.HostSpan.Start,
                    mapped.Changes.Select(change =>
                        new MappedCompletionChange(
                            new Span(change.Span.Start, change.Span.Length),
                            change.NewText ?? string.Empty,
                            IsImportChange(
                                change,
                                data.State.Projection))));
            var relativePosition = mappedHostPosition -
                originalActiveStartAfterChanges;
            if (relativePosition < 0)
            {
                return CommitResult.Unhandled;
            }

            try
            {
                var currentActiveStart = new SnapshotPoint(
                        data.State.HostSnapshot,
                        data.State.Projection.HostSpan.Start)
                    .TranslateTo(
                        currentSnapshot,
                        PointTrackingMode.Negative)
                    .Position;
                caretPosition = GetActiveStartAfterChanges(
                        currentActiveStart,
                        mappedChanges) +
                    relativePosition;
            }
            catch (ArgumentException)
            {
                return CommitResult.Unhandled;
            }
        }

        if (!_undoHistoryRegistry.TryGetHistory(
                buffer,
                out var undoHistory))
        {
            undoHistory = _undoHistoryRegistry.RegisterHistory(buffer);
        }

        using var transaction = undoHistory.CreateTransaction(
            "Akbura C# completion");
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
        transaction.Complete();
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

        return mapped.IncludesCommitCharacter
            ? new CommitResult(
                isHandled: true,
                CommitBehavior.SuppressFurtherTypeCharCommandHandlers)
            : CommitResult.Handled;
    }

    private static bool IsImportChange(
        TextChange change,
        AkburaCSharpProjection projection)
    {
        return projection.ImportContext.IsImportInsertion(change);
    }

    private static int GetActiveStartAfterChanges(
        int activeStart,
        IEnumerable<MappedCompletionChange> changes)
    {
        var result = activeStart;
        foreach (var change in changes.OrderBy(static change =>
                     change.Span.Start))
        {
            if (change.IsImport &&
                change.Span.Start <= activeStart ||
                change.Span.End <= activeStart &&
                change.Span.Start < activeStart)
            {
                result += change.NewText.Length - change.Span.Length;
            }
        }

        return result;
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
            string newText,
            bool isImport)
        {
            Span = span;
            NewText = newText;
            IsImport = isImport;
        }

        public Span Span { get; }

        public string NewText { get; }

        public bool IsImport { get; }
    }
}
