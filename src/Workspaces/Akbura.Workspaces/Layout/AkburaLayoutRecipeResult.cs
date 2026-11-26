using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Pairs a layout reference component with applicability and validation for a captured snapshot.
/// </summary>
public sealed record AkburaLayoutRecipeResult(
    AkburaLayoutRecipeDescriptor Recipe,
    string ExampleSource,
    AkburaLayoutRecipeAvailability Availability,
    ImmutableArray<string> AvailabilityReasons,
    ImmutableArray<string> MissingImports,
    AkburaDocumentSnapshotIdentity Snapshot,
    AkburaLayoutRecipeValidation Validation);
