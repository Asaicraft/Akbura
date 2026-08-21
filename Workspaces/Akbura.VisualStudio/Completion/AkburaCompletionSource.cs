using Akbura.Pools;
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
        AkcssModuleCommitCharacters = [';', '\t', '\n'];

    private static readonly ImmutableArray<char>
        TailwindUtilityCommitCharacters = [' ', '\t', '\n'];

    private static readonly ImmutableArray<char>
        AkcssValueCommitCharacters = [';', ' ', '\t', '\n'];

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

    private static readonly ImageElement AkcssModuleIcon =
        CreateImageElement(
            AkburaCompletionImageMonikers.AkcssModule,
            "AKCSS module");

    private static readonly ImageElement TailwindUtilityIcon =
        CreateImageElement(KnownMonikers.Property, "AKCSS utility");

    private static readonly ImageElement AkcssStyleIcon =
        CreateImageElement(KnownMonikers.Class, "AKCSS style");

    private static readonly ImageElement AkcssValueIcon =
        CreateImageElement(KnownMonikers.Constant, "AKCSS value");

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

    private static readonly CompletionFilter AkcssModuleFilter =
        new("AKCSS modules", "O", AkcssModuleIcon);

    private static readonly CompletionFilter TailwindUtilityFilter =
        new("AKCSS utilities", "U", TailwindUtilityIcon);

    private static readonly CompletionFilter AkcssStyleFilter =
        new("AKCSS styles", "S", AkcssStyleIcon);

    private static readonly CompletionFilter AkcssValueFilter =
        new("Values", "V", AkcssValueIcon);

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
        AkcssModuleFilters = [AkcssModuleFilter];

    private static readonly ImmutableArray<CompletionFilter>
        TailwindUtilityFilters = [TailwindUtilityFilter];

    private static readonly ImmutableArray<CompletionFilter>
        AkcssStyleFilters = [AkcssStyleFilter];

    private static readonly ImmutableArray<CompletionFilter>
        AkcssValueFilters = [AkcssValueFilter];

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
            new(AkcssModuleFilter, isAvailable: true),
            new(AkcssStyleFilter, isAvailable: true),
            new(TailwindUtilityFilter, isAvailable: true),
            new(AkcssValueFilter, isAvailable: true),
            new(KeywordFilter, isAvailable: true),
        ];

    private readonly ITextBuffer _buffer;

    private readonly AkburaEditorDocumentKind _documentKind;

    private readonly AkburaTextBufferContext _bufferContext;

    private readonly IAkburaCompletionService _completionService;

    private readonly AkburaParserService _parserService;

    private readonly AkburaRoslynCompletionService
        _roslynCompletionService;

    public AkburaCompletionSource(
        ITextBuffer buffer,
        AkburaEditorDocumentKind documentKind,
        AkburaTextBufferContext bufferContext,
        IAkburaCompletionService completionService,
        AkburaParserService parserService,
        AkburaRoslynCompletionService roslynCompletionService)
    {
        _buffer = buffer ??
            throw new ArgumentNullException(nameof(buffer));
        _documentKind = documentKind;
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
        if (_documentKind == AkburaEditorDocumentKind.Unknown ||
            !ReferenceEquals(
                triggerLocation.Snapshot.TextBuffer,
                _buffer) ||
            !ShouldParticipate(
                _documentKind,
                trigger,
                triggerLocation))
        {
            return CompletionStartData
                .DoesNotParticipateInCompletion;
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Participating: " +
            $"reason={trigger.Reason}, " +
            $"character='{trigger.Character}', " +
            $"position={triggerLocation.Position}, " +
            $"snapshot={triggerLocation.Snapshot.Version.VersionNumber}.");

        var snapshot = triggerLocation.Snapshot;
        var isUsingDirectiveName =
            AkburaMarkupEditingFacts.IsUsingDirectiveNamePosition(
                snapshot,
                triggerLocation.Position,
                _documentKind == AkburaEditorDocumentKind.Akcss);
        var start = triggerLocation.Position;
        while (start > 0 &&
               AkburaMarkupEditingFacts
                   .IsCompletionNameCharacter(
                       snapshot[start - 1]))
        {
            if (isUsingDirectiveName &&
                snapshot[start - 1] is '.' or ':')
            {
                break;
            }

            start--;
        }

        if (_documentKind == AkburaEditorDocumentKind.Akcss &&
            start > 0 &&
            snapshot[start - 1] == '@')
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

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Context requested: " +
            $"position={position}, " +
            $"snapshot={snapshot.Version.VersionNumber}.");

        var stageTimer = Stopwatch.StartNew();
        var syntacticDocument =
            await _parserService
                .GetSyntacticDocumentAsync(snapshot)
                .ConfigureAwait(false);
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Syntax document", stageTimer.Elapsed);

        cancellationToken.ThrowIfCancellationRequested();

        stageTimer.Restart();
        var isAkcss = _documentKind ==
            AkburaEditorDocumentKind.Akcss;
        var syntaxContext = isAkcss
            ? default
            : syntacticDocument.GetCompletionContext(
                position,
                cancellationToken);
        var akcssContext = isAkcss
            ? syntacticDocument.GetAkcssCompletionContext(
                position,
                cancellationToken)
            : default;
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Syntax context", stageTimer.Elapsed);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Syntax context: " +
            $"kind={(isAkcss ? akcssContext.Kind.ToString() : syntaxContext.Kind.ToString())}, " +
            $"prefix='{(isAkcss ? akcssContext.Prefix : syntaxContext.Prefix)}'.");

        stageTimer.Restart();
        if (syntacticDocument.TryGetCSharpCompletionContext(
                position,
                out var csharpContext,
                cancellationToken))
        {
            var semanticContext = GetLatestSemanticContext(
                snapshot);
            var supplementalResult = isAkcss
                ? akcssContext.Kind is
                    AkcssCompletionContextKind.PropertyValue or
                    AkcssCompletionContextKind.AttachedPropertyExpression or
                    AkcssCompletionContextKind.AkcssModuleName
                    ? _completionService.GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position,
                        cancellationToken)
                    : default
                : csharpContext.Kind ==
                    AkburaCSharpCompletionContextKind.UsingDirectiveName
                    ? _completionService.GetCompletions(
                        syntacticDocument,
                        semanticContext,
                        position,
                        cancellationToken)
                    : syntaxContext.Kind ==
                        AkburaCompletionContextKind.TopLevel
                        ? _completionService.GetCompletions(
                            syntacticDocument,
                            semanticContext: null,
                            position,
                            cancellationToken)
                        : default;
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "C# semantic context",
                stageTimer.Elapsed);

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"C# context: kind={csharpContext.Kind}, " +
                $"hostSpan={csharpContext.HostSpan}, " +
                $"position={csharpContext.HostPosition}.");

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
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Roslyn completion",
                stageTimer.Elapsed);

            stageTimer.Restart();
            var completionContext = csharpResult is { } roslynResult
                ? CreateRoslynCompletionContext(
                    roslynResult,
                    supplementalResult,
                    csharpContext.Kind,
                    cancellationToken)
                : supplementalResult.IsEmpty
                    ? CreateIncompleteCompletionContext()
                    : CreateCoreCompletionContext(
                        snapshot,
                        supplementalResult,
                        isIncomplete: true,
                        cancellationToken);
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Map completion items",
                stageTimer.Elapsed);
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Total",
                totalTimer.Elapsed);

            return completionContext;
        }

        if (isAkcss
                ? akcssContext.IsDefault
                : syntaxContext.IsDefault)
        {
            return CompletionContext.Empty;
        }

        stageTimer.Restart();
        var documentContext = GetLatestSemanticContext(
            snapshot);
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Semantic context", stageTimer.Elapsed);

        cancellationToken.ThrowIfCancellationRequested();

        stageTimer.Restart();
        var result =
            _completionService.GetCompletions(
                syntacticDocument,
                documentContext,
                position,
                cancellationToken);
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Core completion", stageTimer.Elapsed);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Core returned " +
            $"{result.Items.Length} items.");
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Total", totalTimer.Elapsed);
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
        AkburaCSharpCompletionContextKind csharpContextKind,
        CancellationToken cancellationToken)
    {
        var snapshot = result.State.HostSnapshot;
        using var items = ImmutableArrayBuilder<CompletionItem>.Rent(
            result.List.ItemsList.Count +
            supplementalResult.Items.Length);

        foreach (var completion in result.List.ItemsList)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!result.State.Projection.TryMapToHost(
                    completion.Span,
                    out var hostSpan) ||
                hostSpan.Start < 0 ||
                hostSpan.End > snapshot.Length)
            {
                continue;
            }

            var suffix = string.IsNullOrWhiteSpace(
                    completion.InlineDescription)
                ? completion.DisplayTextSuffix
                : completion.DisplayTextSuffix +
                  "  " +
                  completion.InlineDescription;
            var item = new CompletionItem(
                displayText: completion.DisplayText,
                source: this,
                icon: GetRoslynIcon(
                    result.State.Projection,
                    completion),
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix,
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

        var roslynItemCount = items.Count;

        if (!supplementalResult.IsEmpty &&
            IsValidSpan(
                snapshot,
                supplementalResult.ApplicableSpan))
        {
            foreach (var completion in supplementalResult.Items)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ContainsDisplayText(
                        items.WrittenSpan,
                        completion.DisplayText))
                {
                    continue;
                }

                items.Add(CreateCoreCompletionItem(
                    snapshot,
                    supplementalResult.ApplicableSpan,
                    completion));
            }
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Combined completion contains {items.Count} mapped items " +
            $"(Roslyn {roslynItemCount}, supplemental " +
            $"{supplementalResult.Items.Length}).");

        return new CompletionContext(
            items.ToImmutable(),
            ImmutableArray<CompletionFilterWithState>.Empty,
            isIncomplete: csharpContextKind ==
                AkburaCSharpCompletionContextKind.UsingDirectiveName);
    }

    private CompletionContext CreateCoreCompletionContext(
        ITextSnapshot snapshot,
        AkburaCompletionResult result,
        bool isIncomplete,
        CancellationToken cancellationToken)
    {
        if (!IsValidSpan(snapshot, result.ApplicableSpan))
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Invalid applicable span: " +
                $"{result.ApplicableSpan}.");
            return CompletionContext.Empty;
        }

        using var items = ImmutableArrayBuilder<CompletionItem>.Rent(
            result.Items.Length);
        foreach (var completion in result.Items)
        {
            cancellationToken.ThrowIfCancellationRequested();
            items.Add(CreateCoreCompletionItem(
                snapshot,
                result.ApplicableSpan,
                completion));
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Returning " +
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

    private static bool ContainsDisplayText(
        ReadOnlySpan<CompletionItem> items,
        string displayText)
    {
        foreach (var item in items)
        {
            if (string.Equals(
                    item.DisplayText,
                    displayText,
                    StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
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

        using var builder =
            ImmutableArrayBuilder<char>.Rent(characters.Length + 2);
        builder.AddRange(characters.AsSpan());
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
        AkburaEditorDocumentKind documentKind,
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

        if (documentKind == AkburaEditorDocumentKind.Akcss)
        {
            return trigger.Character is
                    '@' or ' ' or '.' or ':' or '-' ||
                char.IsLetterOrDigit(trigger.Character) ||
                trigger.Character == '_';
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

            AkburaCompletionKind.AkcssModule =>
                AkcssModuleCommitCharacters,

            AkburaCompletionKind.AkcssStyle =>
                TailwindUtilityCommitCharacters,

            AkburaCompletionKind.AkcssValue or
            AkburaCompletionKind.AkcssColor =>
                AkcssValueCommitCharacters,

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
            AkburaCompletionKind.AkcssModule =>
                AkcssModuleIcon,
            AkburaCompletionKind.AkcssStyle =>
                AkcssStyleIcon,
            AkburaCompletionKind.AkcssValue or
            AkburaCompletionKind.AkcssColor =>
                AkcssValueIcon,
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
            AkburaCompletionKind.AkcssModule =>
                AkcssModuleFilters,
            AkburaCompletionKind.AkcssStyle =>
                AkcssStyleFilters,
            AkburaCompletionKind.AkcssValue or
            AkburaCompletionKind.AkcssColor =>
                AkcssValueFilters,
            AkburaCompletionKind.TailwindUtility =>
                TailwindUtilityFilters,
            AkburaCompletionKind.Keyword => KeywordFilters,
            _ => PropertyFilters,
        };
    }

}
