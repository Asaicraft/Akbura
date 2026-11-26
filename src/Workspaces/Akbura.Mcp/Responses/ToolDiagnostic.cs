namespace Akbura.Mcp;

/// <summary>
/// Carries a session or request message with its machine-readable code.
/// </summary>
internal sealed record ToolDiagnostic(string Code, string Message);
