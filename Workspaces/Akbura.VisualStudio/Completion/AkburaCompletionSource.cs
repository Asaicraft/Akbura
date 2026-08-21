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
using Microsoft.VisualStudio.Text.Editor;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Akbura.VisualStudio.Completion;

internal sealed class AkburaCompletionSource :
    IAsyncCompletionSource,
    IDisposable
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

    private static readonly ImageElement ExtensionMethodIcon =
        CreateImageElement(
            KnownMonikers.ExtensionMethod,
            "Extension method");

    private static readonly ImageElement MethodIcon =
        CreateImageElement(KnownMonikers.Method, "Method");

    private static readonly ImageElement LocalVariableIcon =
        CreateImageElement(
            KnownMonikers.LocalVariable,
            "Local variable");

    private static readonly ImageElement NamespaceIcon =
        CreateImageElement(KnownMonikers.Namespace, "Namespace");

    private static readonly ImageElement EnumIcon =
        CreateImageElement(KnownMonikers.Enumeration, "Enum");

    private static readonly ImageElement DelegateIcon =
        CreateImageElement(KnownMonikers.Delegate, "Delegate");

    private static readonly ImageElement ConstantIcon =
        CreateImageElement(KnownMonikers.Constant, "Constant");

    private static readonly ImageElement ClassIcon =
        CreateImageElement(KnownMonikers.Class, "Class");

    private static readonly ImageElement StructureIcon =
        CreateImageElement(KnownMonikers.Structure, "Structure");

    private static readonly ImageElement InterfaceIcon =
        CreateImageElement(KnownMonikers.Interface, "Interface");

    private static readonly ImageElement FieldIcon =
        CreateImageElement(KnownMonikers.Field, "Field");

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

    private readonly ITextView _textView;

    private readonly ITextBuffer _buffer;

    private readonly AkburaEditorDocumentKind _documentKind;

    private readonly AkburaTextBufferContext _bufferContext;

    private readonly IAkburaCompletionService _completionService;

    private readonly AkburaParserService _parserService;

    private readonly AkburaRoslynCompletionService
        _roslynCompletionService;

    private readonly AkburaLatestRequestCancellation
        _completionRequests = new();

    private readonly ConditionalWeakTable<
        IAsyncCompletionSession,
        CompletionSessionState> _sessionStates = new();

    private int _completionSnapshotVersion = -1;

    private int _disposeState;

    public AkburaCompletionSource(
        ITextView textView,
        AkburaEditorDocumentKind documentKind,
        AkburaTextBufferContext bufferContext,
        IAkburaCompletionService completionService,
        AkburaParserService parserService,
        AkburaRoslynCompletionService roslynCompletionService)
    {
        _textView = textView ??
            throw new ArgumentNullException(nameof(textView));
        _buffer = textView.TextBuffer;
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

        _buffer.Changed += OnBufferChanged;
        _textView.Closed += OnTextViewClosed;
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
        var snapshot = triggerLocation.Snapshot;
        using var request =
            _completionRequests.Begin(cancellationToken);
        var requestToken = request.Token;
        Volatile.Write(
            ref _completionSnapshotVersion,
            snapshot.Version.VersionNumber);
#if DEBUG
        var totalTimer = Stopwatch.StartNew();
        var stageTimer = Stopwatch.StartNew();
        var outcome = "completed";
        var roslynStatus = "not-requested";
        var preflight = "not-requested";
        var truncated = false;
        var rawItemCount = 0;
        var selectedItemCount = 0;
        var mappedItemCount = 0;
#endif

        try
        {
            EnsureCurrent(request, snapshot);

            var position = triggerLocation.Position;

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Context requested: " +
                $"position={position}, " +
                $"snapshot={snapshot.Version.VersionNumber}.");

            var syntacticDocument =
                await _parserService
                    .GetSyntacticDocumentAsync(snapshot)
                    .ConfigureAwait(false);
#if DEBUG
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Syntax document",
                stageTimer.Elapsed);
#endif

            EnsureCurrent(request, snapshot);

#if DEBUG
            stageTimer.Restart();
