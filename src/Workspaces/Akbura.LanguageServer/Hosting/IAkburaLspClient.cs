using StreamJsonRpc;

namespace Akbura.LanguageServer.Hosting;

internal interface IAkburaLspClient
{
    Task NotifyAsync<TParams>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken);

    Task<TResult?> RequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken);
}

internal sealed class StreamJsonRpcAkburaClient : IAkburaLspClient
{
    private JsonRpc? _rpc;

    public void Attach(JsonRpc rpc)
    {
        ArgumentNullException.ThrowIfNull(rpc);
        if (Interlocked.CompareExchange(ref _rpc, rpc, null) != null)
        {
            throw new InvalidOperationException(
                "The JSON-RPC client is already attached.");
        }
    }

    public Task NotifyAsync<TParams>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return GetRpc().NotifyWithParameterObjectAsync(
            method,
            parameters!);
    }

    public Task<TResult?> RequestAsync<TParams, TResult>(
        string method,
        TParams parameters,
        CancellationToken cancellationToken)
    {
        return GetRpc().InvokeWithParameterObjectAsync<TResult?>(
            method,
            parameters!,
            cancellationToken);
    }

    private JsonRpc GetRpc()
    {
        return Volatile.Read(ref _rpc) ??
            throw new InvalidOperationException(
                "The JSON-RPC client has not been attached.");
    }
}