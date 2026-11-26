namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Reports imports and example capabilities verified against the project's standard references.
/// </summary>
public sealed record AkburaStandardUtilityLibrary(
    string? UtilityImport,
    string? VariantImport,
    AkburaUtilityCatalogDiagnostic? Diagnostic,
    bool SupportsGridBreakpointExample)
{
    public bool IsAvailable => UtilityImport != null;
}