#endif
            var isAkcss = _documentKind ==
                AkburaEditorDocumentKind.Akcss;
            var syntaxContext = isAkcss
                ? default
                : syntacticDocument.GetCompletionContext(
                    position,
                    requestToken);
            var akcssContext = isAkcss
                ? syntacticDocument.GetAkcssCompletionContext(
                    position,
                    requestToken)
                : default;
#if DEBUG
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Syntax context",
                stageTimer.Elapsed);
#endif

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Syntax context: " +
                $"kind={(isAkcss ? akcssContext.Kind.ToString() : syntaxContext.Kind.ToString())}, " +
                $"prefix='{(isAkcss ? akcssContext.Prefix : syntaxContext.Prefix)}'.");

#if DEBUG
            stageTimer.Restart();
#endif
            if (syntacticDocument.TryGetCSharpCompletionContext(
                    position,
                    out var csharpContext,
                    requestToken))
            {
                var sessionState = _sessionStates.GetValue(
                    session,
                    static _ => new CompletionSessionState());
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
                            requestToken)
                        : default
                    : csharpContext.Kind ==
                        AkburaCSharpCompletionContextKind.UsingDirectiveName
                        ? _completionService.GetCompletions(
                            syntacticDocument,
                            semanticContext,
                            position,
                            requestToken)
                        : syntaxContext.Kind ==
                            AkburaCompletionContextKind.TopLevel
                            ? _completionService.GetCompletions(
                                syntacticDocument,
                                semanticContext: null,
                                position,
                                requestToken)
                            : default;
#if DEBUG
                AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                    "C# semantic context",
                    stageTimer.Elapsed);
#endif

                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    $"C# context: kind={csharpContext.Kind}, " +
                    $"hostSpan={csharpContext.HostSpan}, " +
                    $"position={csharpContext.HostPosition}.");

                EnsureCurrent(request, snapshot);

#if DEBUG
                stageTimer.Restart();
#endif
                var csharpResult = await _roslynCompletionService
                    .GetCompletionsAsync(
                        snapshot,
                        syntacticDocument,
                        semanticContext,
                        csharpContext,
                        trigger,
                        sessionState.AllowNonTrigger,
                        requestToken)
                    .ConfigureAwait(false);
#if DEBUG
                roslynStatus = csharpResult.Kind.ToString();
                preflight = csharpResult.Preflight.ToString();
                AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                    "Roslyn completion",
                    stageTimer.Elapsed);
#endif

                EnsureCurrent(request, snapshot);

#if DEBUG
                stageTimer.Restart();
#endif
                CompletionContext completionContext;
                switch (csharpResult.Kind)
                {
                    case AkburaRoslynCompletionResultKind.Completed:
                    {
                        var isIncomplete =
                            csharpResult.Selection.IsIncomplete ||
                            csharpContext.Kind ==
                                AkburaCSharpCompletionContextKind
                                    .UsingDirectiveName;
                        completionContext =
                            CreateRoslynCompletionContext(
                                csharpResult,
                                supplementalResult,
                                isIncomplete,
                                request,
                                snapshot,
                                out var mappedCount);
                        sessionState.SetAllowNonTrigger(isIncomplete);
#if DEBUG
                        rawItemCount =
                            csharpResult.Selection.RawItemCount;
                        selectedItemCount =
                            csharpResult.Selection.Items.Length;
                        mappedItemCount = mappedCount;
                        truncated =
                            csharpResult.Selection.IsIncomplete;
#endif
                        break;
                    }

                    case AkburaRoslynCompletionResultKind.Suppressed:
                        sessionState.SetAllowNonTrigger(
                            supplementalResult.IsIncomplete);
                        completionContext =
                            supplementalResult.IsEmpty
                                ? CompletionContext.Empty
                                : CreateCoreCompletionContext(
                                    snapshot,
                                    supplementalResult,
                                    supplementalResult.IsIncomplete,
                                    requestToken);
                        break;

                    default:
                        sessionState.SetAllowNonTrigger(true);
                        completionContext =
                            supplementalResult.IsEmpty
                                ? CreateIncompleteCompletionContext()
                                : CreateCoreCompletionContext(
                                    snapshot,
                                    supplementalResult,
                                    isIncomplete: true,
                                    requestToken);
                        break;
                }

                EnsureCurrent(request, snapshot);
