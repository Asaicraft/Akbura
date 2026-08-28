using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Akbura.LanguageServer.Dispatch;

internal sealed class AkburaRequestExecutionQueue : IAsyncDisposable
{
    private readonly Channel<AkburaLspWorkItem> _channel;
    private readonly AkburaLspHandlerRegistry _registry;
    private readonly AkburaRequestContextFactory _contextFactory;
    private readonly AkburaServerState _state;
    private readonly IAkburaServerLogger _logger;
    private readonly ConcurrentDictionary<long, Task> _readRequests = new();
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Task _processingTask;
    private long _nextSequence;
    private int _disposeState;

    public AkburaRequestExecutionQueue(
        AkburaLspHandlerRegistry registry,
        AkburaRequestContextFactory contextFactory,
        AkburaServerState state,
        IAkburaServerLogger logger)
    {
        _registry = registry ??
            throw new ArgumentNullException(nameof(registry));
        _contextFactory = contextFactory ??
            throw new ArgumentNullException(nameof(contextFactory));
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _channel = Channel.CreateUnbounded<AkburaLspWorkItem>(
            new UnboundedChannelOptions
            {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false,
            });
        _processingTask = ProcessAsync();
    }

    public async Task<TResult?> ExecuteAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        var descriptor = _registry.GetRequired(method);
        if (_state.Current.IsShuttingDown &&
            !string.Equals(method, LspMethods.Exit, StringComparison.Ordinal))
        {
            throw new AkburaProtocolException(
                LspErrorCodes.InvalidRequest,
                "The language server is shutting down.");
        }

        var item = new AkburaLspWorkItem(
            Interlocked.Increment(ref _nextSequence),
            descriptor,
            parameters,
            cancellationToken);
        await _channel.Writer
            .WriteAsync(item, cancellationToken)
            .ConfigureAwait(false);

        var response = await item.Completion.Task
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        if (response == null)
        {
            return default;
        }

        if (response is TResult typed)
        {
            return typed;
        }

        throw new InvalidOperationException(
            $"Handler '{method}' returned '{response.GetType().Name}' " +
            $"instead of '{typeof(TResult).Name}'.");
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _channel.Writer.TryComplete();
        _shutdown.Cancel();
        try
        {
            await _processingTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        var reads = _readRequests.Values.ToArray();
        if (reads.Length != 0)
        {
            try
            {
                await Task.WhenAll(reads).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task ProcessAsync()
    {
        try
        {
            await foreach (var item in _channel.Reader
                               .ReadAllAsync(_shutdown.Token)
                               .ConfigureAwait(false))
            {
                if (item.Descriptor.MutatesServerState)
                {
                    await ExecuteItemAsync(item).ConfigureAwait(false);
                    continue;
                }

                AkburaRequestContext context;
                try
                {
                    context = _contextFactory.CreateContext(
                        item.Descriptor,
                        item.Parameters);
                }
                catch (Exception exception)
                {
                    item.Completion.TrySetException(exception);
                    continue;
                }

                var task = Task.Run(
                    () => ExecuteItemAsync(item, context),
                    CancellationToken.None);
                _readRequests[item.Sequence] = task;
                _ = task.ContinueWith(
                    static (_, state) =>
                    {
                        var tuple = ((ConcurrentDictionary<long, Task>, long))state!;
                        tuple.Item1.TryRemove(
                            tuple.Item2,
                            out Task? _);
                    },
                    (_readRequests, item.Sequence),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }
        catch (OperationCanceledException)
            when (_shutdown.IsCancellationRequested)
        {
        }
        finally
        {
            while (_channel.Reader.TryRead(out var pending))
            {
                pending.Completion.TrySetCanceled();
            }
        }
    }

    private Task ExecuteItemAsync(AkburaLspWorkItem item)
    {
        AkburaRequestContext context;
        try
        {
            context = _contextFactory.CreateContext(
                item.Descriptor,
                item.Parameters);
        }
        catch (Exception exception)
        {
            item.Completion.TrySetException(exception);
            return Task.CompletedTask;
        }

        return ExecuteItemAsync(item, context);
    }

    private async Task ExecuteItemAsync(
        AkburaLspWorkItem item,
        AkburaRequestContext context)
    {
        try
        {
            item.CancellationToken.ThrowIfCancellationRequested();
            var result = await item.Descriptor.Handler
                .HandleAsync(
                    item.Parameters,
                    context,
                    item.CancellationToken)
                .ConfigureAwait(false);

            if (result.Snapshot != null)
            {
                if (!item.Descriptor.MutatesServerState)
                {
                    throw new InvalidOperationException(
                        $"Read handler '{item.Descriptor.Method}' attempted " +
                        "to publish server state.");
                }

                _state.Publish(result.Snapshot);
            }

            item.Completion.TrySetResult(result.Response);

            if (result.AfterCommit != null)
            {
                _ = RunAfterCommitAsync(
                    result.AfterCommit,
                    _shutdown.Token);
            }
        }
        catch (OperationCanceledException)
        {
            item.Completion.TrySetCanceled(item.CancellationToken);
        }
        catch (Exception exception)
        {
            item.Completion.TrySetException(exception);
        }
    }

    private async Task RunAfterCommitAsync(
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _logger.Log(
                AkburaServerLogLevel.Error,
                "Post-commit action failed.",
                exception);
        }
    }

    private void ThrowIfDisposed()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            throw new ObjectDisposedException(
                nameof(AkburaRequestExecutionQueue));
        }
    }
}