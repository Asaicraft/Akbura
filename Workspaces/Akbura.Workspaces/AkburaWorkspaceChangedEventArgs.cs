namespace Akbura.Workspaces;

public enum AkburaWorkspaceChangeKind
{
    ProjectAdded,
    ProjectChanged,
    ProjectRemoved,
    DocumentOpened,
    DocumentChanged,
    DocumentClosed,
    DocumentRemoved,
}

public sealed class AkburaWorkspaceChangedEventArgs : EventArgs
{
    public AkburaWorkspaceChangedEventArgs(
        AkburaWorkspaceChangeKind kind,
        AkburaSolutionSnapshot oldSolution,
        AkburaSolutionSnapshot newSolution,
        AkburaProjectId projectId,
        AkburaDocumentId? documentId = null)
    {
        Kind = kind;
        OldSolution = oldSolution ??
            throw new ArgumentNullException(nameof(oldSolution));
        NewSolution = newSolution ??
            throw new ArgumentNullException(nameof(newSolution));
        ProjectId = projectId;
        DocumentId = documentId;
    }

    public AkburaWorkspaceChangeKind Kind { get; }

    public AkburaSolutionSnapshot OldSolution { get; }

    public AkburaSolutionSnapshot NewSolution { get; }

    public AkburaProjectId ProjectId { get; }

    public AkburaDocumentId? DocumentId { get; }
}
