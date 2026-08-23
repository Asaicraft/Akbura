using Akbura.Workspaces;
using Akbura.VisualStudio.CSharp;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using RoslynCompletionItem =
    Microsoft.CodeAnalysis.Completion.CompletionItem;
using RoslynCompletionList =
    Microsoft.CodeAnalysis.Completion.CompletionList;
using RoslynCompletionService =
    Microsoft.CodeAnalysis.Completion.CompletionService;

namespace Akbura.VisualStudio.Completion;

internal sealed class AkburaRoslynCompletionService
{
    private readonly AkburaProjectedCSharpDocumentService
        _projectedDocumentService;

    public AkburaRoslynCompletionService(
        AkburaProjectedCSharpDocumentService projectedDocumentService)
    {
        _projectedDocumentService = projectedDocumentService ??
            throw new ArgumentNullException(
                nameof(projectedDocumentService));
    }

    public async Task<AkburaRoslynCompletionResult>
        GetCompletionsAsync(
            ITextSnapshot snapshot,
            AkburaSyntacticDocument syntacticDocument,
            AkburaDocumentContext? semanticContext,
            AkburaCSharpCompletionContext completionContext,
            CompletionTrigger trigger,
            bool allowNonTrigger,
            CancellationToken cancellationToken)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (syntacticDocument == null)
        {
            throw new ArgumentNullException(nameof(syntacticDocument));
        }

        var embeddedContext = new AkburaEmbeddedCSharpContext(
            completionContext.Kind,
            completionContext.OwnerKind,
            completionContext.OwnerSpan,
            completionContext.HostSpan,
            completionContext.HostPosition);
        var projected = await _projectedDocumentService
            .GetProjectedDocumentAsync(
                snapshot,
                syntacticDocument,
                semanticContext,
                embeddedContext,
                cancellationToken)
            .ConfigureAwait(false);
        if (projected == null)
        {
            return AkburaRoslynCompletionResult.Unavailable;
        }

        cancellationToken.ThrowIfCancellationRequested();
        var document = projected.RoslynDocument;
        var projection = projected.Projection;
        var completionService =
            RoslynCompletionService.GetService(document);
        if (completionService == null)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Roslyn completion service was not found.");
            return AkburaRoslynCompletionResult.Unavailable;
        }

        var isExplicit = IsExplicitTrigger(trigger);
        var roslynTrigger =
            AkburaRoslynCompletionTriggerPolicy
                .CreateRoslynTrigger(
                    isExplicit,
                    allowNonTrigger,
                    trigger.Character);
        Microsoft.CodeAnalysis.Text.SourceText? sourceText = null;
        AkburaRoslynCompletionPreflight preflight;
        if (isExplicit)
        {
            preflight =
                AkburaRoslynCompletionTriggerPolicy.Evaluate(
                    isExplicit: true,
                    isIncompleteSession: false,
                    isSupportedInsertion: false,
                    shouldTriggerCompletion: false);
        }
        else if (allowNonTrigger)
        {
            preflight =
                AkburaRoslynCompletionTriggerPolicy.Evaluate(
                    isExplicit: false,
                    isIncompleteSession: true,
                    isSupportedInsertion: false,
                    shouldTriggerCompletion: false);
        }
        else
        {
            var isSupportedInsertion =
                AkburaRoslynCompletionTriggerPolicy
                    .IsSupportedInsertionCharacter(
                        trigger.Character);
            if (isSupportedInsertion)
            {
                sourceText = await document
                    .GetTextAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            preflight =
                AkburaRoslynCompletionTriggerPolicy.Evaluate(
                    isExplicit: false,
                    isIncompleteSession: false,
                    isSupportedInsertion,
                    isSupportedInsertion &&
                    completionService.ShouldTriggerCompletion(
                        sourceText!,
                        projection.ProjectedPosition,
                        roslynTrigger));
        }

        if (preflight is
            AkburaRoslynCompletionPreflight.UnsupportedInsertion or
            AkburaRoslynCompletionPreflight.RoslynSuppressed)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                $"Roslyn completion suppressed: " +
                $"preflight={preflight}, " +
                $"insertion='{trigger.Character}'.");
            return AkburaRoslynCompletionResult.Suppressed(
                preflight);
        }

        sourceText ??= await document
            .GetTextAsync(cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        var completionList = await completionService
            .GetCompletionsAsync(
                document,
                projection.ProjectedPosition,
                roslynTrigger,
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (completionList == null)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Completion,
                "Roslyn returned no completion list.");
            return AkburaRoslynCompletionResult.Suppressed(
                preflight);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var selection =
            AkburaRoslynCompletionItemSelector.Select(
                completionList,
                sourceText,
                projection.ProjectedPosition,
                isExplicit,
                cancellationToken);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Roslyn returned " +
            $"{selection.RawItemCount} raw items, " +
            $"selected {selection.Items.Length}, " +
            $"prefix='{selection.Prefix}', " +
            $"incomplete={selection.IsIncomplete}.");

        var state = new AkburaRoslynCompletionSessionState(
            projected,
            completionService,
            projection);
        return AkburaRoslynCompletionResult.Completed(
            state,
            completionList,
            selection,
            preflight);
    }

    private static bool IsExplicitTrigger(
        CompletionTrigger trigger)
    {
        return trigger.Reason is
            CompletionTriggerReason.Invoke or
            CompletionTriggerReason.InvokeAndCommitIfUnique or
            CompletionTriggerReason.InvokeMatchingType;
    }
}

