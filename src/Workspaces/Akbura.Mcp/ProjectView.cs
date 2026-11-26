using Akbura.Workspaces.Projects;

namespace Akbura.Mcp;

/// <summary>
/// Carries a captured solution or the session status that prevented it from being captured.
/// </summary>
internal sealed record ProjectView(AkburaSolutionSnapshot? Snapshot, string Status, string? Message);
