using Akbura.Language.Syntax;
using Akbura.Workspaces.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupStartTagFormattingTests
{
    public static IEnumerable<object[]> MultilineTags()
    {
        var cases = new (string Source, int[] Levels)[]
        {
            ("<Grid\r\nWidth=\"295\"\r\nRowDefinitions=\"Auto,*,Auto\">\r\n</Grid>", [0, 1, 1, 0]),
            ("<Grid\r\nWidth=\"295\">\r\n<StackPanel\r\nGrid.Row=\"0\"\r\ngap-2>\r\n<NavButton\r\nx.Name=\"dashboard\"\r\nText=\"Dashboard\"/>\r\n</StackPanel>\r\n</Grid>", [0, 1, 1, 2, 2, 2, 3, 3, 1, 0]),
            ("<Grid>\r\n<NavButton\r\nText=\"Dashboard\"\r\nNavigateTo={SetActive}/>\r\n<Separator/>\r\n</Grid>", [0, 1, 2, 2, 1, 0]),
            ("<Button Width=\"100\"\r\nHeight=\"20\">\r\n</Button>", [0, 1, 0]),
            ("<Grid>\r\n<Button\r\nWidth=\"100\"\r\n>\r\n<TextBlock/>\r\n</Button>\r\n</Grid>", [0, 1, 2, 1, 2, 1, 0]),
            ("<Grid>\r\n<Button\r\nWidth=\"100\"\r\n/>\r\n<Separator/>\r\n</Grid>", [0, 1, 2, 1, 1, 0]),
            ("<Button>\r\n<Button.Theme>\r\n<ControlTheme\r\nTargetType=\"Button\">\r\n</ControlTheme>\r\n</Button.Theme>\r\n</Button>", [0, 1, 2, 3, 2, 1, 0]),
            ("<Button\r\nself-stretch\r\nrounded-2xl\r\nfont-bold/>", [0, 1, 1, 1]),
            ("<NavButton\r\nGeometries=${StaticResource Icon.Home}\r\nNavigateTo={SetActive}/>", [0, 1, 1]),
            ("<Button\r\nText=\"Hello\"\r\n", [0, 1, 1]),
            ("<Button\r\n", [0, 1]),
            ("<Grid>\r\n<Button\r\nText=\"Hello\"\r\n</Button>\r\n<Separator/>\r\n</Grid>", [0, 1, 2, 1, 1, 0]),
            ("<Grid>\r\n$if (visible)\r\n{\r\n<Button\r\nText=\"Hello\"/>\r\n}\r\n</Grid>", [0, 1, 1, 2, 3, 1, 0]),
        };

        foreach (var (source, levels) in cases)
        {
            yield return [source, levels, false];
            yield return [source, levels, true];
        }
    }

    [Theory]
    [MemberData(nameof(MultilineTags))]
    public void Formatter_IndentsStartTagsWithoutChangingBodyOrClosingLevels(string source, int[] levels, bool spaces)
    {
        var document = Parse(source);
        Assert.Equal(levels, Levels(document));
        var options = new AkburaFormattingOptions(TabSize: 4, InsertSpaces: spaces);
        using var workspace = new AkburaWorkspace();
        var formatter = workspace.LanguageServices.Formatting;
        var changes = formatter.FormatDocument(document, options);
        var formattedText = document.Text.WithChanges(changes);
        var expected = string.Join("\r\n", source.Split("\r\n").Select((line, index) =>
            line.Length == 0 ? line : (spaces ? new string(' ', levels[index] * 4) : new string('\t', levels[index])) + line));

        Assert.Equal(expected, formattedText.ToString());
        var formatted = document.WithText(formattedText);
        Assert.Equal(levels, Levels(formatted));
        Assert.Empty(formatter.FormatDocument(formatted, options));
        Assert.Equal(Levels(Parse(expected)), Levels(formatted));
    }

    [Fact]
    public void Formatter_RangeUsesContinuationIndentationWithoutChangingOtherLines()
    {
        const string source = "<Grid>\r\n<Button\r\nWidth=\"100\"\r\n/>\r\n<Separator/>\r\n</Grid>";
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var range = TextSpan.FromBounds(source.IndexOf("<Button", StringComparison.Ordinal), source.IndexOf("/>", StringComparison.Ordinal) + 2);
        var changes = workspace.LanguageServices.Formatting.FormatRange(document, range, new AkburaFormattingOptions(InsertSpaces: false));

        Assert.Equal("<Grid>\r\n\t<Button\r\n\t\tWidth=\"100\"\r\n\t/>\r\n<Separator/>\r\n</Grid>", document.Text.WithChanges(changes).ToString());
    }

    [Theory]
    [InlineData("<Button\r\n\r\nWidth=\"100\"/>", 1, 1)]
    [InlineData("<Grid>\r\n<Button\r\n\r\nWidth=\"100\"/>\r\n</Grid>", 2, 2)]
    [InlineData("<Grid>\r\n<Button\r\nText=\"Hello\"\r\n<TextBlock/>\r\n</Button>\r\n</Grid>", 3, 1)]
    public void SyntacticDocument_StartTagSmartIndentRespectsBlankLinesAndRecovery(string source, int line, int expectedLevel)
    {
        Assert.Equal(expectedLevel, Parse(source).GetDesiredIndentationLevel(line));
    }

    [Fact]
    public void SyntacticDocument_ContinuationDoesNotAddOrExtendOutliningRegions()
    {
        var document = Parse("<Grid\r\nWidth=\"100\">\r\n<NavButton\r\nText=\"Hello\"/>\r\n</Grid>");
        var element = document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<MarkupElementSyntax>().First();
        var startTag = Assert.IsType<MarkupStartTagSyntax>(element.StartTag);
        var region = Assert.Single(document.OutliningRegions);

        Assert.Equal(TextSpan.FromBounds(startTag.Span.End, element.EndTag!.Span.End), region.Span);
        Assert.Empty(Parse("<NavButton\r\nText=\"Hello\"/>").OutliningRegions);
    }

    [Fact]
    public void SyntacticDocument_IncrementalStartTagTypingMatchesFreshIndentation()
    {
        const string source = "<Grid>\r\n<Button|/>\r\n<Separator/>\r\n</Grid>";
        var position = source.IndexOf('|');
        var document = Parse(source.Remove(position, 1));

        foreach (var character in "\r\nWidth=\"100\"\r\n")
        {
            var text = document.Text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            Assert.Equal(Levels(Parse(text.ToString())), Levels(document));
        }

        Assert.Equal(new[] { 0, 1, 2, 1, 1, 0 }, Levels(document));
    }

    private static AkburaSyntacticDocument Parse(string source) => AkburaSyntacticDocument.Parse(SourceText.From(source), "Formatting.akbura");

    private static int[] Levels(AkburaSyntacticDocument document) => [.. Enumerable.Range(0, document.Text.Lines.Count).Select(line => document.GetDesiredIndentationLevel(line))];
}
