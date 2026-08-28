namespace Akbura.Workspaces.SignatureHelp;

public interface IAkburaSignatureHelpService
{
    AkburaSignatureHelp? GetSignatureHelp(
        AkburaSyntacticDocument document,
        AkburaDocumentContext? semanticContext,
        int position,
        CancellationToken cancellationToken = default);
}