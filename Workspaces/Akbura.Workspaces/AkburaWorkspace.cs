using Akbura.Language;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Workspaces;

/// <summary>
/// The only mutable object in the core workspace layer.
/// Every published solution, project and document is immutable.
/// CPU-bound methods must be called by editor hosts from a background thread.
/// </summary>
public sealed class AkburaWorkspace : IDisposable
{
    private readonly object _gate = new();
    private AkburaSolutionSnapshot _currentSolution;
    private bool _isDisposed;

    public AkburaWorkspace()
        : this(ProjectContext.CreateSyntaxOnly())
    {
    }

    public AkburaWorkspace(ProjectContext initialContext)
    {
        if (initialContext == null)
        {
            throw new ArgumentNullException(nameof(initialContext));
        }

        var project = AkburaProjectSnapshot.Create(
            initialContext);

        DefaultProjectId = project.Id;
        _currentSolution =
            AkburaSolutionSnapshot.Empty.WithProject(project);

        LanguageServices = new AkburaLanguageServices();
    }

    public AkburaProjectId DefaultProjectId { get; }

    public AkburaSolutionSnapshot CurrentSolution
    {
        get
        {
            lock (_gate)
            {
                return _currentSolution;
            }
        }
    }

    public IAkburaLanguageServices LanguageServices { get; }

    public event EventHandler<AkburaWorkspaceChangedEventArgs>?
        Changed;

    public AkburaProjectSnapshot AddOrUpdateProject(
        ProjectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs;
        AkburaProjectSnapshot result;

        lock (_gate)
        {
            var oldSolution = _currentSolution;
            var projectId =
                AkburaProjectId.FromRoslyn(
                    context.RoslynProjectId);

            AkburaWorkspaceChangeKind kind;

            if (oldSolution.TryGetProject(
                    projectId,
                    out var oldProject))
            {
                result = oldProject.WithContext(context);
                kind = AkburaWorkspaceChangeKind.ProjectChanged;
            }
            else
            {
                result = AkburaProjectSnapshot.Create(context);
                kind = AkburaWorkspaceChangeKind.ProjectAdded;
            }

            var newSolution =
                RebuildProjectReferences(
                    oldSolution.WithProject(result));

            result = newSolution.GetRequiredProject(
                projectId);

            _currentSolution = newSolution;

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                kind,
                oldSolution,
                newSolution,
                result.Id);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    public AkburaDocumentSnapshot OpenOrChangeDocument(
        Uri uri,
        SourceText text,
        IReadOnlyList<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        return OpenOrChangeDocumentContext(
            uri,
            text,
            changes,
            cancellationToken).Document;
    }

    public AkburaDocumentContext OpenOrChangeDocumentContext(
        Uri uri,
        SourceText text,
        IReadOnlyList<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        return OpenOrChangeDocumentContext(
            DefaultProjectId,
            uri,
            text,
            changes,
            cancellationToken);
    }

    public AkburaDocumentContext OpenOrChangeDocumentContext(
        AkburaProjectId projectId,
        Uri uri,
        SourceText text,
        IReadOnlyList<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;
        AkburaDocumentContext result;

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution =
                _currentSolution;

            if (oldSolution.TryGetDocument(
                    uri,
                    out var oldDocument))
            {
                if (oldDocument.ProjectId != projectId)
                {
                    throw new InvalidOperationException(
                        $"Document '{uri}' belongs to project " +
                        $"'{oldDocument.ProjectId}', but project " +
                        $"'{projectId}' was requested.");
                }

                var oldProject =
                    oldSolution.GetRequiredProject(
                        projectId);

                var newDocument =
                    oldDocument.WithText(
                        text,
                        changes,
                        cancellationToken);

                if (ReferenceEquals(
                        newDocument,
                        oldDocument))
                {
                    return new AkburaDocumentContext(
                        oldProject,
                        oldDocument);
                }

                var newProject =
                    oldProject.ReplaceDocument(
                        newDocument);

                var newSolution =
                    RebuildProjectReferences(
                        oldSolution.WithProject(
                            newProject));

                newProject =
                    newSolution.GetRequiredProject(
                        projectId);

                _currentSolution =
                    newSolution;

                eventArgs =
                    new AkburaWorkspaceChangedEventArgs(
                        oldDocument.IsOpen
                            ? AkburaWorkspaceChangeKind.DocumentChanged
                            : AkburaWorkspaceChangeKind.DocumentOpened,
                        oldSolution,
                        newSolution,
                        newProject.Id,
                        newDocument.Id);

                result =
                    new AkburaDocumentContext(
                        newProject,
                        newDocument);
            }
            else
            {
                var oldProject =
                    oldSolution.GetRequiredProject(
                        projectId);

                var newDocument =
                    AkburaDocumentSnapshot.Create(
                        oldProject.Id,
                        uri,
                        text,
                        oldProject.Context.RootNamespace,
                        oldProject.Context.ProjectDirectory,
                        cancellationToken);

                var newProject =
                    oldProject.AddDocument(
                        newDocument);

                var newSolution =
                    RebuildProjectReferences(
                        oldSolution.WithProject(
                            newProject));

                newProject =
                    newSolution.GetRequiredProject(
                        projectId);

                _currentSolution =
                    newSolution;

                eventArgs =
                    new AkburaWorkspaceChangedEventArgs(
                        AkburaWorkspaceChangeKind.DocumentOpened,
                        oldSolution,
                        newSolution,
                        newProject.Id,
                        newDocument.Id);

                result =
                    new AkburaDocumentContext(
                        newProject,
                        newDocument);
            }
        }

        Changed?.Invoke(
            this,
            eventArgs);

        return result;
    }

