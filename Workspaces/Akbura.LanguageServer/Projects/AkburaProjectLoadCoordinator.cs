using Akbura.Workspaces.MSBuild;
using System.Collections.Concurrent;
using System.Text.Json;

namespace Akbura.LanguageServer.Projects;

internal sealed class AkburaProjectLoadCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan ReloadDelay =
        TimeSpan.FromMilliseconds(350);

    private readonly AkburaLanguageServerServices _services;
    private readonly AkburaRequestExecutionQueue _queue;
    private readonly IAkburaProjectLoader _loader;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private readonly ConcurrentDictionary<Uri, AkburaProjectLoadRequest>
        _roots = new(AkburaUriComparer.Instance);
    private readonly CancellationTokenSource _shutdown = new();
    private CancellationTokenSource? _reloadCancellation;
    private int _disposeState;

    public AkburaProjectLoadCoordinator(
        AkburaLanguageServerServices services,
        AkburaRequestExecutionQueue queue,
        IAkburaProjectLoader? loader = null)
    {
        _services = services ??
            throw new ArgumentNullException(nameof(services));
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _loader = loader ?? new MsBuildProjectContextProvider();
        _loader.Changed += OnProjectContextChanged;
    }

    public Task StartAsync(
        AkburaServerSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        foreach (var folder in snapshot.WorkspaceFolders.Values)
        {
            var request = new AkburaProjectLoadRequest(
                folder.Uri,
                folder.Name,
                GetExplicitPath(folder.Uri),
                "initialized");
            _roots[folder.Uri] = request;
        }

        return ReloadAllAsync(cancellationToken);
    }

    public async Task UpdateWorkspaceFoldersAsync(
        ImmutableArray<AkburaWorkspaceFolderState> added,
        ImmutableArray<Uri> removed,
        CancellationToken cancellationToken)
    {
        foreach (var uri in removed)
        {
            _roots.TryRemove(uri, out _);
        }

        foreach (var folder in added)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var request = new AkburaProjectLoadRequest(
                folder.Uri,
                folder.Name,
                GetExplicitPath(folder.Uri),
                "workspace folder added");
            _roots[folder.Uri] = request;
            await LoadAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
    }
    public Task HandleWatchedFilesAsync(
        DidChangeWatchedFilesParams parameters,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (!parameters.Changes.Any(static change =>
                IsReloadRelevant(change.Uri)))
        {
            return Task.CompletedTask;
        }

        return ScheduleReloadAsync("watched files", cancellationToken);
    }

    public Task ScheduleReloadAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return Task.CompletedTask;
        }

        var next = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            _shutdown.Token);
        var previous = Interlocked.Exchange(
            ref _reloadCancellation,
            next);
        previous?.Cancel();
        previous?.Dispose();

        return RunDebouncedReloadAsync(reason, next.Token);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _loader.Changed -= OnProjectContextChanged;
        _shutdown.Cancel();
        var reload = Interlocked.Exchange(
            ref _reloadCancellation,
            null);
        reload?.Cancel();
        reload?.Dispose();
        await _loadGate.WaitAsync().ConfigureAwait(false);
        _loadGate.Release();
        _loader.Dispose();
        _loadGate.Dispose();
        _shutdown.Dispose();
    }

    private async Task RunDebouncedReloadAsync(
        string reason,
        CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(ReloadDelay, cancellationToken)
                .ConfigureAwait(false);
            foreach (var pair in _roots.ToArray())
            {
                _roots[pair.Key] = pair.Value with
                {
                    Reason = reason,
                };
            }

            await ReloadAllAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task ReloadAllAsync(
        CancellationToken cancellationToken)
    {
        foreach (var request in _roots.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadAsync(request, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private async Task LoadAsync(
        AkburaProjectLoadRequest request,
        CancellationToken cancellationToken)
    {
        await _loadGate.WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        try
        {
            string? warning;
            var path = AkburaProjectDiscovery.Discover(
                request.WorkspaceFolder,
                request.ExplicitPath,
                out warning);
            if (warning != null)
            {
                await ApplyAsync(
                        new AkburaProjectLoadResult(
                            request.WorkspaceFolder,
                            request.WorkspaceFolderName,
                            SolutionOrProjectPath: null,
                            ImmutableArray<AkburaLoadedProject>.Empty,
                            ImmutableArray<AkburaProjectLoadDiagnostic>.Empty,
                            warning),
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            if (path == null)
            {
                _services.Logger.Log(
                    AkburaServerLogLevel.Information,
                    $"No solution or project was found for " +
                    $"'{request.WorkspaceFolder}'. Syntax-only mode remains active.");
                return;
            }

            var progressToken =
                $"akbura-project-load-{Guid.NewGuid():N}";
            await ReportProgressAsync(
                    progressToken,
                    "begin",
                    $"Loading {Path.GetFileName(path)}",
                    cancellationToken)
                .ConfigureAwait(false);

            ImmutableArray<AkburaLoadedProject> projects;
            try
            {
                var extension = Path.GetExtension(path);
                projects = string.Equals(
                        extension,
                        ".sln",
                        StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(
                        extension,
                        ".slnx",
                        StringComparison.OrdinalIgnoreCase)
                        ? await _loader.LoadSolutionAsync(
                                path,
                                cancellationToken)
                            .ConfigureAwait(false)
                        : ImmutableArray.Create(
                            await _loader.LoadProjectAsync(
                                    path,
                                    cancellationToken)
                                .ConfigureAwait(false));
            }
            catch (Exception exception)
                when (exception is not OperationCanceledException)
            {
                _services.Logger.Log(
                    AkburaServerLogLevel.Error,
                    $"Project load failed for '{path}'.",
                    exception);
                await ApplyAsync(
                        new AkburaProjectLoadResult(
                            request.WorkspaceFolder,
                            request.WorkspaceFolderName,
                            path,
                            ImmutableArray<AkburaLoadedProject>.Empty,
                            ImmutableArray<AkburaProjectLoadDiagnostic>.Empty,
                            exception.Message),
                        cancellationToken)
                    .ConfigureAwait(false);
                await ReportProgressAsync(
                        progressToken,
                        "end",
                        "Project loading failed",
                        cancellationToken)
                    .ConfigureAwait(false);
                return;
            }

            var diagnostics = projects
                .SelectMany(static project => project.Diagnostics)
                .ToImmutableArray();
            await ApplyAsync(
                    new AkburaProjectLoadResult(
                        request.WorkspaceFolder,
                        request.WorkspaceFolderName,
                        path,
                        projects,
                        diagnostics,
                        ErrorMessage: null),
                    cancellationToken)
                .ConfigureAwait(false);
            await ReportProgressAsync(
                    progressToken,
                    "end",
                    $"Loaded {projects.Length} project(s)",
                    cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private async Task ApplyAsync(
        AkburaProjectLoadResult result,
        CancellationToken cancellationToken)
    {
        await _queue.ExecuteAsync<object?>(
                AkburaInternalMethods.ApplyLoadedProjects,
                result,
                cancellationToken)
            .ConfigureAwait(false);
    }

    private Task ReportProgressAsync(
        string token,
        string kind,
        string message,
        CancellationToken cancellationToken)
    {
        var value = JsonSerializer.SerializeToElement(
            new
            {
                kind,
                title = "Akbura",
                message,
            });
        return _services.Client.NotifyAsync(
            "$/progress",
            new ProgressParams
            {
                Token = JsonSerializer.SerializeToElement(token),
                Value = value,
            },
            cancellationToken);
    }

    private string? GetExplicitPath(Uri folder)
    {
        var explicitPath = _services.Options.SolutionPath ??
            _services.Options.ProjectPath;
        if (explicitPath == null)
        {
            return null;
        }

        if (!folder.IsFile)
        {
            return explicitPath;
        }

        var root = Path.GetFullPath(folder.LocalPath)
            .TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar) +
            Path.DirectorySeparatorChar;
        return Path.GetFullPath(explicitPath).StartsWith(
            root,
            StringComparison.OrdinalIgnoreCase)
                ? explicitPath
                : null;
    }

    private void OnProjectContextChanged(
        object? sender,
        ProjectContextChangedEventArgs eventArgs)
    {
        _ = ScheduleReloadAsync(
            eventArgs.Kind.ToString(),
            _shutdown.Token);
    }

    private static bool IsReloadRelevant(string uriText)
    {
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            !uri.IsFile)
        {
            return false;
        }

        var fileName = Path.GetFileName(uri.LocalPath);
        var extension = Path.GetExtension(uri.LocalPath);
        return extension.Equals(".akbura", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".akcss", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".sln", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Directory.Build.props", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Directory.Build.targets", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("Directory.Packages.props", StringComparison.OrdinalIgnoreCase) ||
            fileName.Equals("global.json", StringComparison.OrdinalIgnoreCase);
    }
}
