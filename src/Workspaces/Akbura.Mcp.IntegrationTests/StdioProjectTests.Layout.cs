using ModelContextProtocol.Client;
using System.Text.Json;

namespace Akbura.Mcp.IntegrationTests;

public sealed partial class StdioProjectTests
{
    private static async Task VerifyLayoutWorkflowAsync(
        McpClient client, ProjectFixture fixture, JsonElement info, CancellationToken cancellationToken)
    {
        var summaries = info.GetProperty("layoutRecipes").EnumerateArray().ToArray();
        Assert.Equal(["responsive-card-grid", "responsive-spacing", "responsive-secondary-content"],
            summaries.Select(recipe => recipe.GetProperty("id").GetString()));
        Assert.All(summaries, recipe =>
        {
            Assert.False(string.IsNullOrEmpty(recipe.GetProperty("description").GetString()));
            Assert.False(recipe.TryGetProperty("exampleSource", out _));
        });

        var fileList = Directory.GetFileSystemEntries(fixture.DirectoryPath).Order().ToArray();
        var originalView = await File.ReadAllTextAsync(fixture.ViewPath, cancellationToken);
        var originalStyles = await File.ReadAllTextAsync(fixture.StylesPath, cancellationToken);
        var projectText = await File.ReadAllTextAsync(fixture.ProjectPath, cancellationToken);
        var result = Content(await client.CallToolAsync("akbura_get_layout_recipe",
            new Dictionary<string, object?> { ["documentPath"] = "View.akbura", ["recipeId"] = "responsive-card-grid" },
            cancellationToken: cancellationToken), "success");
        var attributes = result.GetProperty("recipe");
        Assert.Equal("responsive-card-grid", attributes.GetProperty("id").GetString());
        Assert.Equal(1, attributes.GetProperty("revision").GetInt32());
        Assert.Equal("akbura", attributes.GetProperty("format").GetString());
        Assert.Equal(info.GetProperty("snapshotId").GetString(), result.GetProperty("snapshot").GetProperty("solution").GetString());
        Assert.Equal("requires_import", result.GetProperty("availability").GetString());
        Assert.Contains(result.GetProperty("missingImports").EnumerateArray(),
            item => item.GetString() == "using Akbura.Styles.akcss;");
        var validation = result.GetProperty("validation");
        Assert.Equal("passed", validation.GetProperty("status").GetString());
        Assert.Equal("semantic", validation.GetProperty("completedLevel").GetString());
        Assert.Equal("standalone_reference_component", validation.GetProperty("scope").GetString());
        Assert.False(validation.GetProperty("integratedDocumentValidated").GetBoolean());
        Assert.False(validation.GetProperty("projectRuntimeValidated").GetBoolean());
        Assert.True(validation.GetProperty("hasRepositoryRuntimeTests").GetBoolean());
        Assert.False(File.Exists(validation.GetProperty("examplePath").GetString()));
        Assert.Equal(fileList, Directory.GetFileSystemEntries(fixture.DirectoryPath).Order());
        Assert.Equal(originalView, await File.ReadAllTextAsync(fixture.ViewPath, cancellationToken));
        Assert.Equal(originalStyles, await File.ReadAllTextAsync(fixture.StylesPath, cancellationToken));
        Assert.Equal(projectText, await File.ReadAllTextAsync(fixture.ProjectPath, cancellationToken));

        var unknownRecipe = await client.CallToolAsync("akbura_get_layout_recipe",
            new Dictionary<string, object?> { ["documentPath"] = "View.akbura", ["recipeId"] = "unknown-recipe" },
            cancellationToken: cancellationToken);
        Content(unknownRecipe, "unknown_recipe", isError: true);

        await File.WriteAllTextAsync(fixture.ViewPath, "using Akbura;\r\n<Row gap-3 />", cancellationToken);
        var missingImport = await AnalyzeAsync("View.akbura");
        Assert.Contains(missingImport.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("code").GetString() == "AKBURA_SEMANTIC_TailwindUtilityNotFound" &&
            diagnostic.GetProperty("sourceOwnership").GetString() == "document");

        var source = result.GetProperty("exampleSource").GetString()!;
        await File.WriteAllTextAsync(fixture.ViewPath, source, cancellationToken);
        var integrated = await AnalyzeAsync("View.akbura");
        Assert.False(integrated.GetProperty("hasErrors").GetBoolean(), integrated.GetRawText());
        Assert.True(integrated.GetProperty("isComplete").GetBoolean());
        Assert.Equal("semantic", integrated.GetProperty("completedLevel").GetString());

        var invalid = source
            .Replace("gap-3", "unlisted-gap-3", StringComparison.Ordinal)
            .Replace("col-span-12", "unlisted-span-12", StringComparison.Ordinal);
        await File.WriteAllTextAsync(fixture.ViewPath, invalid, cancellationToken);
        var broken = await AnalyzeAsync("View.akbura", maxDiagnostics: 1);
        var issue = Assert.Single(broken.GetProperty("diagnostics").EnumerateArray());
        Assert.Equal("AKBURA_SEMANTIC_TailwindUtilityNotFound", issue.GetProperty("code").GetString());
        Assert.Equal(fixture.ViewPath, issue.GetProperty("location").GetProperty("path").GetString());
        Assert.True(broken.GetProperty("hasErrors").GetBoolean());
        Assert.True(broken.GetProperty("truncated").GetBoolean());
        Assert.True(broken.GetProperty("counts").GetProperty("errors").GetInt32() > 1);
        Assert.NotEqual(DocumentSnapshot(integrated), DocumentSnapshot(broken));

        await File.WriteAllTextAsync(fixture.ViewPath, source, cancellationToken);
        var corrected = await AnalyzeAsync("View.akbura");
        Assert.False(corrected.GetProperty("hasErrors").GetBoolean());
        Assert.NotEqual(DocumentSnapshot(broken), DocumentSnapshot(corrected));

        await File.WriteAllTextAsync(fixture.StylesPath, "@utilities { UnknownType.example { Width: ; }", cancellationToken);
        var syntax = await AnalyzeAsync("Helpers.akcss", "syntax");
        Assert.Equal("syntax", syntax.GetProperty("requestedLevel").GetString());
        Assert.Equal("syntax", syntax.GetProperty("completedLevel").GetString());
        Assert.True(syntax.GetProperty("hasErrors").GetBoolean());
        Assert.All(syntax.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
        {
            Assert.Equal("syntax", diagnostic.GetProperty("kind").GetString());
            Assert.Equal(fixture.StylesPath, diagnostic.GetProperty("location").GetProperty("path").GetString());
        });

        const string withEmoji = "using Avalonia.Controls;\r\n<StackPanel><TextBlock Text=\"🚀\" /><UnknownWidget /></StackPanel>";
        await File.WriteAllTextAsync(fixture.ViewPath, withEmoji, cancellationToken);
        var unicode = await AnalyzeAsync("View.akbura");
        var offset = withEmoji.IndexOf("UnknownWidget", StringComparison.Ordinal);
        var unicodeIssue = Assert.Single(unicode.GetProperty("diagnostics").EnumerateArray(), diagnostic =>
            diagnostic.GetProperty("location").GetProperty("originalSpan").GetProperty("start").GetInt32() == offset);
        var start = unicodeIssue.GetProperty("location").GetProperty("originalRange").GetProperty("start");
        Assert.Equal(1, start.GetProperty("line").GetInt32());
        Assert.Equal(offset - withEmoji.IndexOf('<'), start.GetProperty("character").GetInt32());

        await File.WriteAllTextAsync(fixture.ViewPath, originalView, cancellationToken);
        await File.WriteAllTextAsync(fixture.StylesPath, originalStyles, cancellationToken);

        async Task<JsonElement> AnalyzeAsync(string path, string level = "semantic", int maxDiagnostics = 100) =>
            Content(await client.CallToolAsync("akbura_analyze_document",
                new Dictionary<string, object?> { ["documentPath"] = path, ["analysisLevel"] = level, ["maxDiagnostics"] = maxDiagnostics },
                cancellationToken: cancellationToken), "completed");

        static string? DocumentSnapshot(JsonElement response) => response.GetProperty("snapshot").GetProperty("document").GetString();
    }
}
