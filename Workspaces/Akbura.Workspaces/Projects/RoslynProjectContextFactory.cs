using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Workspaces.Projects;

/// <summary>
/// Creates host-independent Akbura project contexts from Roslyn projects.
/// </summary>
public sealed class RoslynProjectContextFactory
{
    public async Task<ProjectContext> CreateAsync(
        Project project,
        string? fallbackDocumentPath,
        CancellationToken cancellationToken)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        var compilation = await project
            .GetCompilationAsync(cancellationToken)
            .ConfigureAwait(false);
        if (compilation is not CSharpCompilation csharpCompilation)
        {
            throw new InvalidOperationException(
                $"Project '{project.FilePath ?? project.Name}' " +
                "is not a C# project.");
        }

        return Create(
            project,
            csharpCompilation,
            fallbackDocumentPath);
    }

    public ProjectContext Create(
        Project project,
        CSharpCompilation compilation,
        string? fallbackDocumentPath = null)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        var projectFilePath = project.FilePath ?? string.Empty;
        var fallbackPath = string.IsNullOrWhiteSpace(fallbackDocumentPath)
            ? Environment.CurrentDirectory
            : fallbackDocumentPath!;
        var projectDirectory = !string.IsNullOrWhiteSpace(projectFilePath)
            ? Path.GetDirectoryName(projectFilePath) ??
              Environment.CurrentDirectory
            : Path.GetDirectoryName(fallbackPath) ??
              Environment.CurrentDirectory;

        return new ProjectContext(
            project.Id,
            projectFilePath,
            projectDirectory,
            GetRootNamespace(project, compilation),
            compilation,
            project.ProjectReferences.ToImmutableArray());
    }

    public string GetRootNamespace(
        Project project,
        CSharpCompilation compilation)
    {
        if (project == null)
        {
            throw new ArgumentNullException(nameof(project));
        }

        if (compilation == null)
        {
            throw new ArgumentNullException(nameof(compilation));
        }

        if (project.AnalyzerOptions
                .AnalyzerConfigOptionsProvider
                .GlobalOptions
                .TryGetValue(
                    "build_property.RootNamespace",
                    out var configuredRootNamespace) &&
            !string.IsNullOrWhiteSpace(configuredRootNamespace))
        {
            return configuredRootNamespace;
        }

        if (!string.IsNullOrWhiteSpace(project.DefaultNamespace))
        {
            return project.DefaultNamespace!;
        }

        if (!string.IsNullOrWhiteSpace(compilation.AssemblyName))
        {
            return compilation.AssemblyName!;
        }

        return project.Name;
    }
}