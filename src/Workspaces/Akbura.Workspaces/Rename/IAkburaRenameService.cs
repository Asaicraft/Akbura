namespace Akbura.Workspaces.Rename;

public interface IAkburaRenameService
{
    AkburaRenameInfo GetRenameInfo(
        AkburaDocumentContext context,
        int position,
        CancellationToken cancellationToken = default);

    AkburaWorkspaceEdit GetRenameChanges(
        AkburaDocumentContext context,
        int position,
        string newName,
        CancellationToken cancellationToken = default);
}
