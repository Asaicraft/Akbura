using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed partial class WorkspaceCompletionTests
{
    [Theory]
    [InlineData("StackPanel", "bg", "Panel")]
    [InlineData("Border", "bg", "Border")]
    [InlineData("Button", "bg", "TemplatedControl")]
    [InlineData("TextBlock", "text", "TextBlock")]
    [InlineData("Border", "border", "Border")]
    public void Completion_BuiltInColorsOfferEveryShadeAndCommitExactUtility(string control, string prefix, string target)
    {
        // For example: <Border bg-teal-|/> -> <Border bg-teal-300/>
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        var sourceWithCaret = "using Avalonia.Controls;\r\nusing Akbura.Styles.akcss;\r\n" +
            $"<{control} {prefix}-teal-|/>";
        var position = sourceWithCaret.IndexOf('|');
        var text = SourceText.From(sourceWithCaret.Remove(position, 1));
        var path = Path.GetFullPath("ColorUtilities.akbura");
        var uri = new Uri(path);
        var context = workspace.OpenOrChangeDocumentContext(uri, text);
        var document = AkburaSyntacticDocument.Create(context.Document);
        var result = workspace.LanguageServices.Completion.GetCompletions(document, context, position);

        foreach (var shade in new[] { 50, 100, 200, 300, 400, 500, 600, 700, 800, 900, 950 })
        {
            var item = Assert.Single(result.Items, item => item.DisplayText == $"{prefix}-teal-{shade}");
            Assert.Equal(AkburaCompletionKind.TailwindUtility, item.Kind);
            Assert.Equal(target, item.Suffix);
            Assert.Equal(item.DisplayText, item.InsertText);
        }

        var selected = Assert.Single(result.Items, item => item.DisplayText == prefix + "-teal-300");
        var changed = text.WithChanges(new TextChange(result.ApplicableSpan, selected.InsertText));
        Assert.Contains($"{prefix}-teal-300/>", changed.ToString(), StringComparison.Ordinal);
        context = workspace.OpenOrChangeDocumentContext(uri, changed);
        var operation = AssertBoundColorUtility(workspace, context, changed);
        Assert.Equal(prefix + "-teal-300", operation.Utility!.Name);
        Assert.Empty(operation.Utility.Parameters);
        Assert.Empty(operation.Arguments);
    }

    [Theory]
    [InlineData("StackPanel", "bg")]
    [InlineData("Border", "bg")]
    [InlineData("Button", "bg")]
    [InlineData("TextBlock", "text")]
    [InlineData("Border", "border")]
    public void Completion_BuiltInColorsIncludeUnshadedAndExtendedPalette(string control, string prefix)
    {
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        var path = Path.GetFullPath("ColorUtilities.akbura");
        foreach (var color in new[] { "black", "white", "transparent", "mauve-950", "olive-50", "mist-300", "taupe-500" })
        {
            var sourceWithCaret = "using Avalonia.Controls;\r\nusing Akbura.Styles.akcss;\r\n" +
                $"<{control} {prefix}-{color}|/>";
            var position = sourceWithCaret.IndexOf('|');
            var text = SourceText.From(sourceWithCaret.Remove(position, 1));
            var context = workspace.OpenOrChangeDocumentContext(new Uri(path), text);
            var result = workspace.LanguageServices.Completion.GetCompletions(
                AkburaSyntacticDocument.Create(context.Document), context, position);
            Assert.Contains(result.Items, item => item.DisplayText == prefix + "-" + color);
            var operation = AssertBoundColorUtility(workspace, context, text);
            Assert.Equal(prefix + "-" + color, operation.Utility!.Name);
            Assert.Empty(operation.Utility.Parameters);
        }
    }

    [Theory]
    [InlineData("StackPanel", "bg")]
    [InlineData("Border", "bg")]
    [InlineData("Button", "bg")]
    [InlineData("TextBlock", "text")]
    [InlineData("Border", "border")]
    public void BuiltInColors_CustomNamesStillBindParameterizedUtility(string control, string prefix)
    {
        using var workspace = CreateRuntimeUtilityWorkspace(CreateRuntimeUtilityReferences());
        var source = "using Avalonia.Controls;\r\nusing Akbura.Styles.akcss;\r\n" +
            $"<{control} {prefix}-brand-123/>";
        var text = SourceText.From(source);
        var context = workspace.OpenOrChangeDocumentContext(new Uri(Path.GetFullPath("ColorUtilities.akbura")), text);
        var operation = AssertBoundColorUtility(workspace, context, text);

        Assert.Equal(prefix, operation.Utility!.Name);
        Assert.Equal(2, operation.Utility.Parameters.Length);
        Assert.Equal(2, operation.Arguments.Length);
    }

    private static ITailwindUtilityAttributeOperation AssertBoundColorUtility(AkburaWorkspace workspace, AkburaDocumentContext context, SourceText text)
    {
        var diagnostics = workspace.LanguageServices.Diagnostics.GetDiagnostics(context, new TextSpan(0, text.Length));
        Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Severity == AkburaDiagnosticSeverity.Error);
        var model = context.Project.Compilation.GetSemanticModel(context.Document.SyntaxTree);
        var attribute = Assert.Single(context.Document.SyntaxTree.GetRoot().DescendantNodes().OfType<TailwindAttributeSyntax>());
        var operation = Assert.IsAssignableFrom<ITailwindUtilityAttributeOperation>(model.GetOperation(attribute));
        Assert.NotNull(operation.Utility);
        Assert.False(operation.HasErrors);
        return operation;
    }
}
