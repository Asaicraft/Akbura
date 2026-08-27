namespace Akbura.LanguageServer.Handlers.Diagnostics;

internal sealed class DocumentDiagnosticHandler :
    AkburaLspHandler<DocumentDiagnosticParams, object>
{
    public override string Method =>
        LspMethods.DocumentDiagnostic;

    public override bool RequiresDocument => true;

    public override Uri? GetDocumentUri(
        DocumentDiagnosticParams parameters)
    {
        return AkburaProtocolMapper.ParseUri(
            parameters.TextDocument.Uri);
    }

    public override async Task<AkburaLspHandlerResult<object>> HandleAsync(
        DocumentDiagnosticParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var report = await context.Services.Diagnostics
            .GetDocumentReportAsync(
                context.OpenDocument!.Uri,
                parameters.PreviousResultId,
                cancellationToken)
            .ConfigureAwait(false);
        return new AkburaLspHandlerResult<object>(report);
    }
}

internal sealed class WorkspaceDiagnosticHandler :
    AkburaLspHandler<
        WorkspaceDiagnosticParams,
        WorkspaceDiagnosticReport>
{
    public override string Method =>
        LspMethods.WorkspaceDiagnostic;

    public override async Task<
        AkburaLspHandlerResult<WorkspaceDiagnosticReport>> HandleAsync(
        WorkspaceDiagnosticParams parameters,
        AkburaRequestContext context,
        CancellationToken cancellationToken)
    {
        var previous = parameters.PreviousResultIds
            .GroupBy(static item => item.Uri, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group.Last().Value,
                StringComparer.Ordinal);
        var report = await context.Services.Diagnostics
            .GetWorkspaceReportAsync(previous, cancellationToken)
            .ConfigureAwait(false);
        return new AkburaLspHandlerResult<
            WorkspaceDiagnosticReport>(report);
    }
}
