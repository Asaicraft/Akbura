namespace Akbura.LanguageServer.Dispatch;

internal interface IAkburaLspHandler
{
    string Method { get; }

    bool MutatesServerState { get; }

    bool RequiresDocument { get; }

    bool RequiresSemanticContext { get; }

    Uri? GetDocumentUri(object? parameters);

    Task<AkburaLspHandlerResult> HandleAsync(
        object? parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken);
}

internal abstract class AkburaLspHandler<TParams, TResult> :
    IAkburaLspHandler
{
    public abstract string Method { get; }

    public virtual bool MutatesServerState => false;

    public virtual bool RequiresDocument => false;

    public virtual bool RequiresSemanticContext => false;

    public virtual Uri? GetDocumentUri(TParams parameters)
    {
        return null;
    }

    public abstract Task<AkburaLspHandlerResult<TResult>> HandleAsync(
        TParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken);

    Uri? IAkburaLspHandler.GetDocumentUri(object? parameters)
    {
        return GetDocumentUri(Cast(parameters));
    }

    async Task<AkburaLspHandlerResult> IAkburaLspHandler.HandleAsync(
        object? parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var result = await HandleAsync(
                Cast(parameters),
                context,
                cancellationToken)
            .ConfigureAwait(false);
        return new AkburaLspHandlerResult(
            result.Response,
            result.Snapshot,
            result.AfterCommit);
    }

    private static TParams Cast(object? parameters)
    {
        if (parameters is TParams typed)
        {
            return typed;
        }

        if (parameters == null && default(TParams) == null)
        {
            return default!;
        }

        throw new AkburaProtocolException(
            LspErrorCodes.InvalidParams,
            $"Expected parameters of type '{typeof(TParams).Name}'.");
    }
}