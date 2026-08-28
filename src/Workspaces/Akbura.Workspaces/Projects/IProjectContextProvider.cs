using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Host-specific project loader. Akbura.Workspaces itself does not know
/// whether the context came from MSBuild, Visual Studio, Rider, or tests.
/// </summary>
public interface IProjectContextProvider : IDisposable
{
    event EventHandler<ProjectContextChangedEventArgs>? Changed;

    Task<ProjectContext> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken);

    Task<ImmutableArray<ProjectContext>> OpenSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken);
}
