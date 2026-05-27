using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using static Akbura.UnitTests.ParserHelper;

namespace Akbura.UnitTests;

public class MarkupNodeSyntaxParseTests
{
    [Fact]
    public void RootElement_ParseSuccessfully()
    {
        const string code = "<HelloWorld></HelloWorld>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();

        Assert.NotNull(syntax);
        Assert.Equal("HelloWorld", syntax.Element.StartTag?.Name.ToFullString());
        Assert.NotNull(syntax.Element.EndTag);
        Assert.Equal(0, syntax.Element.Body.Count);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void SelfClosingElement_ParseSuccessfully()
    {
        const string code = "<Input bind:Value={name} />";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var element = syntax.Element;

        Assert.NotNull(element.StartTag);
        Assert.Null(element.EndTag);
        Assert.Equal(SyntaxKind.SlashGreaterToken, element.StartTag!.CloseToken.Kind);
        Assert.Equal(1, element.StartTag.Attributes.Count);
        Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(element.StartTag.Attributes[0]);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ElementWithAttributes_ParseSuccessfully()
    {
        const string code = "<Button Click={count++} out:Result={Console.WriteLine(@value)} flex w-30></Button>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var attributes = syntax.Element.StartTag!.Attributes;

        Assert.Equal(4, attributes.Count);
        Assert.IsType<GreenMarkupPlainAttributeSyntax>(attributes[0]);
        Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(attributes[1]);
        Assert.IsType<GreenTailwindFlagAttributeSyntax>(attributes[2]);
        Assert.IsType<GreenTailwindFullAttributeSyntax>(attributes[3]);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ElementWithPlainAttributeMissingEquals_RecoverSuccessfully()
    {
        const string code = "<Button Click{count++}></Button>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var attribute = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            syntax.Element.StartTag!.Attributes[0]);

        Assert.True(attribute.EqualsToken.IsMissing);
        Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(attribute.Value);
        Assert.NotNull(syntax.Element.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ElementWithPrefixedAttributeMissingName_RecoverSuccessfully()
    {
        const string code = "<Input bind:={search}></Input>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var attribute = Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(
            syntax.Element.StartTag!.Attributes[0]);

        Assert.True(attribute.Name.Identifier.IsMissing);
        Assert.False(attribute.EqualsToken.IsMissing);
        Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(attribute.Value);
        Assert.NotNull(syntax.Element.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ElementWithTailwindAttributeMissingSegment_RecoverSuccessfully()
    {
        const string code = "<Box w-></Box>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var attribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            syntax.Element.StartTag!.Attributes[0]);
        var segment = Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(attribute.Segments[0]);

        Assert.True(segment.Name.Identifier.IsMissing);
        Assert.NotNull(syntax.Element.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void ElementWithMultipleMalformedAttributes_RecoverSuccessfully()
    {
        const string code = "<Grid Title\"Dashboard\" bind:={search} w-></Grid>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var attributes = syntax.Element.StartTag!.Attributes;

        var title = Assert.IsType<GreenMarkupPlainAttributeSyntax>(attributes[0]);
        var bind = Assert.IsType<GreenMarkupPrefixedAttributeSyntax>(attributes[1]);
        var utility = Assert.IsType<GreenTailwindFullAttributeSyntax>(attributes[2]);
        var segment = Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(utility.Segments[0]);

        Assert.True(title.EqualsToken.IsMissing);
        Assert.True(bind.Name.Identifier.IsMissing);
        Assert.True(segment.Name.Identifier.IsMissing);
        Assert.NotNull(syntax.Element.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void NestedElement_ParseSuccessfully()
    {
        const string code = "<Grid><Button>{count}</Button></Grid>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();
        var childContent = Assert.IsType<GreenMarkupElementContentSyntax>(syntax.Element.Body[0]);
        var child = childContent.Element;

        Assert.Equal("Button", child.StartTag?.Name.ToFullString());
        Assert.IsType<GreenMarkupInlineExpressionSyntax>(child.Body[0]);
        Assert.NotNull(child.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void TextContent_ParseSuccessfully()
    {
        const string code = "<Text>Hello world</Text>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();

        Assert.IsType<GreenMarkupTextLiteralSyntax>(syntax.Element.Body[0]);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void CompilationUnit_WithMarkupRoot_ParseSuccessfully()
    {
        const string code = "<HelloWorld></HelloWorld>";

        var parser = MakeParser(code);

        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(1, syntax.Members.Count);
        Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[0]);
        Assert.Equal(code, syntax.Members[0]!.ToFullString());
    }

    [Fact]
    public void Element_MissingEndTag_DoesNotCrash()
    {
        const string code = "<HelloWorld>";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();

        Assert.NotNull(syntax.Element.StartTag);
        Assert.Null(syntax.Element.EndTag);
        Assert.Equal(code, syntax.ToFullString());
    }

    [Fact]
    public void StartTag_MissingCloseToken_ProducesMissingToken()
    {
        const string code = "<HelloWorld";

        var parser = MakeParser(code);

        var syntax = parser.ParseMarkupRootSyntax();

        Assert.True(syntax.Element.StartTag!.CloseToken.IsMissing);
        Assert.Equal(code, syntax.ToFullString());
    }
}
