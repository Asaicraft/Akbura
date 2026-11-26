namespace Akbura.Mcp;

/// <summary>
/// Counts diagnostics by severity before the response limit is applied.
/// </summary>
internal sealed record DiagnosticCountsResponse(int Total, int Errors, int Warnings, int Information, int Hidden);
