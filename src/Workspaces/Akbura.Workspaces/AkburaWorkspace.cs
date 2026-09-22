using Akbura.Language;
using Akbura.Pools;
using Akbura.Workspaces.Resources;
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
    private readonly object _mutationGate = new();
    private AkburaSolutionSnapshot _currentSolution;
    private int _disposeState;

    public AkburaWorkspace() : this(ProjectContext.CreateSyntaxOnly())
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

    public AkburaSolutionSnapshot CurrentSolution =>
        Volatile.Read(ref _currentSolution);

    public IAkburaLanguageServices LanguageServices { get; }

    public event EventHandler<AkburaWorkspaceChangedEventArgs>?
        Changed;

    public AkburaProjectSnapshot AddOrUpdateProject(ProjectContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs;
        AkburaProjectSnapshot result;

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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

            PublishSolution(newSolution);

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                kind,
                oldSolution,
                newSolution,
                result.Id);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    public void RemoveProject(AkburaProjectId projectId)
    {
        ThrowIfDisposed();
        AkburaWorkspaceChangedEventArgs? eventArgs = null;

        lock (_mutationGate)
        {
            ThrowIfDisposed();
            var oldSolution = _currentSolution;
            if (!oldSolution.TryGetProject(projectId, out _))
            {
                return;
            }

            var newSolution = RebuildProjectReferences(
                oldSolution.RemoveProject(projectId));
            PublishSolution(newSolution);
            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectRemoved,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
    }
    public AkburaDocumentSnapshot OpenOrChangeDocument(Uri uri, SourceText text, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
    {
        return OpenOrChangeDocumentContext(
            uri,
            text,
            changes,
            cancellationToken).Document;
    }

    public AkburaDocumentContext OpenOrChangeDocumentContext(Uri uri, SourceText text, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
    {
        return OpenOrChangeDocumentContext(
            DefaultProjectId,
            uri,
            text,
            changes,
            cancellationToken);
    }

    public AkburaDocumentContext OpenOrChangeDocumentContext(AkburaProjectId projectId, Uri uri, SourceText text, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
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

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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
                        oldSolution,
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

                PublishSolution(newSolution);

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
                        newSolution,
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

                PublishSolution(newSolution);

                eventArgs =
                    new AkburaWorkspaceChangedEventArgs(
                        AkburaWorkspaceChangeKind.DocumentOpened,
                        oldSolution,
                        newSolution,
                        newProject.Id,
                        newDocument.Id);

                result =
                    new AkburaDocumentContext(
                        newSolution,
                        newProject,
                        newDocument);
            }
        }

        Changed?.Invoke(
            this,
            eventArgs);

        return result;
    }

    public AkburaDocumentSnapshot OpenDocument(AkburaProjectId projectId, Uri uri, SourceText text, CancellationToken cancellationToken = default)
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

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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

            PublishSolution(newSolution);

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

    /// <summary>
    /// Adds or updates all supplied project documents in one immutable
    /// project transition. Syntax trees and project references are rebuilt
    /// once after every document has been parsed.
    /// </summary>
    public AkburaProjectSnapshot SynchronizeProjectDocuments(AkburaProjectId projectId, ImmutableArray<AkburaDocumentInput> inputs, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;
        AkburaProjectSnapshot result;

        lock (_mutationGate)
        {
            ThrowIfDisposed();

            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var oldProject = oldSolution.GetRequiredProject(projectId);
            var documents = oldProject.Documents;
            var changed = false;

            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (input.Uri == null || input.Text == null)
                {
                    throw new ArgumentException(
                        "Project synchronization inputs must contain a URI and text.",
                        nameof(inputs));
                }

                if (TryGetDocument(
                        documents,
                        input.Uri,
                        out var oldDocument))
                {
                    var newDocument = oldDocument.WithText(
                        input.Text,
                        changes: null,
                        cancellationToken);
                    if (ReferenceEquals(newDocument, oldDocument))
                    {
                        continue;
                    }

                    documents = documents.SetItem(
                        oldDocument.Id,
                        newDocument);
                }
                else
                {
                    var newDocument = AkburaDocumentSnapshot.Create(
                        projectId,
                        input.Uri,
                        input.Text,
                        oldProject.Context.RootNamespace,
                        oldProject.Context.ProjectDirectory,
                        cancellationToken);
                    documents = documents.Add(
                        newDocument.Id,
                        newDocument);
                }

                changed = true;
            }

            if (!changed)
            {
                return oldProject;
            }

            var newProject = oldProject.WithDocuments(documents);
            var newSolution = RebuildProjectReferences(
                oldSolution.WithProject(newProject));
            result = newSolution.GetRequiredProject(projectId);
            PublishSolution(newSolution);

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectChanged,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    internal AkburaProjectSnapshot SynchronizeProjectResourceDocuments(AkburaProjectId projectId, ImmutableArray<ResourceDocumentInput> inputs, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;
        AkburaProjectSnapshot result;

        lock (_mutationGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var oldProject = oldSolution.GetRequiredProject(projectId);
            var builder = ImmutableDictionary.CreateBuilder<
                ResourceDictionaryIdentity,
                ResourceDocumentSnapshot>();

            foreach (var input in inputs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var identity = input.DictionaryIdentity;
                ResourceDocumentSnapshot snapshot;
                if (oldProject.ResourceDocuments.TryGetValue(
                        identity,
                        out var oldDocument) &&
                    DocumentUri.Equals(oldDocument.Uri, input.Uri) &&
                    string.Equals(
                        oldDocument.Input.ProjectKey,
                        input.ProjectKey,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        oldDocument.Input.TargetFramework,
                        input.TargetFramework,
                        StringComparison.Ordinal))
                {
                    snapshot = oldDocument.Version == input.Version &&
                        oldDocument.Text.ContentEquals(input.Text)
                            ? oldDocument
                            : oldDocument.WithText(
                                input.Text,
                                input.Version,
                                changes: null,
                                cancellationToken);
                }
                else
                {
                    snapshot = ResourceDocumentSnapshot.Create(
                        input,
                        cancellationToken);
                }

                builder[identity] = snapshot;
            }

            var resourceDocuments = builder.ToImmutable();
            if (HaveSameResourceDocuments(
                    oldProject.ResourceDocuments,
                    resourceDocuments))
            {
                return oldProject;
            }

            result = oldProject.WithResourceDocuments(resourceDocuments);
            var newSolution = oldSolution.WithProject(result);
            PublishSolution(newSolution);
            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectChanged,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    internal ResourceDocumentSnapshot OpenOrChangeResourceDocument(AkburaProjectId projectId, ResourceDocumentInput input, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        AkburaWorkspaceChangedEventArgs eventArgs;
        ResourceDocumentSnapshot result;

        lock (_mutationGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var project = WithResourceDocument(
                oldSolution.GetRequiredProject(projectId),
                input,
                changes,
                cancellationToken,
                out result);
            var newSolution = oldSolution.WithProject(project);
            PublishSolution(newSolution);
            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectChanged,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
        return result;
    }

    internal void OpenOrChangeResourceDocuments(ImmutableArray<(AkburaProjectId ProjectId, ResourceDocumentInput Input)> updates, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (updates.IsDefault)
        {
            throw new ArgumentException(
                "Resource document updates must be initialized.",
                nameof(updates));
        }

        if (updates.IsEmpty)
        {
            return;
        }

        ImmutableArray<AkburaProjectId> changedProjects;
        AkburaSolutionSnapshot oldSolution;
        AkburaSolutionSnapshot newSolution;
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            oldSolution = _currentSolution;
            newSolution = oldSolution;
            var changed = ImmutableArray.CreateBuilder<AkburaProjectId>(
                updates.Length);
            foreach (var update in updates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!newSolution.TryGetProject(
                        update.ProjectId,
                        out var project))
                {
                    continue;
                }

                project = WithResourceDocument(
                    project,
                    update.Input,
                    changes,
                    cancellationToken,
                    out _);
                newSolution = newSolution.WithProject(project);
                changed.Add(update.ProjectId);
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (changed.Count == 0)
            {
                return;
            }

            changedProjects = changed.ToImmutable();
            PublishSolution(newSolution);
        }

        foreach (var projectId in changedProjects)
        {
            Changed?.Invoke(
                this,
                new AkburaWorkspaceChangedEventArgs(
                    AkburaWorkspaceChangeKind.ProjectChanged,
                    oldSolution,
                    newSolution,
                    projectId));
        }
    }

    internal void RemoveResourceDocument(AkburaProjectId projectId, Uri uri)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        ThrowIfDisposed();
        AkburaWorkspaceChangedEventArgs? eventArgs = null;
        lock (_mutationGate)
        {
            var oldSolution = _currentSolution;
            var project = oldSolution.GetRequiredProject(projectId);
            var documents = project.ResourceDocuments;
            ResourceDictionaryIdentity? identity = null;
            foreach (var pair in documents)
            {
                if (DocumentUri.Equals(pair.Value.Uri, uri))
                {
                    identity = pair.Key;
                    break;
                }
            }

            if (identity == null)
            {
                return;
            }

            project = project.WithResourceDocuments(
                documents.Remove(identity.Value));
            var newSolution = oldSolution.WithProject(project);
            PublishSolution(newSolution);
            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectChanged,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
    }

    private static AkburaProjectSnapshot WithResourceDocument(AkburaProjectSnapshot project, ResourceDocumentInput input, IReadOnlyList<TextChangeRange>? changes, CancellationToken cancellationToken, out ResourceDocumentSnapshot result)
    {
        var documents = project.ResourceDocuments;
        foreach (var pair in documents)
        {
            if (DocumentUri.Equals(pair.Value.Uri, input.Uri) &&
                pair.Key != input.DictionaryIdentity)
            {
                documents = documents.Remove(pair.Key);
                break;
            }
        }

        if (documents.TryGetValue(
                input.DictionaryIdentity,
                out var oldDocument))
        {
            result = oldDocument.WithText(
                input.Text,
                input.Version,
                changes,
                cancellationToken);
        }
        else
        {
            result = ResourceDocumentSnapshot.Create(
                input,
                cancellationToken);
        }

        return project.WithResourceDocuments(
            documents.SetItem(
                input.DictionaryIdentity,
                result));
    }

    internal bool TryRestoreResourceDocument(AkburaProjectId projectId, Uri uri, SourceText expectedText, ResourceDocumentInput? persistedInput, CancellationToken cancellationToken = default)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }

        if (expectedText == null)
        {
            throw new ArgumentNullException(nameof(expectedText));
        }

        if (persistedInput is { } input &&
            !DocumentUri.Equals(input.Uri, uri))
        {
            throw new ArgumentException(
                "The persisted resource input belongs to another URI.",
                nameof(persistedInput));
        }

        ThrowIfDisposed();
        AkburaWorkspaceChangedEventArgs eventArgs;
        lock (_mutationGate)
        {
            ThrowIfDisposed();
            cancellationToken.ThrowIfCancellationRequested();

            var oldSolution = _currentSolution;
            var project = oldSolution.GetRequiredProject(projectId);
            var documents = project.ResourceDocuments;
            ResourceDictionaryIdentity? identity = null;
            ResourceDocumentSnapshot? current = null;
            foreach (var pair in documents)
            {
                if (DocumentUri.Equals(pair.Value.Uri, uri))
                {
                    identity = pair.Key;
                    current = pair.Value;
                    break;
                }
            }

            if (identity == null ||
                current == null ||
                !current.Text.ContentEquals(expectedText))
            {
                return false;
            }

            documents = documents.Remove(identity.Value);
            if (persistedInput is { } persisted)
            {
                var restored = identity.Value ==
                        persisted.DictionaryIdentity
                    ? current.WithText(
                        persisted.Text,
                        persisted.Version,
                        changes: null,
                        cancellationToken)
                    : ResourceDocumentSnapshot.Create(
                        persisted,
                        cancellationToken);
                documents = documents.SetItem(
                    persisted.DictionaryIdentity,
                    restored);
            }

            project = project.WithResourceDocuments(documents);
            var newSolution = oldSolution.WithProject(project);
            PublishSolution(newSolution);
            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.ProjectChanged,
                oldSolution,
                newSolution,
                projectId);
        }

        Changed?.Invoke(this, eventArgs);
        return true;
    }

    public AkburaDocumentSnapshot ChangeDocument(AkburaDocumentId documentId, SourceText newText, IReadOnlyList<TextChangeRange>? changes = null, CancellationToken cancellationToken = default)
    {
        if (newText == null)
        {
            throw new ArgumentNullException(nameof(newText));
        }
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs eventArgs;
        AkburaDocumentSnapshot result;

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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

            PublishSolution(newSolution);

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

    public void CloseDocument(AkburaDocumentId documentId)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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

            PublishSolution(newSolution);

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentClosed,
                oldSolution,
                newSolution,
                newProject.Id,
                documentId);
        }

        Changed?.Invoke(this, eventArgs);
    }

    public void RemoveDocument(AkburaDocumentId documentId)
    {
        ThrowIfDisposed();

        AkburaWorkspaceChangedEventArgs? eventArgs = null;

        lock (_mutationGate)
        {
            ThrowIfDisposed();

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

            PublishSolution(newSolution);

            eventArgs = new AkburaWorkspaceChangedEventArgs(
                AkburaWorkspaceChangeKind.DocumentRemoved,
                oldSolution,
                newSolution,
                newProject.Id,
                documentId);
        }

        Changed?.Invoke(this, eventArgs);
    }

    public bool TryGetDocument(AkburaDocumentId documentId, out AkburaDocumentSnapshot document)
    {
        ThrowIfDisposed();

        var solution = Volatile.Read(
            ref _currentSolution);

        return solution.TryGetDocument(
            documentId,
            out document);
    }

    public bool TryGetDocument(Uri uri, out AkburaDocumentSnapshot document)
    {
        if (uri == null)
        {
            throw new ArgumentNullException(nameof(uri));
        }
        ThrowIfDisposed();

        var solution = Volatile.Read(
            ref _currentSolution);

        return solution.TryGetDocument(
            uri,
            out document);
    }

    public void Dispose()
    {
        lock (_mutationGate)
        {
            Volatile.Write(
                ref _disposeState,
                1);
        }
    }

    private void PublishSolution(AkburaSolutionSnapshot solution)
    {
        Volatile.Write(
            ref _currentSolution,
            solution);
    }

    private static AkburaSolutionSnapshot RebuildProjectReferences(AkburaSolutionSnapshot solution)
    {
        var rebuiltProjects =
            new Dictionary<
                AkburaProjectId,
                AkburaProjectSnapshot>();
        var visiting =
            new HashSet<AkburaProjectId>();

        AkburaProjectSnapshot Rebuild(AkburaProjectId projectId)
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

            using var references =
                ImmutableArrayBuilder<
                    AkburaCompilationReference>.Rent();
            var previousReferences =
                project.Compilation
                    .CompilationReferences;

            foreach (var projectReference in project.Context.ProjectReferences)
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

        foreach (var projectId in solution.Projects.Keys)
        {
            _ = Rebuild(projectId);
        }

        foreach (var project in rebuiltProjects.Values)
        {
            solution = solution.WithProject(project);
        }

        return solution;
    }

    private static bool TryGetDocument(ImmutableDictionary<AkburaDocumentId, AkburaDocumentSnapshot> documents, Uri uri, out AkburaDocumentSnapshot document)
    {
        foreach (var candidate in documents.Values)
        {
            if (DocumentUri.Equals(candidate.Uri, uri))
            {
                document = candidate;
                return true;
            }
        }

        document = null!;
        return false;
    }

    private static bool HaveSameResourceDocuments(ImmutableDictionary<ResourceDictionaryIdentity, ResourceDocumentSnapshot> left, ImmutableDictionary<ResourceDictionaryIdentity, ResourceDocumentSnapshot> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var document) ||
                !ReferenceEquals(pair.Value, document))
            {
                return false;
            }
        }

        return true;
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(
                ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(
                nameof(AkburaWorkspace));
        }
    }
}
