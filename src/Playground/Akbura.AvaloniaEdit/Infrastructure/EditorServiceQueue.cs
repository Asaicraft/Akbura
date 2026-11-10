namespace Akbura.AvaloniaEdit.Infrastructure;

/// <summary>
/// Serializes service execution, retaining at most the newest waiter per stream.
/// Scheduler delivery may finish later; the lease covers execution, not result delivery.
/// </summary>
internal sealed class EditorServiceQueue(IAkburaEditorScheduler scheduler) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<Pending> _pending = [];
    private bool _busy;
    private bool _disposed;

#if DEBUG
    internal EditorPerformance? Performance
    {
        get; init;
    }
#endif

    internal Task<T> RunAsync<T>(string stream, int priority, Func<CancellationToken, Task<T>> operation, CancellationToken token) =>
        scheduler.RunAsync(async hostToken =>
        {
#if DEBUG
            if (Performance?.BeforeService is { } before) await before(stream, hostToken).ConfigureAwait(false);
#endif
            using var lease = await EnterAsync(stream, priority, hostToken).ConfigureAwait(false);
            hostToken.ThrowIfCancellationRequested();
            return await operation(hostToken).ConfigureAwait(false);
        }, token);

    private async Task<Lease> EnterAsync(string stream, int priority, CancellationToken token)
    {
        Pending pending;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            token.ThrowIfCancellationRequested();
            if (!_busy) { _busy = true; return new(this); }
            Pending? previous = null;
            foreach (var item in _pending)
            {
                if (item.Stream == stream)
                {
                    previous = item;
                    break;
                }
            }
            if (previous != null)
            {
                _pending.Remove(previous);
                previous.Ready.TrySetCanceled();
#if DEBUG
                Performance?.Count("ServiceSuperseded");
#endif
            }
            pending = new(stream, priority);
            _pending.Add(pending);
        }
        using var registration = token.Register(() =>
        {
            lock (_gate)
            {
                if (_pending.Remove(pending))
                {
                    pending.Ready.TrySetCanceled(token);
#if DEBUG
                    Performance?.Count("ServiceCanceled");
#endif
                }
            }
        });
        await pending.Ready.Task.ConfigureAwait(false);
        return new(this);
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_pending.Count == 0) { _busy = false; return; }
            var next = _pending[0];
            for (var index = 1; index < _pending.Count; index++)
            {
                if (_pending[index].Priority < next.Priority)
                {
                    next = _pending[index];
                }
            }
            _pending.Remove(next);
            next.Ready.TrySetResult();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            foreach (var pending in _pending)
            {
                pending.Ready.TrySetCanceled();
            }

            _pending.Clear();
        }
    }

    private sealed record Pending(string Stream, int Priority)
    {
        internal TaskCompletionSource Ready
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Lease(EditorServiceQueue owner) : IDisposable
    {
        private EditorServiceQueue? _owner = owner;

        public void Dispose() => Interlocked.Exchange(ref _owner, null)?.Release();
    }
}
