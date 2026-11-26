namespace Akbura.Mcp;

/// <summary>
/// Preserves a canonical issue with its source ownership, identity, and related locations.
/// </summary>
internal sealed record CanonicalDiagnosticResponse(
    string Code,
    string Severity,
    string Message,
    string Kind,
    string LogicalId,
    string SourceOwnership,
    DiagnosticLocationResponse? Location,
    DiagnosticLocationResponse[] RelatedLocations,
    string Provenance,
    IReadOnlyDictionary<string, string?> Properties);
