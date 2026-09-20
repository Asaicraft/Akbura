using System;
using System.Linq;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceClosingTagCompletionRegressionTests
{
    [Theory]
    [InlineData("", "\n", false)]
    [InlineData("", "\r\n", true)]
    [InlineData(" Geometries=${StaticResource \"Icon.Home\"}", "\n", false)]
    [InlineData(" Geometries=${StaticResource \"Icon.Home\"}", "\r\n", false)]
    [InlineData(" Geometries=${StaticResource \"Icon.Home\"}", "\r\n", true)]
    public void SlashAfterSelfClosingChild_CompletesParent(
        string attributes, string newline, bool existingGreater)
    {
        var prefix = "state int count = 0;" + newline + newline +
            "<StackPanel>" + newline + newline +
            "<NavIcon" + attributes + " />" + newline + "\t<";
        var suffix = existingGreater ? ">" : string.Empty;
        var originalText = SourceText.From(prefix + suffix);
        var original = AkburaSyntacticDocument.Parse(originalText, "MainView.akbura");
        var changedText = originalText.WithChanges(
            new TextChange(new TextSpan(prefix.Length, 0), "/"));
        var incremental = original.WithText(changedText);
        var full = AkburaSyntacticDocument.Parse(changedText, "MainView.akbura");
        var position = prefix.Length + 1;

        AssertParent(full, position, "StackPanel", existingGreater);
        AssertParent(incremental, position, "StackPanel", existingGreater);
        Assert.Equal(0, full.GetSlashCompletionIndentationLevel(position));
        Assert.Equal(full.GetSlashCompletionIndentationLevel(position),
            incremental.GetSlashCompletionIndentationLevel(position));

        // Prove the self-closing child's syntax was recognized, not hidden.
        var child = Assert.Single(full.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupStartTagSyntax>()
            .Where(tag => tag.Name.ToFullString().Trim() == "NavIcon"));
        Assert.False(child.CloseToken.IsMissing);
        Assert.Equal(SyntaxKind.SlashGreaterToken, child.CloseToken.Kind);
    }

    [Theory]
    [InlineData("<StackPanel><NavIcon /></", "StackPanel")]
    [InlineData("<StackPanel><Border><NavIcon /></Border></", "StackPanel")]
    [InlineData("<StackPanel><NavIcon/><TextBlock/></", "StackPanel")]
    [InlineData("<StackPanel><NavIcon></", "NavIcon")]
    [InlineData("<StackPanel><Border><NavIcon/></", "Border")]
    public void ClosingContext_UsesTheInnermostStillOpenElement(string source, string expected)
    {
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "MainView.akbura");
        AssertParent(document, source.Length, expected, existingGreater: false);
    }

    [Fact]
    public void SelfClosingRoot_HasNoClosingTagToComplete()
    {
        const string source = "<NavIcon />\n</";
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "MainView.akbura");
        Assert.False(document.TryGetSlashCompletionEdit(source.Length, out _));
        Assert.Null(document.GetSlashCompletionText(source.Length));
        Assert.Null(document.GetSlashCompletionIndentationLevel(source.Length));
    }

    private static void AssertParent(
        AkburaSyntacticDocument document, int position, string parent, bool existingGreater)
    {
        var context = document.GetCompletionContext(position);
        Assert.Equal(AkburaCompletionContextKind.ClosingComponentName, context.Kind);
        Assert.Equal(parent, context.ParentComponentName);
        Assert.True(document.TryGetSlashCompletionEdit(position, out var edit));
        Assert.True(edit.CompletesClosingTag);
        Assert.Equal(parent + (existingGreater ? string.Empty : ">"), edit.InsertionText);
        Assert.Equal(existingGreater ? 1 : 0, edit.OvertypeLength);
        var after = document.Text.WithChanges(new TextChange(new TextSpan(position, 0), edit.InsertionText));
        Assert.EndsWith("</" + parent + ">", after.ToString());
    }
}
