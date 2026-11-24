namespace Akbura.AvaloniaEdit.Infrastructure;

/// <summary>
/// Controls where workspace parsing and language requests execute.
/// </summary>
public interface IAkburaEditorScheduler
{
    Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);
}

/// <summary>
/// Desktop default. A browser without threads does not gain a worker from Task.Run.
/// </summary>
public sealed class AkburaThreadPoolScheduler : IAkburaEditorScheduler
{
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        Task.Run(() => operation(cancellationToken), cancellationToken);
}

/// <summary>
/// For cooperative single-threaded hosts; CPU analysis can block that host's UI.
/// </summary>
public sealed class AkburaInlineScheduler : IAkburaEditorScheduler
{
    public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken) =>
        operation(cancellationToken);
}
