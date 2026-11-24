using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// An immutable catalog of real compiler metadata, prepared from explicit files or complete PE images.
/// </summary>
/// <remarks>
/// Input images are copied. Roslyn releases their pinned memory when references, catalogs and
/// snapshots become unreachable. Disposing a workspace does not invalidate snapshots still in use.
/// Preparing a large catalog is synchronous; hosts should use their own scheduling policy.
/// </remarks>
public sealed class AkburaReferenceSet : IAkburaReferenceResolver
{
    private readonly ImmutableDictionary<string, AssemblyReferenceInfo> _assemblies;

    private AkburaReferenceSet(IEnumerable<PortableExecutableReference> references)
    {
        var assemblies = ImmutableDictionary.CreateBuilder<string, AssemblyReferenceInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in references)
        {
            var info = AssemblyReferenceInfo.Read(reference);
            if (assemblies.TryGetValue(info.Identity.Name, out var previous) && !previous.SameImage(info))
                throw new InvalidOperationException($"Conflicting metadata for '{previous.Identity}' and '{info.Identity}'. Supply one coherent reference set.");
            assemblies[info.Identity.Name] = info;
        }
        _assemblies = assemblies.ToImmutable();
    }

    /// <summary>
    /// Copies complete managed PE images without loading them as executable assemblies.
    /// </summary>
    public static AkburaReferenceSet FromImages(IEnumerable<byte[]> images)
    {
        if (images == null) throw new ArgumentNullException(nameof(images));
        return new(images.Select(AssemblyReferenceInfo.FromImage));
    }

    /// <summary>
    /// Reads an explicit list of assembly files into immutable images. No directory discovery is performed.
    /// </summary>
    public static AkburaReferenceSet FromFiles(IEnumerable<string> assemblyPaths)
    {
        if (assemblyPaths == null) throw new ArgumentNullException(nameof(assemblyPaths));
        return new(assemblyPaths.Select(path => AssemblyReferenceInfo.FromImage(File.ReadAllBytes(path))));
    }

    /// <inheritdoc />
    public PortableExecutableReference? Resolve(AssemblyIdentity identity)
    {
        if (identity == null) throw new ArgumentNullException(nameof(identity));
        var nameOnly = identity.Version == new Version(0, 0, 0, 0) && identity.PublicKeyToken.IsEmpty && string.IsNullOrEmpty(identity.CultureName);
        return _assemblies.TryGetValue(identity.Name, out var reference) &&
            (nameOnly || AssemblyReferenceInfo.Satisfies(identity, reference.Identity))
            ? (PortableExecutableReference)reference.Reference : null;
    }
}
