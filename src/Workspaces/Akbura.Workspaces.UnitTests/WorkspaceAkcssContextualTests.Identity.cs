using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Resources;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceAkcssContextualTests
{
    [Fact]
    public void MixinAndParameterRenameFollowBindingsAcrossFiles()
    {
        const string shared = "@mixin Button Configure(double width) {\r\n" +
            "    Width: Helpers.Use(width, width => width + 1d); // width\r\n}";
        const string consumer = "@using Gallery.Shared.akcss;\r\n@theme Button T { @include Configure(2d); }";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(
            "public static class Helpers { public static double Use(double value, System.Func<double, double> map) => map(value); }");
        var declaration = Open(workspace, "Shared.akcss", Imports + shared);
        var view = Open(workspace, "Hello.akcss", Imports + consumer);
        var services = workspace.LanguageServices;
        var include = (Imports + consumer).IndexOf("Configure", StringComparison.Ordinal);
        var mixin = services.Rename.GetRenameChanges(view, include, "ConfigureSize");
        Assert.Equal(2, mixin.Changes.Count);
        Assert.Contains("@mixin Button ConfigureSize", declaration.Document.Text.WithChanges(mixin.Changes[declaration.Document.Uri]).ToString());
        Assert.Contains("@include ConfigureSize(2d)", view.Document.Text.WithChanges(mixin.Changes[view.Document.Uri]).ToString());

        var currentDeclaration = InCurrentSnapshot(workspace, declaration);
        var parameter = (Imports + shared).IndexOf("width", StringComparison.Ordinal);
        var edits = Assert.Single(services.Rename.GetRenameChanges(currentDeclaration, parameter, "size").Changes).Value;
        Assert.Equal(2, edits.Length);
        Assert.Contains("double size", currentDeclaration.Document.Text.WithChanges(edits).ToString());
        Assert.Contains("Helpers.Use(size, width => width + 1d); // width", currentDeclaration.Document.Text.WithChanges(edits).ToString());
    }

    [Fact]
    public void RenamingALocalPartDoesNotEditAReplacementTemplate()
    {
        const string rules = "\r\n^ /template/ ContentPresenter#PART_Presenter { Background: null; }\r\n";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var original = Open(workspace, "Hello.akcss", Imports + Theme.Insert(Theme.Length - 1, rules));
        var replacement = Theme.Replace("MyTheme", "Replacement", StringComparison.Ordinal)
            .Replace("@template", "@based-on Gallery.Hello.MyTheme; @template", StringComparison.Ordinal);
        var derived = Open(workspace, "Replacement.akcss", Imports + replacement.Insert(replacement.Length - 1, rules));
        var services = workspace.LanguageServices;
        var position = derived.Document.Text.ToString().LastIndexOf("PART_Presenter", StringComparison.Ordinal);
        Assert.Equal(derived.Document.FilePath, services.Definition.GetDefinition(derived, position)!.TargetFilePath);
        var change = Assert.Single(services.Rename.GetRenameChanges(derived, position, "PART_Alternate").Changes);
        Assert.Equal(derived.Document.Uri, change.Key);
        Assert.Equal(2, change.Value.Length);
        Assert.All(change.Value, edit => Assert.Equal("PART_Presenter", derived.Document.Text.ToString(edit.Span)));
        var originalPart = original.Document.Text.ToString().LastIndexOf("PART_Presenter", StringComparison.Ordinal);
        Assert.Equal(original.Document.FilePath,
            services.Definition.GetDefinition(InCurrentSnapshot(workspace, original), originalPart)!.TargetFilePath);
    }

    [Fact]
    public void SharedMixinPartIsAmbiguousAndPreventsPartialRename()
    {
        const string usage = " { @template { " + Template + " } @include PartStyle(); }";
        var source = Imports + "@mixin Button PartStyle() { ^ /template/ #PART_Presenter { Opacity: 1d; } }\r\n" +
            "@theme Button First" + usage + "\r\n@theme Button Second" + usage;
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var context = Open(workspace, "Hello.akcss", source);
        var services = workspace.LanguageServices;
        var sharedPart = source.IndexOf("PART_Presenter", StringComparison.Ordinal);
        var firstDeclaration = source.IndexOf("PART_Presenter", sharedPart + 1, StringComparison.Ordinal);
        var secondDeclaration = source.LastIndexOf("PART_Presenter", StringComparison.Ordinal);
        Assert.Null(services.Definition.GetDefinition(context, sharedPart));
        foreach (var position in new[] { firstDeclaration, secondDeclaration })
        {
            var occurrences = services.References.FindReferences(context, position, includeDeclaration: true).Locations;
            Assert.Equal(new[] { sharedPart, position }, occurrences.Select(static item => item.Span.Start));
            Assert.Throws<InvalidOperationException>(() => services.Rename.GetRenameChanges(context, position, "PART_Other"));
        }
    }

    [Fact]
    public void ExportedThemeReferencesUnifyWithInheritanceAndPreserveUtf16Offsets()
    {
        const string derived = "@theme Button Derived { @based-on Gallery.Hello.MyTheme; }";
        const string source = "using Avalonia.Controls;\r\nusing ThemeModule = Gallery.Hello;\r\n" +
            "<StackPanel><TextBlock Text=\"🐣\" /><Button Theme={ThemeModule.MyTheme} />" +
            "<Button Theme={global::Gallery.Hello.MyTheme} /></StackPanel>";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var declaration = Open(workspace, "Hello.akcss", Imports + Theme);
        Open(workspace, "Derived.akcss", Imports + derived);
        var view = Open(workspace, "View.akbura", source);
        var currentDeclaration = InCurrentSnapshot(workspace, declaration);
        var position = source.IndexOf("MyTheme", StringComparison.Ordinal);
        var result = workspace.LanguageServices.References.FindReferences(view, position, includeDeclaration: true);
        Assert.Equal(4, result.Locations.Length);
        Assert.Contains(result.Locations, location => location.IsDeclaration && location.Uri == declaration.Document.Uri);
        Assert.Equal(new[] { position, source.LastIndexOf("MyTheme", StringComparison.Ordinal) },
            result.Locations.Where(location => location.Uri == view.Document.Uri).Select(static location => location.Span.Start));
        var fromDeclaration = workspace.LanguageServices.References.FindReferences(currentDeclaration,
            (Imports + Theme).IndexOf("MyTheme", StringComparison.Ordinal), includeDeclaration: true);
        Assert.Equal(result.Symbol, fromDeclaration.Symbol);
    }

    [Fact]
    public void ImportedMixinEditsRebindConsumersAndRetainOldSnapshotAnswers()
    {
        const string before = "@mixin Button Configure(double width = 1d) { Width: width; }";
        const string after = "@mixin Button Configure(double width = 2d) { Width: missing; }";
        const string consumer = "@using Gallery.Shared.akcss;\r\n@theme Button T { @include Configure(); }";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        Open(workspace, "Shared.akcss", Imports + before);
        var old = Open(workspace, "Hello.akcss", Imports + consumer);
        var position = old.Document.Text.ToString().IndexOf("Configure", StringComparison.Ordinal);
        var services = workspace.LanguageServices;
        var beforeHover = services.QuickInfo.GetQuickInfo(old, position);
        Assert.NotNull(beforeHover);
        Assert.Contains("1d", beforeHover.Signature);
        Open(workspace, "Shared.akcss", Imports + after);
        var updated = InCurrentSnapshot(workspace, old);
        Assert.Equal(old.Document.Version, updated.Document.Version);
        Assert.Contains("2d", services.QuickInfo.GetQuickInfo(updated, position)!.Signature);
        Assert.Equal(beforeHover.Signature, services.QuickInfo.GetQuickInfo(old, position)!.Signature);

        using var fresh = WorkspaceControlEventHookTests.CreateWorkspace();
        Open(fresh, "Shared.akcss", Imports + after);
        var freshConsumer = Open(fresh, "Hello.akcss", Imports + consumer);
        Assert.Equal(DiagnosticFacts(fresh, freshConsumer), DiagnosticFacts(workspace, updated));
        Open(workspace, "Shared.akcss", Imports + before);
        var undone = InCurrentSnapshot(workspace, old);
        Assert.Equal(DiagnosticFacts(workspace, old), DiagnosticFacts(workspace, undone));
    }

    [Theory]
    [InlineData("Amx.DynamicResource", true)]
    [InlineData("Amx.StaticResource", true)]
    [InlineData("Lookalike.DynamicResource", false)]
    public void ResourceKeysUseBoundAmxMethods(string method, bool isResource)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(
            "public static class Lookalike { public static object DynamicResource(string key) => new(); }");
        var project = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId);
        var path = Path.Combine(Environment.CurrentDirectory, "App.axaml");
        var resources = SourceText.From("""
            <Application xmlns="https://github.com/avaloniaui"
                         xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
                <Application.Resources>
                    <SolidColorBrush x:Key="ButtonBackground" />
                </Application.Resources>
            </Application>
            """);
        workspace.SynchronizeProjectResourceDocuments(project.Id,
            [new ResourceDocumentInput(new Uri(path), path, "App.axaml",
                ResourceAssemblyIdentity.Create(project.Compilation.CSharpCompilation.Assembly.Identity),
                resources, VersionStamp.Create(), "project", "net10.0")]);

        var source = Imports + "@theme Button T { Background: " + method + "(\"ButtonBack|ground\"); }";
        var (context, document, position) = OpenAt(workspace, source, inline: false);
        var completion = workspace.LanguageServices.Completion;
        var result = completion.GetCompletions(document, context, position);
        if (!isResource)
        {
            Assert.DoesNotContain(result.Items, item => item.DisplayText == "ButtonBackground");
            return;
        }

        var item = Assert.Single(result.Items, item => item.DisplayText == "ButtonBackground");
        var change = Assert.Single(completion.GetCompletionChange(document, context, position, item).Changes);
        Assert.Equal("ButtonBackground", document.Text.ToString(change.Span));
        Assert.Equal("ButtonBackground", change.NewText);
    }

    [Fact]
    public void TemplateMarkupImportsBelongToTheActiveInlineBlock()
    {
        const string types = """
            namespace First { public class FirstControl : Avalonia.Controls.Button { } }
            namespace Second { public class SecondControl : Avalonia.Controls.Button { } }
            """;
        const string marked =
            "@akcss {\r\n@using First;\r\n@theme Avalonia.Controls.Button FirstTheme { @template { <FirstControl /> } }\r\n}\r\n" +
            "@akcss {\r\n@theme Avalonia.Controls.Button SecondTheme { @template { <SecondCon| } }\r\n}\r\n" +
            "<Avalonia.Controls.Border />";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(types);
        var position = marked.IndexOf('|');
        var context = Open(workspace, "Inline.akbura", marked.Remove(position, 1));
        var document = AkburaSyntacticDocument.Create(context.Document);
        var completion = workspace.LanguageServices.Completion;
        var result = completion.GetCompletions(document, context, position);
        var item = Assert.Single(result.Items, item => item.DisplayText == "SecondControl");
        var change = completion.GetCompletionChange(document, context, position, item);
        var changed = document.Text.WithChanges(change.Changes).ToString();
        var secondBlock = changed.LastIndexOf("@akcss", StringComparison.Ordinal);
        Assert.Contains("@using Second;", changed[secondBlock..]);
        Assert.DoesNotContain("@using Second;", changed[..secondBlock]);
        Assert.Contains("<SecondControl", changed[secondBlock..]);
    }

    [Fact]
    public void RenameUpdatesLocalThemeInheritanceInItsBlock()
    {
        const string source = "@akcss {\r\n@using Avalonia.Controls;\r\n" +
            "@theme Button BaseTheme { }\r\n@theme Button Derived { @based-on BaseTheme; }\r\n}\r\n" +
            "@akcss { @using Avalonia.Controls; @theme Button BaseTheme { } }\r\n<Avalonia.Controls.Border />";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var context = Open(workspace, "Inline.akbura", source);
        var position = source.IndexOf("BaseTheme", StringComparison.Ordinal);
        var edit = Assert.Single(workspace.LanguageServices.Rename.GetRenameChanges(context, position, "NewBase").Changes);
        Assert.Equal(2, edit.Value.Length);
        var changed = context.Document.Text.WithChanges(edit.Value).ToString();
        Assert.Contains("@theme Button NewBase", changed);
        Assert.Contains("@based-on NewBase;", changed);
        Assert.Contains("@theme Button BaseTheme", changed);
    }

    private static AkburaDocumentContext InCurrentSnapshot(AkburaWorkspace workspace, AkburaDocumentContext context)
    {
        var snapshot = workspace.CurrentSolution;
        var project = snapshot.GetRequiredProject(context.Project.Id);
        return new AkburaDocumentContext(snapshot, project, project.Documents[context.Document.Id]);
    }

    private static (string Code, TextSpan Span, string Message)[] DiagnosticFacts(
        AkburaWorkspace workspace, AkburaDocumentContext context) =>
        workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, context.Document.Text.Length))
            .Select(diagnostic => (diagnostic.Code, diagnostic.Span, diagnostic.Message)).ToArray();
}
