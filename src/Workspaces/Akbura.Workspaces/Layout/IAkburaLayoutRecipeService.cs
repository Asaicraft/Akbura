using System.Collections.Immutable;

namespace Akbura.Workspaces.Layout;

/// <summary>
/// Exposes layout recipe metadata and validates reference components for a document snapshot.
/// </summary>
public interface IAkburaLayoutRecipeService
{
    ImmutableArray<AkburaLayoutRecipeDescriptor> GetRecipes();

    /// <summary>Checks a reference component against one captured document context.</summary>
    /// <returns>The recipe result, or null if the ID is unknown.</returns>
    AkburaLayoutRecipeResult? GetRecipe(
        AkburaDocumentContext context,
        AkburaLayoutRecipeRequest request,
        CancellationToken cancellationToken = default);
}
