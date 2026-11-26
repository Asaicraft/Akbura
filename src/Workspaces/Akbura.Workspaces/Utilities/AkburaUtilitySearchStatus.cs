namespace Akbura.Workspaces.Utilities;

/// <summary>
/// Describes the outcome of resolving a target and searching for its applicable utilities.
/// </summary>
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
