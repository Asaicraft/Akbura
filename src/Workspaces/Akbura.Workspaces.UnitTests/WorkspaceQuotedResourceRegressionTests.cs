using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceQuotedResourceRegressionTests
{
    [Theory]
    [InlineData("\"Icon.Home\"")]
    [InlineData("'Icon.Home'")]
    public void ResourceKeyTyping_ProducesTheSameDocumentAsAFullParse(string key)
    {
        const string prefix = "state int count = 0;\r\n\r\n<NavIcon Geometries=${StaticResource ";
        const string suffix = "} />";
        var text = SourceText.From(prefix + suffix);
        var document = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
        var position = prefix.Length;
        foreach (var character in key)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            var full = AkburaSyntacticDocument.Parse(text, "MainView.akbura");
            Assert.Equal(text.ToString(), document.SyntaxTree.GetRootSyntax().ToFullString());
            Assert.Equal(Shape(full), Shape(document));
            Assert.Equal(full.OutliningRegions.Select(r => (r.Span, r.CollapsedText)),
                document.OutliningRegions.Select(r => (r.Span, r.CollapsedText)));
        }

        Assert.Empty(document.SyntaxTree.GetRootSyntax()
            .DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
            .SelectMany(node => node.GetDiagnostics()));
    }

    private static object[] Shape(AkburaSyntacticDocument document) => document.SyntaxTree.GetRootSyntax()
        .DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
        .Select(node => (object)(node.RawKind, node.Span, node.FullSpan, node.IsMissing,
            string.Join("|", node.GetDiagnostics().Select(d => d.Code))))
        .ToArray();
}