#if DEBUG
                AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                    "Map completion items",
                    stageTimer.Elapsed);
#endif

                return completionContext;
            }

            if (isAkcss
                    ? akcssContext.IsDefault
                    : syntaxContext.IsDefault)
            {
                return CompletionContext.Empty;
            }

#if DEBUG
            stageTimer.Restart();
#endif
            var documentContext = GetLatestSemanticContext(
                snapshot);
#if DEBUG
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Semantic context",
                stageTimer.Elapsed);
#endif

            EnsureCurrent(request, snapshot);

#if DEBUG
            stageTimer.Restart();
#endif
            var result =
                _completionService.GetCompletions(
                    syntacticDocument,
                    documentContext,
                    position,
                    requestToken);
#if DEBUG
            AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
                "Core completion",
                stageTimer.Elapsed);
#endif

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Core returned " +
                $"{result.Items.Length} items.");

            EnsureCurrent(request, snapshot);
            return CreateCoreCompletionContext(
                snapshot,
                result,
                result.IsIncomplete,
                requestToken);
        }
        catch (OperationCanceledException)
        {
#if DEBUG
            outcome = "canceled";
#endif
            throw;
        }
        catch
        {
#if DEBUG
            outcome = "failed";
#endif
            throw;
        }
        finally
        {
#if DEBUG
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.CompletionPerformance,
                $"Completion request: " +
                $"snapshot={snapshot.Version.VersionNumber}, " +
                $"outcome={outcome}, " +
                $"roslyn={roslynStatus}, " +
                $"preflight={preflight}, " +
                $"truncated={truncated}, " +
                $"raw={rawItemCount}, " +
                $"selected={selectedItemCount}, " +
                $"mapped={mappedItemCount}, " +
                $"elapsed={totalTimer.Elapsed.TotalMilliseconds:F2} ms.");
