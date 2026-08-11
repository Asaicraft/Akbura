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

    private readonly object _synchronizationGate = new();

    private readonly Dictionary<ProjectId, ProjectSynchronizationState>
        _projectSynchronizations = new();

    private readonly Dictionary<ProjectId, SemaphoreSlim>
        _projectSynchronizationLocks = new();

    private readonly CancellationTokenSource _disposeCancellation = new();

    private int _disposeState;

    [ImportingConstructor]
    public AkburaVisualStudioWorkspace(
        VisualStudioWorkspace visualStudioWorkspace)
    {
        _visualStudioWorkspace =
            visualStudioWorkspace ??
            throw new ArgumentNullException(
                nameof(visualStudioWorkspace));

        _visualStudioWorkspace.WorkspaceChanged +=
            OnVisualStudioWorkspaceChanged;

        Workspace = new AkburaWorkspace();
    }

    public AkburaWorkspace Workspace { get; }

    internal event EventHandler? ProjectContextChanged;

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

        var totalTimer = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(filePath);

        var stageTimer = Stopwatch.StartNew();
        var project = FindContainingProject(fullPath);
        TracePerformance("FindContainingProject", stageTimer.Elapsed);

        if (project == null)
        {
            Debug.WriteLine(
                $"[Akbura] Roslyn project was not found " +
                $"for '{fullPath}'.");

            return null;
        }

        var synchronization =
            GetOrCreateProjectSynchronizationTaskAsync(
                project,
                fullPath);
        var synchronized = await AwaitWithoutCancelingSourceAsync(
                synchronization,
                cancellationToken)
            .ConfigureAwait(false);

        TracePerformance("Synchronization total", totalTimer.Elapsed);

        return synchronized
            ? new AkburaProjectId(project.Id.Id)
            : null;
    }

    private async Task<bool> GetOrCreateProjectSynchronizationTaskAsync(
        Project project,
        string? activeFilePath)
    {
        var cancellationToken = _disposeCancellation.Token;
        var version = await project
            .GetDependentVersionAsync(cancellationToken)
            .ConfigureAwait(false);

        ProjectSynchronizationState state;
        var startsSynchronization = false;
        lock (_synchronizationGate)
        {
            if (_projectSynchronizations.TryGetValue(
                    project.Id,
                    out var current) &&
                (!current.Task.IsCompleted ||
                 current.Version.Equals(version)))
            {
                state = current;
            }
            else
            {
                state = new ProjectSynchronizationState(version);
                _projectSynchronizations[project.Id] = state;
                startsSynchronization = true;
            }
        }

        if (startsSynchronization)
        {
            _ = CompleteProjectSynchronizationAsync(
                project,
                activeFilePath,
                state);
        }

        return await state.Task.ConfigureAwait(false);
    }

    private async Task CompleteProjectSynchronizationAsync(
        Project project,
        string? activeFilePath,
        ProjectSynchronizationState state)
    {
        var cancellationToken = _disposeCancellation.Token;
        try
        {
            var synchronizationLock = GetProjectSynchronizationLock(
                project.Id);
            await synchronizationLock
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            try
            {
                var result = await SynchronizeProjectAndReferencesAsync(
                        project,
                        activeFilePath,
                        cancellationToken)
                    .ConfigureAwait(false);
                state.TrySetResult(result);
            }
            finally
            {
                synchronizationLock.Release();
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            RemoveFailedSynchronization(project.Id, state);
            state.TrySetCanceled();
        }
        catch (Exception exception)
        {
            RemoveFailedSynchronization(project.Id, state);
            state.TrySetException(exception);
        }
    }

    private async Task<bool>
        SynchronizeProjectAndReferencesAsync(
            Project project,
            string? activeFilePath,
            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var totalTimer = Stopwatch.StartNew();
        var stageTimer = Stopwatch.StartNew();

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

            await GetOrCreateProjectSynchronizationTaskAsync(
                    referencedProject,
                    activeFilePath: null)
                .ConfigureAwait(false);
        }
        TracePerformance("Referenced projects", stageTimer.Elapsed);

        stageTimer.Restart();
        var compilation =
            await project
                .GetCompilationAsync(
                    cancellationToken)
                .ConfigureAwait(false);
        TracePerformance("GetCompilationAsync", stageTimer.Elapsed);

        if (compilation is not
            CSharpCompilation csharpCompilation)
        {
            Debug.WriteLine(
                $"[Akbura] C# compilation was not available " +
                $"for project '{project.Name}'.");

            return false;
        }

        stageTimer.Restart();
        var context =
            CreateProjectContext(
                project,
                csharpCompilation,
                activeFilePath ??
                    project.FilePath ??
                    Environment.CurrentDirectory);
        TracePerformance("CreateProjectContext", stageTimer.Elapsed);

        stageTimer.Restart();
        var akburaProject =
            Workspace.AddOrUpdateProject(context);

        await SynchronizeAkburaDocumentsAsync(
                project,
                akburaProject.Id,
                activeFilePath,
                cancellationToken)
            .ConfigureAwait(false);
        TracePerformance(
            "SynchronizeAkburaDocumentsAsync",
            stageTimer.Elapsed);

        Debug.WriteLine(
            $"[Akbura] Roslyn project synchronized: " +
            $"name={project.Name}, " +
            $"assembly={csharpCompilation.AssemblyName}, " +
            $"trees={csharpCompilation.SyntaxTrees.Count()}, " +
            $"references={csharpCompilation.References.Count()}, " +
            $"projectReferences={project.ProjectReferences.Count()}");

        TracePerformance(
            "SynchronizeProjectAndReferencesAsync total",
            totalTimer.Elapsed);
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

        var loadTasks = documents
            .Select(LoadDocumentAsync)
            .ToArray();
        var loadedDocuments = await Task.WhenAll(loadTasks)
            .ConfigureAwait(false);
        var inputs = ImmutableArray.CreateBuilder<AkburaDocumentInput>(
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

        Debug.WriteLine(
            $"[Akbura] Synchronized {inputs.Count} " +
            $"Akbura documents for " +
            $"project '{project.Name}'.");

        async Task<AkburaDocumentInput?> LoadDocumentAsync(
            TextDocument document)
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
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _visualStudioWorkspace.WorkspaceChanged -=
            OnVisualStudioWorkspaceChanged;

        _disposeCancellation.Cancel();

        lock (_synchronizationGate)
        {
            _projectSynchronizationLocks.Clear();
            _projectSynchronizations.Clear();
        }

        Workspace.Dispose();
        ProjectContextChanged = null;
    }

    private void OnVisualStudioWorkspaceChanged(
        object? sender,
        WorkspaceChangeEventArgs e)
    {
        if (Volatile.Read(ref _disposeState) == 0)
        {
            ProjectContextChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private SemaphoreSlim GetProjectSynchronizationLock(
        ProjectId projectId)
    {
        lock (_synchronizationGate)
        {
            if (!_projectSynchronizationLocks.TryGetValue(
                    projectId,
                    out var synchronizationLock))
            {
                synchronizationLock = new SemaphoreSlim(1, 1);
                _projectSynchronizationLocks.Add(
                    projectId,
                    synchronizationLock);
            }

            return synchronizationLock;
        }
    }

    private void RemoveFailedSynchronization(
        ProjectId projectId,
        ProjectSynchronizationState state)
    {
        lock (_synchronizationGate)
        {
            if (_projectSynchronizations.TryGetValue(
                    projectId,
                    out var current) &&
                ReferenceEquals(current, state))
            {
                _projectSynchronizations.Remove(projectId);
            }
        }
    }

#pragma warning disable VSTHRD003 // The shared source task deliberately outlives the requesting editor snapshot.
    private static async Task<T> AwaitWithoutCancelingSourceAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken)
    {
        if (task.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(
                   () => cancellation.TrySetResult(true)))
        {
            if (!ReferenceEquals(
                    await Task.WhenAny(task, cancellation.Task)
                        .ConfigureAwait(false),
                    task))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await task.ConfigureAwait(false);
        }
    }
#pragma warning restore VSTHRD003

    [Conditional("DEBUG")]
    private static void TracePerformance(
        string stage,
        TimeSpan elapsed)
    {
        Debug.WriteLine(
            $"[Akbura.Completion.Performance] " +
            $"{stage}: {elapsed.TotalMilliseconds:F2} ms");
    }

    private sealed class ProjectSynchronizationState
    {
        private readonly TaskCompletionSource<bool> _completion =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ProjectSynchronizationState(VersionStamp version)
        {
            Version = version;
        }

        public VersionStamp Version { get; }

        public Task<bool> Task => _completion.Task;

        public void TrySetResult(bool result) =>
            _completion.TrySetResult(result);

        public void TrySetCanceled() =>
            _completion.TrySetCanceled();

        public void TrySetException(Exception exception) =>
            _completion.TrySetException(exception);
    }
}
