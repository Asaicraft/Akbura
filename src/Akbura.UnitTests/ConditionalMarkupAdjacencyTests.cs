using Akbura.Language;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class ConditionalMarkupAdjacencyTests
{
    [Theory]
    [InlineData("")]
    [InlineData("\r\n")]
    public void AdjacentSelfClosingElementsInConditionalContentPreserveBothSiblings(string separator)
    {
        var source = "<Border>$if (expanded) { <StackPanel>" +
            "<TextBox x.Name=\"outerSource\" Text=\"inner scoped\" />" + separator +
            "<TextBlock Text=${Binding #outerSource.Text} /></StackPanel> }</Border>";
        var tree = AkburaSyntaxTree.ParseText(source, "ConditionalNames.akbura");
        Assert.Equal(source, tree.GetRoot().ToFullString());
        Assert.False(tree.GetRoot().ContainsDiagnostics);
        var statement = Assert.Single(tree.GetRoot().DescendantNodes().OfType<MarkupIfStatementSyntax>());
        var panel = Assert.Single(statement.Body.Content.OfType<MarkupElementContentSyntax>()).Element;
        Assert.Equal(new[] { "TextBox", "TextBlock" },
            panel.Body.OfType<MarkupElementContentSyntax>().Select(child => child.Element.StartTag!.Name.ToString()));
    }
}
