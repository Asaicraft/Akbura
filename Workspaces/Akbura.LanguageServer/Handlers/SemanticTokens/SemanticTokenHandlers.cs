namespace Akbura.LanguageServer.Handlers.SemanticTokens;

internal sealed class SemanticTokensFullHandler :
    AkburaLspHandler<SemanticTokensParams, Protocol.SemanticTokens>
{
    private readonly AkburaSemanticTokenEncoder _encoder = new();

    public override string Method =>
        LspMethods.SemanticTokensFull;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        SemanticTokensParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<
        AkburaLspHandlerResult<Protocol.SemanticTokens>> HandleAsync(
        SemanticTokensParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        var span = new TextSpan(0, document.Text.Length);
        var classifications = GetClassifications(
            context,
            span,
            cancellationToken);
        var result = _encoder.Encode(
            document.Text,
            classifications,
            context.Services.PositionConverter);
        context.Services.SemanticTokens.Store(document.Uri, result);
        return Task.FromResult(
            new AkburaLspHandlerResult<Protocol.SemanticTokens>(
                result));
    }

    internal static ImmutableArray<AkburaClassifiedSpan>
        GetClassifications(
            AkburaRequestContext context,
            TextSpan span,
            CancellationToken cancellationToken)
    {
        var service =
            context.Services.Workspace.LanguageServices.Classification;
        return context.SemanticDocument != null
            ? service.GetClassifications(
                context.SemanticDocument,
                span,
                cancellationToken)
            : service.GetSyntacticClassifications(
                context.SyntacticDocument!,
                span,
                cancellationToken);
    }
}

internal sealed class SemanticTokensRangeHandler :
    AkburaLspHandler<
        SemanticTokensRangeParams,
        Protocol.SemanticTokens>
{
    private readonly AkburaSemanticTokenEncoder _encoder = new();

    public override string Method =>
        LspMethods.SemanticTokensRange;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        SemanticTokensRangeParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override Task<
        AkburaLspHandlerResult<Protocol.SemanticTokens>> HandleAsync(
        SemanticTokensRangeParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        var span = context.Services.PositionConverter.ToTextSpan(
            document.Text,
            parameters.Range);
        var classifications =
            SemanticTokensFullHandler.GetClassifications(
                context,
                span,
                cancellationToken);
        var result = _encoder.Encode(
            document.Text,
            classifications,
            context.Services.PositionConverter);
        return Task.FromResult(
            new AkburaLspHandlerResult<Protocol.SemanticTokens>(
                result));
    }
}

internal sealed class SemanticTokensDeltaHandler :
    AkburaLspHandler<SemanticTokensDeltaParams, object>
{
    private readonly AkburaSemanticTokenEncoder _encoder = new();

    public override string Method => LspMethods.SemanticTokensFullDelta;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        SemanticTokensDeltaParams parameters) =>
        AkburaProtocolMapper.ParseUri(parameters.TextDocument.Uri);

    public override Task<AkburaLspHandlerResult<object>> HandleAsync(
        SemanticTokensDeltaParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var document = context.OpenDocument!;
        var classifications =
            SemanticTokensFullHandler.GetClassifications(
                context,
                new TextSpan(0, document.Text.Length),
                cancellationToken);
        var current = _encoder.Encode(
            document.Text,
            classifications,
            context.Services.PositionConverter);
        object response = context.Services.SemanticTokens.TryGet(
            document.Uri,
            parameters.PreviousResultId,
            out var previous)
                ? AkburaSemanticTokenCache.CreateDelta(previous, current)
                : current;
        context.Services.SemanticTokens.Store(document.Uri, current);
        return Task.FromResult(
            new AkburaLspHandlerResult<object>(response));
    }
}