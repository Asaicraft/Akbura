namespace Akbura.Mcp;

/// <summary>
/// Describes a required breakpoint extension and its minimum client width.
/// </summary>
internal sealed record RecipeVariantRequirementResponse(string Name, string TypeName, double MinimumClientWidth);
