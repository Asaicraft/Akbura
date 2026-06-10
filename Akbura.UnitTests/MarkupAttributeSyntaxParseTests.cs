using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class MarkupAttributeSyntaxParseTests
{
    [Theory]
    [InlineData("flex", typeof(GreenTailwindFlagAttributeSyntax))]
    [InlineData("w-30", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("space-2-4", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("border-l-2", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("md:w-40", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("md:flex", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("in:aw-{width}", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("{isMobile}:h-15", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("p-{size}", typeof(GreenTailwindFullAttributeSyntax))]
    [InlineData("gap-{state * 2}", typeof(GreenTailwindFullAttributeSyntax))]
    public void TailwindAttribute_ParseSuccessfully(string code, Type expectedType)
    {
        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupAttributeSyntax();

        Assert.NotNull(syntax);
        Assert.IsType(expectedType, syntax);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PrefixedAttribute_ParseSuccessfully()
    {
        const string code = "bind:Search={search}";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupAttributeSyntax();

        Assert.NotNull(syntax);
        Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(syntax);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PlainAttribute_ParseSuccessfully()
    {
        const string code = "Title=\"Dashboard\"";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupAttributeSyntax();

        Assert.NotNull(syntax);
        Assert.IsType<GreenMarkupPlainAttributeSyntax>(syntax);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Theory]
    [InlineData("Click={count++}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    [InlineData("Count={5}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    [InlineData("Title=\"User dashboard\"", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    [InlineData("Placeholder='Name'", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    [InlineData("Value=\"Full Name: {fullName}\"", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    [InlineData("bind={value}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    [InlineData("in={value}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    public void PlainAttribute_Values_ParseSuccessfully(string code, Type expectedValueType)
    {
        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.IsType(expectedValueType, syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PlainDynamicAttribute_CloseBraceIsSeparateToken()
    {
        const string code = "Click={count++}";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());
        var value = Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(syntax.Value);
        var expression = value.Expression;

        Assert.Equal(1, expression.Expression.Tokens.Count);
        var rawToken = expression.Expression.Tokens[0]!;

        Assert.Equal(SyntaxKind.CSharpRawToken, rawToken.Kind);
        Assert.Equal("count++", rawToken.ToFullString());
        Assert.False(expression.CloseBrace.IsMissing);
        Assert.Equal("}", expression.CloseBrace.ToFullString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Theory]
    [InlineData("bind:Search={search}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    [InlineData("out:Result={Console.WriteLine(@value)}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    [InlineData("bind:Value=\"Name\"", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    public void PrefixedAttribute_Values_ParseSuccessfully(string code, Type expectedValueType)
    {
        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.IsType(expectedValueType, syntax.Value);
        Assert.False(syntax.EqualsToken.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TailwindAttribute_MissingName_ProducesMissingToken()
    {
        const string code = "-30";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.True(syntax.Name.Identifier.IsMissing);
        Assert.False(syntax.Minus?.IsMissing);
        Assert.IsType<GreenTailwindNumericSegmentSyntax>(syntax.Segments[0]);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TailwindAttribute_MissingSegment_ProducesMissingToken()
    {
        const string code = "w-";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        var segment = Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(syntax.Segments[0]);

        Assert.Equal("w", syntax.Name.ToFullString());
        Assert.True(segment.Name.Identifier.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TailwindAttribute_MissingNameAfterPrefix_ProducesMissingToken()
    {
        const string code = "md:";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.IsType<GreenSimpleConditionalPrefixSyntax>(syntax.Prefix);
        Assert.True(syntax.Name.Identifier.IsMissing);
        Assert.Null(syntax.Minus);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TailwindAttribute_MissingColonAfterExpressionPrefix_ProducesMissingToken()
    {
        const string code = "{isMobile}h-15";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());
        var prefix = Assert.IsType<GreenExpressionConditionalPrefixSyntax>(syntax.Prefix);

        Assert.True(prefix.Colon.IsMissing);
        Assert.Equal("h", syntax.Name.ToFullString());
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TailwindAttribute_MissingCloseBraceInExpressionSegment_ProducesMissingToken()
    {
        const string code = "p-{size";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());
        var segment = Assert.IsType<GreenTailwindExpressionSegmentSyntax>(syntax.Segments[0]);

        Assert.True(segment.Expression.CloseBrace.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PrefixedAttribute_MissingName_ProducesMissingToken()
    {
        const string code = "bind:={search}";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.True(syntax.Name.Identifier.IsMissing);
        Assert.False(syntax.EqualsToken.IsMissing);
        Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PlainAttribute_MissingValue_DoesNotCrash()
    {
        const string code = "Title=";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.Equal("Title", syntax.Name.ToFullString());
        Assert.False(syntax.EqualsToken.IsMissing);
        Assert.Null(syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Theory]
    [InlineData("Title\"Dashboard\"", typeof(GreenMarkupLiteralAttributeValueSyntax))]
    [InlineData("Click{count++}", typeof(GreenMarkupDynamicAttributeValueSyntax))]
    public void PlainAttribute_MissingEqualsBeforeValue_ProducesMissingToken(
        string code,
        Type expectedValueType)
    {
        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.True(syntax.EqualsToken.IsMissing);
        Assert.IsType(expectedValueType, syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PlainAttribute_MissingCloseQuote_ProducesMissingToken()
    {
        const string code = "Title=\"Dashboard";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.False(syntax.EqualsToken.IsMissing);
        Assert.IsType<GreenMarkupLiteralAttributeValueSyntax>(syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PlainAttribute_MissingCloseBrace_ProducesMissingToken()
    {
        const string code = "Click={count++";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());
        var value = Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(syntax.Value);

        Assert.True(value.Expression.CloseBrace.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void PrefixedAttribute_MissingEqualsBeforeValue_ProducesMissingToken()
    {
        const string code = "bind:Search{search}";

        var parser = MakeParser(code);

        var syntax = Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.True(syntax.EqualsToken.IsMissing);
        Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(syntax.Value);
        Assert.Equal(code, syntax.ToFullString());
    }
}