    public AkburaDocumentSnapshot OpenDocument(
        AkburaProjectId projectId,
        Uri uri,
        SourceText text,
        CancellationToken cancellationToken = default)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs eventArgs;
        AkburaDocumentSnapshot result;

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var project =
                oldSolution.GetRequiredProject(projectId);

            if (project.TryGetDocument(uri, out var oldDocument))
            {
                result = oldDocument.WithText(
                    text,
                    changes: null,
                    cancellationToken);

                project = project.ReplaceDocument(result);
            }
            else
            {
                result = AkburaDocumentSnapshot.Create(
                    projectId,
                    uri,
                    text,
                    project.Context.RootNamespace,
                    project.Context.ProjectDirectory,
                    cancellationToken);

                project = project.AddDocument(result);
            }

            var newSolution =
                RebuildProjectReferences(
                    oldSolution.WithProject(project));

            project = newSolution.GetRequiredProject(
                projectId);

            _currentSolution = newSolution;

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentOpened,
                oldSolution,
                newSolution,
                project.Id,
                result.Id);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    public AkburaDocumentSnapshot ChangeDocument(
        AkburaDocumentId documentId,
        SourceText newText,
        IReadOnlyList<TextChangeRange>? changes = null,
        CancellationToken cancellationToken = default)
    {
        if (newText == null)
        {
            throw new ArgumentNullException(nameof(newText));
        }
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs eventArgs;
        AkburaDocumentSnapshot result;

        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var oldDocument =
                oldSolution.GetRequiredDocument(documentId);

            var project = oldSolution.GetRequiredProject(
                oldDocument.ProjectId);

            result = oldDocument.WithText(
                newText,
                changes,
                cancellationToken);

            var newProject =
                project.ReplaceDocument(result);

            var newSolution =
                RebuildProjectReferences(
                    oldSolution.WithProject(newProject));

            newProject = newSolution.GetRequiredProject(
                project.Id);

            _currentSolution = newSolution;

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentChanged,
                oldSolution,
                newSolution,
                newProject.Id,
                result.Id);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    public void CloseDocument(
        AkburaDocumentId documentId)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;

        lock (_gate)
        {
            var oldSolution = _currentSolution;

            if (!oldSolution.TryGetDocument(
                    documentId,
                    out var oldDocument) ||
                !oldDocument.IsOpen)
            {
                return;
            }

            var project = oldSolution.GetRequiredProject(
                oldDocument.ProjectId);

            var closedDocument =
                oldDocument.WithOpenState(isOpen: false);

            var newProject =
                project.ReplaceDocument(closedDocument);

            var newSolution =
                RebuildProjectReferences(
                    oldSolution.WithProject(newProject));

            newProject = newSolution.GetRequiredProject(
                project.Id);

            _currentSolution = newSolution;

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentClosed,
                oldSolution,
                newSolution,
                newProject.Id,
                documentId);
        }

        Changed?.Invoke(this, eventArgs);
    }

    public void RemoveDocument(
        AkburaDocumentId documentId)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;

        lock (_gate)
        {
            var oldSolution = _currentSolution;

            if (!oldSolution.TryGetDocument(
                    documentId,
                    out var document))
            {
                return;
            }

            var project = oldSolution.GetRequiredProject(
                document.ProjectId);

            var newProject =
                project.RemoveDocument(documentId);

            var newSolution =
                RebuildProjectReferences(
                    oldSolution.WithProject(newProject));

            _currentSolution = newSolution;

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentRemoved,
                oldSolution,
                newSolution,
                newProject.Id,
                documentId);
        }

        Changed?.Invoke(this, eventArgs);
    }

    public bool TryGetDocument(
        AkburaDocumentId documentId,
        out AkburaDocumentSnapshot document)
    {
        ThrowIfDisposed();

        lock (_gate)
        {
            return _currentSolution.TryGetDocument(
                documentId,
                out document);
        }
    }

    public bool TryGetDocument(
        Uri uri,
        out AkburaDocumentSnapshot document)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }
        ThrowIfDisposed();

        lock (_gate)
        {
            return _currentSolution.TryGetDocument(
                uri,
                out document);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _isDisposed = true;
        }
    }

    private static AkburaSolutionSnapshot
        RebuildProjectReferences(
            AkburaSolutionSnapshot solution)
    {
        var rebuiltProjects =
            new Dictionary<
                AkburaProjectId,
                AkburaProjectSnapshot>();
        var visiting =
            new HashSet<AkburaProjectId>();

        AkburaProjectSnapshot Rebuild(
            AkburaProjectId projectId)
        {
            if (rebuiltProjects.TryGetValue(
                    projectId,
                    out var rebuiltProject))
            {
                return rebuiltProject;
            }

            var project =
                solution.GetRequiredProject(projectId);

            /*
             * Roslyn rejects cyclic project references. Keep this guard so
             * a temporarily inconsistent host snapshot cannot recurse
             * forever while the solution is being reloaded.
             */
            if (!visiting.Add(projectId))
            {
                return project;
            }

            var references =
                ImmutableArray.CreateBuilder<
                    AkburaCompilationReference>();
            var previousReferences =
                project.Compilation
                    .CompilationReferences;

            foreach (var projectReference in
                     project.Context.ProjectReferences)
            {
                var referencedProjectId =
                    AkburaProjectId.FromRoslyn(
                        projectReference.ProjectId);

                if (!solution.TryGetProject(
                        referencedProjectId,
                        out _))
                {
                    continue;
                }

                var referencedCompilation =
                    Rebuild(referencedProjectId)
                        .Compilation;
                var referenceIndex = references.Count;
                references.Add(
                    referenceIndex < previousReferences.Length
                        ? previousReferences[referenceIndex]
                            .WithCompilation(
                                referencedCompilation)
                        : referencedCompilation.ToReference());
            }

            visiting.Remove(projectId);

            rebuiltProject =
                project.WithCompilationReferences(
                    references.ToImmutable());
            rebuiltProjects.Add(
                projectId,
                rebuiltProject);
            return rebuiltProject;
        }

        foreach (var projectId in
                 solution.Projects.Keys)
        {
            _ = Rebuild(projectId);
        }

        foreach (var project in
                 rebuiltProjects.Values)
        {
            solution = solution.WithProject(project);
        }

        return solution;
    }

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(AkburaWorkspace));
        }
    }
}
