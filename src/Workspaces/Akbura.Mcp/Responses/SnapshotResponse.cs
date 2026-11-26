namespace Akbura.Mcp;

/// <summary>
/// Identifies the solution, project, and document versions used to produce a result.
/// </summary>
internal sealed record SnapshotResponse(string Solution, string Project, string Document);
