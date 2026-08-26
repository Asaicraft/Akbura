namespace Akbura.LanguageServer.State;

internal sealed record AkburaClientCapabilities(
    bool SupportsSnippets,
    bool SupportsCompletionResolve,
    bool SupportsCodeActionResolve,
    bool SupportsDocumentChanges,
    bool SupportsPullDiagnostics,
    bool SupportsDiagnosticRefresh,
    bool SupportsDynamicFileWatching)
{
    public static AkburaClientCapabilities Default { get; } =
        new(
            SupportsSnippets: true,
            SupportsCompletionResolve: true,
            SupportsCodeActionResolve: true,
            SupportsDocumentChanges: true,
            SupportsPullDiagnostics: false,
            SupportsDiagnosticRefresh: false,
            SupportsDynamicFileWatching: false);
}