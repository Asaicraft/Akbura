using Akbura.Workspaces;
using Akbura.Workspaces.MSBuild;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Text.Json;

namespace Akbura.Mcp;

internal sealed class ProjectSession : IDisposable
{
    private readonly MsBuildProjectContextProvider _loader;
    private readonly ProjectInputs _inputs;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly RoslynProjectContextFactory _contextFactory = new();
    private readonly RoslynProjectDocumentLoader _documentLoader = new();
    private Solution _roslynSolution;

    private ProjectSession(ProjectOptions options, MsBuildProjectSettings settings, MsBuildProjectContextProvider loader,
        Solution roslynSolution, AkburaLoadedProject loaded, ProjectInputs inputs, ImmutableArray<DependencyVersion> packages)
    {
        Options = options;
        Settings = settings;
        _loader = loader;
        _roslynSolution = roslynSolution;
        _inputs = inputs;
        Packages = packages;
        LoadDiagnostics = loaded.Diagnostics;
        Workspace = new(loaded.Context);
    }

    public ProjectOptions Options { get; }

    public MsBuildProjectSettings Settings { get; }

    public AkburaWorkspace Workspace { get; }

    public ImmutableArray<DependencyVersion> Packages { get; }

    public ImmutableArray<AkburaProjectLoadDiagnostic> LoadDiagnostics { get; }

    public static async Task<ProjectSession> OpenAsync(ProjectOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var settings = MsBuildProjectSettings.Evaluate(options.ProjectPath, options.Configuration, options.TargetFramework);
        var assetsRevision = settings.AssetsFile == null ? null : ProjectInputs.FileRevision.Read(settings.AssetsFile);
        var packages = ReadPackages(settings);
        var loader = new MsBuildProjectContextProvider(settings.GlobalProperties, workspaceDiagnosticsActive: true);
        var loadStartedAt = DateTime.UtcNow;
        ProjectSession? session = null;
        ProjectInputs? inputs = null;
        try
        {
            var loaded = await loader.LoadProjectAsync(options.ProjectPath, cancellationToken).ConfigureAwait(false);
            if (loaded.Diagnostics.Any(static diagnostic => diagnostic.Severity == AkburaProjectLoadDiagnosticSeverity.Error))
            {
                throw new InvalidOperationException("MSBuild could not load the project. " +
                    string.Join(Environment.NewLine, loaded.Diagnostics.Select(static diagnostic => diagnostic.Message)));
            }

            var solution = loader.CurrentSolution;
            inputs = new ProjectInputs(solution, loaded.Context.RoslynProjectId, settings, loadStartedAt);
            if (settings.AssetsFile != null && ProjectInputs.FileRevision.Read(settings.AssetsFile) != assetsRevision)
            {
                throw new InvalidOperationException("Project assets changed during startup. Restart the MCP server.");
            }

            session = new ProjectSession(options, settings, loader, solution, loaded, inputs, packages);
            var view = await session.CaptureAsync(cancellationToken, loaded).ConfigureAwait(false);
            if (view.Snapshot == null)
            {
                throw new InvalidOperationException(view.Message ?? "The project could not be synchronized at startup.");
            }

            return session;
        }
        catch
        {
            if (session != null)
            {
                session.Dispose();
            }
            else
            {
                inputs?.Dispose();
                loader.Dispose();
            }
            throw;
        }
    }

