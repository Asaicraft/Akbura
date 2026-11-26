using Akbura.Workspaces.MSBuild;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;

namespace Akbura.Mcp;

/// <summary>
/// Tracks build inputs and saved source files that affect the loaded project snapshot.
/// </summary>
internal sealed class ProjectInputs : IDisposable
{
    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static readonly string[] AncestorInputs =
        ["Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props", "global.json", "NuGet.Config"];

    private readonly Dictionary<string, FileRevision?> _buildInputs = new(PathComparer);
    private readonly ConcurrentDictionary<string, byte> _membershipChanges = new(PathComparer);
    private readonly Dictionary<string, SourceFile> _sources = new(PathComparer);
    private readonly List<FileSystemWatcher> _watchers = [];
    private string? _reloadReason;

    public ProjectInputs(Solution solution, ProjectId selectedProject, MsBuildProjectSettings settings, DateTime loadStartedAt)
    {
        foreach (var path in settings.EvaluationInputs)
        {
            TrackBuildInput(path);
        }

        var directories = new HashSet<string>(PathComparer);
        var references = solution.GetProjectDependencyGraph().GetProjectsThatThisProjectTransitivelyDependsOn(selectedProject);
        foreach (var project in solution.Projects)
        {
            if (project.Id != selectedProject && !references.Contains(project.Id))
            {
                continue;
            }
            if (project.FilePath is { } projectFile)
            {
                TrackBuildInput(projectFile);
                var directory = Path.GetDirectoryName(projectFile)!;
                directories.Add(directory);
                TrackBuildInput(Path.Combine(directory, "obj", "project.assets.json"));
                for (var ancestor = new DirectoryInfo(directory); ancestor != null; ancestor = ancestor.Parent)
                {
                    foreach (var name in AncestorInputs)
                    {
                        TrackBuildInput(Path.Combine(ancestor.FullName, name));
                    }
                }
            }

            foreach (var reference in project.MetadataReferences)
            {
                if (reference is PortableExecutableReference { FilePath: { } path })
                {
                    TrackBuildInput(path);
                }
            }
            foreach (var config in project.AnalyzerConfigDocuments)
            {
                TrackBuildInput(config.FilePath);
            }
            foreach (var document in project.Documents)
            {
                TrackSource(document, isAdditional: false, loadStartedAt);
            }
            foreach (var document in project.AdditionalDocuments)
            {
                if (RoslynProjectDocumentLoader.IsAkburaDocument(document.FilePath))
                {
                    TrackSource(document, isAdditional: true, loadStartedAt);
                }
            }
        }

        try
        {
            foreach (var directory in directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                };
                _watchers.Add(watcher);
                watcher.Changed += OnChanged;
                watcher.Created += OnChanged;
                watcher.Deleted += OnChanged;
                watcher.Renamed += (sender, args) =>
                {
                    RecordChange(args.OldFullPath);
                    OnChanged(sender, args);
                };
                watcher.Error += (_, _) => RequireReload("Source change notifications were interrupted.");
                watcher.EnableRaisingEvents = true;
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public Dictionary<string, SourceFile>.ValueCollection Sources => _sources.Values;

    public string? GetReloadReason(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _reloadReason) is { } reason)
        {
            return reason;
        }

        foreach (var (path, original) in _buildInputs)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (FileRevision.Read(path) != original)
            {
                return RequireReload($"Build input '{path}' changed.");
            }
        }

        foreach (var path in _membershipChanges.Keys)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _membershipChanges.TryRemove(path, out _);
            if (File.Exists(path))
            {
                return RequireReload($"Project input '{path}' was added.");
            }
        }
        return null;
    }

    public string RequireReload(string message)
    {
        var reason = message + " Restart the MCP server after preparing the project.";
        return Interlocked.CompareExchange(ref _reloadReason, reason, null) ?? reason;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Dispose();
        }
    }

    private void TrackBuildInput(string? path)
    {
        if (!string.IsNullOrWhiteSpace(path) && Path.IsPathFullyQualified(path))
        {
            var absolutePath = Path.GetFullPath(path);
            if (!_buildInputs.ContainsKey(absolutePath))
            {
                _buildInputs.Add(absolutePath, FileRevision.Read(absolutePath));
            }
        }
    }

    private void TrackSource(TextDocument document, bool isAdditional, DateTime loadStartedAt)
    {
        if (document.FilePath is not { } path || !Path.IsPathFullyQualified(path))
        {
            return;
        }
        path = Path.GetFullPath(path);
        if (!_sources.TryGetValue(path, out var file))
        {
            file = new SourceFile(path);
            var revision = FileRevision.Read(path);
            // Roslyn already loaded these sources from disk. Re-read any file that
            // might have been saved during the design-time load.
            if (revision is { } current && current.LastWriteUtc < loadStartedAt && current.CreatedUtc < loadStartedAt)
            {
                file.Revision = current;
            }
            _sources.Add(path, file);
        }
        file.Documents.Add(new(document.Id, isAdditional));
    }

    private void OnChanged(object sender, FileSystemEventArgs args) =>
        RecordChange(args.FullPath, args.ChangeType is WatcherChangeTypes.Created or WatcherChangeTypes.Renamed);

    private void RecordChange(string path, bool membershipChange = false)
    {
        if (_sources.TryGetValue(path, out var source))
        {
            source.MarkChanged();
            return;
        }

        if (!membershipChange || _buildInputs.ContainsKey(path))
        {
            return;
        }

        var directory = Path.GetDirectoryName(path);
        while (directory != null)
        {
            if (Path.GetFileName(directory) is "bin" or "obj" or ".git" or ".vs")
            {
                return;
            }
            directory = Path.GetDirectoryName(directory);
        }
        var extension = Path.GetExtension(path);
        if (extension.Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".csproj", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".props", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".targets", StringComparison.OrdinalIgnoreCase) ||
            RoslynProjectDocumentLoader.IsAkburaDocument(path))
        {
            _membershipChanges.TryAdd(path, 0);
        }
    }

    /// <summary>
    /// Captures the timestamps and length used to detect changes to a file.
    /// </summary>
    internal readonly record struct FileRevision(DateTime LastWriteUtc, DateTime CreatedUtc, long Length)
    {
        public static FileRevision? Read(string path)
        {
            var file = new FileInfo(path);
            return file.Exists ? new(file.LastWriteTimeUtc, file.CreationTimeUtc, file.Length) : null;
        }
    }

    /// <summary>
    /// Tracks one source file and its registrations in the loaded Roslyn solution.
    /// </summary>
    internal sealed class SourceFile(string path)
    {
        private long _notifications;
        public string Path { get; } = path;
        public List<SourceDocument> Documents { get; } = [];
        public FileRevision? Revision { get; set; }
        public long AppliedNotification { get; set; }
        public long Notification => Volatile.Read(ref _notifications);
        public void MarkChanged() => Interlocked.Increment(ref _notifications);
    }

    /// <summary>
    /// Identifies the source file's registration as a C# or additional project document.
    /// </summary>
    internal readonly record struct SourceDocument(DocumentId Id, bool IsAdditional);
}
