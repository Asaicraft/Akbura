using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.VisualStudio.Projects;

/// <summary>
/// Tracks native editor requests separately from project identity. This has no
/// VS SDK dependency so the host's invalidation and catch-up path can be tested.
/// </summary>
internal sealed class AkburaProjectContextManager : IDisposable
{
    private readonly Microsoft.CodeAnalysis.Workspace _roslynWorkspace;
    private readonly AkburaWorkspace _workspace;
    private readonly AkburaProjectSynchronizer _synchronizer;
    private readonly Func<string, Project?> _findProject;
    private readonly Func<Project, CancellationToken, Task<AkburaLoadedProject?>> _loadProject;
    private readonly Dictionary<ProjectId, ProjectEntry> _projects = new();
    private readonly HashSet<AkburaEditorProjectContext> _buffers = new();
    private readonly Dictionary<string, OpenText> _openTexts = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _disposal = new();
    private readonly WorkspaceEventRegistration _workspaceRegistration;
    private bool _disposed;
    private long _openTextVersion;

    internal object Gate { get; } = new();

    internal AkburaProjectContextManager(Microsoft.CodeAnalysis.Workspace roslynWorkspace,
        AkburaWorkspace workspace, Func<string, Project?> findProject,
        Func<Project, CancellationToken, Task<AkburaLoadedProject?>>? loadProject = null)
    {
        _roslynWorkspace = roslynWorkspace;
        _workspace = workspace;
        _synchronizer = new AkburaProjectSynchronizer(workspace);
        _findProject = findProject;
        _loadProject = loadProject ?? LoadProjectAsync;
        _workspaceRegistration = _roslynWorkspace.RegisterWorkspaceChangedHandler(OnWorkspaceChanged);
    }

    internal AkburaEditorProjectContext OpenBuffer(string filePath, SourceText text)
    {
        lock (Gate)
        {
            var buffer = new AkburaEditorProjectContext(this, filePath, RegisterOpenText(filePath, text));
            buffer.ProjectId = FindProject(filePath)?.Id;
            _buffers.Add(buffer);
            return buffer;
        }
    }

    internal OpenText RegisterOpenText(string filePath, SourceText text)
    {
        lock (Gate)
        {
            var registration = new OpenText(this, NormalizePath(filePath), text);
            _openTexts[registration.FilePath] = registration;
            _openTextVersion++;
            return registration;
        }
    }

    private static string NormalizePath(string filePath) =>
        Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && !uri.IsFile
            ? uri.AbsoluteUri : Path.GetFullPath(filePath);

    private Project? FindProject(string filePath) =>
        Uri.TryCreate(filePath, UriKind.Absolute, out var uri) && !uri.IsFile
            ? null : _findProject(filePath);

    private SourceText? GetOpenText(Uri uri)
    {
        lock (Gate)
        {
            return uri.IsFile && _openTexts.TryGetValue(Path.GetFullPath(uri.LocalPath), out var open)
                ? open.Text : null;
        }
    }

    internal async Task<AkburaProjectId?> SynchronizeAsync(AkburaEditorProjectContext buffer, CancellationToken cancellationToken)
    {
        while (true)
        {
            ProjectId? projectId;
            lock (Gate)
            {
                if (_disposed || buffer.IsDisposed)
                {
                    return null;
                }

                // Rediscover only when ownership was invalidated or discovery
                // has not succeeded. Typing reuses the owning project.
                buffer.ProjectId ??= FindProject(buffer.FilePath)?.Id;
                projectId = buffer.ProjectId;
            }

            if (projectId == null)
            {
                return null;
            }

            var synchronized = await SynchronizeStateAsync(projectId, cancellationToken).ConfigureAwait(false);
            lock (Gate)
            {
                if (buffer.ProjectId != projectId)
                {
                    continue;
                }

                buffer.SynchronizedContext = synchronized;
                return synchronized != null ? new AkburaProjectId(projectId.Id) : null;
            }
        }
    }

    internal async Task<bool> SynchronizeAsync(ProjectId projectId, CancellationToken cancellationToken) =>
        await SynchronizeStateAsync(projectId, cancellationToken).ConfigureAwait(false) != null;

