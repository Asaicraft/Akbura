using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Utilities;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceUtilityCatalogTests
{
    private const string RowDocument = "using Akbura;\r\n<Row />";

    [Fact]
    public void StandardGapIsImportableAndBecomesAvailableWithoutChangingOldSnapshots()
    {
        using var workspace = CreateWorkspace();
        var context = Open(workspace, RowDocument);
        var catalog = workspace.LanguageServices.UtilityCatalog;
        var snapshot = workspace.CurrentSolution;

        var requiresImport = catalog.FindUtilities(context, new(TargetType: "Row", Query: "gap"));
        var utility = Assert.Single(requiresImport.Utilities, item => item.Name == "gap");
        Assert.Equal("Akbura.Row", utility.DeclaredTargetType);
        Assert.Equal("gap-<value>", utility.UsageSyntax);
        Assert.Equal(new("value", "double", false), Assert.Single(utility.Parameters));
        Assert.Equal(AkburaUtilityAvailability.RequiresImport, utility.Availability);
        Assert.Equal("using Akbura.Styles.akcss;", Assert.Single(utility.RequiredImports));
        Assert.Equal(AkburaUtilityOriginKind.Library, utility.OriginKind);
        Assert.True(requiresImport.IsComplete);
        Assert.Same(snapshot, workspace.CurrentSolution);
        Assert.Equal(RowDocument, context.Document.Text.ToString());
        Assert.Empty(catalog.FindUtilities(context, new(TargetType: "Row", Query: "gap", IncludeImportable: false)).Utilities);

        var imported = Open(workspace, "using Akbura.Styles.akcss;\r\n" + RowDocument);
        var available = catalog.FindUtilities(imported, new(TargetType: "Row", Query: "gap"));
        var importedUtility = Assert.Single(available.Utilities, item => item.Name == "gap");
        Assert.Equal(utility.Id, importedUtility.Id);
        Assert.Equal(AkburaUtilityAvailability.Available, importedUtility.Availability);
        Assert.Empty(importedUtility.RequiredImports);
        Assert.NotEqual(requiresImport.SnapshotId, available.SnapshotId);
        Assert.Equal(requiresImport.SnapshotId, catalog.FindUtilities(context, new(TargetType: "Row", Query: "gap")).SnapshotId);
    }

    [Fact]
    public void TargetApplicabilityUsesActualMarkupElementAndInheritedControlTypes()
    {
        using var workspace = WorkspaceCompletionTests.CreateRuntimeUtilityWorkspace(
            WorkspaceCompletionTests.CreateRuntimeUtilityReferences(),
            "namespace Demo; public class CustomPanel : Avalonia.Controls.StackPanel { }");
        const string source = "using Avalonia.Controls;\r\nusing Akbura;\r\n" +
            "<StackPanel><TextBlock Text=\"🚀\" /><Row /><Button /></StackPanel>";
        var context = Open(workspace, source);
        var catalog = workspace.LanguageServices.UtilityCatalog;
        var row = catalog.FindUtilities(context, new(Position: source.IndexOf("<Row", StringComparison.Ordinal) + 1, Query: "gap"));
        Assert.Equal("Akbura.Row", row.ResolvedTargetType);
        Assert.Contains(row.Utilities, item => item.Name == "gap" && item.DeclaredTargetType == "Akbura.Row");

        var button = catalog.FindUtilities(context, new(Position: source.IndexOf("<Button", StringComparison.Ordinal) + 1, Query: "gap"));
        Assert.Equal("Avalonia.Controls.Button", button.ResolvedTargetType);
        Assert.Equal(AkburaUtilitySearchStatus.Success, button.Status);
        Assert.Empty(button.Utilities);

        var derived = catalog.FindUtilities(context, new(TargetType: "Demo.CustomPanel", Query: "gap"));
        Assert.Equal("Demo.CustomPanel", derived.ResolvedTargetType);
        Assert.Equal("Avalonia.Controls.StackPanel", Assert.Single(derived.Utilities).DeclaredTargetType);

        var colSpan = Assert.Single(catalog.FindUtilities(context, new(TargetType: "Button", Query: "col-span")).Utilities);
        Assert.Equal(new("value", "int", false), Assert.Single(colSpan.Parameters));
        Assert.Equal("col-span-6", colSpan.Example);
    }

    [Fact]
    public void MissingStandardLibraryAndTargetFailuresAreExplicit()
    {
        var references = WorkspaceCompletionTests.CreateRuntimeUtilityReferences()
            .Where(reference => !string.Equals(Path.GetFileName(reference.Display), "Akbura.dll", StringComparison.OrdinalIgnoreCase))
            .ToImmutableArray();
        using var workspace = WorkspaceCompletionTests.CreateRuntimeUtilityWorkspace(references, """
            namespace First { public class Box : Avalonia.Controls.Border { } }
            namespace Second { public class Box : Avalonia.Controls.Border { } }
            """);
        var catalog = workspace.LanguageServices.UtilityCatalog;
        var context = Open(workspace, "using Avalonia.Controls;\r\nusing First;\r\nusing Second;\r\n<Border />");
        var missingLibrary = catalog.FindUtilities(context, new(TargetType: "Border", Query: "col-span"));
        Assert.Equal(AkburaUtilitySearchStatus.Success, missingLibrary.Status);
        Assert.False(missingLibrary.IsComplete);
        Assert.Empty(missingLibrary.Utilities);
        Assert.Contains(missingLibrary.Diagnostics, diagnostic => diagnostic.Code == "standard_library_unavailable");
        Assert.False(catalog.GetStandardLibrary(context.Project).IsAvailable);

        Assert.Equal(AkburaUtilitySearchStatus.UnresolvedTarget,
            catalog.FindUtilities(context, new(TargetType: "Does.Not.Exist")).Status);
        Assert.Equal(AkburaUtilitySearchStatus.AmbiguousTarget,
            catalog.FindUtilities(context, new(TargetType: "Box")).Status);
        Assert.Equal(AkburaUtilitySearchStatus.Success,
            catalog.FindUtilities(context, new(TargetType: "Second.Box")).Status);
        Assert.Equal(AkburaUtilitySearchStatus.InvalidPosition,
            catalog.FindUtilities(context, new(Position: context.Document.Text.Length)).Status);
        Assert.Equal(AkburaUtilitySearchStatus.InvalidPosition,
            catalog.FindUtilities(context, new(Position: 4)).Status);
        Assert.Equal(AkburaUtilitySearchStatus.InvalidTargetSelection,
            catalog.FindUtilities(context, new(Position: 4, TargetType: "Border")).Status);

        using var syntaxOnly = new AkburaWorkspace();
        Assert.Equal(AkburaUtilitySearchStatus.MissingReferences, syntaxOnly.LanguageServices.UtilityCatalog.FindUtilities(
            Open(syntaxOnly, RowDocument), new(TargetType: "Akbura.Row")).Status);
    }

    [Fact]
    public void SameNameDefinitionsKeepSignaturesTargetsAndSourceLocationsAfterSynchronization()
    {
        using var workspace = CreateWorkspace();
        var viewPath = new Uri(Path.GetFullPath("View.akbura"));
        var stylesPath = new Uri(Path.GetFullPath("Helpers.akcss"));
        var source = SourceText.From("using Avalonia.Controls;\r\nusing Helpers.akcss;\r\n<Border />");
        const string utilities = """
            @using Avalonia.Controls;
            @utilities {
                Control.shared-(int amount) { }
                Border.shared-(int amount) { }
                Border.shared-(string text) { }
            }
            """;
        var projectId = workspace.DefaultProjectId;
        var catalog = workspace.LanguageServices.UtilityCatalog;
        var search = new AkburaUtilitySearchRequest(TargetType: "Border", Query: "shared");
        workspace.SynchronizeProjectDocuments(projectId, [new(viewPath, source), new(stylesPath, SourceText.From(utilities))]);
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(viewPath, out var oldContext));

        var old = catalog.FindUtilities(oldContext, search);
        Assert.Equal(3, old.Utilities.Length);
        Assert.Equal(3, old.Utilities.Select(item => item.Id).Distinct().Count());
        Assert.All(old.Utilities, item =>
        {
            Assert.Equal(AkburaUtilityAvailability.Available, item.Availability);
            Assert.Equal(AkburaUtilityOriginKind.Source, item.OriginKind);
            Assert.Equal(stylesPath.LocalPath, item.SourceLocation?.FilePath);
        });
        Assert.Contains(old.Utilities, item => item.DeclaredTargetType == "Avalonia.Controls.Control");

        var changed = SourceText.From(utilities.Replace("string text", "double amount", StringComparison.Ordinal));
        workspace.SynchronizeProjectDocuments(projectId, [new(viewPath, source), new(stylesPath, changed)]);
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(viewPath, out var newContext));
        var result = catalog.FindUtilities(newContext, search);
        Assert.NotEqual(old.SnapshotId, result.SnapshotId);
        Assert.Contains(result.Utilities, item => item.Parameters[0].Type == "double");
        Assert.DoesNotContain(result.Utilities, item => item.Parameters[0].Type == "string");
        Assert.Contains(catalog.FindUtilities(oldContext, search).Utilities, item => item.Parameters[0].Type == "string");
    }

    [Theory]
    [InlineData("расстояние между элементами")]
    [InlineData("расстояние между карточками")]
    [InlineData("spacing between items")]
    public void KeywordsAndLimitsApplyAfterTypeFiltering(string query)
    {
        using var workspace = CreateWorkspace();
        var context = Open(workspace, RowDocument);
        var catalog = workspace.LanguageServices.UtilityCatalog;

        var result = catalog.FindUtilities(context, new(TargetType: "Akbura.Row", Query: query, Category: "layout", Limit: 2));
        Assert.Equal(["gap", "gap-x"], result.Utilities.Select(item => item.Name));
        Assert.True(result.Truncated);

        var exact = catalog.FindUtilities(context, new(TargetType: "Akbura.Row", Query: "gap", Limit: 1));
        Assert.Equal("gap", Assert.Single(exact.Utilities).Name);
        Assert.True(exact.Truncated);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            catalog.FindUtilities(context, new(TargetType: "Akbura.Row", Limit: 101)));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() =>
            catalog.FindUtilities(context, new(TargetType: "Akbura.Row", Query: "gap"), cancellation.Token));
    }

    private static AkburaWorkspace CreateWorkspace() =>
        WorkspaceCompletionTests.CreateRuntimeUtilityWorkspace(WorkspaceCompletionTests.CreateRuntimeUtilityReferences());

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, string source) =>
        workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("CatalogView.akbura")), SourceText.From(source));
}
