namespace Akbura.Mcp;

/// <summary>
/// Reports applicable utility definitions, availability, and search limits for one snapshot.
/// </summary>
internal sealed record UtilitySearchResponse(
    string Status,
    string? ResolvedTargetType,
    UtilityResponse[] Utilities,
    string? SnapshotId,
    bool IsComplete,
    bool Truncated,
    ToolDiagnostic[] Diagnostics);
