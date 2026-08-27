namespace Akbura.LanguageServer.State;

internal enum AkburaWorkspaceFolderLoadState
{
    NotStarted,
    Loading,
    Loaded,
    Failed,
}

internal sealed record AkburaWorkspaceFolderState(
    Uri Uri,
    string Name,
    string? SolutionOrProjectPath,
    ImmutableArray<AkburaProjectId> ProjectIds,
    AkburaWorkspaceFolderLoadState LoadState,
    string? ErrorMessage)
{
    public static AkburaWorkspaceFolderState Create(
        Uri uri,
        string name)
    {
        return new AkburaWorkspaceFolderState(
            uri,
            name,
            SolutionOrProjectPath: null,
            ImmutableArray<AkburaProjectId>.Empty,
            AkburaWorkspaceFolderLoadState.NotStarted,
            ErrorMessage: null);
    }
}