    private async Task<SynchronizationState?> SynchronizeStateAsync(ProjectId projectId, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var project = _roslynWorkspace.CurrentSolution.GetProject(projectId);
            if (project == null)
            {
                return null;
            }

            var version = await project.GetDependentVersionAsync(cancellationToken).ConfigureAwait(false);
            ProjectEntry entry;
            SynchronizationState state;
            lock (Gate)
            {
                if (_disposed)
                {
                    return null;
                }

                if (!_projects.TryGetValue(projectId, out entry!))
                {
                    _projects.Add(projectId, entry = new ProjectEntry());
                }

                if (!IsCurrentProject(project))
                {
                    continue;
                }

                // Share running work, then validate its revision after awaiting.
                // A completion for A can never satisfy a request for B.
                state = entry.Current!;
                if (state == null || state.Task.IsCompleted &&
                    (state.Generation != entry.Generation || !state.Version.Equals(version)))
                {
                    state = new SynchronizationState(entry.Generation, version);
                    entry.Current = state;
                    var newState = state;
                    _ = Task.Run(() => CompleteSynchronizationAsync(project, entry, newState));
                }
            }

            var result = await AwaitWithoutCancelingSourceAsync(state.Task, cancellationToken).ConfigureAwait(false);
            // Never retry a captured Project: even a successful operation may
            // have completed after a newer Roslyn solution became available.
            project = _roslynWorkspace.CurrentSolution.GetProject(projectId);
            if (project == null)
            {
                return null;
            }

            version = await project.GetDependentVersionAsync(cancellationToken).ConfigureAwait(false);
            lock (Gate)
            {
                if (state.Generation == entry.Generation && state.Version.Equals(version) && IsCurrentProject(project))
                {
                    return result ? state : null;
                }
            }
        }
    }

    private bool IsCurrentProject(Project project) =>
        _roslynWorkspace.CurrentSolution.GetProject(project.Id) is { } current &&
        current.Version.Equals(project.Version);

    internal bool IsCurrentContext(AkburaEditorProjectContext buffer) =>
        buffer.ProjectId is { } id && _projects.TryGetValue(id, out var entry) &&
        entry.Current is { } state && ReferenceEquals(state, buffer.SynchronizedContext) &&
        state.Generation == entry.Generation;

    internal async Task<AkburaDocumentContext?> OpenDocumentAsync(AkburaEditorProjectContext buffer,
        long requestVersion, Uri uri, SourceText text, CancellationToken cancellationToken)
    {
        ProjectEntry entry;
        ProjectId projectId;
        lock (Gate)
        {
            if (buffer.ProjectId is not { } owner || !_projects.TryGetValue(owner, out entry!))
            {
                return null;
            }
            projectId = owner;
        }

        // Serialize document mutations with project application. A request
        // queued before a load must revalidate after acquiring the gate, so it
        // cannot write obsolete text over the freshly synchronized project.
        await entry.SynchronizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            lock (Gate)
            {
                if (!buffer.IsCurrent(requestVersion) || !IsCurrentContext(buffer))
                {
                    return null;
                }
            }
            return _workspace.OpenOrChangeDocumentContext(new AkburaProjectId(projectId.Id), uri, text,
                changes: null, cancellationToken);
        }
        finally
        {
            entry.SynchronizationGate.Release();
        }
    }

    private async Task CompleteSynchronizationAsync(Project project, ProjectEntry entry, SynchronizationState state)
    {
        var cancellationToken = _disposal.Token;
        try
        {
            await entry.SynchronizationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                foreach (var reference in project.ProjectReferences)
                {
                    await SynchronizeAsync(reference.ProjectId, cancellationToken).ConfigureAwait(false);
                }

                var loaded = await _loadProject(project, cancellationToken).ConfigureAwait(false);
                var result = false;
                if (loaded != null)
                {
                    while (true)
                    {
                        var solution = _roslynWorkspace.CurrentSolution;
                        var current = solution.GetProject(project.Id);
                        var currentVersion = current == null ? default :
                            await current.GetDependentVersionAsync(cancellationToken).ConfigureAwait(false);
                        long textVersion;
                        lock (Gate)
                        {
                            if (_disposed || state.Generation != entry.Generation || current == null ||
                                !currentVersion.Equals(state.Version))
                            {
                                break;
                            }
                            if (!ReferenceEquals(solution, _roslynWorkspace.CurrentSolution))
                            {
                                continue;
                            }
                            textVersion = _openTextVersion;
                        }

                        // Parsing and applying can be expensive. Never hold the
                        // editor request/publication gate during semantic work.
                        // Rebase open texts on every application; an edit during
                        // parsing needs another application, not another load.
                        _synchronizer.ApplyProject(current, loaded, GetOpenText, null, cancellationToken);
                        lock (Gate)
                        {
                            if (_disposed || state.Generation != entry.Generation)
                            {
                                break;
                            }
                            if (_openTextVersion != textVersion)
                            {
                                continue;
                            }
                            result = true;
                            break;
                        }
                    }
                }

                Complete(entry, state, result);
            }
            finally
            {
                entry.SynchronizationGate.Release();
            }
        }
        catch (OperationCanceledException)
        {
            Forget(entry, state);
            state.Completion.TrySetCanceled();
        }
        catch (Exception exception)
        {
            Forget(entry, state);
            state.Completion.TrySetException(exception);
        }
    }

    private void Complete(ProjectEntry entry, SynchronizationState state, bool result)
    {
        lock (Gate)
        {
            if (!result)
            {
                Forget(entry, state);
            }

            state.Completion.TrySetResult(result);
        }
    }

    private void Forget(ProjectEntry entry, SynchronizationState state)
    {
        lock (Gate)
        {
            if (ReferenceEquals(entry.Current, state))
            {
                entry.Current = null;
            }
        }
    }

    internal async Task<AkburaLoadedProject?> LoadProjectAsync(Project project, CancellationToken cancellationToken)
    {
        if (await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false) is not CSharpCompilation compilation ||
            !compilation.References.Any())
        {
            return null;
        }

        // Stage persisted Roslyn inputs. Resolve live text only at application,
        // so a buffer closed during loading cannot revive its discarded text.
        return await _synchronizer.LoadProjectAsync(project, compilation, null, null, cancellationToken)
            .ConfigureAwait(false);
    }

    private void OnWorkspaceChanged(WorkspaceChangeEventArgs args)
    {
        AkburaEditorProjectContext[] invalidated;
        lock (Gate)
        {
            if (_disposed)
            {
                return;
            }

            var reset = args.Kind is WorkspaceChangeKind.SolutionCleared or WorkspaceChangeKind.SolutionRemoved or
                WorkspaceChangeKind.SolutionAdded or WorkspaceChangeKind.SolutionReloaded ||
                args.OldSolution.Id != args.NewSolution.Id;
            var affected = new HashSet<ProjectId>();
            if (args.ProjectId is { } changedProject)
            {
                AddDependents(changedProject);
            }
            else if (!reset)
            {
                var changes = args.NewSolution.GetChanges(args.OldSolution);
                foreach (var id in changes.GetAddedProjects().Concat(changes.GetRemovedProjects()).Select(project => project.Id)
                    .Concat(changes.GetProjectChanges().Select(change => change.ProjectId)))
                {
                    AddDependents(id);
                }
            }

            foreach (var pair in _projects)
            {
                if (reset || affected.Contains(pair.Key))
                {
                    pair.Value.Generation++;
                    if (_roslynWorkspace.CurrentSolution.GetProject(pair.Key) == null)
                    {
                        _workspace.RemoveProject(new AkburaProjectId(pair.Key.Id));
                    }
                }
            }

            var buffers = new List<AkburaEditorProjectContext>();
            foreach (var buffer in _buffers)
            {
                if (reset || buffer.ProjectId is { } owner && affected.Contains(owner) ||
                    FindProject(buffer.FilePath) is { } discovered && buffer.ProjectId != discovered.Id && affected.Contains(discovered.Id))
                {
                    buffer.ProjectId = FindProject(buffer.FilePath)?.Id;
                    buffer.Invalidate();
                    buffers.Add(buffer);
                }
            }

            invalidated = buffers.ToArray();

            void AddDependents(ProjectId id)
            {
                affected.Add(id);
                affected.UnionWith(args.OldSolution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(id));
                affected.UnionWith(args.NewSolution.GetProjectDependencyGraph().GetProjectsThatTransitivelyDependOnThisProject(id));
            }
        }

        // Never access editor objects here: Roslyn can notify on a background
        // thread. The adapter schedules the latest snapshot on the UI thread.
        foreach (var buffer in invalidated)
        {
            buffer.NotifyChanged();
        }
    }

