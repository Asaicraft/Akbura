using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class AkcssIncrementalParserTests
{
    [Fact]
    public void UtilitySelector_ParsesHyphenatedStaticAndParameterizedNames()
    {
        const string code =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        Control.self-start { HorizontalAlignment: Left; }\n" +
            "        Control.min-w-(double value) { MinWidth: value; }\n" +
            "        TextBlock.text-2xl { FontSize: 24; }\n" +
            "    }\n" +
            "}";

        var syntax = Parse(code);
        var block = Assert.IsType<GreenInlineAkcssBlockSyntax>(syntax.Members[0]);
        var section = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(block.Members[0]);
        var selfStart = section.Utilities[0]!;
        var minWidth = section.Utilities[1]!;
        var text2Xl = section.Utilities[2]!;

        Assert.Equal("self-start", selfStart.Selector.Name.Identifier.ValueText);
        Assert.Equal(0, selfStart.Selector.Parameters.Count);
        Assert.Equal("min-w", minWidth.Selector.Name.Identifier.ValueText);
        Assert.Equal(1, minWidth.Selector.Parameters.Count);
        Assert.Equal("text-2xl", text2Xl.Selector.Name.Identifier.ValueText);
        Assert.Equal(0, text2Xl.Selector.Parameters.Count);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void StyleRuleAssignmentEdit_ReusesSiblingBodyMembersAndRules()
    {
        const string oldCode =
            "@akcss {\n" +
            "    .card {\n" +
            "        Padding: 12;\n" +
            "        Background: \"Red\";\n" +
            "        BorderBrush: \"Blue\";\n" +
            "    }\n" +
            "\n" +
            "    .panel {\n" +
            "        Margin: 4;\n" +
            "    }\n" +
            "}";
        const string newCode =
            "@akcss {\n" +
            "    .card {\n" +
            "        Padding: 12;\n" +
            "        Background: \"Green\";\n" +
            "        BorderBrush: \"Blue\";\n" +
            "    }\n" +
            "\n" +
            "    .panel {\n" +
            "        Margin: 4;\n" +
            "    }\n" +
            "}";

        var (oldBlock, newBlock) = ParseInlineAkcssIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("\"Red\""),
            oldLength: "\"Red\"".Length,
            newLength: "\"Green\"".Length);

        var oldCard = Assert.IsType<GreenAkcssStyleRuleSyntax>(oldBlock.Members[0]);
        var newCard = Assert.IsType<GreenAkcssStyleRuleSyntax>(newBlock.Members[0]);

        Assert.NotSame(oldBlock, newBlock);
        Assert.NotSame(oldCard, newCard);
        Assert.Same(oldCard.Selector.DotToken, newCard.Selector.DotToken);
        Assert.Same(oldCard.Selector.Name.Identifier, newCard.Selector.Name.Identifier);
        Assert.Same(oldCard.OpenBrace, newCard.OpenBrace);
        Assert.Same(oldCard.Members[0], newCard.Members[0]);
        Assert.NotSame(oldCard.Members[1], newCard.Members[1]);
        Assert.Same(oldCard.Members[2], newCard.Members[2]);
        Assert.Same(oldBlock.Members[1], newBlock.Members[1]);
        Assert.Equal(newCode, newBlock.ToFullString());
    }

    [Fact]
    public void UtilityExpressionEdit_ReusesParametersAndSiblingUtilities()
    {
        const string oldCode =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double width) {\n" +
            "            Width: width * Spacing;\n" +
            "            Height: 10;\n" +
            "        }\n" +
            "\n" +
            "        .gap-(int value) {\n" +
            "            RowGap: value * Spacing;\n" +
            "        }\n" +
            "    }\n" +
            "}";
        const string newCode =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double width) {\n" +
            "            Width: width * Unit;\n" +
            "            Height: 10;\n" +
            "        }\n" +
            "\n" +
            "        .gap-(int value) {\n" +
            "            RowGap: value * Spacing;\n" +
            "        }\n" +
            "    }\n" +
            "}";

        var (oldBlock, newBlock) = ParseInlineAkcssIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Spacing"),
            oldLength: "Spacing".Length,
            newLength: "Unit".Length);

        var oldSection = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(oldBlock.Members[0]);
        var newSection = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(newBlock.Members[0]);
        var oldWidth = oldSection.Utilities[0]!;
        var newWidth = newSection.Utilities[0]!;

        Assert.NotSame(oldSection, newSection);
        Assert.NotSame(oldWidth, newWidth);
        Assert.Same(oldWidth.Selector.Parameters[0]!, newWidth.Selector.Parameters[0]!);
        Assert.NotSame(oldWidth.Members[0], newWidth.Members[0]);
        Assert.Same(oldWidth.Members[1], newWidth.Members[1]);
        Assert.Same(oldSection.Utilities[1]!, newSection.Utilities[1]!);
        Assert.Equal(newCode, newBlock.ToFullString());
    }

    [Fact]
    public void IfDirectiveAssignmentEdit_ReusesOuterBodySiblings()
    {
        const string oldCode =
            "@akcss {\n" +
            "    .card {\n" +
            "        Padding: 12;\n" +
            "\n" +
            "        @if(IsHovered) {\n" +
            "            Background: \"Blue\";\n" +
            "            BorderBrush: \"DodgerBlue\";\n" +
            "        }\n" +
            "\n" +
            "        Margin: 4;\n" +
            "    }\n" +
            "}";
        const string newCode =
            "@akcss {\n" +
            "    .card {\n" +
            "        Padding: 12;\n" +
            "\n" +
            "        @if(IsHovered) {\n" +
            "            Background: \"AliceBlue\";\n" +
            "            BorderBrush: \"DodgerBlue\";\n" +
            "        }\n" +
            "\n" +
            "        Margin: 4;\n" +
            "    }\n" +
            "}";

        var (oldBlock, newBlock) = ParseInlineAkcssIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("\"Blue\""),
            oldLength: "\"Blue\"".Length,
            newLength: "\"AliceBlue\"".Length);

        var oldRule = Assert.IsType<GreenAkcssStyleRuleSyntax>(oldBlock.Members[0]);
        var newRule = Assert.IsType<GreenAkcssStyleRuleSyntax>(newBlock.Members[0]);
        var oldIf = Assert.IsType<GreenAkcssIfDirectiveSyntax>(oldRule.Members[1]);
        var newIf = Assert.IsType<GreenAkcssIfDirectiveSyntax>(newRule.Members[1]);

        Assert.Same(oldRule.Members[0], newRule.Members[0]);
        Assert.NotSame(oldIf, newIf);
        Assert.Same(oldIf.Condition, newIf.Condition);
        Assert.NotSame(oldIf.Members[0], newIf.Members[0]);
        Assert.Same(oldIf.Members[1], newIf.Members[1]);
        Assert.Same(oldRule.Members[2], newRule.Members[2]);
        Assert.Equal(newCode, newBlock.ToFullString());
    }

    [Fact]
    public void InsertUtilityDeclaration_ReusesSurroundingUtilities()
    {
        const string oldCode =
            "@akcss {\n" +
            "    @utilities {\n" +
            "        .w-(double width) { Width: width * Spacing; }\n" +
            "        .gap-(int value) { RowGap: value * Spacing; }\n" +
            "    }\n" +
            "}";
        const string inserted =
            "        .accent { BorderBrush: \"DodgerBlue\"; }\n";
        var insertPosition = oldCode.IndexOf("        .gap", StringComparison.Ordinal);
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldBlock, newBlock) = ParseInlineAkcssIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        var oldSection = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(oldBlock.Members[0]);
        var newSection = Assert.IsType<GreenAkcssUtilitiesSectionSyntax>(newBlock.Members[0]);

        Assert.Equal(2, oldSection.Utilities.Count);
        Assert.Equal(3, newSection.Utilities.Count);
        Assert.Same(oldSection.Utilities[0], newSection.Utilities[0]);
        Assert.IsType<GreenAkcssUtilityDeclarationSyntax>(newSection.Utilities[1]);
        Assert.Same(oldSection.Utilities[1], newSection.Utilities[2]);
        Assert.Equal(newCode, newBlock.ToFullString());
    }

    private static (GreenInlineAkcssBlockSyntax OldBlock, GreenInlineAkcssBlockSyntax NewBlock)
        ParseInlineAkcssIncremental(
            string newCode,
            string oldCode,
            int changeStart,
            int oldLength,
            int newLength)
    {
        var oldSyntax = Parse(oldCode);
        var oldBlock = Assert.IsType<GreenInlineAkcssBlockSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(new TextSpan(changeStart, oldLength), newLength);
        var syntax = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(1, syntax.Members.Count);
        return (oldBlock, Assert.IsType<GreenInlineAkcssBlockSyntax>(syntax.Members[0]));
    }

    private static GreenAkburaDocumentSyntax Parse(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        return parser.ParseCompilationUnit();
    }

    private static GreenAkburaDocumentSyntax ParseIncremental(
        string code,
        GreenAkburaDocumentSyntax oldSyntax,
        IEnumerable<TextChangeRange>? changes)
    {
        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        using var parser = ParserHelper.MakeIncrementalParser(code, oldTree, changes);
        return parser.ParseCompilationUnit();
    }
}
