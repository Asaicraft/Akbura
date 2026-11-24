using Microsoft.CodeAnalysis;
using System.Reflection;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Configures an existing workspace project without owning an independent project snapshot.
/// </summary>
/// <remarks>
/// Mutations are synchronous and publish through the owning workspace. Schedule expensive
/// reference preparation and source parsing using the host's scheduling policy.
/// Compiler references provide design-time visibility; they do not install a preview runtime.
/// </remarks>
public sealed class AkburaProject
{
    internal AkburaProject(AkburaWorkspace workspace, AkburaProjectId id)
    {
        Workspace = workspace;
        Id = id;
    }

    public AkburaWorkspace Workspace
    {
        get;
    }

    public AkburaProjectId Id
    {
        get;
    }

    /// <summary>
    /// Gets the currently published immutable project information.
    /// </summary>
    public ProjectContext Context => Workspace.GetProjectContext(this);

    /// <summary>
    /// Gets or configures the explicit metadata catalog used to resolve assembly dependencies.
    /// </summary>
    public IAkburaReferenceResolver? ReferenceResolver
    {
        get => Workspace.GetProjectResolver(this);
        set => Update(update => update.ReferenceResolver = value);
    }

    /// <summary>
    /// Adds real metadata for a loaded assembly and resolves its dependencies from the staged set or catalog.
    /// </summary>
    public void AddAssembly(Assembly assembly) => Update(update => update.AddAssembly(assembly));

    /// <summary>
    /// Adds the assembly containing <typeparamref name="T"/> without constructing or initializing that type.
    /// </summary>
    public void AddAssemblyOf<T>() => AddAssembly(typeof(T).Assembly);

    /// <summary>
    /// Copies a complete managed PE image into compiler metadata without loading executable code.
    /// </summary>
    public void AddAssemblyImage(byte[] assemblyBytes) => Update(update => update.AddAssemblyImage(assemblyBytes));

    /// <summary>
    /// Adds a caller-owned Roslyn reference, preserving aliases and embedding properties.
    /// </summary>
    public void AddReference(MetadataReference reference) => Update(update => update.AddReference(reference));

    /// <summary>
    /// Adds a C# source tree identified by a relative, case-sensitive virtual path.
    /// </summary>
    public void AddSource(string path, string source) => Update(update => update.AddSource(path, source));

    /// <summary>
    /// Updates one existing source tree while preserving its parsing options.
    /// </summary>
    public void UpdateSource(string path, string source) => Update(update => update.UpdateSource(path, source));

    /// <summary>
    /// Removes a C# source tree and returns whether that virtual path existed.
    /// </summary>
    public bool RemoveSource(string path)
    {
        var removed = false;
        Update(update => update.RemoveSource(path, result => removed = result));
        return removed;
    }

    /// <summary>
    /// Stages and validates one atomic configuration change. Failure publishes none of the staged operations.
    /// </summary>
    public void Update(Action<AkburaProjectUpdate> configure) => Workspace.UpdateProject(this, configure);
}