#pragma warning disable VSTHRD003 // Caller cancellation must not cancel synchronization shared by other buffers.
    private static async Task<T> AwaitWithoutCancelingSourceAsync<T>(Task<T> task, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => canceled.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await task.ConfigureAwait(false);
        }
    }
#pragma warning restore VSTHRD003

    internal void Close(AkburaEditorProjectContext buffer)
    {
        lock (Gate)
        {
            _buffers.Remove(buffer);
            buffer.OpenText.Dispose();
        }
    }

    public void Dispose()
    {
        lock (Gate)
        {
            _disposed = true;
            _workspaceRegistration.Dispose();
            foreach (var buffer in _buffers.ToArray())
            {
                buffer.Dispose();
            }
        }

        _disposal.Cancel();
    }

    internal sealed class OpenText : IDisposable
    {
        private readonly AkburaProjectContextManager _owner;
        internal string FilePath { get; }
        internal SourceText Text { get; private set; }

        internal OpenText(AkburaProjectContextManager owner, string filePath, SourceText text)
        {
            _owner = owner;
            FilePath = filePath;
            Text = text;
        }

        internal void Update(SourceText text)
        {
            lock (_owner.Gate)
            {
                Text = text;
                _owner._openTextVersion++;
            }
        }

        public void Dispose()
        {
            lock (_owner.Gate)
            {
                if (_owner._openTexts.TryGetValue(FilePath, out var current) && ReferenceEquals(this, current))
                {
                    _owner._openTexts.Remove(FilePath);
                    _owner._openTextVersion++;
                }
            }
        }
    }

    private sealed class ProjectEntry
    {
        internal long Generation;
        internal SynchronizationState? Current;
        internal SemaphoreSlim SynchronizationGate { get; } = new(1, 1);
    }

    private sealed class SynchronizationState
    {
        internal long Generation { get; }
        internal VersionStamp Version { get; }
        internal TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal Task<bool> Task => Completion.Task;

        internal SynchronizationState(long generation, VersionStamp version)
        {
            Generation = generation;
            Version = version;
        }
    }
}

