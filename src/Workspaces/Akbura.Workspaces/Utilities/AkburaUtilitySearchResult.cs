using System.Collections.Immutable;

namespace Akbura.Workspaces.Utilities;

public enum AkburaUtilitySearchStatus
{
    Success,
    InvalidDocument,
    InvalidTargetSelection,
    InvalidPosition,
    UnresolvedTarget,
    AmbiguousTarget,
    MissingReferences,
}

public sealed record AkburaUtilitySearchResult(
    AkburaUtilitySearchStatus Status,
    string? ResolvedTargetType,
    ImmutableArray<AkburaUtilityDescriptor> Utilities,
    string SnapshotId,
    bool IsComplete,
    bool Truncated,
    ImmutableArray<AkburaUtilityCatalogDiagnostic> Diagnostics);

public sealed record AkburaUtilityCatalogDiagnostic(string Code, string Message);

/// <summary>Imports for standard features found in this project's references.</summary>
public sealed record AkburaStandardUtilityLibrary(
    string? UtilityImport,
    string? VariantImport,
    AkburaUtilityCatalogDiagnostic? Diagnostic,
    bool SupportsGridBreakpointExample)
{
    public bool IsAvailable => UtilityImport != null;
}
