namespace Akbura.Mcp;

/// <summary>
/// Carries a layout reference example and its applicability to the selected document.
/// </summary>
internal sealed record RecipeToolResponse(
    string Status,
    string? DocumentPath,
    RecipeDescriptorResponse? Recipe,
    string? ExampleSource,
    string? Availability,
    string[] AvailabilityReasons,
    string[] MissingImports,
    SnapshotResponse? Snapshot,
    RecipeValidationResponse? Validation,
    ToolDiagnostic[] ProjectDiagnostics,
    ToolDiagnostic? Error);
