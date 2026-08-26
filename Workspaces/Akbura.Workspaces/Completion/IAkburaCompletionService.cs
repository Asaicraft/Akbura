namespace Akbura.Workspaces.Completion;

public interface IAkburaCompletionService
{
    AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default);

    AkburaCompletionChange GetCompletionChange(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        AkburaCompletionItem item,
        CancellationToken cancellationToken = default);
}
