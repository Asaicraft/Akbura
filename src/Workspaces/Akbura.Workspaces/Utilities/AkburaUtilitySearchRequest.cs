namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Supplies exactly one target. Position is an absolute zero-based UTF-16 offset.
/// TargetType is resolved with the document's imports and project references.
/// </summary>
public sealed record AkburaUtilitySearchRequest(
    int? Position = null,
    string? TargetType = null,
    string? Query = null,
    string? Category = null,
    bool IncludeImportable = true,
    int Limit = 20);
