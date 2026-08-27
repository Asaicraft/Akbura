using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Immutable C# project information required by the Akbura compiler.
/// The provider that owns the Roslyn/MSBuild workspace creates this object.
/// </summary>
public sealed record ProjectContext
{
    public ProjectContext(
        ProjectId roslynProjectId,
        string projectFilePath,
        string projectDirectory,
        string rootNamespace,
        CSharpCompilation csharpCompilation,
        ImmutableArray<ProjectReference> projectReferences)
    {
        RoslynProjectId = roslynProjectId ??
            throw new ArgumentNullException(nameof(roslynProjectId));

        ProjectFilePath = projectFilePath ?? string.Empty;
        ProjectDirectory = projectDirectory ?? string.Empty;
        RootNamespace = rootNamespace ?? string.Empty;

        CSharpCompilation = RemoveSelfMetadataReferences(
            csharpCompilation ??
                throw new ArgumentNullException(nameof(csharpCompilation)));

        ProjectReferences = projectReferences.IsDefault
            ? ImmutableArray<ProjectReference>.Empty
            : projectReferences;
    }

    public ProjectId RoslynProjectId { get; }

    public string ProjectFilePath { get; }

    public string ProjectDirectory { get; }

    public string RootNamespace { get; }

    public CSharpCompilation CSharpCompilation { get; }

    public ImmutableArray<ProjectReference> ProjectReferences { get; }

    /// <summary>
    /// Creates an isolated project context suitable for syntax-only editor features.
    /// It is intentionally not an MSBuild project.
    /// </summary>
    public static ProjectContext CreateSyntaxOnly(
        string assemblyName = "Akbura.SyntaxOnly",
        string rootNamespace = "")
    {
        var projectId = ProjectId.CreateNewId(assemblyName);
        var compilation = CSharpCompilation.Create(
            assemblyName,
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));

        return new ProjectContext(
            projectId,
            projectFilePath: string.Empty,
            projectDirectory: Environment.CurrentDirectory,
            rootNamespace,
            compilation,
            ImmutableArray<ProjectReference>.Empty);
    }

    private static CSharpCompilation RemoveSelfMetadataReferences(
        CSharpCompilation compilation)
    {
        if (compilation.AssemblyName is not { Length: > 0 } assemblyName)
        {
            return compilation;
        }

        using var referencesToRemove =
            ImmutableArrayBuilder<MetadataReference>.Rent();

        foreach (var reference in compilation.ExternalReferences)
        {
            if (!IsCurrentAssemblyReference(
                    compilation,
                    reference,
                    assemblyName))
            {
                continue;
            }

            referencesToRemove.Add(reference);
        }

        if (referencesToRemove.Count == 0)
        {
            return compilation;
        }

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            $"Removed {referencesToRemove.Count} metadata reference(s) " +
            $"with the current project assembly name '{assemblyName}'.");

        return compilation.RemoveReferences(
            referencesToRemove.ToImmutable());
    }

    private static bool IsCurrentAssemblyReference(
        CSharpCompilation compilation,
        MetadataReference reference,
        string assemblyName)
    {
        if (TryGetReferencedAssembly(
                compilation,
                reference,
                out var referencedAssembly) &&
            string.Equals(
                referencedAssembly.Identity.Name,
                assemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (reference is PortableExecutableReference portableReference &&
            TryGetAssemblyName(portableReference, out var metadataName) &&
            string.Equals(
                metadataName,
                assemblyName,
                StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var display = reference.Display;
        return !string.IsNullOrWhiteSpace(display) &&
            string.Equals(
                Path.GetFileNameWithoutExtension(display),
                assemblyName,
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetReferencedAssembly(
        CSharpCompilation compilation,
        MetadataReference reference,
        out IAssemblySymbol assembly)
    {
        try
        {
            if (compilation.GetAssemblyOrModuleSymbol(reference) is
                IAssemblySymbol referencedAssembly)
            {
                assembly = referencedAssembly;
                return true;
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (InvalidCastException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        assembly = null!;
        return false;
    }

    private static bool TryGetAssemblyName(
        PortableExecutableReference reference,
        out string assemblyName)
    {
        try
        {
            using var metadata = reference.GetMetadata();
            if (metadata is AssemblyMetadata assemblyMetadata)
            {
                foreach (var module in assemblyMetadata.GetModules())
                {
                    var reader = module.GetMetadataReader();
                    if (reader.IsAssembly)
                    {
                        assemblyName = reader.GetString(
                            reader.GetAssemblyDefinition().Name);
                        return true;
                    }
                }
            }
        }
        catch (BadImageFormatException)
        {
        }
        catch (IOException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        assemblyName = string.Empty;
        return false;
    }
}
