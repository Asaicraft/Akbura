using Akbura.Workspaces.Classification;
using Akbura.Workspaces.Documents;
using System;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis.Text;
using Xunit;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceTagDelimiterClassificationTests
{
    [Theory]
    [InlineData("<Border><TextBlock /></Border>")]
    [InlineData("<Border>\r\n<TextBlock />\r\n</Border>")]
    [InlineData("<StackPanel><StackPanel.Children><Border/></StackPanel.Children></StackPanel>")]
    public void AllTagDelimiters_HaveOneClassification(string source)
    {
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, source);
        for (var i = 0; i < source.Length; i++)
        {
            if (source[i] is not ('<' or '>')) continue;
            var token = Assert.Single(result, span => span.Span.Contains(i));
            Assert.Equal(AkburaClassificationKind.Punctuation, token.Kind);
        }
        foreach (var marker in new[] { "</", "/>" })
        {
            var offset = source.IndexOf(marker, StringComparison.Ordinal);
            if (offset < 0) continue;
            Assert.Contains(result, span => span.Span == new TextSpan(offset, 2) &&
                span.Kind == AkburaClassificationKind.Punctuation);
        }
    }

    [Fact]
    public void CSharpComparisons_RemainOperators()
    {
        const string source = "<Border IsVisible={1 < 2 && 3 > 2}/>";
        using var workspace = new AkburaWorkspace();
        var result = Classify(workspace, source);
        foreach (var expression in new[] { "1 < 2", "3 > 2" })
        {
            var position = source.IndexOf(expression, StringComparison.Ordinal) + 2;
            Assert.Contains(result, span => span.Span.Contains(position) &&
                span.Kind == AkburaClassificationKind.Operator);
        }
    }

    private static ImmutableArray<AkburaClassifiedSpan> Classify(AkburaWorkspace workspace, string source)
    {
        var document = AkburaSyntacticDocument.Parse(SourceText.From(source), "Tags.akbura");
        return workspace.LanguageServices.Classification.GetSyntacticClassifications(
            document, new TextSpan(0, source.Length));
    }
}
