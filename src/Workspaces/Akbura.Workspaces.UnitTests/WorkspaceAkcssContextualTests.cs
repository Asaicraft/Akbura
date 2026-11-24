using Akbura.Language.Syntax;
using Akbura.Workspaces.Completion;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceAkcssContextualTests
{
    private const string Imports = "@using Avalonia.Controls;\r\n@using Avalonia.Controls.Presenters;\r\n";
    private const string Template = "<ContentPresenter x.Name=\"PART_Presenter\" />";
    private const string Theme = "@theme Button MyTheme { @template { " + Template + " } }";

    [Theory]
    [InlineData("@th|", "@theme", "@theme ")]
    [InlineData("@theme Button T { Pa|", "Padding", "Padding: ")]
    [InlineData("@theme Button T { ^ /template/ ContentPresenter { Co|nt } }", "Content", "Content: ")]
    [InlineData("@theme Button T { ^:po| { } }", "pointerover", "pointerover")]
    [InlineData("@theme Button T { ^ /tem| { } }", "/template/", "/template/")]
    [InlineData("@theme Button T { @template { " + Template + " } ^ /template/ ContentPresenter#PA| { } }", "PART_Presenter", "PART_Presenter")]
    [InlineData("@theme Button T { @template { <ContentPres| } }", "ContentPresenter", "ContentPresenter")]
    [InlineData("@theme Button T { @template { <ContentPresenter Pa| /> } }", "Padding", "Padding=\"\"")]
    [InlineData("@theme Button T { @template { <ContentPresenter> @template-bind Background, Bo|rderThickness; </ContentPresenter> } }",
        "BorderThickness", "BorderThickness")]
    [InlineData("@mixin Button Configure() { } @theme Button T { @include Con|(); }", "Configure", "Configure")]
    [InlineData("@mixin Button Configure(double x = 1d) { } @theme Button T { @include Con|; }", "Configure", "Configure()")]
    public void CompletionUsesTheMostSpecificRegion(string source, string expected, string insertion)
    {
        foreach (var inline in new[] { false, true })
        {
            using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
            var (context, document, position) = OpenAt(workspace, Imports + source, inline);
            var completion = workspace.LanguageServices.Completion;
            var result = completion.GetCompletions(document, context, position);
            var item = Assert.Single(result.Items, item => item.DisplayText == expected);
            var change = completion.GetCompletionChange(document, context, position, item);
            Assert.Contains(change.Changes, edit => edit.NewText == insertion);
            Assert.All(change.Changes, edit => Assert.True(edit.Span.End <= document.Text.Length));
            Assert.InRange(change.NewPosition, 0, document.Text.WithChanges(change.Changes).Length);
        }
    }

    [Theory]
    [InlineData("@theme Bu| T { }", AkburaCSharpCompletionContextKind.Type)]
    [InlineData("@mixin Button Configure(Thick|ness value) { }", AkburaCSharpCompletionContextKind.Type)]
    [InlineData("(global::Avalonia.Controls.Bor|der) > TextBlock { }", AkburaCSharpCompletionContextKind.Type)]
    [InlineData("@theme Button Derived { @based-on Hello.|; }", AkburaCSharpCompletionContextKind.Expression)]
    [InlineData("@mixin Button Config(double width) { } @theme Button T { @include Config(1|d); }",
        AkburaCSharpCompletionContextKind.Expression)]
    [InlineData("@mixin Button Config(double width) { } @theme Button T { @include Config(|); }",
        AkburaCSharpCompletionContextKind.Expression)]
    public void CSharpProjectionPreservesThePhysicalFragment(string source, AkburaCSharpCompletionContextKind expected)
    {
        foreach (var inline in new[] { false, true })
        {
            using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
            var (context, document, position) = OpenAt(workspace, Imports + source, inline);
            Assert.True(document.TryGetCSharpCompletionContext(position, out var csharp));
            Assert.Equal(expected, csharp.Kind);
            Assert.True(AkburaCSharpProjectionFactory.TryCreate(document, context, csharp, out var projection));
            Assert.Equal(document.Text.ToString(csharp.HostSpan),
                projection.Root.ToFullString().Substring(projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IncludeHelpTracksSyntaxCommasAndResolvesToTheMixin(bool inline)
    {
        const string source = "@mixin Button Configure(double width, double height = 2d) { Width: width; }\r\n" +
            "@theme Button T { @include Configure(Helpers.Make(1d, 2d), |); }";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace(
            "public static class Helpers { public static double Make(double first, double second) => first; }");
        var (context, document, position) = OpenAt(workspace, Imports + source, inline);
        var services = workspace.LanguageServices;
        var help = services.SignatureHelp.GetSignatureHelp(document, context, position);
        Assert.NotNull(help);
        Assert.Equal(1, help.ActiveParameter);
        var signature = Assert.Single(help.Signatures);
        Assert.Contains("mixin Button Configure", signature.Label);
        Assert.Contains("height = 2d", signature.Label);

        var nested = OpenAt(workspace, Imports + source.Replace("Make(1d, 2d), |", "Make(1d, |2d), "), inline);
        var nestedHelp = services.SignatureHelp.GetSignatureHelp(nested.Syntactic, nested.Context, nested.Position);
        Assert.NotNull(nestedHelp);
        Assert.Equal(1, nestedHelp.ActiveParameter);
        Assert.Contains("Make", Assert.Single(nestedHelp.Signatures).Label);

        var includePosition = document.Text.ToString().LastIndexOf("Configure", StringComparison.Ordinal);
        var definition = services.Definition.GetDefinition(context, includePosition);
        Assert.NotNull(definition);
        Assert.Equal(context.Document.FilePath, definition.TargetFilePath);
        Assert.Contains("Configure", services.QuickInfo.GetQuickInfo(context, includePosition)!.Signature);
    }

    [Theory]
    [InlineData("global::Gallery.Hello.MyTheme")]
    [InlineData("Gallery.Hello.MyTheme")]
    [InlineData("Hello.MyTheme")]
    [InlineData("ThemeModule.MyTheme")]
    public void PublicThemeUsesTheOriginalDeclaration(string access)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var declaration = Open(workspace, "Hello.akcss", Imports + Theme);
        var source = "using Avalonia.Controls;\r\nusing Gallery;\r\nusing ThemeModule = Gallery.Hello;\r\n<Button Theme={" + access + "} />";
        var view = Open(workspace, "View.akbura", source);
        var position = source.IndexOf("MyTheme", StringComparison.Ordinal);
        var definition = workspace.LanguageServices.Definition.GetDefinition(view, position);
        Assert.NotNull(definition);
        Assert.Equal(declaration.Document.FilePath, definition.TargetFilePath);
        var rename = workspace.LanguageServices.Rename.GetRenameInfo(view, position);
        Assert.False(rename.CanRename);
        Assert.Contains("ordinary C#", rename.ErrorMessage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ContentPresenter")]
    public void KnownPartsKeepInheritedTemplateIdentity(string type)
    {
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var original = Open(workspace, "Hello.akcss", Imports + Theme);
        var source = Imports + "@theme Button Derived { @based-on Gallery.Hello.MyTheme; ^ /template/ " + type +
            "#PART_Presenter { Background: null; } }";
        var derived = Open(workspace, "Derived.akcss", source);
        var position = source.IndexOf("PART_Presenter", StringComparison.Ordinal);
        var definition = workspace.LanguageServices.Definition.GetDefinition(derived, position);
        Assert.NotNull(definition);
        Assert.Equal(original.Document.FilePath, definition.TargetFilePath);
        Assert.Contains("Template part", workspace.LanguageServices.QuickInfo.GetQuickInfo(derived, position)!.Details);
    }

    [Fact]
    public void TemplateBindingsShowBothEndpointsAndCannotBeRenamed()
    {
        var source = Imports + "@theme Button T { @template { <ContentPresenter> @template-bind Background; </ContentPresenter> } }";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var document = Open(workspace, "Hello.akcss", source);
        var position = source.IndexOf("Background", StringComparison.Ordinal);
        var hover = workspace.LanguageServices.QuickInfo.GetQuickInfo(document, position);
        Assert.NotNull(hover);
        Assert.Equal("Button.Background → ContentPresenter.Background", hover.Signature);
        Assert.False(workspace.LanguageServices.Rename.GetRenameInfo(document, position).CanRename);
    }

    [Fact]
    public void DeclarationClassificationAndSymbolsKeepBodiesUnbound()
    {
        var source = Imports +
            "@mixin ContentPresenter Bindings(double width) { Width: missing; }\r\n" +
            "@theme Button T { @template { <ContentPresenter x.Name=\"PART_Presenter\">@include Bindings(2d);</ContentPresenter> }\r\n" +
            "^ /template/ (global::Avalonia.Controls.Presenters.ContentPresenter) { Width: missing; }\r\n}";
        using var workspace = WorkspaceControlEventHookTests.CreateWorkspace();
        var context = Open(workspace, "Hello.akcss", source);
        var services = workspace.LanguageServices;
        var model = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        var assignments = context.Document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<AkcssAssignmentSyntax>().ToArray();
        var typeStart = source.IndexOf("ContentPresenter", StringComparison.Ordinal);
        var types = services.Classification.GetDeclarationClassifications(context, new TextSpan(0, source.Length));
        Assert.Contains(types, span => span.Kind == AkburaClassificationKind.ClassName && span.Span.Start == typeStart);
        Assert.Contains(services.DocumentSymbols.GetSymbols(context), item => item.Name == "T");
        Assert.All(assignments, assignment => Assert.False(model.TryGetCachedBoundNode(assignment, out _)));
    }

    private static (AkburaDocumentContext Context, AkburaSyntacticDocument Syntactic, int Position) OpenAt(
        AkburaWorkspace workspace, string source, bool inline)
    {
        if (inline)
        {
            source = "@akcss {\r\n" + source + "\r\n}\r\n<Avalonia.Controls.Border />";
        }

        var position = source.IndexOf('|');
        Assert.True(position >= 0);
        var context = Open(workspace, inline ? "Inline.akbura" : "Hello.akcss", source.Remove(position, 1));
        return (context, AkburaSyntacticDocument.Create(context.Document), position);
    }

    private static AkburaDocumentContext Open(AkburaWorkspace workspace, string fileName, string source) =>
        workspace.OpenOrChangeDocumentContext(new Uri(Path.Combine(Environment.CurrentDirectory, fileName)), SourceText.From(source));
}
