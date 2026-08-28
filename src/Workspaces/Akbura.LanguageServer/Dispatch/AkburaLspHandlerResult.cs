namespace Akbura.LanguageServer.Dispatch;

internal class AkburaLspHandlerResult
{
    public AkburaLspHandlerResult(
        object? response,
        AkburaServerSnapshot? snapshot = null,
        Func<CancellationToken, Task>? afterCommit = null)
    {
        Response = response;
        Snapshot = snapshot;
        AfterCommit = afterCommit;
    }

    public object? Response { get; }

    public AkburaServerSnapshot? Snapshot { get; }

    public Func<CancellationToken, Task>? AfterCommit { get; }

    public static AkburaLspHandlerResult FromResponse(object? response)
    {
        return new AkburaLspHandlerResult(response);
    }
}

internal sealed class AkburaLspHandlerResult<TResult> :
    AkburaLspHandlerResult
{
    public AkburaLspHandlerResult(
        TResult response,
        AkburaServerSnapshot? snapshot = null,
        Func<CancellationToken, Task>? afterCommit = null)
        : base(response, snapshot, afterCommit)
    {
        TypedResponse = response;
    }

    public TResult TypedResponse { get; }
}