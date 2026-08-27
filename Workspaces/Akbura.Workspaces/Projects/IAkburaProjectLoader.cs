using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Loads complete Akbura project inputs for standalone hosts.
/// </summary>
public interface IAkburaProjectLoader : IDisposable
{
    event EventHandler<ProjectContextChangedEventArgs>? Changed;

    Task<AkburaLoadedProject> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken);

    Task<ImmutableArray<AkburaLoadedProject>> LoadSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken);
}