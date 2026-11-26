namespace Akbura.Workspaces.Layout;

/// <summary>
/// Describes whether a recipe can be used in the selected document context.
/// </summary>
public enum AkburaLayoutRecipeAvailability
{
    Available,
    RequiresImport,
    Unavailable,
    Unknown,
}