internal sealed class AkburaEditorProjectContext : IDisposable
{
    private readonly AkburaProjectContextManager _manager;
    private long _requestedVersion;

    internal string FilePath { get; }
    internal ProjectId? ProjectId { get; set; }
    internal bool IsDisposed { get; private set; }
    internal object? SynchronizedContext { get; set; }
    internal AkburaProjectContextManager.OpenText OpenText { get; }
    internal event Action? Changed;

    internal AkburaEditorProjectContext(AkburaProjectContextManager manager, string filePath, AkburaProjectContextManager.OpenText openText)
    {
        _manager = manager;
        FilePath = filePath;
        OpenText = openText;
    }

    internal long Request(SourceText text)
    {
        lock (_manager.Gate)
        {
            OpenText.Update(text);
            return ++_requestedVersion;
        }
    }

    internal void Invalidate() => ++_requestedVersion;
    internal void NotifyChanged() => Changed?.Invoke();

    internal bool IsCurrent(long requestVersion)
    {
        lock (_manager.Gate)
        {
            return !IsDisposed && requestVersion == _requestedVersion;
        }
    }

    internal bool TryPublish(long requestVersion, Action publish)
        => TryPublish(requestVersion, publish, requireSynchronizedContext: false);

    internal bool TryPublishSemantic(long requestVersion, Action publish)
        => TryPublish(requestVersion, publish, requireSynchronizedContext: true);

    private bool TryPublish(long requestVersion, Action publish, bool requireSynchronizedContext)
    {
        lock (_manager.Gate)
        {
            if (!IsCurrent(requestVersion) || requireSynchronizedContext && !_manager.IsCurrentContext(this))
            {
                return false;
            }

            publish();
            return true;
        }
    }

    internal Task<AkburaProjectId?> SynchronizeAsync(CancellationToken cancellationToken) =>
        _manager.SynchronizeAsync(this, cancellationToken);

    internal Task<AkburaDocumentContext?> OpenDocumentAsync(long requestVersion, Uri uri, SourceText text, CancellationToken cancellationToken) =>
        _manager.OpenDocumentAsync(this, requestVersion, uri, text, cancellationToken);

    public void Dispose()
    {
        lock (_manager.Gate)
        {
            IsDisposed = true;
            Changed = null;
            _manager.Close(this);
        }
    }
}
