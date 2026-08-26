using System.Diagnostics;

namespace Akbura.LanguageServer.Hosting;

internal sealed class AkburaParentProcessMonitor : IDisposable
{
    private readonly AkburaServerLifetime _lifetime;
    private readonly IAkburaServerLogger _logger;
    private Process? _process;
    private int _disposeState;

    public AkburaParentProcessMonitor(
        AkburaServerLifetime lifetime,
        IAkburaServerLogger logger)
    {
        _lifetime = lifetime ??
            throw new ArgumentNullException(nameof(lifetime));
        _logger = logger ??
            throw new ArgumentNullException(nameof(logger));
    }

    public void Start(int? processId)
    {
        if (processId is not > 0 ||
            Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        try
        {
            var process = Process.GetProcessById(processId.Value);
            process.EnableRaisingEvents = true;
            process.Exited += OnExited;
            if (Interlocked.CompareExchange(
                    ref _process,
                    process,
                    null) != null)
            {
                process.Exited -= OnExited;
                process.Dispose();
            }
        }
        catch (ArgumentException)
        {
            _logger.Log(
                AkburaServerLogLevel.Warning,
                $"Client process {processId} is not running.");
            _lifetime.RequestExit();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        var process = Interlocked.Exchange(ref _process, null);
        if (process != null)
        {
            process.Exited -= OnExited;
            process.Dispose();
        }
    }

    private void OnExited(object? sender, EventArgs eventArgs)
    {
        _logger.Log(
            AkburaServerLogLevel.Information,
            "The client process exited; stopping Akbura LSP.");
        _lifetime.RequestExit();
    }
}
