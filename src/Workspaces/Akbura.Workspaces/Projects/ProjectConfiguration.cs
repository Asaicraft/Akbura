using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Reflection;

namespace Akbura.Workspaces.Projects;

internal sealed class ProjectConfiguration
{
    private readonly Dictionary<string, AssemblyReferenceInfo> _references = new(StringComparer.OrdinalIgnoreCase);

    internal ProjectConfiguration(CSharpCompilation compilation, IAkburaReferenceResolver? resolver)
    {
        Compilation = compilation;
        Resolver = resolver;
        foreach (var reference in compilation.References) Register(AssemblyReferenceInfo.Read(reference));
    }

    internal CSharpCompilation Compilation
    {
        get; set;
    }

    internal IAkburaReferenceResolver? Resolver
    {
        get; set;
    }

    internal List<Assembly> Assemblies
    {
        get;
    } = new();

    internal SyntaxTree? FindSource(string path) => Compilation.SyntaxTrees.SingleOrDefault(tree =>
    {
        try { return AkburaProjectUpdate.NormalizePath(tree.FilePath) == path; }
        catch (ArgumentException) { return false; }
    });

    internal void AddReference(MetadataReference reference)
    {
        var info = AssemblyReferenceInfo.Read(reference);
        Register(info);
        if (Compilation.References.Any(existing => existing.Properties.Equals(reference.Properties) &&
            AssemblyReferenceInfo.Read(existing).SameImage(info))) return;
        Compilation = Compilation.AddReferences(reference);
    }

    internal void CompleteReferences()
    {
        foreach (var assembly in Assemblies)
        {
            var identity = AssemblyReferenceInfo.IdentityOf(assembly);
            if (_references.TryGetValue(identity.Name, out var existing) && AssemblyReferenceInfo.Satisfies(identity, existing.Identity))
            {
                AddReference(existing.Reference.WithAliases([]).WithEmbedInteropTypes(false));
                continue;
            }
            var reference = Resolver?.Resolve(identity);
            if (reference == null && Resolver == null)
            {
                string location;
                try { location = assembly.Location; }
                catch (Exception error) when (error is NotSupportedException)
                {
                    throw new InvalidOperationException($"Metadata for '{identity}' is unavailable. Configure ReferenceResolver with prepared PE images.", error);
                }
                if (!string.IsNullOrEmpty(location)) reference = AssemblyReferenceInfo.FromImage(File.ReadAllBytes(location));
            }
            if (reference == null) throw Missing(identity, "AddAssembly");
            var resolved = AssemblyReferenceInfo.Read(reference);
            if (!AssemblyReferenceInfo.Satisfies(identity, resolved.Identity))
                throw new InvalidOperationException($"Resolver returned '{resolved.Identity}' for requested assembly '{identity}'.");
            AddReference(reference);
        }

        var pending = new Queue<AssemblyReferenceInfo>(_references.Values);
        var checkedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.Count > 0)
        {
            var requesting = pending.Dequeue();
            if (!checkedNames.Add(requesting.Identity.Name)) continue;
            foreach (var dependency in requesting.Dependencies)
            {
                if (_references.TryGetValue(dependency.Name, out var available))
                {
                    if (!AssemblyReferenceInfo.Satisfies(dependency, available.Identity))
                        throw new InvalidOperationException($"Assembly '{requesting.Identity}' requires '{dependency}', but the project contains '{available.Identity}'. Supply a compatible reference set.");
                    continue;
                }
                var reference = Resolver?.Resolve(dependency);
                if (reference == null) throw Missing(dependency, requesting.Identity.ToString());
                var info = AssemblyReferenceInfo.Read(reference);
                if (!AssemblyReferenceInfo.Satisfies(dependency, info.Identity))
                    throw new InvalidOperationException($"Resolver returned '{info.Identity}' for dependency '{dependency}' requested by '{requesting.Identity}'.");
                AddReference(reference);
                pending.Enqueue(info);
            }
        }
    }

    private void Register(AssemblyReferenceInfo info)
    {
        if (string.Equals(info.Identity.Name, Compilation.AssemblyName, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Assembly '{info.Identity}' is a self-reference to project '{Compilation.AssemblyName}'.");
        if (_references.TryGetValue(info.Identity.Name, out var previous) && !previous.SameImage(info))
            throw new InvalidOperationException($"Conflicting assembly references '{previous.Identity}' and '{info.Identity}'. Different versions or metadata images cannot be mixed.");
        _references[info.Identity.Name] = info;
    }

    private static InvalidOperationException Missing(AssemblyIdentity identity, string requesting) => new(
        $"Missing compiler metadata '{identity}', requested by '{requesting}'. Supply it in the complete batch or configure ReferenceResolver with a prepared reference set. Hosts without assembly files must supply metadata images.");
}
