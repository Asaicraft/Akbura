using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Layout;
using Akbura.Workspaces.Projects;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

/// <summary>
/// Checks recipe requirements, standalone validation, and isolation from the live Workspace.
/// </summary>
public sealed class WorkspaceLayoutRecipeTests
{
    [Fact]
    public void CatalogValidatesTheCanonicalExamplesWithoutUpdatingTheWorkspace()
    {
        using var workspace = CreateWorkspace();
        const string documentSource = "using Avalonia.Controls;\r\n<Border />";
        var uri = new Uri(Path.GetFullPath("LayoutTarget.akbura"));
        var context = workspace.OpenOrChangeDocumentContext(uri, SourceText.From(documentSource));
        var service = workspace.LanguageServices.LayoutRecipes;
        var recipes = service.GetRecipes();

        Assert.Equal(["responsive-card-grid", "responsive-spacing", "responsive-secondary-content"],
            recipes.Select(recipe => recipe.Id));

        foreach (var descriptor in recipes)
        {
            var result = service.GetRecipe(context, new(descriptor.Id));
            Assert.NotNull(result);
            Assert.Equal(1, result.Recipe.Revision);
            Assert.Equal("akbura", result.Recipe.Format);
            Assert.NotEmpty(result.Recipe.RequiredTypes);
            Assert.NotEmpty(result.Recipe.RequiredUtilities);
            Assert.NotEmpty(result.Recipe.RequiredVariants);
            Assert.NotEmpty(result.Recipe.Preconditions);
            Assert.Equal(ReadCanonicalExample(descriptor.SuggestedFileName), result.ExampleSource);
            Assert.True(result.Validation.Status == AkburaLayoutRecipeValidationStatus.Passed,
                string.Join(Environment.NewLine, result.AvailabilityReasons.Concat(result.Validation.Diagnostics.Select(item => item.Message))));
            Assert.Equal(AkburaLayoutRecipeAvailability.RequiresImport, result.Availability);
            Assert.Contains("using Akbura.Styles.akcss;", result.MissingImports);
            Assert.Contains("using Akbura.Markup;", result.MissingImports);
            Assert.DoesNotContain("using Avalonia.Controls;", result.MissingImports);
            Assert.Equal(context.Solution.Version.ToString(), result.Snapshot.Solution);
            Assert.Equal(context.Project.Version.ToString(), result.Snapshot.Project);
            Assert.Equal(context.Document.Version.ToString(), result.Snapshot.Document);
            Assert.Same(context.Solution, workspace.CurrentSolution);
            Assert.False(File.Exists(result.Validation.ExamplePath));
            Assert.Equal(documentSource, context.Document.Text.ToString());
        }
        Assert.Null(service.GetRecipe(context, new("unknown-recipe")));
    }

    [Fact]
    public void GlobalImportsAndReferenceRemovalAreReflectedInTheCapturedRecipeContext()
    {
        using var workspace = CreateWorkspace();
        workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("GlobalUsings.akbura")), SourceText.From("""
            using Akbura;
            using Avalonia.Controls;
            using Akbura.Styles.akcss;
            using Akbura.Markup;
            """));
        var context = Open(workspace, "<Row />");
        var service = workspace.LanguageServices.LayoutRecipes;
        var request = new AkburaLayoutRecipeRequest("responsive-card-grid");
        var available = service.GetRecipe(context, request)!;
        Assert.Equal(AkburaLayoutRecipeAvailability.Available, available.Availability);
        Assert.Empty(available.MissingImports);
        Assert.Equal(AkburaLayoutRecipeValidationStatus.Passed, available.Validation.Status);

        var original = context.Project.Context;
        var withoutRuntime = original.CSharpCompilation.RemoveReferences(original.CSharpCompilation.References.Where(
            reference => string.Equals(Path.GetFileName(reference.Display), "Akbura.dll", StringComparison.OrdinalIgnoreCase)));
        workspace.AddOrUpdateProject(new ProjectContext(original.RoslynProjectId,
            original.ProjectFilePath, original.ProjectDirectory, original.RootNamespace,
            withoutRuntime, original.ProjectReferences));
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(context.Document.Id, out var changed));

        var unavailable = service.GetRecipe(changed, request)!;
        Assert.Equal(AkburaLayoutRecipeAvailability.Unavailable, unavailable.Availability);
        Assert.Equal(AkburaLayoutRecipeValidationStatus.NotRun, unavailable.Validation.Status);
        Assert.Contains(unavailable.AvailabilityReasons, reason => reason.Contains("Akbura.Row", StringComparison.Ordinal));
        Assert.NotEqual(available.Snapshot.Project, unavailable.Snapshot.Project);
        Assert.Equal(AkburaLayoutRecipeAvailability.Available, service.GetRecipe(context, request)!.Availability);
    }

    [Fact]
    public void IncompleteProjectAndAShadowingUtilityProduceDifferentOutcomes()
    {
        using var workspace = CreateWorkspace();
        var context = Open(workspace, """
            using Akbura;
            using Avalonia.Controls;
            using Akbura.Markup;

            @akcss {
                @using Akbura;
                @utilities {
                    Row.gap-(string value) { }
                }
            }

            <Row />
            """);
        var service = workspace.LanguageServices.LayoutRecipes;
        const string id = "responsive-card-grid";
        var shadowed = service.GetRecipe(context, new(id))!;
        Assert.Equal(AkburaLayoutRecipeAvailability.Unavailable, shadowed.Availability);
        Assert.Contains(shadowed.AvailabilityReasons, reason => reason.Contains("gap", StringComparison.Ordinal));

        var incomplete = service.GetRecipe(context, new(id, IsProjectComplete: false))!;
        Assert.Equal(AkburaLayoutRecipeAvailability.Unknown, incomplete.Availability);
        Assert.Equal(AkburaLayoutRecipeValidationStatus.NotRun, incomplete.Validation.Status);
        Assert.Empty(incomplete.Validation.Diagnostics);
        Assert.NotEmpty(incomplete.ExampleSource);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => service.GetRecipe(context, new(id), cancellation.Token));
    }

    private static AkburaWorkspace CreateWorkspace() => WorkspaceCompletionTests.CreateRuntimeUtilityWorkspace(
        WorkspaceCompletionTests.CreateRuntimeUtilityReferences());

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, string source) =>
        workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("LayoutTarget.akbura")), SourceText.From(source));

    private static string ReadCanonicalExample(string filename)
    {
        using var stream = typeof(IAkburaLayoutRecipeService).Assembly.GetManifestResourceStream(
            "Akbura.Workspaces.Layout.Recipes." + filename);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