    public async Task<ProjectView> CaptureAsync(CancellationToken cancellationToken, AkburaLoadedProject? initialProject = null)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_inputs.GetReloadReason(cancellationToken) is { } reload)
            {
                return new(null, "reload_required", reload);
            }

            var next = _roslynSolution;
            List<(ProjectInputs.SourceFile File, ProjectInputs.FileRevision Revision, long Notification)>? updatedFiles = null;
            HashSet<ProjectId>? affected = null;
            ProjectDependencyGraph? graph = null;
            foreach (var file in _inputs.Sources)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var notification = file.Notification;
                var revision = ProjectInputs.FileRevision.Read(file.Path);
                if (revision == null)
                {
                    return new(null, "reload_required", _inputs.RequireReload($"Project source '{file.Path}' is missing."));
                }
                if (revision == file.Revision && notification == file.AppliedNotification)
                {
                    continue;
                }

                var text = SourceText.From(await File.ReadAllTextAsync(file.Path, cancellationToken).ConfigureAwait(false));
                if (ProjectInputs.FileRevision.Read(file.Path) != revision)
                {
                    return new(null, "project_not_ready", $"Project source '{file.Path}' is being saved. Retry the request.");
                }
                foreach (var document in file.Documents)
                {
                    var current = document.IsAdditional ? next.GetAdditionalDocument(document.Id) : next.GetDocument(document.Id);
                    if ((await current!.GetTextAsync(cancellationToken).ConfigureAwait(false)).ContentEquals(text))
                    {
                        continue;
                    }

                    next = document.IsAdditional
                        ? next.WithAdditionalDocumentText(document.Id, text, PreservationMode.PreserveIdentity)
                        : next.WithDocumentText(document.Id, text, PreservationMode.PreserveIdentity);
                    var projectId = document.Id.ProjectId;
                    if ((affected ??= []).Add(projectId))
                    {
                        graph ??= _roslynSolution.GetProjectDependencyGraph();
                        affected.UnionWith(graph.GetProjectsThatTransitivelyDependOnThisProject(projectId));
                    }
                }
                (updatedFiles ??= []).Add((file, revision.Value, notification));
            }

            if (initialProject == null && affected == null)
            {
                CommitFileRevisions();
                return new(Workspace.CurrentSolution, "ready", null);
            }

            graph ??= next.GetProjectDependencyGraph();

            var selected = initialProject?.Context.RoslynProjectId;
            var initialReferences = selected == null ? null : graph.GetProjectsThatThisProjectTransitivelyDependsOn(selected);
            var updates = new List<(ProjectContext Context, ImmutableArray<Akbura.Workspaces.Documents.AkburaDocumentInput> Documents)>();
            foreach (var id in graph.GetTopologicallySortedProjects(cancellationToken))
            {
                if (initialProject != null && id == selected && affected?.Contains(id) != true)
                {
                    updates.Add((initialProject.Context, initialProject.Documents));
                }
                else if ((id == selected || initialReferences?.Contains(id) == true || affected?.Contains(id) == true) &&
                    next.GetProject(id) is { Language: LanguageNames.CSharp } project)
                {
                    var context = await _contextFactory.CreateAsync(project, project.FilePath, cancellationToken).ConfigureAwait(false);
                    var documents = await _documentLoader.LoadAsync(project, openTextProvider: null,
                        excludedDocument: null, cancellationToken).ConfigureAwait(false);
                    updates.Add((context, documents));
                }
            }

            if (_inputs.GetReloadReason(cancellationToken) is { } changed)
            {
                return new(null, "reload_required", changed);
            }

            // Publish the prepared changes under the session gate. After publication, every request
            // retains its own immutable solution, including while a later request synchronizes.
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var update in updates)
            {
                var project = Workspace.AddOrUpdateProject(update.Context);
                Workspace.SynchronizeProjectDocuments(project.Id, update.Documents);
            }
            _roslynSolution = next;
            CommitFileRevisions();
            return new(Workspace.CurrentSolution, "ready", null);

            void CommitFileRevisions()
            {
                if (updatedFiles == null) return;
                foreach (var (file, revision, notification) in updatedFiles)
                {
                    file.Revision = revision;
                    file.AppliedNotification = notification;
                }
            }
        }
        catch (IOException exception)
        {
            return new(null, "project_not_ready", exception.Message);
        }
        catch (UnauthorizedAccessException exception)
        {
            return new(null, "project_not_ready", exception.Message);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _inputs.Dispose();
        Workspace.Dispose();
        _loader.Dispose();
        _gate.Dispose();
    }

    private static ImmutableArray<DependencyVersion> ReadPackages(MsBuildProjectSettings settings)
    {
        if (settings.AssetsFile is not { } assetsFile)
        {
            return [];
        }
        if (!File.Exists(assetsFile))
        {
            throw new InvalidOperationException($"Project assets are missing at '{assetsFile}'. Restore and build the selected project before starting the server.");
        }

        using var stream = File.OpenRead(assetsFile);
        using var assets = JsonDocument.Parse(stream);
        var root = assets.RootElement;
        var targetFramework = settings.TargetFramework;
        if (root.TryGetProperty("project", out var project) &&
            project.TryGetProperty("frameworks", out var frameworks))
        {
            foreach (var framework in frameworks.EnumerateObject())
            {
                if (framework.Value.TryGetProperty("targetAlias", out var alias) &&
                    string.Equals(alias.GetString(), targetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    targetFramework = framework.Name;
                    break;
                }
            }
        }

        if (!root.TryGetProperty("targets", out var targets) || !targets.TryGetProperty(targetFramework, out var target))
        {
            throw new InvalidOperationException($"Project assets do not contain '{settings.TargetFramework}'. Restore the selected project for that target framework.");
        }

        var packages = ImmutableArray.CreateBuilder<DependencyVersion>();
        foreach (var entry in target.EnumerateObject())
        {
            if (!entry.Value.TryGetProperty("type", out var type) || type.GetString() != "package") continue;
            var separator = entry.Name.LastIndexOf('/');
            if (separator > 0)
            {
                packages.Add(new(entry.Name[..separator], entry.Name[(separator + 1)..]));
            }
        }
        packages.Sort(static (left, right) => StringComparer.OrdinalIgnoreCase.Compare(left.Name, right.Name));
        return packages.ToImmutable();
    }
}

internal sealed record ProjectView(AkburaSolutionSnapshot? Snapshot, string Status, string? Message);

internal sealed record DependencyVersion(string Name, string Version);
