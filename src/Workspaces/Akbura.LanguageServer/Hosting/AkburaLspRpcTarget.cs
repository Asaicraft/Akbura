using StreamJsonRpc;

namespace Akbura.LanguageServer.Hosting;

internal sealed class AkburaLspRpcTarget
{
    private readonly AkburaRequestExecutionQueue _queue;

    public AkburaLspRpcTarget(AkburaRequestExecutionQueue queue)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
    }

    [JsonRpcMethod(
        LspMethods.Initialize,
        UseSingleObjectParameterDeserialization = true)]
    public Task<InitializeResult?> InitializeAsync(
        InitializeParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<InitializeResult>(
            LspMethods.Initialize,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.Initialized,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> InitializedAsync(
        InitializedParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.Initialized,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(LspMethods.Shutdown)]
    public Task<object?> ShutdownAsync(
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.Shutdown,
            parameters: null,
            cancellationToken);
    }

    [JsonRpcMethod(LspMethods.Exit)]
    public Task<object?> ExitAsync(
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.Exit,
            parameters: null,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidOpen,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidOpenAsync(
        DidOpenTextDocumentParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidOpen,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidChange,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidChangeAsync(
        DidChangeTextDocumentParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidChange,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidClose,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidCloseAsync(
        DidCloseTextDocumentParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidClose,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidSave,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidSaveAsync(
        DidSaveTextDocumentParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidSave,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidChangeWatchedFiles,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidChangeWatchedFilesAsync(
        DidChangeWatchedFilesParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidChangeWatchedFiles,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DidChangeWorkspaceFolders,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DidChangeWorkspaceFoldersAsync(
        DidChangeWorkspaceFoldersParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object?>(
            LspMethods.DidChangeWorkspaceFolders,
            parameters,
            cancellationToken);
    }
    [JsonRpcMethod(
        LspMethods.Completion,
        UseSingleObjectParameterDeserialization = true)]
    public Task<CompletionList?> CompletionAsync(
        CompletionParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<CompletionList>(
            LspMethods.Completion,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.CompletionResolve,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Protocol.CompletionItem?> ResolveCompletionAsync(
        Protocol.CompletionItem parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Protocol.CompletionItem>(
            LspMethods.CompletionResolve,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.Hover,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Hover?> HoverAsync(
        TextDocumentPositionParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Hover?>(
            LspMethods.Hover,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.Definition,
        UseSingleObjectParameterDeserialization = true)]
    public Task<LocationLink[]?> DefinitionAsync(
        DefinitionParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<LocationLink[]?>(
            LspMethods.Definition,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.CodeAction,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Protocol.CodeAction[]?> CodeActionAsync(
        CodeActionParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Protocol.CodeAction[]>(
            LspMethods.CodeAction,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.CodeActionResolve,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Protocol.CodeAction?> ResolveCodeActionAsync(
        Protocol.CodeAction parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Protocol.CodeAction>(
            LspMethods.CodeActionResolve,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.SemanticTokensFull,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Protocol.SemanticTokens?> SemanticTokensFullAsync(
        SemanticTokensParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Protocol.SemanticTokens>(
            LspMethods.SemanticTokensFull,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.SemanticTokensRange,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Protocol.SemanticTokens?> SemanticTokensRangeAsync(
        SemanticTokensRangeParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<Protocol.SemanticTokens>(
            LspMethods.SemanticTokensRange,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.SemanticTokensFullDelta,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> SemanticTokensFullDeltaAsync(
        SemanticTokensDeltaParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object>(
            LspMethods.SemanticTokensFullDelta,
            parameters,
            cancellationToken);
    }
    [JsonRpcMethod(
        LspMethods.DocumentDiagnostic,
        UseSingleObjectParameterDeserialization = true)]
    public Task<object?> DocumentDiagnosticAsync(
        DocumentDiagnosticParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<object>(
            LspMethods.DocumentDiagnostic,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.WorkspaceDiagnostic,
        UseSingleObjectParameterDeserialization = true)]
    public Task<WorkspaceDiagnosticReport?> WorkspaceDiagnosticAsync(
        WorkspaceDiagnosticParams parameters,
        CancellationToken cancellationToken)
    {
        return ExecuteAsync<WorkspaceDiagnosticReport>(
            LspMethods.WorkspaceDiagnostic,
            parameters,
            cancellationToken);
    }

    [JsonRpcMethod(
        LspMethods.DocumentSymbol,
        UseSingleObjectParameterDeserialization = true)]
    public Task<DocumentSymbol[]?> DocumentSymbolAsync(
        DocumentSymbolParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<DocumentSymbol[]>(
            LspMethods.DocumentSymbol,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.WorkspaceSymbol,
        UseSingleObjectParameterDeserialization = true)]
    public Task<SymbolInformation[]?> WorkspaceSymbolAsync(
        WorkspaceSymbolParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<SymbolInformation[]>(
            LspMethods.WorkspaceSymbol,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.FoldingRange,
        UseSingleObjectParameterDeserialization = true)]
    public Task<FoldingRange[]?> FoldingRangeAsync(
        FoldingRangeParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<FoldingRange[]>(
            LspMethods.FoldingRange,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.DocumentHighlight,
        UseSingleObjectParameterDeserialization = true)]
    public Task<DocumentHighlight[]?> DocumentHighlightAsync(
        DocumentHighlightParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<DocumentHighlight[]>(
            LspMethods.DocumentHighlight,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.References,
        UseSingleObjectParameterDeserialization = true)]
    public Task<Location[]?> ReferencesAsync(
        ReferenceParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<Location[]>(
            LspMethods.References,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.PrepareRename,
        UseSingleObjectParameterDeserialization = true)]
    public Task<PrepareRenameResult?> PrepareRenameAsync(
        PrepareRenameParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<PrepareRenameResult?>(
            LspMethods.PrepareRename,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.Rename,
        UseSingleObjectParameterDeserialization = true)]
    public Task<WorkspaceEdit?> RenameAsync(
        RenameParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<WorkspaceEdit>(
            LspMethods.Rename,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.SignatureHelp,
        UseSingleObjectParameterDeserialization = true)]
    public Task<SignatureHelp?> SignatureHelpAsync(
        SignatureHelpParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<SignatureHelp?>(
            LspMethods.SignatureHelp,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.Formatting,
        UseSingleObjectParameterDeserialization = true)]
    public Task<TextEdit[]?> FormattingAsync(
        DocumentFormattingParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<TextEdit[]>(
            LspMethods.Formatting,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.RangeFormatting,
        UseSingleObjectParameterDeserialization = true)]
    public Task<TextEdit[]?> RangeFormattingAsync(
        DocumentRangeFormattingParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<TextEdit[]>(
            LspMethods.RangeFormatting,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.OnTypeFormatting,
        UseSingleObjectParameterDeserialization = true)]
    public Task<TextEdit[]?> OnTypeFormattingAsync(
        DocumentOnTypeFormattingParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<TextEdit[]>(
            LspMethods.OnTypeFormatting,
            parameters,
            cancellationToken);

    [JsonRpcMethod(
        LspMethods.Typing,
        UseSingleObjectParameterDeserialization = true)]
    public Task<AkburaTypingResponse?> TypingAsync(
        AkburaTypingParams parameters,
        CancellationToken cancellationToken) =>
        ExecuteAsync<AkburaTypingResponse>(
            LspMethods.Typing,
            parameters,
            cancellationToken);

    private async Task<TResult?> ExecuteAsync<TResult>(
        string method,
        object? parameters,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _queue.ExecuteAsync<TResult>(
                    method,
                    parameters,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (AkburaProtocolException exception)
        {
            throw new LocalRpcException(exception.Message, exception)
            {
                ErrorCode = exception.Code,
            };
        }
    }
}
