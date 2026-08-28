namespace Akbura.LanguageServer.Projects;

internal sealed record AkburaProjectLoadResult(
    Uri WorkspaceFolder,
    string WorkspaceFolderName,
    string? SolutionOrProjectPath,
    ImmutableArray<AkburaLoadedProject> Projects,
    ImmutableArray<AkburaProjectLoadDiagnostic> Diagnostics,
    string? ErrorMessage)
{
    public bool Succeeded => ErrorMessage == null;
}
