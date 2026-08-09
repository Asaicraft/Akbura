using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceSyntacticDocumentTests
{
    [Fact]
    public void SyntacticDocument_MarkupProvidesOutliningAndIndentation()
    {
        const string source = """
            <StackPanel>
                <Border>
                    <TextBlock/>
                </Border>
            </StackPanel>
            """;
        var text = SourceText.From(source);

        var document = AkburaSyntacticDocument.Parse(
            text,
            "Component.akbura");

        Assert.Equal(2, document.OutliningRegions.Length);
        Assert.All(
            document.OutliningRegions,
            static region => Assert.Equal("...", region.CollapsedText));
        Assert.Contains(
            "</StackPanel>",
            text.ToString(document.OutliningRegions[0].Span),
            StringComparison.Ordinal);
        Assert.Contains(
            "</Border>",
            text.ToString(document.OutliningRegions[1].Span),
            StringComparison.Ordinal);
        Assert.Equal(0, document.GetDesiredIndentationLevel(0));
        Assert.Equal(1, document.GetDesiredIndentationLevel(1));
        Assert.Equal(2, document.GetDesiredIndentationLevel(2));
        Assert.Equal(1, document.GetDesiredIndentationLevel(3));
        Assert.Equal(0, document.GetDesiredIndentationLevel(4));
    }

    [Fact]
    public void SyntacticDocument_AkcssProvidesOutliningAndIndentation()
    {
        const string source = """
            @utilities {
                .card-(double value) {
                    Width: value;
                }
            }
            """;
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Styles.akcss");

        Assert.Equal(2, document.OutliningRegions.Length);
        Assert.All(
            document.OutliningRegions,
            static region => Assert.Equal(
                "{ ... }",
                region.CollapsedText));
        Assert.Equal(0, document.GetDesiredIndentationLevel(0));
        Assert.Equal(1, document.GetDesiredIndentationLevel(1));
        Assert.Equal(2, document.GetDesiredIndentationLevel(2));
        Assert.Equal(1, document.GetDesiredIndentationLevel(3));
        Assert.Equal(0, document.GetDesiredIndentationLevel(4));
    }

    [Theory]
    [InlineData("<StackPanel>\n", "Incomplete.akbura", 1)]
    [InlineData("@utilities {\n", "Incomplete.akcss", 1)]
    public void SyntacticDocument_IncompleteBlockStillProvidesIndentation(
        string source,
        string filePath,
        int expectedIndentation)
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            filePath);

        Assert.Empty(document.OutliningRegions);
        Assert.Equal(
            expectedIndentation,
            document.GetDesiredIndentationLevel(1));
    }

    [Fact]
    public void SyntacticDocument_SelfClosingMarkupDoesNotIndentFollowingLine()
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From("<TextBlock/>\n"),
            "Component.akbura");

        Assert.Empty(document.OutliningRegions);
        Assert.Equal(0, document.GetDesiredIndentationLevel(1));
    }
}
