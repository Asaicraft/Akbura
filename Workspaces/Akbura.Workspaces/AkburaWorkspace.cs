using Microsoft.CodeAnalysis.Text;

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
                oldSolution.WithProject(result);

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

            if (oldSolution.TryGetDocument(
                    uri,
                    out var oldDocument))
            {
                var project = oldSolution.GetRequiredProject(
                    oldDocument.ProjectId);

                result = oldDocument.WithText(
                    text,
                    changes,
                    cancellationToken);

                if (ReferenceEquals(result, oldDocument))
                {
                    return oldDocument;
                }

                var newProject =
                    project.ReplaceDocument(result);

                var newSolution =
                    oldSolution.WithProject(newProject);

                _currentSolution = newSolution;

                eventArgs =
                    new AkburaWorkspaceChangedEventArgs(
                        ReferenceEquals(result, oldDocument)
                            ? AkburaWorkspaceChangeKind.DocumentOpened
                            : AkburaWorkspaceChangeKind.DocumentChanged,
                        oldSolution,
                        newSolution,
                        newProject.Id,
                        result.Id);
            }
            else
            {
                var project =
                    oldSolution.GetRequiredProject(
                        DefaultProjectId);

                result = AkburaDocumentSnapshot.Create(
                    project.Id,
                    uri,
                    text,
                    cancellationToken);

                var newProject =
                    project.AddDocument(result);

                var newSolution =
                    oldSolution.WithProject(newProject);

                _currentSolution = newSolution;

                eventArgs =
                    new AkburaWorkspaceChangedEventArgs(
                        AkburaWorkspaceChangeKind.DocumentOpened,
                        oldSolution,
                        newSolution,
                        newProject.Id,
                        result.Id);
            }
        }

        Changed?.Invoke(this, eventArgs);
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
                    cancellationToken);

                project = project.AddDocument(result);
            }

            var newSolution =
                oldSolution.WithProject(project);

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
                oldSolution.WithProject(newProject);

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
                oldSolution.WithProject(newProject);

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
                oldSolution.WithProject(newProject);

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

    private void ThrowIfDisposed()
    {
        if (_isDisposed)
        {
            throw new ObjectDisposedException(
                nameof(AkburaWorkspace));
        }
    }
}
