namespace Akbura.Mcp;

/// <summary>
/// Describes a required AKCSS utility signature and the control that uses it.
/// </summary>
internal sealed record RecipeUtilityRequirementResponse(
    string Name, string TargetType, string DeclaredTargetType, string[] ParameterTypes);
