using Akbura.Workspaces;
using Akbura.Pools;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.LanguageServices;
using Akbura.VisualStudio.Projects;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Diagnostics;

namespace Akbura.VisualStudio;

[Export(typeof(AkburaVisualStudioWorkspace))]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaVisualStudioWorkspace : IDisposable
{
    private readonly VisualStudioWorkspace _visualStudioWorkspace;

    internal AkburaProjectContextManager ProjectContexts { get; }

    private int _disposeState;

    [ImportingConstructor]
    public AkburaVisualStudioWorkspace(VisualStudioWorkspace visualStudioWorkspace)
    {
        _visualStudioWorkspace =
            visualStudioWorkspace ??
            throw new ArgumentNullException(
                nameof(visualStudioWorkspace));

        _visualStudioWorkspace.WorkspaceChanged +=
            OnVisualStudioWorkspaceChanged;

        Workspace = new AkburaWorkspace();
        ProjectContexts = new AkburaProjectContextManager(
            _visualStudioWorkspace, Workspace, FindContainingProject);
    }

    public AkburaWorkspace Workspace { get; }

    internal event EventHandler<WorkspaceChangeEventArgs>? ProjectContextChanged;

    internal event EventHandler<WorkspaceChangeEventArgs>?
        ResourceProjectContextChanged;

    internal Project? FindRoslynProjectForDocument(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        return FindContainingProject(
            Path.GetFullPath(filePath));
    }

    internal async Task<ImmutableArray< AkburaResourceDocumentRegistration>> OpenOrChangeResourceDocumentAsync(string filePath, SourceText text, IReadOnlyList<TextChangeRange>? changes, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return ImmutableArray<
                AkburaResourceDocumentRegistration>.Empty;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            throw new ArgumentException(
                "A resource document path is required.",
                nameof(filePath));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var fullPath = Path.GetFullPath(filePath);
        var projects = FindResourceDocumentProjects(fullPath);
        if (projects.IsDefaultOrEmpty)
        {
            return ImmutableArray<
                AkburaResourceDocumentRegistration>.Empty;
        }

        var registrations = new List<
            AkburaResourceDocumentRegistration>(projects.Length);
        var updates = ImmutableArray.CreateBuilder<(
            AkburaProjectId ProjectId,
            ResourceDocumentInput Input)>(projects.Length);
        var version = VersionStamp.Create();
        foreach (var project in projects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await ProjectContexts.SynchronizeAsync(
                    project.Id, cancellationToken)
                .ConfigureAwait(false))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var projectId = new AkburaProjectId(project.Id.Id);
            if (!Workspace.CurrentSolution.TryGetProject(
                    projectId,
                    out var projectSnapshot) ||
                !TryFindResourceDocumentInput(
                    projectSnapshot,
                    fullPath,
                    out var input))
            {
                continue;
            }

            var liveInput = input.WithText(text, version);
            updates.Add((projectId, liveInput));
            registrations.Add(
                new AkburaResourceDocumentRegistration(
                    projectId,
                    liveInput));
        }

        Workspace.OpenOrChangeResourceDocuments(
            updates.ToImmutable(),
            changes,
            cancellationToken);
        return registrations.ToImmutableArray();
    }

    internal void ChangeResourceDocument(ImmutableArray<AkburaResourceDocumentRegistration> registrations, SourceText text, IReadOnlyList<TextChangeRange>? changes, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        var version = VersionStamp.Create();
        var updates = ImmutableArray.CreateBuilder<(
            AkburaProjectId ProjectId,
            ResourceDocumentInput Input)>(registrations.Length);
        foreach (var registration in registrations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            updates.Add(
                (registration.ProjectId,
                registration.Input.WithText(
                    text,
                    version)));
        }

        Workspace.OpenOrChangeResourceDocuments(
            updates.ToImmutable(),
            changes,
            cancellationToken);
    }

    internal void RemoveResourceDocuments(ImmutableArray<AkburaResourceDocumentRegistration> registrations)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        foreach (var registration in registrations)
        {
            if (!Workspace.CurrentSolution.TryGetProject(
                    registration.ProjectId,
                    out _))
            {
                continue;
            }

            Workspace.RemoveResourceDocument(
                registration.ProjectId,
                registration.Input.Uri);
        }
    }

    internal void RestorePersistedResourceDocuments(string filePath, ImmutableArray<AkburaResourceDocumentRegistration> registrations, SourceText expectedText)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        if (expectedText == null)
        {
            throw new ArgumentNullException(nameof(expectedText));
        }

        var fullPath = Path.GetFullPath(filePath);
        SourceText? persistedText = null;
        try
        {
            if (File.Exists(fullPath))
            {
                using var stream = File.OpenRead(fullPath);
                persistedText = SourceText.From(stream);
            }
        }
        catch (IOException exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Could not restore Avalonia resource document " +
                $"'{fullPath}' from disk: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Could not restore Avalonia resource document " +
                $"'{fullPath}' from disk: {exception.Message}");
        }

        var currentOwners = FindResourceDocumentProjects(fullPath)
            .Select(static project =>
                new AkburaProjectId(project.Id.Id))
            .ToImmutableHashSet();

        foreach (var registration in registrations)
        {
            if (!Workspace.CurrentSolution.TryGetProject(
                    registration.ProjectId,
                    out _))
            {
                continue;
            }

            var persistedInput = persistedText != null &&
                    currentOwners.Contains(registration.ProjectId)
                ? registration.Input.WithText(
                    persistedText,
                    VersionStamp.Create())
                : (ResourceDocumentInput?)null;
            Workspace.TryRestoreResourceDocument(
                registration.ProjectId,
                registration.Input.Uri,
                expectedText,
                persistedInput);
        }
    }

    private ImmutableArray<Project> FindResourceDocumentProjects(string fullPath)
    {
        return _visualStudioWorkspace
            .CurrentSolution
            .Projects
            .Where(static project =>
                project.Language == LanguageNames.CSharp)
            .Where(project =>
                project.AdditionalDocuments.Any(document =>
                    RoslynResourceDocumentLoader
                        .IsAvaloniaResourceDocument(document.FilePath) &&
                    PathsEqual(document.FilePath, fullPath)))
            .ToImmutableArray();
    }

    private static bool TryFindResourceDocumentInput(AkburaProjectSnapshot project, string fullPath, out ResourceDocumentInput input)
    {
        foreach (var document in project.ResourceDocuments.Values)
        {
            if (PathsEqual(document.PhysicalPath, fullPath))
            {
                input = document.Input;
                return true;
            }
        }

        input = default;
        return false;
    }

    /// <summary>
    /// Finds the C# project that owns the specified Akbura document
    /// and synchronizes it with the Akbura workspace.
    /// </summary>
    public async Task<AkburaProjectId?> SynchronizeProjectAsync(string filePath, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var totalTimer = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(filePath);

        var stageTimer = Stopwatch.StartNew();
        var project = FindContainingProject(fullPath);
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "FindContainingProject",
            stageTimer.Elapsed);

        if (project == null)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Roslyn project was not found " +
                $"for '{fullPath}'.");

            return null;
        }

        var synchronized = await ProjectContexts.SynchronizeAsync(project.Id, cancellationToken)
            .ConfigureAwait(false);
        AkburaWorkspaceDiagnostics.WriteCompletionElapsed("Synchronization total", totalTimer.Elapsed);
        return synchronized ? new AkburaProjectId(project.Id.Id) : null;
    }

    private Project? FindContainingProject(string filePath)
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

        Project? bestProject = null;
        var bestDirectoryLength = -1;
        var bestReadinessScore = -1;

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

            var readinessScore =
                GetProjectReadinessScore(project);

            if (directoryLength < bestDirectoryLength ||
                directoryLength == bestDirectoryLength &&
                readinessScore <= bestReadinessScore)
            {
                continue;
            }

            bestProject = project;
            bestDirectoryLength =
                directoryLength;
            bestReadinessScore = readinessScore;
        }

        return bestProject;
    }

    private static int GetProjectReadinessScore(Project project)
    {
        var score = 0;

        if (project.Documents.Any())
        {
            score += 4;
        }

        if (project.MetadataReferences.Any())
        {
            score += 2;
        }

        if (project.ProjectReferences.Any())
        {
            score++;
        }

        return score;
    }

    private static bool ContainsFile(IEnumerable<TextDocument> documents, string filePath)
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

    private static bool IsContainedByDirectory(string filePath, string directoryPath)
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

    private static bool PathsEqual(string? left, string? right)
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

    private static ProjectContext CreateProjectContext(Project project, CSharpCompilation compilation, string documentFilePath)
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

    private static string GetRootNamespace(Project project, CSharpCompilation compilation)
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

    private async Task SynchronizeAkburaDocumentsAsync(Project project, AkburaProjectId projectId, string? activeFilePath, CancellationToken cancellationToken)
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

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            $"Found {documents.Length} " +
            $"Akbura documents in " +
            $"project '{project.Name}'.");

        var loadTasks = documents
            .Select(LoadDocumentAsync)
            .ToArray();
        var loadedDocuments = await Task.WhenAll(loadTasks)
            .ConfigureAwait(false);
        using var inputs = ImmutableArrayBuilder<AkburaDocumentInput>.Rent(
            loadedDocuments.Length);
        foreach (var input in loadedDocuments)
        {
            if (input.HasValue)
            {
                inputs.Add(input.Value);
            }
        }

        Workspace.SynchronizeProjectDocuments(
            projectId,
            inputs.ToImmutable(),
            cancellationToken);

        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            $"Synchronized {inputs.Count} " +
            $"Akbura documents for " +
            $"project '{project.Name}'.");

        async Task<AkburaDocumentInput?> LoadDocumentAsync(TextDocument document)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var filePath = document.FilePath;
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            var fullPath = Path.GetFullPath(filePath);
            if (activeFilePath != null &&
                PathsEqual(fullPath, activeFilePath))
            {
                return null;
            }

            var text = await document
                .GetTextAsync(cancellationToken)
                .ConfigureAwait(false);
            return text == null
                ? null
                : new AkburaDocumentInput(
                    new Uri(fullPath),
                    text);
        }
    }

    private static ImmutableArray<TextDocument> GetAkburaDocuments(Project project)
    {
        using var builder =
            ImmutableArrayBuilder<TextDocument>.Rent();
        var paths = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        AddDocuments(project.Documents, paths, builder);
        AddDocuments(project.AdditionalDocuments, paths, builder);

        return builder.ToImmutable();
    }

    private static void AddDocuments(IEnumerable<TextDocument> documents, HashSet<string> paths, ImmutableArrayBuilder<TextDocument> builder)
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

    internal bool TryResolveProjectSource(AkburaDefinition definition, out string filePath)
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

        foreach (var project in _visualStudioWorkspace.CurrentSolution.Projects)
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
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Workspace,
                    $"Embedded source resolved to " +
                    $"solution project file '{filePath}'.");

                return true;
            }
        }

        filePath = null!;
        return false;
    }

    private static bool TryResolveProjectSource(Project project, string sourcePath, out string filePath)
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

    private static bool DocumentLogicalPathMatches(TextDocument document, string sourcePath)
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

    private static bool PhysicalPathEndsWithSourcePath(string filePath, string sourcePath)
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

    private static string NormalizeFileSystemPath(string path)
    {
        return path
            .Replace(
                Path.AltDirectorySeparatorChar,
                Path.DirectorySeparatorChar)
            .Replace(
                '/',
                Path.DirectorySeparatorChar);
    }

    private static string NormalizeLogicalPath(string path)
    {
        return path
            .Replace('\\', '/')
            .TrimStart('/');
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _visualStudioWorkspace.WorkspaceChanged -=
            OnVisualStudioWorkspaceChanged;

        ProjectContexts.Dispose();

        Workspace.Dispose();
        ProjectContextChanged = null;
        ResourceProjectContextChanged = null;
    }

    private void OnVisualStudioWorkspaceChanged(object? sender, WorkspaceChangeEventArgs e)
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            ProjectContextChanged?.Invoke(this, e);
            ResourceProjectContextChanged?.Invoke(this, e);
        }
    }

}
