using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.VisualStudio.Completion;

internal sealed class AkburaCompletionSource : IAsyncCompletionSource
{
    private static readonly object CompletionItemKey = new();

    private readonly ITextBuffer _buffer;

    private readonly bool _isAkburaDocument;

    private readonly AkburaTextBufferContext _bufferContext;

    private readonly IAkburaCompletionService _completionService;

    private readonly AkburaParserService _parserService;

    public AkburaCompletionSource(
        ITextBuffer buffer,
        bool isAkburaDocument,
        AkburaTextBufferContext bufferContext,
        IAkburaCompletionService completionService,
        AkburaParserService parserService)
    {
        _buffer = buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _isAkburaDocument = isAkburaDocument;
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(
                nameof(bufferContext));
        _completionService = completionService ??
            throw new ArgumentNullException(
                nameof(completionService));
        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));
    }

    public CompletionStartData InitializeCompletion(
        CompletionTrigger trigger,
        SnapshotPoint triggerLocation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_isAkburaDocument ||
            !ReferenceEquals(
                triggerLocation.Snapshot.TextBuffer,
                _buffer) ||
            !ShouldParticipate(
                trigger,
                triggerLocation))
        {
            return CompletionStartData
                .DoesNotParticipateInCompletion;
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Participating: " +
            $"reason={trigger.Reason}, " +
            $"character='{trigger.Character}', " +
            $"position={triggerLocation.Position}, " +
            $"snapshot={triggerLocation.Snapshot.Version.VersionNumber}.");

        var snapshot = triggerLocation.Snapshot;
        var start = triggerLocation.Position;
        while (start > 0 &&
               AkburaMarkupEditingFacts
                   .IsCompletionNameCharacter(
                       snapshot[start - 1]))
        {
            start--;
        }

        return new CompletionStartData(
            CompletionParticipation.ProvidesItems,
            new SnapshotSpan(
                snapshot,
                Span.FromBounds(
                    start,
                    triggerLocation.Position)));
    }

    public async Task<CompletionContext> GetCompletionContextAsync(
     IAsyncCompletionSession session,
     CompletionTrigger trigger,
     SnapshotPoint triggerLocation,
     SnapshotSpan applicableToSpan,
     CancellationToken cancellationToken)
    {
        var snapshot =
            triggerLocation.Snapshot;

        var position =
            triggerLocation.Position;

        Debug.WriteLine(
            $"[Akbura.Completion] Context requested: " +
            $"position={position}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

        var syntacticDocument =
            await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        var syntaxContext =
            syntacticDocument.GetCompletionContext(
                position);

        Debug.WriteLine(
            $"[Akbura.Completion] Syntax context: " +
            $"kind={syntaxContext.Kind}, " +
            $"prefix='{syntaxContext.Prefix}'.");

        if (syntaxContext.IsDefault)
        {
            return CompletionContext.Empty;
        }

        var documentContext =
            await _bufferContext
                .GetPublishedDocumentContextAsync(
                    snapshot,
                    cancellationToken)
                .ConfigureAwait(false);

        if (documentContext == null)
        {
            Debug.WriteLine(
                "[Akbura.Completion] No document context.");

            return CompletionContext.Empty;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result =
            _completionService.GetCompletions(
                syntacticDocument,
                documentContext,
                position,
                cancellationToken);

        Debug.WriteLine(
            $"[Akbura.Completion] Core returned " +
            $"{result.Items.Length} items.");

        foreach (var completion in result.Items.Take(10))
        {
            Debug.WriteLine(
                $"[Akbura.Completion] Item: " +
                $"{completion.DisplayText}");
        }

        var sourceSpan = result.ApplicableSpan;

        if (sourceSpan.Start < 0 ||
            sourceSpan.End > snapshot.Length)
        {
            Debug.WriteLine(
                $"[Akbura.Completion] Invalid applicable span: " +
                $"{sourceSpan}.");

            return CompletionContext.Empty;
        }

        var snapshotSpan =
            new SnapshotSpan(
                snapshot,
                new Span(
                    sourceSpan.Start,
                    sourceSpan.Length));

        var items =
            ImmutableArray.CreateBuilder<CompletionItem>(
                result.Items.Length);

        foreach (var completion in result.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item =
                new CompletionItem(
                    displayText:
                        completion.DisplayText,
                    source:
                        this,
                    icon:
                        default!,
                    filters:
                        ImmutableArray<CompletionFilter>.Empty,
                    suffix:
                        string.Empty,
                    insertText:
                        completion.InsertText,
                    sortText:
                        completion.SortText,
                    filterText:
                        completion.FilterText,
                    automationText:
                        completion.DisplayText,
                    attributeIcons:
                        ImmutableArray<ImageElement>.Empty,
                    commitCharacters:
                        GetCommitCharacters(
                            completion.Kind),
                    applicableToSpan:
                        snapshotSpan,
                    isCommittedAsSnippet:
                        false,
                    isPreselected:
                        false);

            item.Properties.AddProperty(
                CompletionItemKey,
                completion);

            items.Add(item);
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Returning " +
            $"{items.Count} VS items.");

        return new CompletionContext(
            items.ToImmutable());
    }

    public Task<object> GetDescriptionAsync(
        IAsyncCompletionSession session,
        CompletionItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(
            item.Properties.TryGetProperty(
                CompletionItemKey,
                out AkburaCompletionItem completion)
                ? completion.Description
                : item.DisplayText);
    }

    private static bool ShouldParticipate(
        CompletionTrigger trigger,
        SnapshotPoint triggerLocation)
    {
        if (trigger.Reason is
            CompletionTriggerReason.Invoke or
            CompletionTriggerReason.InvokeAndCommitIfUnique or
            CompletionTriggerReason.InvokeMatchingType)
        {
            return true;
        }

        if (trigger.Reason !=
            CompletionTriggerReason.Insertion)
        {
            return false;
        }

        if (trigger.Character is
            '<' or '/' or ' ' or '.' or ':')
        {
            return true;
        }

        return AkburaMarkupEditingFacts
                   .IsCompletionNameCharacter(
                       trigger.Character) &&
               AkburaMarkupEditingFacts
                   .IsPotentialCompletionPosition(
                       triggerLocation.Snapshot,
                       triggerLocation.Position);
    }

    private static ImmutableArray<char> GetCommitCharacters(
        AkburaCompletionKind kind)
    {
        return kind switch
        {
            AkburaCompletionKind.Component or
            AkburaCompletionKind.PropertyElement =>
                ImmutableArray.Create('>', ' ', '\t', '\n'),

            AkburaCompletionKind.ClosingTag =>
                ImmutableArray.Create('>', '\t', '\n'),

            _ => ImmutableArray.Create('=', ' ', '\t', '\n'),
        };
    }

}
