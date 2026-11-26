using System.Collections.Immutable;

namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Contains applicable utility definitions and completeness information for a captured snapshot.
/// </summary>
public sealed record AkburaUtilitySearchResult(
    AkburaUtilitySearchStatus Status,
    string? ResolvedTargetType,
    ImmutableArray<AkburaUtilityDescriptor> Utilities,
    string SnapshotId,
    bool IsComplete,
    bool Truncated,
    ImmutableArray<AkburaUtilityCatalogDiagnostic> Diagnostics);
