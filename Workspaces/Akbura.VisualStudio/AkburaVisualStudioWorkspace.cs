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

        var synchronizedProjects =
            new HashSet<ProjectId>();

        var synchronized =
            await SynchronizeProjectAndReferencesAsync(
                    project,
                    fullPath,
                    synchronizedProjects,
                    cancellationToken)
                .ConfigureAwait(false);

        return synchronized
            ? new AkburaProjectId(project.Id.Id)
            : null;
    }

    private async Task<bool>
        SynchronizeProjectAndReferencesAsync(
            Project project,
            string? activeFilePath,
            HashSet<ProjectId> synchronizedProjects,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!synchronizedProjects.Add(project.Id))
        {
            return true;
        }

        foreach (var projectReference in
                 project.ProjectReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var referencedProject =
                project.Solution.GetProject(
                    projectReference.ProjectId);
            if (referencedProject == null ||
                referencedProject.Language !=
                    LanguageNames.CSharp)
            {
                continue;
            }

            await SynchronizeProjectAndReferencesAsync(
                    referencedProject,
                    activeFilePath: null,
                    synchronizedProjects,
                    cancellationToken)
                .ConfigureAwait(false);
        }

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

            return false;
        }

        var context =
            CreateProjectContext(
                project,
                csharpCompilation,
                activeFilePath ??
                    project.FilePath ??
                    Environment.CurrentDirectory);

        var akburaProject =
            Workspace.AddOrUpdateProject(context);

        await SynchronizeAkburaDocumentsAsync(
                project,
                akburaProject.Id,
                activeFilePath,
                cancellationToken)
            .ConfigureAwait(false);

        Debug.WriteLine(
            $"[Akbura] Roslyn project synchronized: " +
            $"name={project.Name}, " +
            $"assembly={csharpCompilation.AssemblyName}, " +
            $"trees={csharpCompilation.SyntaxTrees.Count()}, " +
            $"references={csharpCompilation.References.Count()}, " +
            $"projectReferences={project.ProjectReferences.Count()}");

        return true;
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
            GetRootNamespace(
                project,
                compilation),
            compilation,
            [.. project.ProjectReferences]);
    }

    private static string GetRootNamespace(
        Project project,
        CSharpCompilation compilation)
    {
        if (project.AnalyzerOptions
                .AnalyzerConfigOptionsProvider
                .GlobalOptions
                .TryGetValue(
                    "build_property.RootNamespace",
                    out var configuredRootNamespace) &&
            !string.IsNullOrWhiteSpace(
                configuredRootNamespace))
        {
            return configuredRootNamespace;
        }

        if (!string.IsNullOrWhiteSpace(
                project.DefaultNamespace))
        {
            return project.DefaultNamespace!;
        }

        if (!string.IsNullOrWhiteSpace(
                compilation.AssemblyName))
        {
            return compilation.AssemblyName!;
        }

        return project.Name;
    }

    private async Task SynchronizeAkburaDocumentsAsync(
        Project project,
        AkburaProjectId projectId,
        string? activeFilePath,
        CancellationToken cancellationToken)
    {
        var documents =
            GetAkburaDocuments(project)
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
            $"Akbura documents in " +
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

            if (activeFilePath != null &&
                PathsEqual(
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
                $"[Akbura] Project document synchronized: " +
                $"'{Path.GetFileName(fullPath)}'.");
        }

        Debug.WriteLine(
            $"[Akbura] Synchronized {synchronizedCount} " +
            $"Akbura documents for " +
            $"project '{project.Name}'.");
    }

    private static ImmutableArray<TextDocument>
        GetAkburaDocuments(Project project)
    {
        var builder =
            ImmutableArray.CreateBuilder<TextDocument>();
        var paths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddDocuments(project.Documents);
        AddDocuments(project.AdditionalDocuments);

        return builder.ToImmutable();

        void AddDocuments(
            IEnumerable<TextDocument> documents)
        {
            foreach (var document in documents)
            {
                if (!IsAkburaDocument(document.FilePath))
                {
                    continue;
                }

                var fullPath =
                    Path.GetFullPath(document.FilePath!);
                if (paths.Add(fullPath))
                {
                    builder.Add(document);
                }
            }
        }
    }

    private static bool IsAkburaDocument(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var extension = Path.GetExtension(filePath);

        return string.Equals(
                   extension,
                   ".akbura",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   extension,
                   ".akcss",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGlobalUsingsDocument(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        return string.Equals(
                   fileName,
                   "GlobalUsings.akbura",
                   StringComparison.OrdinalIgnoreCase) ||
               string.Equals(
                   fileName,
                   "GlobalUsings.akcss",
                   StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryResolveProjectSource(
        AkburaDefinition definition,
        out string filePath)
    {
        if (definition == null)
        {
            throw new ArgumentNullException(
                nameof(definition));
        }

        var assemblyName =
            definition.TargetAssemblyName;
        var sourcePath =
            definition.TargetSourcePath;
        if (string.IsNullOrWhiteSpace(assemblyName) ||
            string.IsNullOrWhiteSpace(sourcePath))
        {
            filePath = null!;
            return false;
        }

        foreach (var project in
                 _visualStudioWorkspace
                     .CurrentSolution
                     .Projects)
        {
            if (project.Language != LanguageNames.CSharp ||
                !string.Equals(
                    project.AssemblyName,
                    assemblyName,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryResolveProjectSource(
                    project,
                    sourcePath!,
                    out filePath))
            {
                Debug.WriteLine(
                    $"[Akbura] Embedded source resolved to " +
                    $"solution project file '{filePath}'.");

                return true;
            }
        }

        filePath = null!;
        return false;
    }

    private static bool TryResolveProjectSource(
        Project project,
        string sourcePath,
        out string filePath)
    {
        var projectDirectory =
            string.IsNullOrWhiteSpace(project.FilePath)
                ? null
                : Path.GetDirectoryName(project.FilePath);

        if (!string.IsNullOrWhiteSpace(projectDirectory))
        {
            var expectedPath = Path.IsPathRooted(sourcePath)
                ? Path.GetFullPath(sourcePath)
                : Path.GetFullPath(
                    Path.Combine(
                        projectDirectory!,
                        NormalizeFileSystemPath(sourcePath)));
            if (File.Exists(expectedPath))
            {
                filePath = expectedPath;
                return true;
            }
        }

        TextDocument? suffixMatch = null;
        foreach (var document in GetAkburaDocuments(project))
        {
            if (!IsAkburaDocument(document.FilePath))
            {
                continue;
            }

            if (DocumentLogicalPathMatches(
                    document,
                    sourcePath))
            {
                filePath = Path.GetFullPath(
                    document.FilePath!);
                return true;
            }

            if (!PhysicalPathEndsWithSourcePath(
                    document.FilePath!,
                    sourcePath))
            {
                continue;
            }

            if (suffixMatch != null)
            {
                filePath = null!;
                return false;
            }

            suffixMatch = document;
        }

        if (suffixMatch?.FilePath is { } matchedPath)
        {
            filePath = Path.GetFullPath(matchedPath);
            return true;
        }

        filePath = null!;
        return false;
    }

    private static bool DocumentLogicalPathMatches(
        TextDocument document,
        string sourcePath)
    {
        var logicalPath = string.Join(
            "/",
            document.Folders
                .Append(document.Name));

        return string.Equals(
            NormalizeLogicalPath(logicalPath),
            NormalizeLogicalPath(sourcePath),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool PhysicalPathEndsWithSourcePath(
        string filePath,
        string sourcePath)
    {
        var normalizedFilePath =
            NormalizeLogicalPath(
                Path.GetFullPath(filePath));
        var normalizedSourcePath =
            NormalizeLogicalPath(sourcePath);

        return string.Equals(
                   normalizedFilePath,
                   normalizedSourcePath,
                   StringComparison.OrdinalIgnoreCase) ||
               normalizedFilePath.EndsWith(
                   "/" + normalizedSourcePath,
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeFileSystemPath(
        string path)
    {
        return path
            .Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .Replace(
                '/',
                Path.DirectorySeparatorChar);
    }

    private static string NormalizeLogicalPath(
        string path)
    {
        return path
            .Replace('\\', '/')
            .TrimStart('/');
    }

    public void Dispose()
    {
        Workspace.Dispose();
    }
}
