using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Akbura.Workspaces.Projects;

internal sealed class AssemblyReferenceInfo
{
    private static readonly ConditionalWeakTable<MetadataReference, AssemblyReferenceInfo> Cache = new();

    private AssemblyReferenceInfo(MetadataReference reference, AssemblyIdentity identity,
        ImmutableArray<AssemblyIdentity> dependencies, string fingerprint, Compilation? compilation = null)
    {
        Reference = reference;
        Identity = identity;
        Dependencies = dependencies;
        Fingerprint = fingerprint;
        Compilation = compilation;
    }

    internal MetadataReference Reference
    {
        get;
    }

    internal AssemblyIdentity Identity
    {
        get;
    }

    internal ImmutableArray<AssemblyIdentity> Dependencies
    {
        get;
    }

    private string Fingerprint
    {
        get;
    }

    private Compilation? Compilation
    {
        get;
    }

    internal static AssemblyReferenceInfo Read(MetadataReference reference) => Cache.GetValue(reference, Create);

    internal bool SameImage(AssemblyReferenceInfo other) => Identity.Equals(other.Identity) &&
        (Compilation != null || other.Compilation != null
            ? ReferenceEquals(Compilation, other.Compilation)
            : string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal));

    internal static PortableExecutableReference FromImage(byte[] image)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        // Roslyn owns/pins this private immutable copy until the last reference becomes unreachable.
        var reference = MetadataReference.CreateFromImage(ImmutableArray.CreateRange(image));
        _ = Read(reference);
        return reference;
    }

    internal static AssemblyIdentity IdentityOf(Assembly assembly)
    {
        var name = assembly.GetName();
        return new AssemblyIdentity(name.Name!, name.Version, name.CultureName,
            ImmutableArray.CreateRange(name.GetPublicKeyToken() ?? []));
    }

    internal static bool Satisfies(AssemblyIdentity requested, AssemblyIdentity available)
    {
        if (requested.Equals(available)) return true;
        // Framework facades built for an earlier .NET target bind to the coherent newer
        // framework set supplied by the host. Other dependencies require an exact identity.
        return (requested.Name == "System" || requested.Name.StartsWith("System.", StringComparison.Ordinal) ||
                requested.Name is "netstandard" or "mscorlib" or "Microsoft.CSharp") &&
            requested.PublicKeyToken.Length > 0 && available.Version >= requested.Version &&
            string.Equals(requested.Name, available.Name, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(requested.CultureName, available.CultureName, StringComparison.OrdinalIgnoreCase) &&
            requested.PublicKeyToken.SequenceEqual(available.PublicKeyToken);
    }

    private static AssemblyReferenceInfo Create(MetadataReference reference)
    {
        if (reference.Properties.Kind != MetadataImageKind.Assembly)
            throw new ArgumentException("A managed assembly reference is required; standalone modules are not supported.", nameof(reference));
        if (reference is CompilationReference compilationReference)
        {
            var compilation = compilationReference.Compilation;
            return new(reference, compilation.Assembly.Identity,
                compilation.Assembly.Modules.SelectMany(module => module.ReferencedAssemblies).Distinct().ToImmutableArray(),
                string.Empty, compilation);
        }
        if (reference is not PortableExecutableReference portable)
            throw new ArgumentException("The reference must expose managed assembly metadata.", nameof(reference));
        try
        {
            using var metadata = portable.GetMetadata();
            if (metadata is not AssemblyMetadata assemblyMetadata || assemblyMetadata.GetModules().Length != 1)
                throw new BadImageFormatException("A complete single-module managed assembly image is required.");
            var reader = assemblyMetadata.GetModules()[0].GetMetadataReader();
            if (!reader.IsAssembly) throw new BadImageFormatException("The image does not contain an assembly definition.");
            var definition = reader.GetAssemblyDefinition();
            var identity = new AssemblyIdentity(reader.GetString(definition.Name), definition.Version,
                reader.GetString(definition.Culture), ImmutableArray.CreateRange(reader.GetBlobBytes(definition.PublicKey)),
                hasPublicKey: (definition.Flags & AssemblyFlags.PublicKey) != 0);
            var dependencies = reader.AssemblyReferences.Select(handle =>
            {
                var dependency = reader.GetAssemblyReference(handle);
                return new AssemblyIdentity(reader.GetString(dependency.Name), dependency.Version,
                    reader.GetString(dependency.Culture), ImmutableArray.CreateRange(reader.GetBlobBytes(dependency.PublicKeyOrToken)),
                    hasPublicKey: (dependency.Flags & AssemblyFlags.PublicKey) != 0);
            }).ToImmutableArray();
            return new(reference, identity, dependencies, FingerprintMetadata(reader));
        }
        catch (Exception error) when (error is BadImageFormatException or IOException or InvalidOperationException)
        {
            throw new ArgumentException($"Cannot read managed assembly metadata from '{reference.Display ?? "in-memory image"}'.", nameof(reference), error);
        }
    }

    private static unsafe string FingerprintMetadata(MetadataReader reader)
    {
        // This block is used only for comparing metadata, never as a PE image or executable code.
        var bytes = new byte[reader.MetadataLength];
        Marshal.Copy((IntPtr)reader.MetadataPointer, bytes, 0, bytes.Length);
        using var hash = SHA256.Create();
        return Convert.ToBase64String(hash.ComputeHash(bytes));
    }
}