#endif
        }
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
        bool isIncomplete,
        AkburaLatestRequest request,
        ITextSnapshot snapshot,
        out int mappedItemCount)
    {
        var state = result.State ??
            throw new InvalidOperationException(
                "Completed Roslyn result has no session state.");
        var list = result.List ??
            throw new InvalidOperationException(
                "Completed Roslyn result has no completion list.");
        var selectedItems = result.Selection.Items;
        using var items = ImmutableArrayBuilder<CompletionItem>.Rent(
            selectedItems.Length +
            supplementalResult.Items.Length);
        var defaultCommitCharacters = AddCompletionGestures(
            list.Rules.DefaultCommitCharacters);
        Dictionary<
            Microsoft.CodeAnalysis.Completion.CompletionItemRules,
            ImmutableArray<char>>? customCommitCharacters = null;
        var mappedSpans = new Dictionary<
            Microsoft.CodeAnalysis.Text.TextSpan,
            SnapshotSpan?>();

        foreach (var completion in selectedItems)
        {
            EnsureCurrent(request, snapshot);

            if (!mappedSpans.TryGetValue(
                    completion.Span,
                    out var applicableSpan))
            {
                applicableSpan = state.Projection.TryMapToHost(
                        completion.Span,
                        out var hostSpan) &&
                    hostSpan.Start >= 0 &&
                    hostSpan.End <= snapshot.Length
                        ? new SnapshotSpan(
                            snapshot,
                            new Span(
                                hostSpan.Start,
                                hostSpan.Length))
                        : null;
                mappedSpans.Add(
                    completion.Span,
                    applicableSpan);
            }

            if (applicableSpan == null)
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
                    state.Projection,
                    completion),
                filters: ImmutableArray<CompletionFilter>.Empty,
                suffix,
                insertText: completion.DisplayText,
                sortText: completion.SortText,
                filterText: completion.FilterText,
                automationText: completion.DisplayText,
                attributeIcons: ImmutableArray<ImageElement>.Empty,
                commitCharacters: GetRoslynCommitCharacters(
                    list,
                    completion,
                    defaultCommitCharacters,
                    ref customCommitCharacters),
                applicableToSpan: applicableSpan.Value,
                isCommittedAsSnippet: false,
                isPreselected: false);
            item.Properties.AddProperty(
                AkburaCompletionProperties.RoslynItem,
                new AkburaRoslynCompletionItemData(
                    state,
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
                EnsureCurrent(request, snapshot);
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

        mappedItemCount = items.Count;
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Combined completion contains {items.Count} mapped items " +
            $"(Roslyn {roslynItemCount}, supplemental " +
            $"{supplementalResult.Items.Length}).");

        return new CompletionContext(
            items.ToImmutable(),
            ImmutableArray<CompletionFilterWithState>.Empty,
            isIncomplete);
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
        Microsoft.CodeAnalysis.Completion.CompletionItem item,
        ImmutableArray<char> defaultCommitCharacters,
        ref Dictionary<
            Microsoft.CodeAnalysis.Completion.CompletionItemRules,
            ImmutableArray<char>>? cache)
    {
        var itemRules = item.Rules;
        var rules = itemRules.CommitCharacterRules;
        if (rules.IsDefaultOrEmpty)
        {
            return defaultCommitCharacters;
        }

        cache ??= [];
        if (cache.TryGetValue(
                itemRules,
                out var cachedCharacters))
        {
            return cachedCharacters;
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
        var result = characters
            .OrderBy(static character => character)
            .ToImmutableArray();
        cache.Add(itemRules, result);
        return result;
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

    private void EnsureCurrent(
        AkburaLatestRequest request,
        ITextSnapshot snapshot)
    {
        var cancellationToken = request.Token;
        cancellationToken.ThrowIfCancellationRequested();

        if (request.IsCurrent &&
            ReferenceEquals(
                snapshot,
                _buffer.CurrentSnapshot))
        {
            return;
        }

        request.Cancel();
        throw new OperationCanceledException(
            cancellationToken);
    }

    private void OnBufferChanged(
        object? sender,
        TextContentChangedEventArgs eventArgs)
    {
        if (Volatile.Read(
                ref _completionSnapshotVersion) !=
            eventArgs.After.Version.VersionNumber)
        {
            _completionRequests.CancelCurrent();
        }
    }

    private void OnTextViewClosed(
        object? sender,
        EventArgs eventArgs)
    {
        Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref _disposeState,
                1) != 0)
        {
            return;
        }

        _buffer.Changed -=
            OnBufferChanged;
        _textView.Closed -= OnTextViewClosed;
        _completionRequests.Dispose();
    }

    private sealed class CompletionSessionState
    {
        private int _allowNonTrigger;

        public bool AllowNonTrigger =>
            Volatile.Read(ref _allowNonTrigger) != 0;

        public void SetAllowNonTrigger(
            bool value)
        {
            Volatile.Write(
                ref _allowNonTrigger,
                value ? 1 : 0);
        }
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
            return ExtensionMethodIcon;
        }

        if (tags.Contains("Method"))
        {
            return MethodIcon;
        }

        if (tags.Contains("Property"))
        {
            return PropertyIcon;
        }

        if (tags.Contains("Local"))
        {
            return LocalVariableIcon;
        }

        if (tags.Contains("Parameter"))
        {
            return ParameterIcon;
        }

        if (tags.Contains("Namespace"))
        {
            return NamespaceIcon;
        }

        if (tags.Contains("Enum"))
        {
            return EnumIcon;
        }

        if (tags.Contains("Delegate"))
        {
            return DelegateIcon;
        }

        if (tags.Contains("Constant"))
        {
            return ConstantIcon;
        }

        if (tags.Contains("Keyword"))
        {
            return KeywordIcon;
        }

        if (tags.Contains("Class"))
        {
            return ClassIcon;
        }

        if (tags.Contains("Structure"))
        {
            return StructureIcon;
        }

        if (tags.Contains("Interface"))
        {
            return InterfaceIcon;
        }

        if (tags.Contains("Field"))
        {
            return FieldIcon;
        }

        if (tags.Contains("Event"))
        {
            return EventIcon;
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
