namespace Akbura.Workspaces.Layout;

/// <summary>
/// Selects a layout recipe and supplies the project loading completeness for validation.
/// </summary>
public sealed record AkburaLayoutRecipeRequest(string RecipeId, bool IsProjectComplete = true);
