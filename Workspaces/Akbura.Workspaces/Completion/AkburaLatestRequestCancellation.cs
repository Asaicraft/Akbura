namespace Akbura.Workspaces.Completion;

internal sealed class AkburaLatestRequestCancellation : IDisposable
{
    private AkburaLatestRequest? _current;

    private int _disposeState;

    public AkburaLatestRequest Begin(
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        var request = new AkburaLatestRequest(
            this,
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken));
        var previous = Interlocked.Exchange(
            ref _current,
            request);

        previous?.Cancel();

        if (Volatile.Read(ref _disposeState) != 0)
        {
            request.Cancel();
            Complete(request);
            request.DisposeSource();
            throw new ObjectDisposedException(
                nameof(AkburaLatestRequestCancellation));
        }

        return request;
    }

    public void CancelCurrent()
    {
        Volatile.Read(ref _current)?.Cancel();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref _disposeState,
                1) != 0)
        {
            return;
        }

        var current = Interlocked.Exchange(
            ref _current,
            null);
        current?.Cancel();
    }

    internal bool IsCurrent(
        AkburaLatestRequest request)
    {
        return Volatile.Read(ref _disposeState) == 0 &&
            ReferenceEquals(
                Volatile.Read(ref _current),
                request) &&
            !request.Token.IsCancellationRequested;
    }

    internal void Complete(
        AkburaLatestRequest request)
    {
        Interlocked.CompareExchange(
            ref _current,
            null,
            request);
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(
                nameof(AkburaLatestRequestCancellation));
        }
    }
}

internal sealed class AkburaLatestRequest : IDisposable
{
    private readonly AkburaLatestRequestCancellation _owner;

    private readonly CancellationToken _token;

    private CancellationTokenSource? _source;

    internal AkburaLatestRequest(
        AkburaLatestRequestCancellation owner,
        CancellationTokenSource source)
    {
        _owner = owner ??
            throw new ArgumentNullException(nameof(owner));
        _source = source ??
            throw new ArgumentNullException(nameof(source));
        _token = source.Token;
    }

    public CancellationToken Token => _token;

    public bool IsCurrent => _owner.IsCurrent(this);

    public void Cancel()
    {
        var source = Volatile.Read(ref _source);
        if (source == null)
        {
            return;
        }

        try
        {
            source.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        _owner.Complete(this);
        DisposeSource();
    }

    internal void DisposeSource()
    {
        Interlocked.Exchange(
                ref _source,
                null)
            ?.Dispose();
    }
}
