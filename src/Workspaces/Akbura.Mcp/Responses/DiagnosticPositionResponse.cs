namespace Akbura.Mcp;

/// <summary>
/// Identifies a zero-based line and UTF-16 character offset within that line.
/// </summary>
internal sealed record DiagnosticPositionResponse(int Line, int Character);
