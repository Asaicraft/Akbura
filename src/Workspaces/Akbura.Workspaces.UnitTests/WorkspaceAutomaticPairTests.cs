using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceAutomaticPairTests
{
    [Theory]
    [InlineData("|", '<', ">", "MarkupText")]
    [InlineData("<StackPanel>\n    |\n</StackPanel>", '<', ">", "MarkupText")]
    [InlineData("<StackPanel>\n    |\n</StackPanel>", '{', "}", "MarkupText")]
    [InlineData("<Button Content=|/>", '"', "\"", "MarkupStartTag")]
    [InlineData("<Button Content=|/>", '{', "}", "MarkupStartTag")]
    [InlineData("<Button Content=$|/>", '{', "}", "MarkupExtension")]
    public void MarkupPairsAreContextual(
        string sourceWithCaret,
        char openingCharacter,
        string closingText,
        string contextKind)
    {
        var (document, position) = Parse(sourceWithCaret, "Component.akbura");

        var decision = document.GetAutomaticPairDecision(
            position,
            openingCharacter);

        Assert.True(decision.IsValid);
        Assert.Equal(contextKind, decision.ContextKind.ToString());
        Assert.Equal(closingText, decision.ClosingText);
    }

    [Theory]
    [InlineData("<Button Content=\"hello |world\"/>", '{')]
    [InlineData("<Button Content=\"hello |world\"/>", '(')]
    [InlineData("<Button Content=\"hello |world\"/>", '[')]
    [InlineData("<Button Content=\"hello |world\"/>", '<')]
    [InlineData("<Button Content=\"hello |world\"/>", '"')]
    [InlineData("// comment |", '{')]
    [InlineData("/* comment | */", '(')]
    [InlineData("<Button Content={\"hello |world\"}/>", '{')]
    [InlineData("<Button Content={\"hello |world\"}/>", '<')]
    public void LiteralTextAndCommentsDoNotCreatePairs(
        string sourceWithCaret,
        char openingCharacter)
    {
        var (document, position) = Parse(sourceWithCaret, "Component.akbura");

        var decision = document.GetAutomaticPairDecision(
            position,
            openingCharacter);

        Assert.False(decision.IsValid);
    }

    [Theory]
    [InlineData("state string text = |;", '"', "\"")]
    [InlineData("state object value = Call|;", '(', ")")]
    [InlineData("state object value = items|;", '[', "]")]
    [InlineData("state List| values;", '<', ">")]
    [InlineData("<Button Content={GetValue|}/>", '(', ")")]
    [InlineData("void Update()\n{\n    if (ready) |\n}", '{', "}")]
    public void EmbeddedCSharpUsesRoslynSyntax(
        string sourceWithCaret,
        char openingCharacter,
        string closingText)
    {
        var (document, position) = Parse(sourceWithCaret, "Component.akbura");

        var decision = document.GetAutomaticPairDecision(
            position,
            openingCharacter);

        Assert.True(decision.IsValid);
        Assert.Equal(AkburaPairContextKind.EmbeddedCSharp, decision.ContextKind);
        Assert.Equal(closingText, decision.ClosingText);
    }

    [Theory]
    [InlineData("state bool result = left | right;", '<')]
    [InlineData("state string text = \"left | right\";", '<')]
    [InlineData("state string text = \"left | right\";", '{')]
    [InlineData("<Button>|</Button>", '[')]
    public void EmbeddedCSharpRejectsNonConstructTokens(
        string sourceWithCaret,
        char openingCharacter)
    {
        var (document, position) = Parse(sourceWithCaret, "Component.akbura");

        Assert.False(document.GetAutomaticPairDecision(
            position,
            openingCharacter).IsValid);
    }

    [Theory]
    [InlineData(".card |", '{', "}")]
    [InlineData("@utilities |", '{', "}")]
    [InlineData(".card { @if| }", '(', ")")]
    [InlineData("@utilities { .gap-| }", '(', ")")]
    public void StandaloneAndInlineAkcssProduceTheSameDecision(
        string akcssWithCaret,
        char openingCharacter,
        string closingText)
    {
        var (standalone, standalonePosition) = Parse(
            akcssWithCaret,
            "Styles.akcss");
        var inlineWithCaret = "@akcss {\n" +
            akcssWithCaret +
            "\n}\n\n<Border/>";
        var (inline, inlinePosition) = Parse(
            inlineWithCaret,
            "Component.akbura");

        var standaloneDecision = standalone.GetAutomaticPairDecision(
            standalonePosition,
            openingCharacter);
        var inlineDecision = inline.GetAutomaticPairDecision(
            inlinePosition,
            openingCharacter);

        Assert.True(standaloneDecision.IsValid);
        Assert.True(inlineDecision.IsValid);
        Assert.Equal(closingText, standaloneDecision.ClosingText);
        Assert.Equal(standaloneDecision.ClosingText, inlineDecision.ClosingText);
        Assert.Equal(AkburaPairContextKind.AkcssSyntax, standaloneDecision.ContextKind);
        Assert.Equal(standaloneDecision.ContextKind, inlineDecision.ContextKind);
    }

    [Fact]
    public void InlineAkcssKeywordCanStartItsBlock()
    {
        var (document, position) = Parse(
            "@akcss |\n\n<Border/>",
            "Component.akbura");

        var decision = document.GetAutomaticPairDecision(position, '{');

        Assert.True(decision.IsValid);
        Assert.Equal("}", decision.ClosingText);
        Assert.Equal(AkburaPairContextKind.AkcssSyntax, decision.ContextKind);
    }

    [Theory]
    [InlineData(
        "@using Akbura.Styles.akcss;\n\n" +
        ".hello {|",
        "Styles.akcss")]
    [InlineData("@utilities {|", "Styles.akcss")]
    [InlineData(
        "@utilities {\n" +
        "    .gap-(double value) {|",
        "Styles.akcss")]
    [InlineData(
        ".card {\n" +
        "    @if (true) {|",
        "Styles.akcss")]
    [InlineData(
        "@akcss {|\n\n" +
        "<Border/>",
        "Component.akbura")]
    public void StructuralAkcssOpenBraceCanBeClosedAfterInsertion(
        string sourceWithCaret,
        string filePath)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            filePath);

        Assert.True(document.ShouldAutoCloseCurlyBrace(position));
    }

    [Theory]
    [InlineData(
        "@using Akbura.Styles.akcss;\n\n" +
        "// {|",
        "Styles.akcss")]
    [InlineData(
        "@using Akbura.Styles.akcss;\n\n" +
        ".hello {\n" +
        "    Content: \"{|\";\n" +
        "}",
        "Styles.akcss")]
    public void LiteralAndCommentOpenBraceIsNotStructuralAkcss(
        string sourceWithCaret,
        string filePath)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            filePath);

        Assert.False(document.ShouldAutoCloseCurlyBrace(position));
    }
    [Theory]
    [InlineData("<Button/|>", "", 1, false)]
    [InlineData("<Button/|", ">", 0, false)]
    [InlineData("<StackPanel>\n    </|>", "StackPanel", 1, true)]
    [InlineData("<StackPanel>\n    </|", "StackPanel>", 0, true)]
    public void SlashCompletionReusesGeneratedGreaterThan(
        string sourceWithCaret,
        string insertionText,
        int overtypeLength,
        bool completesClosingTag)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        var result = document.TryGetSlashCompletionEdit(
            position,
            out var edit);

        Assert.True(result);
        Assert.Equal(insertionText, edit.InsertionText);
        Assert.Equal(overtypeLength, edit.OvertypeLength);
        Assert.Equal(completesClosingTag, edit.CompletesClosingTag);
    }
    [Theory]
    [InlineData("state string text = \"\"\"|\"\"\";", 0, 3, true)]
    [InlineData("state string text = $\"\"\"|\"\"\";", 1, 3, true)]
    [InlineData("state string text = $$$$$\"\"\"\"\"\"\"|\"\"\"\"\"\"\";", 5, 7, true)]
    [InlineData("state string text = \"\"\"|;", 0, 3, false)]
    public void RawStringInfoUsesRoslynDelimiters(
        string sourceWithCaret,
        int dollarCount,
        int quoteCount,
        bool hasClosingDelimiter)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        var result = document.TryGetRawStringInfo(
            position,
            out var info);

        Assert.True(result);
        Assert.Equal(dollarCount, info.DollarCount);
        Assert.Equal(quoteCount, info.QuoteCount);
        Assert.Equal(hasClosingDelimiter, info.HasClosingDelimiter);
        Assert.True(info.IsAtEndOfOpeningDelimiter);
    }

    [Theory]
    [InlineData("state string text = \"\"\"hello |world\"\"\";", 0, 3)]
    [InlineData("state string text = $$\"\"\"hello |world\"\"\";", 2, 3)]
    [InlineData("state string text = \"\"\"\"hello |world\"\"\"\";", 0, 4)]
    public void RawStringInfoWorksInsideExistingContent(
        string sourceWithCaret,
        int dollarCount,
        int quoteCount)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        var result = document.TryGetRawStringInfo(
            position,
            out var info);

        Assert.True(result);
        Assert.Equal(dollarCount, info.DollarCount);
        Assert.Equal(quoteCount, info.QuoteCount);
        Assert.True(info.HasClosingDelimiter);
        Assert.False(info.IsAtEndOfOpeningDelimiter);
        Assert.True(info.OpeningSpan.End < position);
        Assert.True(info.ClosingSpan.Start > position);
    }
    [Theory]
    [InlineData("state string text = @\"\"|;")]
    [InlineData("state string text = \"hello|\";")]
    public void NonRawStringsDoNotProduceRawStringInfo(
        string sourceWithCaret)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        Assert.False(document.TryGetRawStringInfo(
            position,
            out _));
    }
    [Theory]
    [InlineData("state string text = $\"{|}\";", 1, false, 1, true)]
    [InlineData("state string text = $$\"\"\"{{|}}\"\"\";", 2, true, 2, true)]
    [InlineData("state string text = $$$$$\"\"\"{{{{{|}}}}}\"\"\";", 5, true, 5, true)]
    [InlineData("state string text = $$\"\"\"{{|\"\"\";", 2, true, 2, false)]
    public void InterpolationInfoUsesDollarArity(
        string sourceWithCaret,
        int dollarCount,
        bool isRaw,
        int requiredBraceCount,
        bool hasClosingDelimiter)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        var result = document.TryGetInterpolationInfo(
            position,
            out var info);

        Assert.True(result);
        Assert.Equal(dollarCount, info.DollarCount);
        Assert.Equal(isRaw, info.IsRaw);
        Assert.Equal(requiredBraceCount, info.RequiredBraceCount);
        Assert.Equal(hasClosingDelimiter, info.HasClosingDelimiter);
        Assert.True(info.IsAtEndOfOpeningDelimiter);
    }

    [Theory]
    [InlineData("state string text = $$\"\"\"{|\"\"\";")]
    [InlineData("state string text = $$$$$\"\"\"{{{{|\"\"\";")]
    public void ShortRawBraceRunsRemainLiteralText(
        string sourceWithCaret)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        Assert.False(document.TryGetInterpolationInfo(
            position,
            out _));
    }
    [Theory]
    [InlineData("state string text = $|;", '"', "\"")]
    [InlineData("state string text = @|;", '"', "\"")]
    [InlineData("state object value = GetValue|();", '<', ">")]
    [InlineData("state Dictionary<string, List|> value;", '<', ">")]
    public void CSharpStringPrefixesAndGenericsCreatePairs(
        string sourceWithCaret,
        char openingCharacter,
        string closingText)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            "Component.akbura");

        var decision = document.GetAutomaticPairDecision(
            position,
            openingCharacter);

        Assert.True(decision.IsValid);
        Assert.Equal(closingText, decision.ClosingText);
    }

    [Theory]
    [InlineData("state string text = $\"|\";", '{')]
    [InlineData("state string text = $$\"\"\"|\"\"\";", '{')]
    [InlineData("state string text = $$\"\"\"{|\"\"\";", '{')]
    [InlineData("void Update()\n{\n    // comment |\n}", '(')]
    [InlineData(".card { // comment |\n}", '{')]
    [InlineData("@akcss { .card { // comment |\n} }", '(')]
    public void InterpolationTextAndCommentsDoNotUseFixedPairs(
        string sourceWithCaret,
        char openingCharacter)
    {
        var filePath = sourceWithCaret.StartsWith(
                ".card",
                StringComparison.Ordinal)
            ? "Styles.akcss"
            : "Component.akbura";
        var (document, position) = Parse(
            sourceWithCaret,
            filePath);

        Assert.False(document.GetAutomaticPairDecision(
            position,
            openingCharacter).IsValid);
    }
    private static (AkburaSyntacticDocument Document, int Position) Parse(
        string sourceWithCaret,
        string filePath)
    {
        var position = sourceWithCaret.IndexOf('|');
        Assert.True(position >= 0, "The test source must contain a caret marker.");
        var source = sourceWithCaret.Remove(position, 1);
        return (
            AkburaSyntacticDocument.Parse(
                SourceText.From(source),
                filePath),
            position);
    }
}
