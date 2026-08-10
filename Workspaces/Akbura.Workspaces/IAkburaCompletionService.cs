namespace Akbura.Workspaces;

public interface IAkburaCompletionService
{
    AkburaCompletionResult GetCompletions(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default);
}
