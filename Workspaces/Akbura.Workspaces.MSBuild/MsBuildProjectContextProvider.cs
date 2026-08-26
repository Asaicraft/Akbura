using Akbura.Pools;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;
using System.Collections.Concurrent;
using System.Collections.Immutable;

namespace Akbura.Workspaces.MSBuild;

public sealed class MsBuildProjectContextProvider :
    IProjectContextProvider,
    IAkburaProjectLoader
{
    private readonly MSBuildWorkspace _workspace;
    private readonly RoslynProjectContextFactory _contextFactory = new();
    private readonly RoslynProjectDocumentLoader _documentLoader = new();
    private readonly ConcurrentDictionary<ProjectId, ProjectContext>
        _contexts = new();
    private readonly ConcurrentQueue<AkburaProjectLoadDiagnostic>
        _loadDiagnostics = new();
    private readonly CancellationTokenSource _disposeCancellation = new();
    private int _disposeState;

    public MsBuildProjectContextProvider()
    {
        if (!MSBuildLocator.IsRegistered)
        {
            MSBuildLocator.RegisterDefaults();
        }

        _workspace = MSBuildWorkspace.Create();
        _workspace.WorkspaceChanged += OnWorkspaceChanged;
        _workspace.WorkspaceFailed += OnWorkspaceFailed;
    }

    public event EventHandler<ProjectContextChangedEventArgs>?
        Changed;

    public async Task<ProjectContext> OpenProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        return (await LoadProjectAsync(
                projectPath,
                cancellationToken)
            .ConfigureAwait(false)).Context;
    }

    public async Task<ImmutableArray<ProjectContext>> OpenSolutionAsync(
        string solutionPath,
        CancellationToken cancellationToken)
    {
        var loaded = await LoadSolutionAsync(
                solutionPath,
                cancellationToken)
            .ConfigureAwait(false);
        using var contexts =
            ImmutableArrayBuilder<ProjectContext>.Rent(loaded.Length);
        foreach (var project in loaded)
        {
            contexts.Add(project.Context);
        }

        return contexts.ToImmutable();
    }

    public async Task<AkburaLoadedProject> LoadProjectAsync(
        string projectPath,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidatePath(projectPath, nameof(projectPath));

        var project = await _workspace
            .OpenProjectAsync(
                Path.GetFullPath(projectPath),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        return await CreateLoadedProjectAsync(
                project,
                cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<ImmutableArray<AkburaLoadedProject>>
        LoadSolutionAsync(
            string solutionPath,
            CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ValidatePath(solutionPath, nameof(solutionPath));

        var solution = await _workspace
            .OpenSolutionAsync(
                Path.GetFullPath(solutionPath),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var projects = solution.Projects
            .Where(static project =>
                project.Language == LanguageNames.CSharp)
            .ToArray();
        var loadedProjects = new AkburaLoadedProject[projects.Length];
        for (var index = 0; index < projects.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            loadedProjects[index] = await CreateLoadedProjectAsync(
                    projects[index],
                    cancellationToken)
                .ConfigureAwait(false);
        }

        using var loaded =
            ImmutableArrayBuilder<AkburaLoadedProject>.Rent(
                loadedProjects.Length);
        loaded.AddRange(loadedProjects);
        return loaded.ToImmutable();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _workspace.WorkspaceChanged -= OnWorkspaceChanged;
        _workspace.WorkspaceFailed -= OnWorkspaceFailed;
        _disposeCancellation.Cancel();
        _disposeCancellation.Dispose();
        _workspace.Dispose();
    }

    private async Task<AkburaLoadedProject> CreateLoadedProjectAsync(
        Project project,
        CancellationToken cancellationToken)
    {
        var context = await _contextFactory
            .CreateAsync(
                project,
                project.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        var documents = await _documentLoader
            .LoadAsync(
                project,
                openTextProvider: null,
                excludedDocument: null,
                cancellationToken)
            .ConfigureAwait(false);
        _contexts[project.Id] = context;

        using var diagnostics =
            ImmutableArrayBuilder<AkburaProjectLoadDiagnostic>.Rent();
        while (_loadDiagnostics.TryDequeue(out var diagnostic))
        {
            diagnostics.Add(diagnostic);
        }

        return new AkburaLoadedProject(
            context,
            documents,
            diagnostics.ToImmutable());
    }

    private void OnWorkspaceChanged(
        object? sender,
        WorkspaceChangeEventArgs eventArgs)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        _ = PublishWorkspaceChangeAsync(
            eventArgs,
            _disposeCancellation.Token);
    }

    private async Task PublishWorkspaceChangeAsync(
        WorkspaceChangeEventArgs eventArgs,
        CancellationToken cancellationToken)
    {
        try
        {
            if (eventArgs.Kind == WorkspaceChangeKind.ProjectRemoved &&
                eventArgs.ProjectId is { } removedProjectId)
            {
                _contexts.TryRemove(
                    removedProjectId,
                    out var oldContext);
                Changed?.Invoke(
                    this,
                    new ProjectContextChangedEventArgs(
                        ProjectContextChangeKind.ProjectRemoved,
                        oldContext,
                        newContext: null));
                return;
            }

            if (eventArgs.ProjectId is { } projectId)
            {
                await PublishProjectChangeAsync(
                        projectId,
                        eventArgs.Kind,
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            foreach (var project in
                     _workspace.CurrentSolution.Projects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (project.Language == LanguageNames.CSharp)
                {
                    await PublishProjectChangeAsync(
                            project.Id,
                            eventArgs.Kind,
                            cancellationToken)
                        .ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _loadDiagnostics.Enqueue(
                new AkburaProjectLoadDiagnostic(
                    AkburaProjectLoadDiagnosticSeverity.Error,
                    exception.Message));
        }
    }

    private async Task PublishProjectChangeAsync(
        ProjectId projectId,
        WorkspaceChangeKind workspaceChangeKind,
        CancellationToken cancellationToken)
    {
        var project = _workspace.CurrentSolution.GetProject(projectId);
        if (project == null ||
            project.Language != LanguageNames.CSharp)
        {
            return;
        }

        _contexts.TryGetValue(projectId, out var oldContext);
        var newContext = await _contextFactory
            .CreateAsync(
                project,
                project.FilePath,
                cancellationToken)
            .ConfigureAwait(false);
        _contexts[projectId] = newContext;

        Changed?.Invoke(
            this,
            new ProjectContextChangedEventArgs(
                GetChangeKind(workspaceChangeKind),
                oldContext,
                newContext));
    }

    private void OnWorkspaceFailed(
        object? sender,
        WorkspaceDiagnosticEventArgs eventArgs)
    {
        _loadDiagnostics.Enqueue(
            new AkburaProjectLoadDiagnostic(
                eventArgs.Diagnostic.Kind == WorkspaceDiagnosticKind.Failure
                    ? AkburaProjectLoadDiagnosticSeverity.Error
                    : AkburaProjectLoadDiagnosticSeverity.Warning,
                eventArgs.Diagnostic.Message));
    }

    private static ProjectContextChangeKind GetChangeKind(
        WorkspaceChangeKind kind)
    {
        return kind is WorkspaceChangeKind.ProjectChanged or
            WorkspaceChangeKind.ProjectReloaded or
            WorkspaceChangeKind.SolutionChanged or
            WorkspaceChangeKind.SolutionReloaded
                ? ProjectContextChangeKind.ProjectReloaded
                : kind is WorkspaceChangeKind.ProjectRemoved
                    ? ProjectContextChangeKind.ProjectRemoved
                    : kind.ToString().IndexOf(
                            "Document",
                            StringComparison.Ordinal) >= 0
                        ? ProjectContextChangeKind.CompilationChanged
                        : ProjectContextChangeKind.ReferencesChanged;
    }

    private static void ValidatePath(
        string path,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException(
                "A project or solution path is required.",
                parameterName);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(
                nameof(MsBuildProjectContextProvider));
        }
    }
}