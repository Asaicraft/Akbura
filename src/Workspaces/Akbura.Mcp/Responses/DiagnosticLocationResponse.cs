namespace Akbura.Mcp;

/// <summary>
/// Describes a diagnostic's source and any original coordinates available for it.
/// </summary>
internal sealed record DiagnosticLocationResponse(
    string Path,
    string? Uri,
    DiagnosticTextSpanResponse? OriginalSpan,
    DiagnosticRangeResponse? OriginalRange,
    string? MappingLimitation);
