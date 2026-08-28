using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using System.Runtime.CompilerServices;

namespace Akbura.VisualStudio.CSharp;

internal sealed class AkburaProjectedCSharpDocumentCache : IDisposable
{
    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private ConditionalWeakTable<ITextSnapshot, ProjectionSnapshotCache> _snapshotCaches = new();

    private int _disposeState;

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

        ThrowIfDisposed();

        var caches = Volatile.Read(
            ref _snapshotCaches);

        var snapshotCache = caches.GetValue(
            snapshot,
            static _ => new ProjectionSnapshotCache());

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

                if (task == null)
                {
                    task = Task.FromException<
                        AkburaProjectedCSharpDocument?>(
                            new InvalidOperationException(
                                "The projected C# document factory " +
                                "returned a null task."));
                }
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
        if (Interlocked.Exchange(
                ref _disposeState,
                1) != 0)
        {
            return;
        }

        _workspaceHost.ProjectContextChanged -= OnProjectContextChanged;

        Volatile.Write(
            ref _snapshotCaches,
            new ConditionalWeakTable<
                ITextSnapshot,
                ProjectionSnapshotCache>());
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

    private void OnProjectContextChanged(
        object? sender,
        EventArgs eventArgs)
    {
        if (Volatile.Read(
                ref _disposeState) != 0)
        {
            return;
        }

        Volatile.Write(
            ref _snapshotCaches,
            new ConditionalWeakTable<
                ITextSnapshot,
                ProjectionSnapshotCache>());
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(
                ref _disposeState) != 0)
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

    private readonly record struct ProjectionKey(
        Akbura.Language.Syntax.SyntaxKind OwnerKind,
        TextSpan OwnerSpan,
        AkburaCSharpCompletionContextKind Kind);
}
