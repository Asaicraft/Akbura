namespace Akbura.Mcp;

/// <summary>
/// Represents an absolute source span by its zero-based UTF-16 start and length.
/// </summary>
internal sealed record DiagnosticTextSpanResponse(int Start, int Length);
