using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

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

        CSharpCompilation = csharpCompilation ??
            throw new ArgumentNullException(nameof(csharpCompilation));

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
}
