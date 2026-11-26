using Akbura.Workspaces.Layout;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.ComponentModel;

namespace Akbura.Mcp;

internal sealed partial class McpTools
{
    private RecipeSummaryResponse[] GetRecipeSummaries()
    {
        var recipes = session.Workspace.LanguageServices.LayoutRecipes.GetRecipes();
        var summaries = new RecipeSummaryResponse[recipes.Length];
        for (var index = 0; index < recipes.Length; index++)
        {
            var recipe = recipes[index];
            summaries[index] = new(recipe.Id, recipe.Title, recipe.Description);
        }
        return summaries;
    }

    [McpServerTool(Name = "akbura_get_layout_recipe", UseStructuredContent = true, OutputSchemaType = typeof(RecipeToolResponse),
        ReadOnly = true, OpenWorld = false)]
    [Description("Retrieve a standalone layout reference component and check its requirements in the selected project.")]
    public async Task<CallToolResult> GetLayoutRecipeAsync(
        [Description("Project-relative or absolute path of a registered, saved .akbura document.")]
        string documentPath,
        [Description("A stable recipe ID returned by akbura_project_info.")]
        string recipeId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recipeId))
        {
            return RecipeError(documentPath, "invalid_arguments", "Supply a recipe ID from akbura_project_info.");
        }
        if (string.IsNullOrWhiteSpace(documentPath))
        {
            return RecipeError(documentPath, "unknown_document", "Supply the path of a registered .akbura document.");
        }

        var view = await session.CaptureAsync(cancellationToken).ConfigureAwait(false);
        if (view.Snapshot is not { } snapshot)
        {
            return RecipeError(documentPath, view.Status, view.Message!);
        }
        if (SelectDocument(snapshot, documentPath, allowAkcss: false, out var error) is not { } context)
        {
            return RecipeError(documentPath, "unknown_document", error!);
        }

        var result = session.Workspace.LanguageServices.LayoutRecipes.GetRecipe(context,
            new(recipeId, IsLoadComplete), cancellationToken);
        if (result == null)
        {
            return RecipeError(documentPath, "unknown_recipe", "This recipe ID is not published by akbura_project_info.");
        }

        var recipe = result.Recipe;
        var availability = result.Availability switch
        {
            AkburaLayoutRecipeAvailability.Available => "available",
            AkburaLayoutRecipeAvailability.RequiresImport => "requires_import",
            AkburaLayoutRecipeAvailability.Unavailable => "unavailable",
            _ => "unknown",
        };
        var validation = result.Validation;
        var diagnosticResponses = McpDiagnosticMapper.MapDiagnostics(validation.Diagnostics,
            validation.ExamplePath ?? string.Empty, "example", validation.Diagnostics.Length, cancellationToken).Diagnostics;

        var response = new RecipeToolResponse("success", context.Document.FilePath, MapRecipe(recipe),
            result.ExampleSource, availability, result.AvailabilityReasons.ToArray(), result.MissingImports.ToArray(),
            McpDiagnosticMapper.MapSnapshot(result.Snapshot),
            new(validation.Status switch
            {
                AkburaLayoutRecipeValidationStatus.Passed => "passed",
                AkburaLayoutRecipeValidationStatus.Failed => "failed",
                AkburaLayoutRecipeValidationStatus.Incomplete => "incomplete",
                _ => "not_run",
            },
            validation.CompletedLevel is { } level ? McpDiagnosticMapper.MapLevel(level) : null,
            validation.ExamplePath, "standalone_reference_component", HasRepositoryRuntimeTests: true,
            IntegratedDocumentValidated: false, ProjectRuntimeValidated: false, diagnosticResponses),
            GetLoadDiagnostics(), null);

        cancellationToken.ThrowIfCancellationRequested();
        return FormatResponse(response, isError: false, $"{recipe.Title} is a reference component. Project availability is {availability}.");
    }

    private static RecipeDescriptorResponse MapRecipe(AkburaLayoutRecipeDescriptor recipe)
    {
        var utilities = new RecipeUtilityRequirementResponse[recipe.RequiredUtilities.Length];
        for (var index = 0; index < utilities.Length; index++)
        {
            var requirement = recipe.RequiredUtilities[index];
            utilities[index] = new(requirement.Name, requirement.TargetType, requirement.DeclaredTargetType, requirement.ParameterTypes.ToArray());
        }

        var variants = new RecipeVariantRequirementResponse[recipe.RequiredVariants.Length];
        for (var index = 0; index < variants.Length; index++)
        {
            var requirement = recipe.RequiredVariants[index];
            variants[index] = new(requirement.Name, requirement.TypeName, requirement.MinimumClientWidth);
        }

        return new(recipe.Id, recipe.Revision, recipe.Title, recipe.Description, recipe.Tags.ToArray(),
            recipe.Format, recipe.SuggestedFileName, recipe.RequiredImports.ToArray(), recipe.RequiredTypes.ToArray(),
            utilities, variants, recipe.Preconditions.ToArray(), recipe.ExpectedBehavior.ToArray(),
            recipe.IntegrationNotes.ToArray(), recipe.Limitations.ToArray());
    }

    private static CallToolResult RecipeError(string? path, string status, string message) =>
        FormatResponse(new RecipeToolResponse(status, path, null, null, null, [], [], null, null, [], new(status, message)),
            isError: true, message);
}
