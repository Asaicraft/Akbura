namespace Akbura.LanguageServer.UnitTests;

public sealed class RequestExecutionQueueTests
{
    [Fact]
    public async Task MutationsArePublishedSequentially()
    {
        await using var fixture = new QueueFixture(
            new SequenceMutationHandler());

        var first = fixture.Queue.ExecuteAsync<int>(
            "test/mutate",
            null,
            CancellationToken.None);
        var second = fixture.Queue.ExecuteAsync<int>(
            "test/mutate",
            null,
            CancellationToken.None);

        var responses = await Task.WhenAll(first, second);

        Assert.Equal(new[] { 1, 2 }, responses);
        Assert.Equal(2, fixture.State.Current.Sequence);
    }

    [Fact]
    public async Task ReadRequestsCanRunInParallel()
    {
        var handler = new ParallelReadHandler();
        await using var fixture = new QueueFixture(handler);

        var first = fixture.Queue.ExecuteAsync<int>(
            "test/read",
            null,
            CancellationToken.None);
        var second = fixture.Queue.ExecuteAsync<int>(
            "test/read",
            null,
            CancellationToken.None);
        await handler.BothStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        handler.Release.TrySetResult();

        await Task.WhenAll(first, second);
        Assert.Equal(2, handler.MaximumConcurrency);
    }

    private sealed class SequenceMutationHandler :
        AkburaLspHandler<object?, int>
    {
        public override string Method => "test/mutate";

        public override bool MutatesServerState => true;

        public override Task<AkburaLspHandlerResult<int>> HandleAsync(
            object? parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
        {
            var next = context.ServerSnapshot.Next(context.Solution);
            return Task.FromResult(
                new AkburaLspHandlerResult<int>(
                    checked((int)next.Sequence),
                    next));
        }
    }

    private sealed class ParallelReadHandler :
        AkburaLspHandler<object?, int>
    {
        private int _active;
        private int _maximum;
        private int _started;

        public override string Method => "test/read";

        public TaskCompletionSource BothStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaximumConcurrency => Volatile.Read(ref _maximum);

        public override async Task<AkburaLspHandlerResult<int>> HandleAsync(
            object? parameters,
            AkburaRequestContext context,
            CancellationToken cancellationToken)
        {
            var active = Interlocked.Increment(ref _active);
            UpdateMaximum(active);
            if (Interlocked.Increment(ref _started) == 2)
            {
                BothStarted.TrySetResult();
            }

            try
            {
                await Release.Task.WaitAsync(cancellationToken);
                return new AkburaLspHandlerResult<int>(active);
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMaximum(int value)
        {
            while (true)
            {
                var current = Volatile.Read(ref _maximum);
                if (current >= value ||
                    Interlocked.CompareExchange(
                        ref _maximum,
                        value,
                        current) == current)
                {
                    return;
                }
            }
        }
    }

    private sealed class QueueFixture : IAsyncDisposable
    {
        private readonly AkburaWorkspace _workspace;
        private readonly AkburaServerLifetime _lifetime;
        private readonly NullLogger _logger;
        private readonly AkburaParentProcessMonitor _monitor;

        public QueueFixture(params IAkburaLspHandler[] handlers)
        {
            _workspace = new AkburaWorkspace();
            _lifetime = new AkburaServerLifetime();
            _logger = new NullLogger();
            _monitor = new AkburaParentProcessMonitor(
                _lifetime,
                _logger);
            var services = new AkburaLanguageServerServices(
                _workspace,
                new NullClient(),
                _logger,
                new Utf16PositionConverter(),
                _lifetime,
                _monitor,
                AkburaServerOptions.Parse([]));
            State = new AkburaServerState(
                AkburaServerSnapshot.Create(_workspace));
            Queue = new AkburaRequestExecutionQueue(
                new AkburaLspHandlerRegistry(handlers),
                new AkburaRequestContextFactory(State, services),
                State,
                _logger);
        }

        public AkburaServerState State { get; }

        public AkburaRequestExecutionQueue Queue { get; }

        public async ValueTask DisposeAsync()
        {
            await Queue.DisposeAsync();
            _monitor.Dispose();
            _lifetime.Dispose();
            _workspace.Dispose();
            _logger.Dispose();
        }
    }

    private sealed class NullClient : IAkburaLspClient
    {
        public Task NotifyAsync<TParams>(
            string method,
            TParams parameters,
            CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<TResult?> RequestAsync<TParams, TResult>(
            string method,
            TParams parameters,
            CancellationToken cancellationToken) =>
            Task.FromResult(default(TResult));
    }

    private sealed class NullLogger : IAkburaServerLogger
    {
        public void Log(
            AkburaServerLogLevel level,
            string message,
            Exception? exception = null)
        {
        }

        public void Dispose()
        {
        }
    }
}