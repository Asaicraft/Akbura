namespace Akbura.LanguageServer.UnitTests;

public sealed class LanguageServiceTests
{
    [Fact]
    public void SymbolsAndFoldingWorkWithoutMsBuild()
    {
        using var workspace = new AkburaWorkspace();
        var uri = new Uri("file:///symbols.akbura");
        var context = workspace.OpenOrChangeDocumentContext(
            uri,
            SourceText.From(
                "state int count = 0;\n" +
                "<StackPanel>\n<Button/>\n</StackPanel>"));

        var symbols = workspace.LanguageServices.DocumentSymbols
            .GetSymbols(context);
        var folds = workspace.LanguageServices.FoldingRanges
            .GetFoldingRanges(context);

        Assert.Contains(symbols, symbol => symbol.Name == "count");
        Assert.Contains(symbols, symbol => symbol.Name == "StackPanel");
        Assert.NotEmpty(folds);
    }

    [Fact]
    public void FormatterIndentsStructuralMarkup()
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From("<StackPanel>\n<Button/>\n</StackPanel>"),
            "Format.akbura");
        using var workspace = new AkburaWorkspace();

        var changes = workspace.LanguageServices.Formatting.FormatDocument(
            document,
            new Akbura.Workspaces.Formatting.AkburaFormattingOptions(
                TabSize: 2));
        var formatted = document.Text.WithChanges(changes).ToString();

        Assert.Equal(
            "<StackPanel>\n  <Button/>\n</StackPanel>",
            formatted);
    }
}