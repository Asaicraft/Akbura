using Akbura.VisualStudio.Editor;
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

    private readonly AkburaParserService _parserService;

    [ImportingConstructor]
    public AkburaCompletionCommitManagerProvider(IAsyncCompletionBroker completionBroker, ITextUndoHistoryRegistry undoHistoryRegistry, AkburaParserService parserService)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(
                nameof(undoHistoryRegistry));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
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
                _undoHistoryRegistry,
                _parserService));
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

    private readonly AkburaParserService _parserService;

    public AkburaCompletionCommitManager(IAsyncCompletionBroker completionBroker, ITextUndoHistoryRegistry undoHistoryRegistry, AkburaParserService parserService)
    {
        _completionBroker = completionBroker ??
            throw new ArgumentNullException(
                nameof(completionBroker));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(
                nameof(undoHistoryRegistry));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public IEnumerable<char> PotentialCommitCharacters =>
        CommitCharacters;

    public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var computedItems = session.GetComputedItems(token);
        return !computedItems.UsesSoftSelection &&
            !computedItems.SuggestionItemSelected &&
            computedItems.SelectedItem?.CommitCharacters.Contains(
                typedChar) == true;
    }

    public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var currentSnapshot = buffer.CurrentSnapshot;
        if (!IsCurrentCompletionContext(
                session,
                buffer,
                item,
                currentSnapshot,
                token))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Stale completion commit rejected: " +
                $"sourceSnapshot={item.ApplicableToSpan.Snapshot.Version.VersionNumber}, " +
                $"snapshot={currentSnapshot.Version.VersionNumber}.");
            return new CommitResult(
                isHandled: true,
                CommitBehavior.CancelCommit);
        }

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

        // The old attribute popup may still be visible while its asynchronous
        // context switch is pending. Do not let an immediate Tab/Enter/click
        // commit an item from that old catalog across the new ${ opener.
        if (applicableSpan.GetText().IndexOf("${", StringComparison.Ordinal) >= 0 &&
            ReferenceEquals(session.TextView.TextBuffer, buffer))
        {
            var position = session.TextView.Caret.Position.BufferPosition
                .TranslateTo(currentSnapshot, PointTrackingMode.Positive).Position;
            if (!_parserService.TryGetCachedSyntacticDocument(
                    currentSnapshot,
                    out var document))
            {
                return new CommitResult(isHandled: true, CommitBehavior.CancelCommit);
            }

            token.ThrowIfCancellationRequested();
            if (!ReferenceEquals(currentSnapshot, buffer.CurrentSnapshot))
            {
                return new CommitResult(isHandled: true, CommitBehavior.CancelCommit);
            }

            var context = document.GetCompletionContext(position, token);
            if (context.Kind == AkburaCompletionContextKind.MarkupExtensionType &&
                (completion.Kind != AkburaCompletionKind.MarkupExtension ||
                 applicableSpan.Start.Position != context.ApplicableSpan.Start))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    "Stale completion commit rejected after entering a markup extension.");
                return new CommitResult(isHandled: true, CommitBehavior.CancelCommit);
            }
        }

        if (completion.Kind == AkburaCompletionKind.MarkupExtension)
        {
            // EdgeInclusive must include newly typed name characters, but it
            // can also include a closing brace inserted after the list opened.
            // Narrow the translated range before replacing anything in the buffer.
            var trackedSpan = new TextSpan(applicableSpan.Start.Position, applicableSpan.Length);
            if (!AkburaMarkupExtensionCompletionFacts.TryGetNameReplacementSpan(
                    SourceText.From(currentSnapshot.GetText()), trackedSpan, out var nameSpan))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    $"Markup extension commit rejected: item='{completion.DisplayText}', " +
                    $"snapshot={currentSnapshot.Version.VersionNumber}, trackedSpan={trackedSpan}.");
                // Do not delegate an unsafe range to the editor's default commit.
                return new CommitResult(isHandled: true, CommitBehavior.CancelCommit);
            }

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Markup extension commit: item='{completion.DisplayText}', " +
                $"sourceSnapshot={item.ApplicableToSpan.Snapshot.Version.VersionNumber}, " +
                $"snapshot={currentSnapshot.Version.VersionNumber}, " +
                $"trackedSpan={trackedSpan}, replacementSpan={nameSpan}.");
            applicableSpan = new SnapshotSpan(
                currentSnapshot, new Span(nameSpan.Start, nameSpan.Length));
        }

        var namespaceImportChange = CreateNamespaceImportChange(
            currentSnapshot,
            completion.NamespaceImport,
            applicableSpan.Start.Position,
            token);

        var triggerNextCompletion =
            completion.TriggerCompletionAfterInsert &&
            (typedChar == ' ' ||
             completion.CaretOffsetFromEnd > 0 ||
             completion.InsertText.EndsWith(".", StringComparison.Ordinal) ||
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
        var nextCharacter = applicableSpan.End.Position;
        while (nextCharacter < currentSnapshot.Length && char.IsWhiteSpace(currentSnapshot[nextCharacter]))
        {
            nextCharacter++;
        }

        var overtypeOpeningParenthesis = typedChar == '(' &&
            AkburaMarkupStatementCompletionFacts.IsStatement(completion) && completion.DisplayText != "$else" &&
            nextCharacter < currentSnapshot.Length && currentSnapshot[nextCharacter] == '(';
        if (overtypeOpeningParenthesis)
        {
            replacementText = AkburaMarkupStatementCompletionFacts.GetInsertTextBeforeExistingParenthesis(
                completion, nextCharacter > applicableSpan.End.Position);
        }

        var overtypeClosingQuote =
            typedChar is '"' or '\'' &&
            applicableSpan.Start.Position > 0 &&
            currentSnapshot[applicableSpan.Start.Position - 1] == typedChar &&
            applicableSpan.End.Position < currentSnapshot.Length &&
            currentSnapshot[applicableSpan.End.Position] == typedChar;

        using var edit = buffer.CreateEdit();
        if (!edit.Replace(
                applicableSpan.Span,
                replacementText))
        {
            return CommitResult.Unhandled;
        }

        var importDeltaBeforeCompletion = 0;
        if (namespaceImportChange is { } importChange)
        {
            var importText = importChange.NewText ?? string.Empty;
            if ((uint)importChange.Span.Start >
                    (uint)currentSnapshot.Length ||
                (uint)importChange.Span.End >
                    (uint)currentSnapshot.Length)
            {
                return CommitResult.Unhandled;
            }

            var importSpan = new Span(
                importChange.Span.Start,
                importChange.Span.Length);
            if (!edit.Replace(importSpan, importText))
            {
                return CommitResult.Unhandled;
            }

            if (importChange.Span.End <=
                applicableSpan.Start.Position)
            {
                importDeltaBeforeCompletion =
                    importText.Length -
                    importChange.Span.Length;
            }
        }

        var appliedSnapshot = edit.Apply();
        if (ReferenceEquals(session.TextView.TextBuffer, buffer))
        {
            var caretPosition = applicableSpan.Start.Position +
                importDeltaBeforeCompletion +
                replacementText.Length -
                (overtypeOpeningParenthesis ? 0 : completion.CaretOffsetFromEnd) +
                (overtypeOpeningParenthesis ? nextCharacter - applicableSpan.End.Position + 1 : 0) +
                (overtypeClosingQuote ? 1 : 0);
            session.TextView.Caret.MoveTo(
                new SnapshotPoint(appliedSnapshot, caretPosition));

            if (triggerNextCompletion)
            {
                TriggerNextCompletion(
                    session,
                    appliedSnapshot,
                    caretPosition);
            }
        }

        var suppressTypedCharacter =
            triggerNextCompletion || overtypeClosingQuote || overtypeOpeningParenthesis ||
            AkburaMarkupStatementCompletionFacts.IncludesCommitCharacter(completion, typedChar) ||
            (completion.CaretOffsetFromEnd > 0 &&
             typedChar is '=' or ' ');
        return suppressTypedCharacter
            ? new CommitResult(
                isHandled: true,
                CommitBehavior.SuppressFurtherTypeCharCommandHandlers)
            : CommitResult.Handled;
    }

    private bool IsCurrentCompletionContext(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, ITextSnapshot currentSnapshot, CancellationToken cancellationToken)
    {
        var hasSyntacticContext = item.Properties.TryGetProperty(
            AkburaCompletionProperties.SyntacticContext,
            out AkburaSyntacticCompletionContext syntacticContext);
        var hasCSharpContext = item.Properties.TryGetProperty(
            AkburaCompletionProperties.CSharpContext,
            out AkburaCSharpCompletionContext csharpContext);
        if (!hasSyntacticContext && !hasCSharpContext)
        {
            return true;
        }

        if (!ReferenceEquals(
                session.TextView.TextBuffer,
                buffer) ||
            !ReferenceEquals(
                item.ApplicableToSpan.Snapshot.TextBuffer,
                buffer))
        {
            return false;
        }

        var position = session.TextView.Caret.Position.BufferPosition
            .TranslateTo(
                currentSnapshot,
                PointTrackingMode.Positive)
            .Position;
        if (!_parserService.TryGetCachedSyntacticDocument(
                currentSnapshot,
                out var document))
        {
            return false;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!ReferenceEquals(
                currentSnapshot,
                buffer.CurrentSnapshot))
        {
            return false;
        }

        if (hasSyntacticContext)
        {
            var currentContext = document.GetCompletionContext(
                position,
                cancellationToken);
            return IsMatchingSyntacticContext(
                item.ApplicableToSpan.Snapshot,
                currentSnapshot,
                syntacticContext,
                currentContext);
        }

        return document.TryGetCSharpCompletionContext(
                position,
                out var currentCSharpContext,
                cancellationToken) &&
            IsMatchingCSharpContext(
                item.ApplicableToSpan.Snapshot,
                currentSnapshot,
                csharpContext,
                currentCSharpContext);
    }

    private static bool IsMatchingSyntacticContext(ITextSnapshot sourceSnapshot, ITextSnapshot currentSnapshot, AkburaSyntacticCompletionContext source, AkburaSyntacticCompletionContext current)
    {
        if (source.Kind != current.Kind ||
            !string.Equals(
                source.ComponentName,
                current.ComponentName,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.ParentComponentName,
                current.ParentComponentName,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.AttributeName,
                current.AttributeName,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.MarkupExtensionName,
                current.MarkupExtensionName,
                StringComparison.Ordinal) ||
            !string.Equals(
                source.MarkupExtensionArgumentName,
                current.MarkupExtensionArgumentName,
                StringComparison.Ordinal) ||
            source.MarkupExtensionArgumentIndex !=
                current.MarkupExtensionArgumentIndex ||
            !string.Equals(
                source.CompletedPath,
                current.CompletedPath,
                StringComparison.Ordinal) ||
            !TryTranslateSpan(
                sourceSnapshot,
                currentSnapshot,
                source.ApplicableSpan,
                out var applicableSpan) ||
            applicableSpan != current.ApplicableSpan ||
            !TextOutsideSpanMatches(
                sourceSnapshot,
                currentSnapshot,
                source.ApplicableSpan,
                current.ApplicableSpan))
        {
            return false;
        }

        if (source.Kind is not (
                AkburaCompletionContextKind.MarkupExtensionType or
                AkburaCompletionContextKind.MarkupExtensionArgumentName or
                AkburaCompletionContextKind.MarkupExtensionArgumentValue or
                AkburaCompletionContextKind.BindingPath))
        {
            return true;
        }

        return TryTranslateSpan(
                sourceSnapshot,
                currentSnapshot,
                source.MarkupExtensionSpan,
                out var extensionSpan) &&
            extensionSpan == current.MarkupExtensionSpan;
    }

    private static bool IsMatchingCSharpContext(ITextSnapshot sourceSnapshot, ITextSnapshot currentSnapshot, AkburaCSharpCompletionContext source, AkburaCSharpCompletionContext current)
    {
        return source.Kind == current.Kind &&
            source.OwnerKind == current.OwnerKind &&
            TryTranslateSpan(
                sourceSnapshot,
                currentSnapshot,
                source.OwnerSpan,
                out var ownerSpan) &&
            ownerSpan == current.OwnerSpan &&
            TryTranslateSpan(
                sourceSnapshot,
                currentSnapshot,
                source.HostSpan,
                out var hostSpan) &&
            hostSpan == current.HostSpan;
    }

    private static bool TextOutsideSpanMatches(ITextSnapshot sourceSnapshot, ITextSnapshot currentSnapshot, TextSpan sourceSpan, TextSpan currentSpan)
    {
        if (sourceSpan.Start != currentSpan.Start ||
            sourceSnapshot.Length - sourceSpan.End !=
                currentSnapshot.Length - currentSpan.End)
        {
            return false;
        }

        for (var index = 0; index < sourceSpan.Start; index++)
        {
            if (sourceSnapshot[index] != currentSnapshot[index])
            {
                return false;
            }
        }

        var sourceIndex = sourceSpan.End;
        var currentIndex = currentSpan.End;
        while (sourceIndex < sourceSnapshot.Length)
        {
            if (sourceSnapshot[sourceIndex] !=
                currentSnapshot[currentIndex])
            {
                return false;
            }

            sourceIndex++;
            currentIndex++;
        }

        return true;
    }

    private static bool TryTranslateSpan(ITextSnapshot sourceSnapshot, ITextSnapshot currentSnapshot, TextSpan sourceSpan, out TextSpan currentSpan)
    {
        if (sourceSpan.Start < 0 ||
            sourceSpan.End > sourceSnapshot.Length)
        {
            currentSpan = default;
            return false;
        }

        try
        {
            var translated = new SnapshotSpan(
                    sourceSnapshot,
                    new Span(sourceSpan.Start, sourceSpan.Length))
                .TranslateTo(
                    currentSnapshot,
                    SpanTrackingMode.EdgeInclusive);
            currentSpan = new TextSpan(
                translated.Start.Position,
                translated.Length);
            return true;
        }
        catch (ArgumentException)
        {
            currentSpan = default;
            return false;
        }
    }

    private TextChange? CreateNamespaceImportChange(ITextSnapshot snapshot, string? namespaceName, int position, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(namespaceName))
        {
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = ThreadHelper.JoinableTaskFactory.Run(
            async () => await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false));
        cancellationToken.ThrowIfCancellationRequested();

        return AkburaUsingEditService.TryCreateNamespaceImportChange(
            document.Text,
            document.SyntaxTree,
            namespaceName!,
            position,
            out var change)
                ? change
                : null;
    }

    private CommitResult TryCommitRoslynCompletion(IAsyncCompletionSession session, ITextBuffer buffer, AkburaRoslynCompletionItemData data, char typedChar, CancellationToken cancellationToken)
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
        foreach (var change in mappedChanges.OrderByDescending(static change => change.Span.Start))
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

    private static bool IsImportChange(TextChange change, AkburaCSharpProjection projection)
    {
        return projection.ImportContext.IsImportInsertion(change);
    }

    private static int GetActiveStartAfterChanges(int activeStart, IEnumerable<MappedCompletionChange> changes)
    {
        var result = activeStart;
        foreach (var change in changes.OrderBy(static change => change.Span.Start))
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

    private void TriggerNextCompletion(IAsyncCompletionSession currentSession, ITextSnapshot snapshotAfterCommit, int caretPosition)
    {
        var textView = currentSession.TextView;
        var trackingPoint = snapshotAfterCommit.CreateTrackingPoint(
            caretPosition,
            PointTrackingMode.Positive);
        var trigger = new CompletionTrigger(
            CompletionTriggerReason.Invoke,
            snapshotAfterCommit,
            '\0');

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
        public MappedCompletionChange(Span span, string newText, bool isImport)
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
