namespace Akbura.LanguageServer.Protocol;

public static class LspMethods
{
    public const string Initialize = "initialize";
    public const string Initialized = "initialized";
    public const string Shutdown = "shutdown";
    public const string Exit = "exit";
    public const string DidOpen = "textDocument/didOpen";
    public const string DidChange = "textDocument/didChange";
    public const string DidClose = "textDocument/didClose";
    public const string DidSave = "textDocument/didSave";
    public const string DidChangeWatchedFiles = "workspace/didChangeWatchedFiles";
    public const string DidChangeWorkspaceFolders =
        "workspace/didChangeWorkspaceFolders";
    public const string Completion = "textDocument/completion";
    public const string CompletionResolve = "completionItem/resolve";
    public const string Hover = "textDocument/hover";
    public const string Definition = "textDocument/definition";
    public const string CodeAction = "textDocument/codeAction";
    public const string CodeActionResolve = "codeAction/resolve";
    public const string SemanticTokensFull = "textDocument/semanticTokens/full";
    public const string SemanticTokensRange = "textDocument/semanticTokens/range";
    public const string SemanticTokensFullDelta =
        "textDocument/semanticTokens/full/delta";
    public const string DocumentDiagnostic = "textDocument/diagnostic";
    public const string WorkspaceDiagnostic = "workspace/diagnostic";
    public const string DocumentSymbol = "textDocument/documentSymbol";
    public const string WorkspaceSymbol = "workspace/symbol";
    public const string FoldingRange = "textDocument/foldingRange";
    public const string DocumentHighlight = "textDocument/documentHighlight";
    public const string References = "textDocument/references";
    public const string PrepareRename = "textDocument/prepareRename";
    public const string Rename = "textDocument/rename";
    public const string SignatureHelp = "textDocument/signatureHelp";
    public const string Formatting = "textDocument/formatting";
    public const string RangeFormatting = "textDocument/rangeFormatting";
    public const string OnTypeFormatting = "textDocument/onTypeFormatting";
    public const string PublishDiagnostics = "textDocument/publishDiagnostics";
    public const string DiagnosticRefresh = "workspace/diagnostic/refresh";
    public const string RegisterCapability = "client/registerCapability";
    public const string ShowMessage = "window/showMessage";
    public const string LogMessage = "window/logMessage";
    public const string Progress = "$/progress";
}