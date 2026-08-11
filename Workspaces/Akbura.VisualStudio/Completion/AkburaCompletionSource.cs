using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Adornments;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.VisualStudio.Completion;

internal sealed class AkburaCompletionSource : IAsyncCompletionSource
{
    private static readonly ImmutableArray<char>
        ComponentCommitCharacters = ['>', ' ', '\t', '\n'];

    private static readonly ImmutableArray<char>
        ClosingTagCommitCharacters = ['>', '\t', '\n'];

    private static readonly ImmutableArray<char>
        MemberCommitCharacters = ['=', ' ', '\t', '\n'];

    private static readonly ImageElement ComponentIcon =
        CreateImageElement(KnownMonikers.Class, "Component");

    private static readonly ImageElement ParameterIcon =
        CreateImageElement(KnownMonikers.Parameter, "Parameter");

    private static readonly ImageElement PropertyIcon =
        CreateImageElement(KnownMonikers.Property, "Property");

    private static readonly ImageElement EventIcon =
        CreateImageElement(KnownMonikers.Event, "Event");

    private static readonly ImageElement CommandIcon =
        CreateImageElement(KnownMonikers.Method, "Command");

    private static readonly ImageElement ClosingTagIcon =
        CreateImageElement(KnownMonikers.GoToNext, "Closing tag");

    private static readonly CompletionFilter ComponentFilter =
        new("Components", "C", ComponentIcon);

    private static readonly CompletionFilter ParameterFilter =
        new("Parameters", "A", ParameterIcon);

    private static readonly CompletionFilter PropertyFilter =
        new("Properties", "P", PropertyIcon);

    private static readonly CompletionFilter EventFilter =
        new("Events", "E", EventIcon);

    private static readonly CompletionFilter CommandFilter =
        new("Commands", "M", CommandIcon);

    private static readonly ImmutableArray<CompletionFilter>
        ComponentFilters = [ComponentFilter];

    private static readonly ImmutableArray<CompletionFilter>
        ParameterFilters = [ParameterFilter];

    private static readonly ImmutableArray<CompletionFilter>
        PropertyFilters = [PropertyFilter];

    private static readonly ImmutableArray<CompletionFilter>
        EventFilters = [EventFilter];

    private static readonly ImmutableArray<CompletionFilter>
        CommandFilters = [CommandFilter];

    private static readonly ImmutableArray<CompletionFilterWithState>
        CompletionFilters =
        [
            new(ComponentFilter, isAvailable: true),
            new(ParameterFilter, isAvailable: true),
            new(PropertyFilter, isAvailable: true),
            new(EventFilter, isAvailable: true),
            new(CommandFilter, isAvailable: true),
        ];

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
        var totalTimer = Stopwatch.StartNew();
        var snapshot =
            triggerLocation.Snapshot;

        var position =
            triggerLocation.Position;

        Debug.WriteLine(
            $"[Akbura.Completion] Context requested: " +
            $"position={position}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

        var stageTimer = Stopwatch.StartNew();
        var syntacticDocument =
            await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);
        TracePerformance("Syntax document", stageTimer.Elapsed);

        cancellationToken.ThrowIfCancellationRequested();

        stageTimer.Restart();
        var syntaxContext =
            syntacticDocument.GetCompletionContext(
                position);
        TracePerformance("Syntax context", stageTimer.Elapsed);

        Debug.WriteLine(
            $"[Akbura.Completion] Syntax context: " +
            $"kind={syntaxContext.Kind}, " +
            $"prefix='{syntaxContext.Prefix}'.");

        if (syntaxContext.IsDefault)
        {
            return CompletionContext.Empty;
        }

        stageTimer.Restart();
        AkburaDocumentContext? documentContext = null;
        if (_bufferContext.TryGetLatestDocumentContext(
                out var latestContext,
                out var semanticSnapshot) &&
            semanticSnapshot.Version.VersionNumber <=
                snapshot.Version.VersionNumber)
        {
            documentContext = latestContext;
        }
        TracePerformance("Semantic context", stageTimer.Elapsed);

        cancellationToken.ThrowIfCancellationRequested();

        stageTimer.Restart();
        var result =
            _completionService.GetCompletions(
                syntacticDocument,
                documentContext,
                position,
                cancellationToken);
        TracePerformance("Core completion", stageTimer.Elapsed);

        Debug.WriteLine(
            $"[Akbura.Completion] Core returned " +
            $"{result.Items.Length} items.");

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

        stageTimer.Restart();
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
                        GetIcon(completion.Kind),
                    filters:
                        GetFilters(completion.Kind),
                    suffix:
                        completion.Suffix,
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
                AkburaCompletionProperties.CoreItem,
                completion);

            items.Add(item);
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Returning " +
            $"{items.Count} VS items.");

        TracePerformance("VS item conversion", stageTimer.Elapsed);
        TracePerformance("Total", totalTimer.Elapsed);

        return new CompletionContext(
            items.ToImmutable(),
            CompletionFilters,
            result.IsIncomplete);
    }

    public Task<object> GetDescriptionAsync(
        IAsyncCompletionSession session,
        CompletionItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<object>(
            item.Properties.TryGetProperty(
                AkburaCompletionProperties.CoreItem,
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
                ComponentCommitCharacters,

            AkburaCompletionKind.ClosingTag =>
                ClosingTagCommitCharacters,

            _ => MemberCommitCharacters,
        };
    }

    private static ImageElement GetIcon(
        AkburaCompletionKind kind)
    {
        return kind switch
        {
            AkburaCompletionKind.Component => ComponentIcon,
            AkburaCompletionKind.ClosingTag => ClosingTagIcon,
            AkburaCompletionKind.Parameter => ParameterIcon,
            AkburaCompletionKind.Event => EventIcon,
            AkburaCompletionKind.Command => CommandIcon,
            _ => PropertyIcon,
        };
    }

    private static ImageElement CreateImageElement(
        ImageMoniker moniker,
        string automationName)
    {
        return new ImageElement(
            new ImageId(moniker.Guid, moniker.Id),
            automationName);
    }

    private static ImmutableArray<CompletionFilter> GetFilters(
        AkburaCompletionKind kind)
    {
        return kind switch
        {
            AkburaCompletionKind.Component or
            AkburaCompletionKind.ClosingTag => ComponentFilters,
            AkburaCompletionKind.Parameter => ParameterFilters,
            AkburaCompletionKind.Event => EventFilters,
            AkburaCompletionKind.Command => CommandFilters,
            _ => PropertyFilters,
        };
    }

    [Conditional("DEBUG")]
    private static void TracePerformance(
        string stage,
        TimeSpan elapsed)
    {
        Debug.WriteLine(
            $"[Akbura.Completion.Performance] " +
            $"{stage}: {elapsed.TotalMilliseconds:F2} ms");
    }

}
