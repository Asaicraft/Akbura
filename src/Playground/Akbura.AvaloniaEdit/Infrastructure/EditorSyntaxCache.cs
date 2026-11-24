using Avalonia.Threading;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Akbura.AvaloniaEdit.Infrastructure;

/// <summary>
/// Caches completed syntax in a bounded, direct-mapped table, like GreenNodeCache.
/// The UI thread owns entries and pending requests; only syntax construction runs
/// through the host scheduler. Feature cancellation cancels its wait, not shared work.
/// </summary>
internal sealed class EditorSyntaxCache(EditorServiceQueue services,
    CancellationToken lifetime, Func<EditorSyntaxCache.Key, bool> isCurrent) : IDisposable
{
    internal const int CacheSize = 2;
    internal const int CacheMask = CacheSize - 1;

    private readonly Entry?[] _entries = new Entry[CacheSize];
    private Entry? _accepted;
    private Request? _pending;
    private bool _disposed;

#if DEBUG
    internal EditorPerformance? Performance
    {
        get; init;
    }
#endif

    internal readonly record struct Key(object Buffer, long Revision, string FilePath, SourceText Text);

    internal AkburaSyntacticDocument? Accepted
    {
        get
        {
            DispatcherVerifyAccessDebug();
            return _accepted?.Syntax;
        }
    }

    internal Task<AkburaSyntacticDocument> GetAsync(Key key, CancellationToken token)
    {
        DispatcherVerifyAccessDebug();
        ObjectDisposedException.ThrowIf(_disposed, this);
        token.ThrowIfCancellationRequested();
        var hash = GetCacheHash(key);
        var entry = _entries[hash & CacheMask];
        if (entry != null && entry.Hash == hash && Matches(entry.Key, key))
        {
            if (isCurrent(key))
            {
                _accepted = entry;
                var previous = _pending;
                _pending = null;
                previous?.Cancellation.Cancel();
            }
#if DEBUG
            Performance?.Count("SyntaxCacheHit");
#endif
            return entry.Task.WaitAsync(token);
        }
        if (_pending is { } pending && Matches(pending.Key, key))
        {
#if DEBUG
            Performance?.Count("SyntaxSharedWait");
#endif
            return pending.Completion.Task.WaitAsync(token);
        }

        var basis = _accepted is { } accepted && ReferenceEquals(accepted.Key.Buffer, key.Buffer) &&
            accepted.Key.FilePath == key.FilePath ? accepted.Syntax : null;
        var request = new Request(key, CancellationTokenSource.CreateLinkedTokenSource(lifetime));
        var superseded = _pending;
        _pending = request;
        superseded?.Cancellation.Cancel();
        _ = BuildAsync(request, basis, hash);
        return request.Completion.Task.WaitAsync(token);
    }

    internal static int GetCacheHash(Key key) => HashCode.Combine(
        RuntimeHelpers.GetHashCode(key.Buffer), key.Revision,
        StringComparer.Ordinal.GetHashCode(key.FilePath), RuntimeHelpers.GetHashCode(key.Text));

    private static bool Matches(Key left, Key right) => left.Revision == right.Revision &&
        ReferenceEquals(left.Buffer, right.Buffer) && ReferenceEquals(left.Text, right.Text) &&
        string.Equals(left.FilePath, right.FilePath, StringComparison.Ordinal);

    [Conditional("DEBUG")]
    private static void DispatcherVerifyAccessDebug() => Dispatcher.UIThread.VerifyAccess();

    private async Task BuildAsync(Request request, AkburaSyntacticDocument? basis, int hash)
    {
        AkburaSyntacticDocument? syntax = null;
        Exception? error = null;
        try
        {
            syntax = await services.RunAsync("syntax", 0, token =>
            {
#if DEBUG
                using var measurement = Performance?.Measure("Syntax");
                Performance?.Count(basis == null ? "Parse" : "WithText");
#endif
                return Task.FromResult(basis == null
                    ? AkburaSyntacticDocument.Parse(request.Key.Text, request.Key.FilePath, token)
                    : basis.WithText(request.Key.Text, request.Key.Text.GetChangeRanges(basis.Text), token));
            }, request.Cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception failure)
        {
            error = failure;
        }

        // Do not rely on a host having installed a SynchronizationContext at Bind time.
        // A worker returns immutable syntax; all cache mutation happens on the UI thread.
        try
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                Complete(request, syntax, error, hash);
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() => Complete(request, syntax, error, hash));
            }
        }
        catch (Exception failure)
        {
            request.Completion.TrySetException(failure);
            _ = request.Completion.Task.Exception;
        }
    }

    private void Complete(Request request, AkburaSyntacticDocument? syntax, Exception? error, int hash)
    {
        DispatcherVerifyAccessDebug();
        try
        {
            if (error is OperationCanceledException canceled)
            {
                request.Completion.TrySetCanceled(canceled.CancellationToken);
                return;
            }
            if (error != null)
            {
                request.Completion.TrySetException(error);
                // Observe the shared fault even when all feature waits were canceled.
                _ = request.Completion.Task.Exception;
                return;
            }

            if (!_disposed && ReferenceEquals(request, _pending) &&
                !request.Cancellation.IsCancellationRequested && isCurrent(request.Key))
            {
                var entry = new Entry(hash, request.Key, syntax!, request.Completion.Task);
                _entries[hash & CacheMask] = entry;
                _accepted = entry;
#if DEBUG
                Performance?.Count("SyntaxPublished");
#endif
            }
#if DEBUG
            else Performance?.Count("SyntaxRejected");
#endif
            request.Completion.TrySetResult(syntax!);
        }
        finally
        {
            if (ReferenceEquals(request, _pending))
            {
                _pending = null;
            }
            request.Cancellation.Dispose();
        }
    }

    internal void Clear()
    {
        DispatcherVerifyAccessDebug();
        var pending = _pending;
        _pending = null;
        _accepted = null;
        Array.Clear(_entries);
        pending?.Cancellation.Cancel();
    }

    public void Dispose()
    {
        DispatcherVerifyAccessDebug();
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        Clear();
    }

    private sealed class Entry(int hash, Key key, AkburaSyntacticDocument syntax, Task<AkburaSyntacticDocument> task)
    {
        internal readonly int Hash = hash;
        internal readonly Key Key = key;
        internal readonly AkburaSyntacticDocument Syntax = syntax;
        internal readonly Task<AkburaSyntacticDocument> Task = task;
    }

    private sealed class Request(Key key, CancellationTokenSource cancellation)
    {
        internal readonly Key Key = key;
        internal readonly CancellationTokenSource Cancellation = cancellation;
        internal readonly TaskCompletionSource<AkburaSyntacticDocument> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
