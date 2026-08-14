using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.Runtime.CompilerServices;

namespace Akbura.VisualStudio.CSharp;

internal sealed class AkburaProjectedCSharpDocumentCache : IDisposable
{
    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private readonly object _gate = new();

    private ConditionalWeakTable<ITextSnapshot, ProjectionSnapshotCache>
        _snapshotCaches = new();

    private bool _disposed;

    public AkburaProjectedCSharpDocumentCache(
        AkburaVisualStudioWorkspace workspaceHost)
    {
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _workspaceHost.ProjectContextChanged += OnProjectContextChanged;
    }

    public Task<AkburaProjectedCSharpDocument?> GetOrCreateAsync(
        ITextSnapshot snapshot,
        AkburaEmbeddedCSharpContext context,
        Func<Task<AkburaProjectedCSharpDocument?>> factory)
    {
        if (snapshot == null)
        {
            throw new ArgumentNullException(nameof(snapshot));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        ProjectionSnapshotCache snapshotCache;
        lock (_gate)
        {
            ThrowIfDisposed();
            snapshotCache = _snapshotCaches.GetValue(
                snapshot,
                static _ => new ProjectionSnapshotCache());
        }

        var key = new ProjectionKey(
            context.OwnerKind,
            context.OwnerSpan,
            context.Kind);
        Task<AkburaProjectedCSharpDocument?> task;
        lock (snapshotCache.Gate)
        {
            if (snapshotCache.Documents.TryGetValue(key, out task!))
            {
                return task;
            }

            try
            {
                task = factory();
            }
            catch (Exception exception)
            {
                task = Task.FromException<AkburaProjectedCSharpDocument?>(
                    exception);
            }

            snapshotCache.Documents.Add(key, task);
        }

        _ = task.ContinueWith(
            completed => RemoveFailedOrEmptyEntry(
                snapshotCache,
                key,
                completed),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        return task;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _snapshotCaches = new ConditionalWeakTable<
                ITextSnapshot,
                ProjectionSnapshotCache>();
        }

        _workspaceHost.ProjectContextChanged -= OnProjectContextChanged;
    }

    private static void RemoveFailedOrEmptyEntry(
        ProjectionSnapshotCache cache,
        ProjectionKey key,
        Task<AkburaProjectedCSharpDocument?> task)
    {
        #pragma warning disable VSTHRD002 // This synchronous continuation only inspects an already completed task.
        var remove = task.IsCanceled ||
            task.IsFaulted ||
            task.Status == TaskStatus.RanToCompletion &&
            task.Result == null;
        #pragma warning restore VSTHRD002

        if (!remove)
        {
            return;
        }

        lock (cache.Gate)
        {
            if (cache.Documents.TryGetValue(key, out var current) &&
                ReferenceEquals(current, task))
            {
                cache.Documents.Remove(key);
            }
        }
    }

    private void OnProjectContextChanged(object? sender, EventArgs eventArgs)
    {
        lock (_gate)
        {
            if (!_disposed)
            {
                _snapshotCaches = new ConditionalWeakTable<
                    ITextSnapshot,
                    ProjectionSnapshotCache>();
            }
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(
                nameof(AkburaProjectedCSharpDocumentCache));
        }
    }

    private sealed class ProjectionSnapshotCache
    {
        public object Gate { get; } = new();

        public Dictionary<ProjectionKey, Task<AkburaProjectedCSharpDocument?>>
            Documents { get; } = new();
    }

    private readonly struct ProjectionKey : IEquatable<ProjectionKey>
    {
        public ProjectionKey(
            Akbura.Language.Syntax.SyntaxKind ownerKind,
            TextSpan ownerSpan,
            AkburaCSharpCompletionContextKind kind)
        {
            OwnerKind = ownerKind;
            OwnerSpan = ownerSpan;
            Kind = kind;
        }

        public Akbura.Language.Syntax.SyntaxKind OwnerKind { get; }

        public TextSpan OwnerSpan { get; }

        public AkburaCSharpCompletionContextKind Kind { get; }

        public bool Equals(ProjectionKey other)
        {
            return OwnerKind == other.OwnerKind &&
                OwnerSpan.Equals(other.OwnerSpan) &&
                Kind == other.Kind;
        }

        public override bool Equals(object? obj)
        {
            return obj is ProjectionKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)OwnerKind;
                hashCode = (hashCode * 397) ^ OwnerSpan.GetHashCode();
                hashCode = (hashCode * 397) ^ (int)Kind;
                return hashCode;
            }
        }
    }
}
