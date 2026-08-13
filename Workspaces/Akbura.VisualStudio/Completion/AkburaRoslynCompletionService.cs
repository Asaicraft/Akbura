using Akbura.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Text;
using System.Diagnostics;
using System.Text;
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
    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    public AkburaRoslynCompletionService(
        AkburaVisualStudioWorkspace workspaceHost)
    {
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
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

        if (semanticContext == null)
        {
            Debug.WriteLine(
                "[Akbura.Completion] Roslyn projection unavailable: " +
                "no semantic project snapshot has been published yet.");
            return null;
        }

        if (!AkburaCSharpProjectionFactory.TryCreate(
                syntacticDocument,
                semanticContext,
                completionContext,
                out var projection,
                cancellationToken))
        {
            Debug.WriteLine(
                "[Akbura.Completion] Roslyn projection could not be " +
                "created for the current C# fragment.");
            return null;
        }

        cancellationToken.ThrowIfCancellationRequested();

        var project = _workspaceHost
            .FindRoslynProjectForDocument(
                syntacticDocument.FilePath);
        if (project == null)
        {
            Debug.WriteLine(
                "[Akbura.Completion] Roslyn projection project was not found.");
            return null;
        }

        var document = CreateProjectionDocument(
            project,
            projection,
            syntacticDocument.FilePath);
        var completionService =
            RoslynCompletionService.GetService(document);
        if (completionService == null)
        {
            Debug.WriteLine(
                "[Akbura.Completion] Roslyn completion service was not found.");
            return null;
        }

        var roslynTrigger = trigger.Reason ==
                CompletionTriggerReason.Insertion
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
            Debug.WriteLine(
                "[Akbura.Completion] Roslyn returned no completion list.");
            return null;
        }

        Debug.WriteLine(
            $"[Akbura.Completion] Roslyn returned " +
            $"{completionList.ItemsList.Count} raw items.");

        var state = new AkburaRoslynCompletionSessionState(
            snapshot,
            document,
            completionService,
            projection);
        return new AkburaRoslynCompletionResult(
            state,
            completionList);
    }

    private static Document CreateProjectionDocument(
        Project project,
        AkburaCSharpProjection projection,
        string akburaFilePath)
    {
        var name = Path.GetFileNameWithoutExtension(
                akburaFilePath) +
            ".AkburaCompletion.cs";
        var filePath = akburaFilePath +
            ".completion.cs";
        var text = SourceText.From(
            projection.Root.ToFullString(),
            Encoding.UTF8);

        var document = project.AddDocument(
            name,
            text,
            filePath: filePath);
        return document.WithSyntaxRoot(projection.Root);
    }
}

internal sealed class AkburaRoslynCompletionSessionState
{
    public AkburaRoslynCompletionSessionState(
        ITextSnapshot hostSnapshot,
        Document document,
        RoslynCompletionService service,
        AkburaCSharpProjection projection)
    {
        HostSnapshot = hostSnapshot ??
            throw new ArgumentNullException(nameof(hostSnapshot));
        Document = document ??
            throw new ArgumentNullException(nameof(document));
        Service = service ??
            throw new ArgumentNullException(nameof(service));
        Projection = projection ??
            throw new ArgumentNullException(nameof(projection));
    }

    public ITextSnapshot HostSnapshot { get; }

    public Document Document { get; }

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
