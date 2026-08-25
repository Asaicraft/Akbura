namespace Akbura.Workspaces.Projects;

public enum ProjectContextChangeKind
{
    ProjectReloaded,
    CompilationChanged,
    ReferencesChanged,
    ProjectRemoved,
}

public sealed class ProjectContextChangedEventArgs : EventArgs
{
    public ProjectContextChangedEventArgs(
        ProjectContextChangeKind kind,
        ProjectContext? oldContext,
        ProjectContext? newContext)
    {
        Kind = kind;
        OldContext = oldContext;
        NewContext = newContext;
    }

    public ProjectContextChangeKind Kind { get; }

    public ProjectContext? OldContext { get; }

    public ProjectContext? NewContext { get; }
}
