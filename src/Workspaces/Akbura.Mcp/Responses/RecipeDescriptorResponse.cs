namespace Akbura.Mcp;

/// <summary>
/// Describes the identity, requirements, and expected behavior of a layout recipe.
/// </summary>
internal sealed record RecipeDescriptorResponse(
    string Id,
    int Revision,
    string Title,
    string Description,
    string[] Tags,
    string Format,
    string SuggestedFileName,
    string[] RequiredImports,
    string[] RequiredTypes,
    RecipeUtilityRequirementResponse[] RequiredUtilities,
    RecipeVariantRequirementResponse[] RequiredVariants,
    string[] Preconditions,
    string[] ExpectedBehavior,
    string[] IntegrationNotes,
    string[] Limitations);
