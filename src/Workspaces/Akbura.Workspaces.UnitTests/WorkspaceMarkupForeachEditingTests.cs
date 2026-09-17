using Akbura.Language.Syntax;
using Akbura.Workspaces.AutomaticPairing;
using Akbura.Workspaces.Formatting;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceMarkupForeachEditingTests
{
    [Fact]
    public void ForeachAndOrdinaryGuardBlocks_IndentAndFoldTheirActualBraces()
    {
        const string source = "<StackPanel>\r\n$foreach (var item in values; key: item.Id)\r\n{\r\n" +
            "if (item.Id > 0)\r\n{\r\ncontinue;\r\n}\r\n<TextBlock Text={item.Name}/>\r\n" +
            "}\r\n<Separator/>\r\n</StackPanel>";
        var document = Parse(source);
        var expectedLevels = new[] { 0, 1, 1, 2, 2, 3, 2, 2, 1, 1, 0 };

        Assert.Equal(expectedLevels, Levels(document));
        var blocks = document.SyntaxTree.GetRootSyntax().DescendantNodes().OfType<MarkupCodeBlockSyntax>().ToArray();
        Assert.Equal(2, blocks.Length);
        Assert.All(blocks, block => Assert.Contains(document.OutliningRegions,
            region => region.Span == TextSpan.FromBounds(block.OpenBraceToken.Span.Start, block.CloseBraceToken.Span.End)));

        using var workspace = new AkburaWorkspace();
        var changes = workspace.LanguageServices.Formatting.FormatDocument(document, new AkburaFormattingOptions());
        var expected = string.Join("\r\n", source.Split("\r\n").Select((line, index) =>
            new string(' ', expectedLevels[index] * 4) + line));
        Assert.Equal(expected, document.Text.WithChanges(changes).ToString());
    }

    [Theory]
    [InlineData("<StackPanel>$foreach |</StackPanel>", '(', ")")]
    [InlineData("<StackPanel>$foreach (var item in values) |</StackPanel>", '{', "}")]
    [InlineData("<StackPanel>$foreach (var item in values) { if (item > 0) | }</StackPanel>", '{', "}")]
    public void ForeachDelimiters_ArePairedByTheSharedTypingService(string source, char opening, string closing)
    {
        var position = source.IndexOf('|');
        var document = Parse(source.Remove(position, 1));
        using var workspace = new AkburaWorkspace();
        var result = workspace.LanguageServices.Typing.GetResult(document, new AkburaTypingCommand(
            AkburaTypingCommandKind.Type, position, opening.ToString(),
            new AkburaTypingOptions(TabSize: 4, IndentSize: 4, InsertSpaces: true, NewLine: "\r\n"),
            Session: null));

        Assert.True(result.Handled);
        Assert.Equal(opening + closing, Assert.Single(result.Changes).NewText);
        Assert.NotNull(result.Session);
    }

    [Theory]
    [InlineData("{", '{')]
    [InlineData("var copy = item;", ';')]
    public void OnTypeFormatting_HandlesForeachBodyAndLocalStatements(string line, char trigger)
    {
        var source = "<StackPanel>\r\n$foreach (var item in values)\r\n{\r\n" + line + "\r\n}\r\n</StackPanel>";
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var position = source.IndexOf(line, source.IndexOf('{') + 1, StringComparison.Ordinal) + line.Length;
        var changes = workspace.LanguageServices.Formatting.FormatOnType(document, position, trigger,
            new AkburaFormattingOptions());

        Assert.NotEmpty(changes);
        var changed = document.Text.WithChanges(changes).ToString();
        Assert.Contains("\r\n        " + line + "\r\n", changed);
    }

    [Fact]
    public void CharacterwiseForeachTyping_FullAndIncrementalDocumentsHaveIdenticalEditingFacts()
    {
        const string prefix = "<StackPanel>\r\n<Border/>\r\n";
        const string suffix = "\r\n<Separator/>\r\n</StackPanel>";
        const string insertion = "$foreach (var item in values; key: item.Id)\r\n{\r\n" +
            "if (item.Id > 0)\r\n{\r\ncontinue;\r\n}\r\n<TextBlock Text={item.Name}/>\r\n}";
        var text = SourceText.From(prefix + suffix);
        var document = Parse(text.ToString());
        var position = prefix.Length;
        foreach (var character in insertion)
        {
            text = text.WithChanges(new TextChange(new TextSpan(position++, 0), character.ToString()));
            document = document.WithText(text);
            var fresh = Parse(text.ToString());

            Assert.Equal(text.ToString(), document.SyntaxTree.GetRootSyntax().ToFullString());
            Assert.Equal(Levels(fresh), Levels(document));
            Assert.Equal(fresh.OutliningRegions.Select(region => (region.Span, region.CollapsedText)),
                document.OutliningRegions.Select(region => (region.Span, region.CollapsedText)));
            Assert.Equal(Shape(fresh), Shape(document));
        }
    }

    private static int[] Levels(AkburaSyntacticDocument document) => [.. Enumerable.Range(0, document.Text.Lines.Count).Select(line => document.GetDesiredIndentationLevel(line))];

    private static object[] Shape(AkburaSyntacticDocument document) => [.. document.SyntaxTree.GetRootSyntax()
        .DescendantNodesAndTokensAndSelf().Select(node => (object)(node.RawKind, node.Span, node.FullSpan,
            node.IsMissing, string.Join("|", node.GetDiagnostics().Select(diagnostic => diagnostic.Code))))];

    private static AkburaSyntacticDocument Parse(string source) => AkburaSyntacticDocument.Parse(SourceText.From(source), "Loop.akbura");
}
