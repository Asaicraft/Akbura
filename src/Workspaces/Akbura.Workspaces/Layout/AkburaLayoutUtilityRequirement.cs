using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Identifies the standard utility signature required on a recipe control.
/// </summary>
public sealed record AkburaLayoutUtilityRequirement(
    string Name,
    string TargetType,
    string DeclaredTargetType,
    ImmutableArray<string> ParameterTypes);
