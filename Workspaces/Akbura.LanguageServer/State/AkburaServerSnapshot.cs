namespace Akbura.LanguageServer.State;

internal sealed record AkburaServerSnapshot(
    long Sequence,
    AkburaSolutionSnapshot Solution,
    ImmutableDictionary<Uri, AkburaOpenDocument> OpenDocuments,
    ImmutableDictionary<Uri, AkburaWorkspaceFolderState> WorkspaceFolders,
    AkburaClientCapabilities ClientCapabilities,
    AkburaPositionEncoding PositionEncoding,
    int? ClientProcessId,
    bool IsInitializeReceived,
    bool IsInitialized,
    bool IsShuttingDown)
{
    public static AkburaServerSnapshot Create(
        AkburaWorkspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        return new AkburaServerSnapshot(
            Sequence: 0,
            workspace.CurrentSolution,
            ImmutableDictionary.Create<Uri, AkburaOpenDocument>(
                AkburaUriComparer.Instance),
            ImmutableDictionary.Create<Uri, AkburaWorkspaceFolderState>(
                AkburaUriComparer.Instance),
            AkburaClientCapabilities.Default,
            AkburaPositionEncoding.Utf16,
            ClientProcessId: null,
            IsInitializeReceived: false,
            IsInitialized: false,
            IsShuttingDown: false);
    }

    public AkburaServerSnapshot Next(
        AkburaSolutionSnapshot solution)
    {
        return this with
        {
            Sequence = Sequence + 1,
            Solution = solution,
        };
    }
}