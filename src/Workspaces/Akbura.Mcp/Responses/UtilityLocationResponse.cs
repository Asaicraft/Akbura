namespace Akbura.Mcp;

/// <summary>
/// Identifies a utility definition by its file path and UTF-16 source span.
/// </summary>
internal sealed record UtilityLocationResponse(string FilePath, int Start, int Length);
