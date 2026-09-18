using Akbura.TestUtilities.Documentation;

namespace Akbura.UnitTests;

public sealed class DocumentationCorpusTests
{
    [Fact]
    public void RegisteredPages_EveryAkburaBlockHasExactlyOneExplicitTestContract()
    {
        var catalog = DocumentationExampleCatalog.All;
        Assert.Equal(catalog.Count, catalog.Select(example => example.Id).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(catalog.Count, catalog.Select(example => (example.Path, example.Section, example.Ordinal)).Distinct().Count());

        foreach (var path in DocumentationExampleCatalog.FullyCoveredPages)
        {
            var actual = MarkdownDocumentation.Read(path)
                .Where(block => block.Language == "akbura")
                .Select(block => (block.Section, block.Ordinal)).ToArray();
            var expected = catalog.Where(example => example.Path == path)
                .Select(example => (example.Section, example.Ordinal)).ToArray();
            Assert.NotEmpty(actual);
            Assert.Equal(expected, actual);
        }
    }

    [Fact]
    public void EveryCatalogEntry_ResolvesNonemptySourceAndRetainsTheDocumentedBlockVerbatim()
    {
        foreach (var example in DocumentationExampleCatalog.All)
        {
            var block = example.ReadBlock();
            Assert.False(string.IsNullOrWhiteSpace(block.Code), block.Location);
            Assert.True(block.StartLine > 0, block.Location);
            var input = example.CompleteSource(block);
            Assert.Equal(block.Code, input.Substring(example.Prefix.Length, block.Code.Length));
            Assert.Equal(example.Prefix.Length + block.Code.Length + example.Suffix.Length, input.Length);
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    [InlineData("\r")]
    public void Extractor_PreservesAllCodeCharactersAndReportsTheOriginalLine(string newline)
    {
        var code = "using Avalonia.Controls;" + newline + "<TextBlock Text=\"hello\" />" + newline;
        var markdown = "# Page" + newline + "## Sample" + newline + "```akbura" + newline + code + "```" + newline;
        var block = Assert.Single(MarkdownDocumentation.Extract(markdown, "sample.md"));
        Assert.Equal(code, block.Code);
        Assert.Equal(4, block.StartLine);
        Assert.Equal("Sample", block.Section);
        Assert.Equal(0, block.Ordinal);
    }

    [Fact]
    public void Extractor_DoesNotTreatCodeHeadingsOrShortFencesAsMarkdownStructure()
    {
        const string markdown = "## Real\n````akbura\n# not a section\n```\n<Border/>\n````\n" +
            "~~~akbura\n<Button/>\n~~~\n";
        var blocks = MarkdownDocumentation.Extract(markdown, "sample.md");
        Assert.Equal(2, blocks.Count);
        Assert.Equal("# not a section\n```\n<Border/>\n", blocks[0].Code);
        Assert.Equal("Real", blocks[0].Section);
        Assert.Equal("Real", blocks[1].Section);
        Assert.Equal(1, blocks[1].Ordinal);
    }

    [Fact]
    public void Extractor_CountsLanguagesSeparatelyAndAcceptsAFinalFenceWithoutNewline()
    {
        const string markdown = "## Sample\n```text\noutput\n```\n```akbura\n<Border/>\n```";
        var blocks = MarkdownDocumentation.Extract(markdown, "sample.md");
        Assert.Equal(new[] { "text", "akbura" }, blocks.Select(block => block.Language));
        Assert.All(blocks, block => Assert.Equal(0, block.Ordinal));
    }

    [Fact]
    public void MissingDocumentationOrAnUnclosedFence_FailsInsteadOfSilentlySkipping()
    {
        Assert.Throws<InvalidOperationException>(() => MarkdownDocumentation.Read("does-not-exist.md"));
        Assert.Throws<InvalidOperationException>(() => MarkdownDocumentation.Extract("```akbura\n<Border/>\n", "broken.md"));
        Assert.Throws<InvalidOperationException>(() => MarkdownDocumentation.Get(
            DocumentationExampleCatalog.ForeachPage, "This section is intentionally absent", 0));
    }
}
