using Akbura.Workspaces;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.VisualStudio.LanguageServices;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace Akbura.VisualStudio;

[Export(typeof(AkburaVisualStudioWorkspace))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaVisualStudioWorkspace : IDisposable
{
    private readonly VisualStudioWorkspace _visualStudioWorkspace;

    [ImportingConstructor]
    public AkburaVisualStudioWorkspace(
        VisualStudioWorkspace visualStudioWorkspace)
    {
        _visualStudioWorkspace =
            visualStudioWorkspace ??
            throw new ArgumentNullException(
                nameof(visualStudioWorkspace));

        Workspace = new AkburaWorkspace();
    }

    public AkburaWorkspace Workspace { get; }

    /// <summary>
    /// Finds the C# project that owns the specified Akbura document
    /// and synchronizes it with the Akbura workspace.
    /// </summary>
    public async Task<AkburaProjectId?> SynchronizeProjectAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var fullPath = Path.GetFullPath(filePath);

        var project = FindContainingProject(fullPath);

        if (project == null)
        {
            Debug.WriteLine(
                $"[Akbura] Roslyn project was not found " +
                $"for '{fullPath}'.");

            return null;
        }

        cancellationToken
            .ThrowIfCancellationRequested();

        var compilation =
            await project
                .GetCompilationAsync(
                    cancellationToken)
                .ConfigureAwait(false);

        if (compilation is not
            CSharpCompilation csharpCompilation)
        {
            Debug.WriteLine(
                $"[Akbura] C# compilation was not available " +
                $"for project '{project.Name}'.");

            return null;
        }

        var context =
            CreateProjectContext(
                project,
                csharpCompilation,
                fullPath);

        var akburaProject = Workspace.AddOrUpdateProject(context);

        await SynchronizeAkburaDocumentsAsync(
                project,
                akburaProject.Id,
                fullPath,
                cancellationToken)
            .ConfigureAwait(false);

        Debug.WriteLine(
            $"[Akbura] Roslyn project synchronized: " +
            $"name={project.Name}, " +
            $"assembly={csharpCompilation.AssemblyName}, " +
            $"trees={csharpCompilation.SyntaxTrees.Count()}, " +
            $"references={csharpCompilation.References.Count()}");

        return akburaProject.Id;
    }

    private Project? FindContainingProject(
        string filePath)
    {
        var solution =
            _visualStudioWorkspace
                .CurrentSolution;

        var csharpProjects =
            solution.Projects
                .Where(
                    static project =>
                        project.Language ==
                        LanguageNames.CSharp)
                .ToImmutableArray();

        /*
         * Prefer an explicit Roslyn document relationship.
         *
         * Akbura files will commonly appear as AdditionalDocuments
         * when they are included through AdditionalFiles.
         */
        foreach (var project in csharpProjects)
        {
            if (ContainsFile(
                    project.Documents,
                    filePath) ||
                ContainsFile(
                    project.AdditionalDocuments,
                    filePath) ||
                ContainsFile(
                    project.AnalyzerConfigDocuments,
                    filePath))
            {
                return project;
            }
        }

        /*
         * A custom project item may not be represented as a Roslyn
         * document. In that case, choose the nearest containing
         * project directory.
         */
        Project? bestProject = null;
        var bestDirectoryLength = -1;

        foreach (var project in csharpProjects)
        {
            var projectFilePath =
                project.FilePath;

            if (string.IsNullOrWhiteSpace(
                    projectFilePath))
            {
                continue;
            }

            var projectDirectory =
                Path.GetDirectoryName(
                    projectFilePath);

            if (string.IsNullOrWhiteSpace(
                    projectDirectory))
            {
                continue;
            }

            if (!IsContainedByDirectory(
                    filePath,
                    projectDirectory))
            {
                continue;
            }

            var directoryLength =
                Path.GetFullPath(
                        projectDirectory)
                    .Length;

            if (directoryLength <=
                bestDirectoryLength)
            {
                continue;
            }

            bestProject = project;
            bestDirectoryLength =
                directoryLength;
        }

        return bestProject;
    }

    private static bool ContainsFile(
        IEnumerable<TextDocument> documents,
        string filePath)
    {
        foreach (var document in documents)
        {
            if (PathsEqual(
                    document.FilePath,
                    filePath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsContainedByDirectory(
        string filePath,
        string directoryPath)
    {
        var normalizedFilePath =
            Path.GetFullPath(filePath);

        var normalizedDirectoryPath =
            Path.GetFullPath(directoryPath)
                .TrimEnd(
                    Path.DirectorySeparatorChar,
                    Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;

        return normalizedFilePath.StartsWith(
            normalizedDirectoryPath,
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PathsEqual(
        string? left,
        string? right)
    {
        if (string.IsNullOrWhiteSpace(left) ||
            string.IsNullOrWhiteSpace(right))
        {
            return false;
        }

        return string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            StringComparison.OrdinalIgnoreCase);
    }

    private static ProjectContext
        CreateProjectContext(
            Project project,
            CSharpCompilation compilation,
            string documentFilePath)
    {
        var projectFilePath =
            project.FilePath ??
            string.Empty;

        var projectDirectory =
            !string.IsNullOrWhiteSpace(
                projectFilePath)
                ? Path.GetDirectoryName(
                      projectFilePath)
                  ?? string.Empty
                : Path.GetDirectoryName(
                      documentFilePath)
                  ?? Environment.CurrentDirectory;

        return new ProjectContext(
            project.Id,
            projectFilePath,
            projectDirectory,
            project.DefaultNamespace ??
                string.Empty,
            compilation,
            [.. project.ProjectReferences]);
    }

    private async Task SynchronizeAkburaDocumentsAsync(
        Project project,
        AkburaProjectId projectId,
        string activeFilePath,
        CancellationToken cancellationToken)
    {
        var documents =
            project.AdditionalDocuments
                .Where(
                    static document =>
                        IsAkburaDocument(
                            document.FilePath))
                .OrderBy(
                    static document =>
                        IsGlobalUsingsDocument(
                            document.FilePath)
                            ? 0
                            : 1)
                .ToImmutableArray();

        Debug.WriteLine(
            $"[Akbura] Found {documents.Length} " +
            $"Akbura additional documents in " +
            $"project '{project.Name}'.");

        var synchronizedCount = 0;

        foreach (var document in documents)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var filePath =
                document.FilePath;

            if (string.IsNullOrWhiteSpace(
                    filePath))
            {
                continue;
            }

            var fullPath =
                Path.GetFullPath(filePath);

            if (PathsEqual(
                    fullPath,
                    activeFilePath))
            {
                continue;
            }

            var text =
                await document
                    .GetTextAsync(
                        cancellationToken)
                    .ConfigureAwait(false);

            if (text == null)
            {
                continue;
            }

            Workspace.OpenOrChangeDocumentContext(
                projectId,
                new Uri(fullPath),
                text,
                changes: null,
                cancellationToken);

            synchronizedCount++;

            Debug.WriteLine(
                $"[Akbura] Additional document synchronized: " +
                $"'{Path.GetFileName(fullPath)}'.");
        }

        Debug.WriteLine(
            $"[Akbura] Synchronized {synchronizedCount} " +
            $"additional Akbura documents for " +
            $"project '{project.Name}'.");
    }

    private static bool IsAkburaDocument(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
                string.Equals(
                    Path.GetExtension(filePath),
                    ".akbura",
                    StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGlobalUsingsDocument(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
                string.Equals(
                    Path.GetFileName(filePath),
                    "GlobalUsings.akbura",
                    StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}