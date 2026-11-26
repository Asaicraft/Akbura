namespace Akbura.Mcp;

/// <summary>
/// Reports the stages, canonical diagnostics, and snapshot of a saved document analysis.
/// </summary>
internal sealed record AnalysisToolResponse(
    string Status,
    string? DocumentPath,
    SnapshotResponse? Snapshot,
    string? RequestedLevel,
    string? CompletedLevel,
    string Scope,
    bool IsComplete,
    CanonicalDiagnosticResponse[] Diagnostics,
    DiagnosticCountsResponse Counts,
    bool HasErrors,
    bool Truncated,
    string[] IncompleteReasons,
    ToolDiagnostic[] ProjectDiagnostics,
    ToolDiagnostic? Error);
