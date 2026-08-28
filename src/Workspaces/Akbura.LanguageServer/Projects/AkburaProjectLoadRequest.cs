namespace Akbura.LanguageServer.Projects;

internal sealed record AkburaProjectLoadRequest(
    Uri WorkspaceFolder,
    string WorkspaceFolderName,
    string? ExplicitPath,
    string Reason);
