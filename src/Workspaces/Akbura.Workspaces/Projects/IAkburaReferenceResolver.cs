using Microsoft.CodeAnalysis;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Resolves compiler metadata from an explicitly prepared catalog. Resolution is synchronous
/// and must not load executable assemblies or perform network requests.
/// </summary>
/// <remarks>
/// Presets may request an unsigned, version-zero identity to discover an assembly by name.
/// Dependency and loaded-assembly requests carry their actual identity and are validated by the workspace.
/// </remarks>
public interface IAkburaReferenceResolver
{
    PortableExecutableReference? Resolve(AssemblyIdentity identity);
}
