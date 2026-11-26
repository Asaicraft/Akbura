namespace Akbura.Mcp;

/// <summary>
/// Represents a source range with an exclusive end position.
/// </summary>
internal sealed record DiagnosticRangeResponse(DiagnosticPositionResponse Start, DiagnosticPositionResponse End);
