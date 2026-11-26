namespace Akbura.Workspaces.Layout;

/// <summary>
/// Identifies the breakpoint extension and minimum client width used by a recipe.
/// </summary>
public sealed record AkburaLayoutVariantRequirement(
    string Name,
    string TypeName,
    double MinimumClientWidth);
