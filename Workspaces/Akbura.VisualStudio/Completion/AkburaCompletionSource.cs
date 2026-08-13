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

    private static readonly ImmutableArray<char>
        MarkupExtensionCommitCharacters = [' ', '\t', '\n'];

    private static readonly ImmutableArray<char>
        TailwindUtilityCommitCharacters = [' ', '\t', '\n'];

    private static readonly ImmutableArray<char>
        KeywordCommitCharacters = [' ', '\t', '\n'];

    private static readonly ImageElement ComponentIcon =
        CreateImageElement(KnownMonikers.Class, "Component");

    private static readonly ImageElement ParameterIcon =
        CreateImageElement(KnownMonikers.Parameter, "Parameter");

    private static readonly ImageElement PropertyIcon =
        CreateImageElement(KnownMonikers.Property, "Property");

    private static readonly ImageElement StateIcon =
        CreateImageElement(AkburaCompletionImageMonikers.State, "State");

    private static readonly ImageElement EventIcon =
        CreateImageElement(KnownMonikers.Event, "Event");

    private static readonly ImageElement CommandIcon =
        CreateImageElement(KnownMonikers.Method, "Command");

    private static readonly ImageElement ClosingTagIcon =
        CreateImageElement(KnownMonikers.GoToNext, "Closing tag");

    private static readonly ImageElement MarkupExtensionIcon =
        CreateImageElement(KnownMonikers.Extension, "Markup extension");

    private static readonly ImageElement TailwindUtilityIcon =
        CreateImageElement(KnownMonikers.Property, "AKCSS utility");

    private static readonly ImageElement KeywordIcon =
        CreateImageElement(
            KnownMonikers.IntellisenseKeyword,
            "Keyword");

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

    private static readonly CompletionFilter MarkupExtensionFilter =
        new("Markup extensions", "X", MarkupExtensionIcon);

    private static readonly CompletionFilter TailwindUtilityFilter =
        new("AKCSS utilities", "U", TailwindUtilityIcon);

    private static readonly CompletionFilter KeywordFilter =
        new("Keywords", "K", KeywordIcon);

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

    private static readonly ImmutableArray<CompletionFilter>
        MarkupExtensionFilters = [MarkupExtensionFilter];

    private static readonly ImmutableArray<CompletionFilter>
        TailwindUtilityFilters = [TailwindUtilityFilter];

    private static readonly ImmutableArray<CompletionFilter>
        KeywordFilters = [KeywordFilter];

    private static readonly ImmutableArray<CompletionFilterWithState>
        CompletionFilters =
        [
            new(ComponentFilter, isAvailable: true),
            new(ParameterFilter, isAvailable: true),
            new(PropertyFilter, isAvailable: true),
            new(EventFilter, isAvailable: true),
            new(CommandFilter, isAvailable: true),
            new(MarkupExtensionFilter, isAvailable: true),
            new(TailwindUtilityFilter, isAvailable: true),
            new(KeywordFilter, isAvailable: true),
        ];

    private readonly ITextBuffer _buffer;

    private readonly bool _isAkburaDocument;

    private readonly AkburaTextBufferContext _bufferContext;

    private readonly IAkburaCompletionService _completionService;

    private readonly AkburaParserService _parserService;

    private readonly AkburaRoslynCompletionService
        _roslynCompletionService;

    public AkburaCompletionSource(
        ITextBuffer buffer,
        bool isAkburaDocument,
        AkburaTextBufferContext bufferContext,
        IAkburaCompletionService completionService,
        AkburaParserService parserService,
        AkburaRoslynCompletionService roslynCompletionService)
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
        _roslynCompletionService = roslynCompletionService ??
            throw new ArgumentNullException(
                nameof(roslynCompletionService));
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
                position,
                cancellationToken);
        TracePerformance("Syntax context", stageTimer.Elapsed);

        Debug.WriteLine(
            $"[Akbura.Completion] Syntax context: " +
            $"kind={syntaxContext.Kind}, " +
            $"prefix='{syntaxContext.Prefix}'.");

        stageTimer.Restart();
        if (syntacticDocument.TryGetCSharpCompletionContext(
                position,
                out var csharpContext,
                cancellationToken))
        {
            var semanticContext = GetLatestSemanticContext(
                snapshot);
            var supplementalResult = syntaxContext.Kind ==
                    AkburaCompletionContextKind.TopLevel
                ? _completionService.GetCompletions(
                    syntacticDocument,
                    semanticContext: null,
                    position,
                    cancellationToken)
                : default;
            TracePerformance(
                "C# semantic context",
                stageTimer.Elapsed);

            stageTimer.Restart();
            var csharpResult = await _roslynCompletionService
                .GetCompletionsAsync(
                    snapshot,
                    syntacticDocument,
                    semanticContext,
                    csharpContext,
                    trigger,
                    cancellationToken)
                .ConfigureAwait(false);
            TracePerformance(
                "Roslyn completion",
                stageTimer.Elapsed);
            TracePerformance(
                "Total",
                totalTimer.Elapsed);

            return csharpResult is { } roslynResult
                ? CreateRoslynCompletionContext(
                    roslynResult,
                    supplementalResult,
                    cancellationToken)
                : supplementalResult.IsEmpty
                    ? CreateIncompleteCompletionContext()
                    : CreateCoreCompletionContext(
                        snapshot,
                        supplementalResult,
                        isIncomplete: true,
                        cancellationToken);
        }

        if (syntaxContext.IsDefault)
        {
            return CompletionContext.Empty;
        }

        stageTimer.Restart();
        var documentContext = GetLatestSemanticContext(
            snapshot);
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
        TracePerformance("Total", totalTimer.Elapsed);
        return CreateCoreCompletionContext(
            snapshot,
            result,
            result.IsIncomplete,
            cancellationToken);
    }

    public async Task<object> GetDescriptionAsync(
        IAsyncCompletionSession session,
        CompletionItem item,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (item.Properties.TryGetProperty(
                AkburaCompletionProperties.RoslynItem,
                out AkburaRoslynCompletionItemData roslynData))
        {
            var description = await roslynData.State.Service
                .GetDescriptionAsync(
                    roslynData.State.Document,
                    roslynData.Item,
                    cancellationToken)
                .ConfigureAwait(false);
            if (description == null)
            {
                return item.DisplayText;
            }

            return string.Concat(
                description.TaggedParts.Select(static part =>
                    part.Text));
        }

        return item.Properties.TryGetProperty(
            AkburaCompletionProperties.CoreItem,
            out AkburaCompletionItem completion)
                ? completion.Description
                : item.DisplayText;
    }

    private AkburaDocumentContext? GetLatestSemanticContext(
        ITextSnapshot snapshot)
    {
        if (_bufferContext.TryGetLatestDocumentContext(
                out var latestContext,
                out var semanticSnapshot) &&
            semanticSnapshot.Version.VersionNumber <=
                snapshot.Version.VersionNumber)
        {
            return latestContext;
        }

        return null;
    }

    private CompletionContext CreateRoslynCompletionContext(
        AkburaRoslynCompletionResult result,
        AkburaCompletionResult supplementalResult,
        CancellationToken cancellationToken)
    {
        var snapshot = result.State.HostSnapshot;
        var items = ImmutableArray.CreateBuilder<CompletionItem>(
            result.List.ItemsList.Count +
            supplementalResult.Items.Length);

        foreach (var completion in result.List.ItemsList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Auto-import and expanded items can edit generated C# outside
            // the single source span that maps back to the Akbura document.
            if (completion.IsComplexTextEdit ||
                !result.State.Projection.TryMapToHost(
                    completion.Span,
                    out var hostSpan) ||
                hostSpan.Start < 0 ||
                hostSpan.End > snapshot.Length)
            {
                continue;
            }

            var item = new CompletionItem(
                displayText: completion.DisplayText,
                source: this,
                icon: GetRoslynIcon(
                    result.State.Projection,
                    completion),
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix: completion.DisplayTextSuffix,
                insertText: completion.DisplayText,
                sortText: completion.SortText,
                filterText: completion.FilterText,
                automationText: completion.DisplayText,
                attributeIcons: ImmutableArray<ImageElement>.Empty,
                commitCharacters: GetRoslynCommitCharacters(
                    result.List,
                    completion),
                applicableToSpan: new SnapshotSpan(
                    snapshot,
                    new Span(
                        hostSpan.Start,
                        hostSpan.Length)),
                isCommittedAsSnippet: false,
                isPreselected: false);
            item.Properties.AddProperty(
                AkburaCompletionProperties.RoslynItem,
                new AkburaRoslynCompletionItemData(
                    result.State,
                    completion));
            items.Add(item);
        }

        if (!supplementalResult.IsEmpty &&
            IsValidSpan(
                snapshot,
                supplementalResult.ApplicableSpan))
        {
            foreach (var completion in supplementalResult.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (items.Any(item => string.Equals(
                        item.DisplayText,
                        completion.DisplayText,
                        StringComparison.Ordinal)))
                {
                    continue;
                }

                items.Add(CreateCoreCompletionItem(
                    snapshot,
                    supplementalResult.ApplicableSpan,
                    completion));
            }
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Roslyn returned " +
            $"{items.Count} mapped items.");

        return new CompletionContext(
            items.ToImmutable(),
            supplementalResult.IsEmpty
                ? ImmutableArray<CompletionFilterWithState>.Empty
                : CompletionFilters,
            isIncomplete: false);
    }

    private CompletionContext CreateCoreCompletionContext(
        ITextSnapshot snapshot,
        AkburaCompletionResult result,
        bool isIncomplete,
        CancellationToken cancellationToken)
    {
        if (!IsValidSpan(snapshot, result.ApplicableSpan))
        {
            Debug.WriteLine(
                $"[Akbura.Completion] Invalid applicable span: " +
                $"{result.ApplicableSpan}.");
            return CompletionContext.Empty;
        }

        var items = ImmutableArray.CreateBuilder<CompletionItem>(
            result.Items.Length);
        foreach (var completion in result.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CreateCoreCompletionItem(
                snapshot,
                result.ApplicableSpan,
                completion));
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Returning " +
            $"{items.Count} VS items.");
        return new CompletionContext(
            items.ToImmutable(),
            CompletionFilters,
            isIncomplete);
    }

    private CompletionItem CreateCoreCompletionItem(
        ITextSnapshot snapshot,
        Microsoft.CodeAnalysis.Text.TextSpan sourceSpan,
        AkburaCompletionItem completion)
    {
        var item = new CompletionItem(
            displayText: completion.DisplayText,
            source: this,
            icon: GetIcon(completion.Kind),
            filters: GetFilters(completion.Kind),
            suffix: completion.Suffix,
            insertText: completion.InsertText,
            sortText: completion.SortText,
            filterText: completion.FilterText,
            automationText: completion.DisplayText,
            attributeIcons: ImmutableArray<ImageElement>.Empty,
            commitCharacters: GetCommitCharacters(completion.Kind),
            applicableToSpan: new SnapshotSpan(
                snapshot,
                new Span(sourceSpan.Start, sourceSpan.Length)),
            isCommittedAsSnippet: false,
            isPreselected: false);
        item.Properties.AddProperty(
            AkburaCompletionProperties.CoreItem,
            completion);
        return item;
    }

    private static bool IsValidSpan(
        ITextSnapshot snapshot,
        Microsoft.CodeAnalysis.Text.TextSpan span)
    {
        return span.Start >= 0 && span.End <= snapshot.Length;
    }

    private static ImmutableArray<char> GetRoslynCommitCharacters(
        Microsoft.CodeAnalysis.Completion.CompletionList list,
        Microsoft.CodeAnalysis.Completion.CompletionItem item)
    {
        var rules = item.Rules.CommitCharacterRules;
        if (rules.IsDefaultOrEmpty)
        {
            return AddCompletionGestures(
                list.Rules.DefaultCommitCharacters);
        }

        var characters = new HashSet<char>(
            list.Rules.DefaultCommitCharacters);
        foreach (var rule in rules)
        {
            switch (rule.Kind)
            {
                case Microsoft.CodeAnalysis.Completion
                    .CharacterSetModificationKind.Add:
                    characters.UnionWith(rule.Characters);
                    break;

                case Microsoft.CodeAnalysis.Completion
                    .CharacterSetModificationKind.Remove:
                    characters.ExceptWith(rule.Characters);
                    break;

                case Microsoft.CodeAnalysis.Completion
                    .CharacterSetModificationKind.Replace:
                    characters.Clear();
                    characters.UnionWith(rule.Characters);
                    break;
            }
        }

        characters.Add('\t');
        characters.Add('\n');
        return characters
            .OrderBy(static character => character)
            .ToImmutableArray();
    }

    private static ImmutableArray<char> AddCompletionGestures(
        ImmutableArray<char> characters)
    {
        if (characters.Contains('\t') &&
            characters.Contains('\n'))
        {
            return characters;
        }

        var builder = characters.ToBuilder();
        if (!characters.Contains('\t'))
        {
            builder.Add('\t');
        }

        if (!characters.Contains('\n'))
        {
            builder.Add('\n');
        }

        return builder.ToImmutable();
    }

    private static CompletionContext
        CreateIncompleteCompletionContext()
    {
        return new CompletionContext(
            ImmutableArray<CompletionItem>.Empty,
            ImmutableArray<CompletionFilterWithState>.Empty,
            isIncomplete: true);
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

        if (trigger.Character == '{' &&
            AkburaMarkupEditingFacts
                .IsMarkupExtensionTypeCompletionPosition(
                    triggerLocation.Snapshot,
                    triggerLocation.Position))
        {
            return true;
        }

        if (char.IsLetterOrDigit(trigger.Character) ||
            trigger.Character == '_')
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

            AkburaCompletionKind.MarkupExtension =>
                MarkupExtensionCommitCharacters,

            AkburaCompletionKind.TailwindUtility =>
                TailwindUtilityCommitCharacters,

            AkburaCompletionKind.Keyword =>
                KeywordCommitCharacters,

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
            AkburaCompletionKind.MarkupExtension =>
                MarkupExtensionIcon,
            AkburaCompletionKind.TailwindUtility =>
                TailwindUtilityIcon,
            AkburaCompletionKind.Keyword => KeywordIcon,
            _ => PropertyIcon,
        };
    }

    private static ImageElement GetRoslynIcon(
        AkburaCSharpProjection projection,
        Microsoft.CodeAnalysis.Completion.CompletionItem item)
    {
        if (projection.IsStateName(item.DisplayText))
        {
            return StateIcon;
        }

        var tags = item.Tags;
        if (tags.Contains("ExtensionMethod"))
        {
            return CreateImageElement(
                KnownMonikers.ExtensionMethod,
                "Extension method");
        }

        if (tags.Contains("Method"))
        {
            return CreateImageElement(
                KnownMonikers.Method,
                "Method");
        }

        if (tags.Contains("Property"))
        {
            return PropertyIcon;
        }

        if (tags.Contains("Local"))
        {
            return CreateImageElement(
                KnownMonikers.LocalVariable,
                "Local variable");
        }

        if (tags.Contains("Parameter"))
        {
            return ParameterIcon;
        }

        if (tags.Contains("Namespace"))
        {
            return CreateImageElement(
                KnownMonikers.Namespace,
                "Namespace");
        }

        if (tags.Contains("Enum"))
        {
            return CreateImageElement(
                KnownMonikers.Enumeration,
                "Enum");
        }

        if (tags.Contains("Delegate"))
        {
            return CreateImageElement(
                KnownMonikers.Delegate,
                "Delegate");
        }

        if (tags.Contains("Constant"))
        {
            return CreateImageElement(
                KnownMonikers.Constant,
                "Constant");
        }

        if (tags.Contains("Keyword"))
        {
            return CreateImageElement(
                KnownMonikers.IntellisenseKeyword,
                "Keyword");
        }

        if (tags.Contains("Class"))
        {
            return CreateImageElement(
                KnownMonikers.Class,
                "Class");
        }

        if (tags.Contains("Structure"))
        {
            return CreateImageElement(
                KnownMonikers.Structure,
                "Structure");
        }

        if (tags.Contains("Interface"))
        {
            return CreateImageElement(
                KnownMonikers.Interface,
                "Interface");
        }

        if (tags.Contains("Field"))
        {
            return CreateImageElement(
                KnownMonikers.Field,
                "Field");
        }

        if (tags.Contains("Event"))
        {
            return CreateImageElement(
                KnownMonikers.Event,
                "Event");
        }

        return PropertyIcon;
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
            AkburaCompletionKind.MarkupExtension =>
                MarkupExtensionFilters,
            AkburaCompletionKind.TailwindUtility =>
                TailwindUtilityFilters,
            AkburaCompletionKind.Keyword => KeywordFilters,
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
