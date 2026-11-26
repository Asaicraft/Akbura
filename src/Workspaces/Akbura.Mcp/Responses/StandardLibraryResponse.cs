namespace Akbura.Mcp;

/// <summary>
/// Reports the standard AKCSS utility and variant imports supported by the project.
/// </summary>
internal sealed record StandardLibraryResponse(
    bool IsAvailable,
    string? UtilityImport,
    string? VariantImport);
