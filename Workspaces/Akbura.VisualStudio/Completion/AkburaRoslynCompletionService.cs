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
using RoslynCompletionTrigger =
    Microsoft.CodeAnalysis.Completion.CompletionTrigger;

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

    public async Task<AkburaRoslynCompletionResult?>
        GetCompletionsAsync(
            ITextSnapshot snapshot,
            AkburaSyntacticDocument syntacticDocument,
            AkburaDocumentContext? semanticContext,
            AkburaCSharpCompletionContext completionContext,
            CompletionTrigger trigger,
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
            return null;
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
            return null;
        }

        var roslynTrigger = trigger.Reason ==
                    CompletionTriggerReason.Insertion &&
                !char.IsLetterOrDigit(trigger.Character) &&
                trigger.Character != '_' &&
                !char.IsWhiteSpace(trigger.Character)
            ? RoslynCompletionTrigger.CreateInsertionTrigger(
                trigger.Character)
            : RoslynCompletionTrigger.Invoke;
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
            return null;
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Completion,
            $"Roslyn returned " +
            $"{completionList.ItemsList.Count} raw items.");

        var state = new AkburaRoslynCompletionSessionState(
            projected,
            completionService,
            projection);
        return new AkburaRoslynCompletionResult(
            state,
            completionList);
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
    public AkburaRoslynCompletionResult(
        AkburaRoslynCompletionSessionState state,
        RoslynCompletionList list)
    {
        State = state;
        List = list;
    }

    public AkburaRoslynCompletionSessionState State { get; }

    public RoslynCompletionList List { get; }
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
