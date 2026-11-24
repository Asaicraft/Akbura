namespace Akbura.Workspaces;

public sealed partial class AkburaWorkspace
{
    private readonly Dictionary<AkburaProjectId, AkburaProject> _projectFacades = new();
    private readonly Dictionary<AkburaProjectId, IAkburaReferenceResolver?> _projectResolvers = new();

    [ThreadStatic]
    private static HashSet<AkburaWorkspace>? _configuringWorkspaces;

    /// <summary>
    /// Gets the configuration facade for the existing default project.
    /// </summary>
    public AkburaProject DefaultProject => GetProject(DefaultProjectId);

    /// <summary>
    /// Gets a facade that keeps the identity of an existing project across updates.
    /// </summary>
    public AkburaProject GetProject(AkburaProjectId projectId)
    {
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            _ = _currentSolution.GetRequiredProject(projectId);
            if (!_projectFacades.TryGetValue(projectId, out var project))
            {
                project = new AkburaProject(this, projectId);
                _projectFacades.Add(projectId, project);
            }
            return project;
        }
    }

    internal ProjectContext GetProjectContext(AkburaProject project)
    {
        lock (_mutationGate)
        {
            return RequireProject(project).Context;
        }
    }

    internal IAkburaReferenceResolver? GetProjectResolver(AkburaProject project)
    {
        lock (_mutationGate)
        {
            _ = RequireProject(project);
            return _projectResolvers.TryGetValue(project.Id, out var resolver) ? resolver : null;
        }
    }

    internal void UpdateProject(AkburaProject project, Action<AkburaProjectUpdate> configure)
    {
        if (configure == null) throw new ArgumentNullException(nameof(configure));
        _ = GetProjectContext(project);
        var configuring = _configuringWorkspaces ??= new();
        if (!configuring.Add(this)) throw new InvalidOperationException("Nested project configuration batches in the same workspace are not supported.");
        var update = new AkburaProjectUpdate();
        try
        {
            // Staging happens outside the mutation gate. Commit applies operations to the
            // latest state, including editor text and concurrent project updates.
            try { configure(update); }
            finally { update.Seal(); }

            AkburaWorkspaceChangedEventArgs? notification = null;
            lock (_mutationGate)
            {
                var previous = RequireProject(project);
                _projectResolvers.TryGetValue(project.Id, out var resolver);
                var state = update.Apply(previous.Context, resolver);
                if (ReferenceEquals(state.Compilation, previous.CSharpCompilation) && ReferenceEquals(state.Resolver, resolver)) return;
                var context = new ProjectContext(previous.Context.RoslynProjectId,
                    previous.Context.ProjectFilePath, previous.Context.ProjectDirectory, previous.Context.RootNamespace,
                    state.Compilation, previous.Context.ProjectReferences, previous.Context.DiagnosticPublisher);
                var solution = RebuildProjectReferences(_currentSolution.WithProject(previous.WithContext(context)));
                var oldSolution = _currentSolution;
                _projectResolvers[project.Id] = state.Resolver;
                PublishSolution(solution);
                notification = new(AkburaWorkspaceChangeKind.ProjectChanged, oldSolution, solution, project.Id);
            }
            Changed?.Invoke(this, notification);
        }
        finally
        {
            configuring.Remove(this);
            if (configuring.Count == 0) _configuringWorkspaces = null;
        }
    }

    private AkburaProjectSnapshot RequireProject(AkburaProject project)
    {
        ThrowIfDisposed();
        if (!_projectFacades.TryGetValue(project.Id, out var registered) || !ReferenceEquals(registered, project))
            throw new InvalidOperationException($"Project '{project.Id}' has been removed. Obtain a new facade for a newly added project.");
        return _currentSolution.GetRequiredProject(project.Id);
    }
}
