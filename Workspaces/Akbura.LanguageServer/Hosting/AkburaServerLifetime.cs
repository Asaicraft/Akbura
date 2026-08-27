namespace Akbura.LanguageServer.Hosting;

internal sealed class AkburaServerLifetime : IDisposable
{
    private readonly TaskCompletionSource<int> _exit =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _shutdownRequested;
    private int _disposeState;

    public bool IsShutdownRequested =>
        Volatile.Read(ref _shutdownRequested) != 0;

    public Task<int> ExitTask => _exit.Task;

    public void RequestShutdown()
    {
        Volatile.Write(ref _shutdownRequested, 1);
    }

    public void RequestExit()
    {
        _exit.TrySetResult(IsShutdownRequested ? 0 : 1);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) == 0)
        {
            _exit.TrySetResult(IsShutdownRequested ? 0 : 1);
        }
    }
}