internal sealed class AkburaRoslynCompletionSessionState
{
    public AkburaRoslynCompletionSessionState(
        AkburaProjectedCSharpDocument projectedDocument,
        RoslynCompletionService service,
        AkburaCSharpProjection projection)
    {
        ProjectedDocument = projectedDocument ??
            throw new ArgumentNullException(nameof(projectedDocument));
        Service = service ??
            throw new ArgumentNullException(nameof(service));
        Projection = projection ??
            throw new ArgumentNullException(nameof(projection));
    }

    public AkburaProjectedCSharpDocument ProjectedDocument { get; }

    public ITextSnapshot HostSnapshot => ProjectedDocument.HostSnapshot;

    public Microsoft.CodeAnalysis.Document Document =>
        ProjectedDocument.RoslynDocument;

    public RoslynCompletionService Service { get; }

    public AkburaCSharpProjection Projection { get; }
}

internal readonly struct AkburaRoslynCompletionResult
{
    private AkburaRoslynCompletionResult(
        AkburaRoslynCompletionResultKind kind,
        AkburaRoslynCompletionPreflight preflight,
        AkburaRoslynCompletionSessionState? state,
        RoslynCompletionList? list,
        AkburaRoslynCompletionSelection selection)
    {
        Kind = kind;
        Preflight = preflight;
        State = state;
        List = list;
        Selection = selection;
    }

    public static AkburaRoslynCompletionResult Unavailable { get; } =
        new(
            AkburaRoslynCompletionResultKind.Unavailable,
            AkburaRoslynCompletionPreflight.Unavailable,
            state: null,
            list: null,
            selection: default);

    public AkburaRoslynCompletionResultKind Kind { get; }

    public AkburaRoslynCompletionPreflight Preflight { get; }

    public AkburaRoslynCompletionSessionState? State { get; }

    public RoslynCompletionList? List { get; }

    public AkburaRoslynCompletionSelection Selection { get; }

    public static AkburaRoslynCompletionResult Suppressed(
        AkburaRoslynCompletionPreflight preflight)
    {
        return new AkburaRoslynCompletionResult(
            AkburaRoslynCompletionResultKind.Suppressed,
            preflight,
            state: null,
            list: null,
            selection: default);
    }

    public static AkburaRoslynCompletionResult Completed(
        AkburaRoslynCompletionSessionState state,
        RoslynCompletionList list,
        AkburaRoslynCompletionSelection selection,
        AkburaRoslynCompletionPreflight preflight)
    {
        return new AkburaRoslynCompletionResult(
            AkburaRoslynCompletionResultKind.Completed,
            preflight,
            state ?? throw new ArgumentNullException(nameof(state)),
            list ?? throw new ArgumentNullException(nameof(list)),
            selection);
    }
}

internal enum AkburaRoslynCompletionResultKind
{
    Unavailable,
    Suppressed,
    Completed,
}

internal sealed class AkburaRoslynCompletionItemData
{
    public AkburaRoslynCompletionItemData(
        AkburaRoslynCompletionSessionState state,
        RoslynCompletionItem item)
    {
        State = state ??
            throw new ArgumentNullException(nameof(state));
        Item = item ??
            throw new ArgumentNullException(nameof(item));
    }

    public AkburaRoslynCompletionSessionState State { get; }

    public RoslynCompletionItem Item { get; }
}
