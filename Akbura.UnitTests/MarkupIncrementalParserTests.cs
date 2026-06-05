using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupIncrementalParserTests
{
    [Fact]
    public void LiteralAttributeValueEdit_ReusesSiblingAttributes()
    {
        const string oldCode = "<StackPanel class=\"card\" Text=\"Hi\" Role=\"Panel\"/>";
        const string newCode = "<StackPanel class=\"card\" Text=\"Hello\" Role=\"Panel\"/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Hi"),
            oldLength: "Hi".Length,
            newLength: "Hello".Length);

        var oldStartTag = oldMarkup.Element.StartTag!;
        var newStartTag = newMarkup.Element.StartTag!;

        Assert.NotSame(oldMarkup, newMarkup);
        Assert.Same(oldStartTag.LessToken, newStartTag.LessToken);
        Assert.Same(oldStartTag.Name, newStartTag.Name);
        Assert.Same(oldStartTag.Attributes[0], newStartTag.Attributes[0]);
        Assert.NotSame(oldStartTag.Attributes[1], newStartTag.Attributes[1]);
        Assert.Same(oldStartTag.Attributes[2], newStartTag.Attributes[2]);
        Assert.Same(oldStartTag.CloseToken, newStartTag.CloseToken);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void InsertAttribute_ReusesSurroundingAttributes()
    {
        const string oldCode = "<Button Text=\"Save\" Role=\"Action\"/>";
        const string inserted = "class=\"primary\" ";
        var insertPosition = oldCode.IndexOf("Role");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        var oldAttributes = oldMarkup.Element.StartTag!.Attributes;
        var newAttributes = newMarkup.Element.StartTag!.Attributes;

        Assert.Equal(2, oldAttributes.Count);
        Assert.Equal(3, newAttributes.Count);
        Assert.Same(oldAttributes[0], newAttributes[0]);
        Assert.IsType<GreenMarkupPlainAttributeSyntax>(newAttributes[1]);
        Assert.Same(oldAttributes[1], newAttributes[2]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void DynamicAttributeExpressionEdit_ReusesAttributeNameAndBraces()
    {
        const string oldCode = "<Button OnClick={count++} class=\"primary\"/>";
        const string newCode = "<Button OnClick={count += 1} class=\"primary\"/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("count++"),
            oldLength: "count++".Length,
            newLength: "count += 1".Length);

        var oldOnClick = Assert.IsType<GreenMarkupPlainAttributeSyntax>(oldMarkup.Element.StartTag!.Attributes[0]);
        var newOnClick = Assert.IsType<GreenMarkupPlainAttributeSyntax>(newMarkup.Element.StartTag!.Attributes[0]);
        var oldValue = Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(oldOnClick.Value);
        var newValue = Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(newOnClick.Value);

        Assert.NotSame(oldOnClick, newOnClick);
        Assert.Same(oldOnClick.Name.Identifier, newOnClick.Name.Identifier);
        Assert.Same(oldOnClick.EqualsToken, newOnClick.EqualsToken);
        Assert.Same(oldValue.Expression.OpenBrace, newValue.Expression.OpenBrace);
        Assert.NotSame(oldValue.Expression.Expression, newValue.Expression.Expression);
        Assert.Equal(oldValue.Expression.CloseBrace.ToFullString(), newValue.Expression.CloseBrace.ToFullString());
        var oldStartTag = oldMarkup.Element.StartTag!;
        var newStartTag = newMarkup.Element.StartTag!;
        Assert.Equal(oldStartTag.Attributes[1]!.ToFullString(), newStartTag.Attributes[1]!.ToFullString());
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TextContentEdit_ReusesSiblingMarkupContent()
    {
        const string oldCode =
            "<StackPanel>\n" +
            "    <TextBlock Text=\"Header\"/>\n" +
            "    <TextBlock>Second</TextBlock>\n" +
            "    <TextBlock Text=\"Footer\"/>\n" +
            "</StackPanel>";
        const string newCode =
            "<StackPanel>\n" +
            "    <TextBlock Text=\"Header\"/>\n" +
            "    <TextBlock>Changed</TextBlock>\n" +
            "    <TextBlock Text=\"Footer\"/>\n" +
            "</StackPanel>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Second"),
            oldLength: "Second".Length,
            newLength: "Changed".Length);

        var oldChildren = ElementContents(oldMarkup);
        var newChildren = ElementContents(newMarkup);

        Assert.Equal(3, oldChildren.Length);
        Assert.Equal(3, newChildren.Length);
        Assert.Same(oldChildren[0], newChildren[0]);
        Assert.NotSame(oldChildren[1], newChildren[1]);
        Assert.Same(oldChildren[2], newChildren[2]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TextContentEdit_ReusesSiblingMarkupContentWithDynamicAttributes()
    {
        const string oldCode =
            "<StackPanel>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"First\"/>\n" +
            "    </Row>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"Old\"/>\n" +
            "    </Row>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"Third\"/>\n" +
            "    </Row>\n" +
            "</StackPanel>";
        const string newCode =
            "<StackPanel>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"First\"/>\n" +
            "    </Row>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"New\"/>\n" +
            "    </Row>\n" +
            "    <Row IsVisible={isOpen} class=\"item\">\n" +
            "        <TextBlock Text=\"Third\"/>\n" +
            "    </Row>\n" +
            "</StackPanel>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Old"),
            oldLength: "Old".Length,
            newLength: "New".Length);

        var oldChildren = ElementContents(oldMarkup);
        var newChildren = ElementContents(newMarkup);

        Assert.Equal(3, oldChildren.Length);
        Assert.Equal(3, newChildren.Length);
        Assert.Same(oldChildren[0], newChildren[0]);
        Assert.NotSame(oldChildren[1], newChildren[1]);
        Assert.Same(oldChildren[2], newChildren[2]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void InsertChildElement_ReusesSurroundingChildContent()
    {
        const string oldCode =
            "<StackPanel>\n" +
            "    <TextBlock Text=\"Header\"/>\n" +
            "    <TextBlock Text=\"Footer\"/>\n" +
            "</StackPanel>";
        const string inserted = "    <Button Text=\"Save\"/>\n";
        var insertPosition = oldCode.IndexOf("    <TextBlock Text=\"Footer\"");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        var oldChildren = ElementContents(oldMarkup);
        var newChildren = ElementContents(newMarkup);

        Assert.Equal(2, oldChildren.Length);
        Assert.Equal(3, newChildren.Length);
        Assert.Same(oldChildren[0], newChildren[0]);
        Assert.IsType<GreenMarkupElementContentSyntax>(newChildren[1]);
        Assert.Same(oldChildren[1], newChildren[2]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void NestedAttributeEdit_ReusesUnchangedNestedSiblings()
    {
        const string oldCode =
            "<StackPanel class=\"card\">\n" +
            "    <TextBlock Text=\"Title\" class=\"title\"/>\n" +
            "    <Button Text=\"Save\" class=\"primary\" Role=\"Action\"/>\n" +
            "    <Border class=\"box\"/>\n" +
            "</StackPanel>";
        const string newCode =
            "<StackPanel class=\"card\">\n" +
            "    <TextBlock Text=\"Title\" class=\"title\"/>\n" +
            "    <Button Text=\"Save\" class=\"accent\" Role=\"Action\"/>\n" +
            "    <Border class=\"box\"/>\n" +
            "</StackPanel>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("primary"),
            oldLength: "primary".Length,
            newLength: "accent".Length);

        var oldChildren = ElementContents(oldMarkup);
        var newChildren = ElementContents(newMarkup);
        var oldButton = oldChildren[1].Element;
        var newButton = newChildren[1].Element;

        Assert.Same(oldChildren[0], newChildren[0]);
        Assert.NotSame(oldChildren[1], newChildren[1]);
        Assert.Same(oldChildren[2], newChildren[2]);
        Assert.Same(oldButton.StartTag!.Attributes[0], newButton.StartTag!.Attributes[0]);
        Assert.NotSame(oldButton.StartTag.Attributes[1], newButton.StartTag.Attributes[1]);
        Assert.Same(oldButton.StartTag.Attributes[2], newButton.StartTag.Attributes[2]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TailwindSegmentEdit_ReusesNameAndMinusSlots()
    {
        const string oldCode = "<StackPanel w-30 p-4/>";
        const string newCode = "<StackPanel w-40 p-4/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("30"),
            oldLength: "30".Length,
            newLength: "40".Length);

        var oldAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(newMarkup.Element.StartTag!.Attributes[0]);
        var oldSegments = oldAttribute.Segments.AsSeparatedList<GreenTailwindSegmentSyntax>();
        var newSegments = newAttribute.Segments.AsSeparatedList<GreenTailwindSegmentSyntax>();

        Assert.NotSame(oldAttribute, newAttribute);
        Assert.Same(oldAttribute.Name.Identifier, newAttribute.Name.Identifier);
        Assert.Same(oldAttribute.Minus, newAttribute.Minus);
        Assert.NotSame(oldSegments[0], newSegments[0]);
        Assert.Same(oldMarkup.Element.StartTag.Attributes[1], newMarkup.Element.StartTag.Attributes[1]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TailwindSegmentInsert_ReusesExistingRightSegment()
    {
        const string oldCode = "<StackPanel gap-4/>";
        const string inserted = "x-";
        var insertPosition = oldCode.IndexOf("4");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        var oldAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(newMarkup.Element.StartTag!.Attributes[0]);
        var oldSegments = oldAttribute.Segments.AsSeparatedList<GreenTailwindSegmentSyntax>();
        var newSegments = newAttribute.Segments.AsSeparatedList<GreenTailwindSegmentSyntax>();

        Assert.Equal(oldAttribute.Name.ToFullString(), newAttribute.Name.ToFullString());
        Assert.Same(oldAttribute.Minus, newAttribute.Minus);
        Assert.Equal(1, oldSegments.Count);
        Assert.Equal(2, newSegments.Count);
        Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(newSegments[0]);
        Assert.Same(oldSegments[0], newSegments[1]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void QualifiedComponentNameEdit_ReusesAliasQualifierTokensAndAttributes()
    {
        const string oldCode = "<ak::Demo.Controls.Button Text=\"Hi\"/>";
        const string newCode = "<ak::Demo.Controls.Panel Text=\"Hi\"/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Button"),
            oldLength: "Button".Length,
            newLength: "Panel".Length);

        var oldName = Assert.IsType<GreenMarkupQualifiedComponentNameSyntax>(oldMarkup.Element.StartTag!.Name);
        var newName = Assert.IsType<GreenMarkupQualifiedComponentNameSyntax>(newMarkup.Element.StartTag!.Name);
        var oldAlias = oldName.AliasQualifier!;
        var newAlias = newName.AliasQualifier!;
        var oldSegments = oldName.Name.Segments.AsSeparatedList<GreenMarkupNameSegmentSyntax>();
        var newSegments = newName.Name.Segments.AsSeparatedList<GreenMarkupNameSegmentSyntax>();

        Assert.NotSame(oldName, newName);
        Assert.Same(oldAlias.Alias.Identifier, newAlias.Alias.Identifier);
        Assert.Same(oldAlias.DoubleColon, newAlias.DoubleColon);
        Assert.Equal(3, oldSegments.Count);
        Assert.Equal(3, newSegments.Count);
        Assert.Equal(oldSegments[0]!.ToFullString(), newSegments[0]!.ToFullString());
        Assert.Equal(oldSegments[1]!.ToFullString(), newSegments[1]!.ToFullString());
        Assert.NotEqual(oldSegments[2]!.ToFullString(), newSegments[2]!.ToFullString());
        Assert.Same(oldMarkup.Element.StartTag.Attributes[0], newMarkup.Element.StartTag.Attributes[0]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void OldMarkupWithDiagnostics_IsNotReusedAsWholeRoot()
    {
        const string code = "<Button>@if(isOpen){<FirstControl/>}</Button>";

        using var oldParser = ParserHelper.MakeParser(code);
        var oldMarkup = oldParser.ParseMarkupRootSyntax();
        var oldText = Assert.IsType<GreenMarkupTextLiteralSyntax>(oldMarkup.Element.Body[0]);
        Assert.True(oldText.ContainsDiagnosticsDirectly);

        var oldSyntax = GreenSyntaxFactory.AkburaDocumentSyntax(
            GreenSyntaxFactory.List<GreenNode>(oldMarkup),
            GreenSyntaxFactory.Token(SyntaxKind.EndOfFileToken));
        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();

        using var parser = ParserHelper.MakeIncrementalParser(code, oldTree, changes: null);
        var incremental = parser.ParseCompilationUnit();
        var newMarkup = Assert.IsType<GreenMarkupRootSyntax>(incremental.Members[0]);

        Assert.NotSame(oldMarkup, newMarkup);
        Assert.Equal(code, incremental.ToFullString());
    }

    [Fact]
    public void ConditionalRenderingMarkupEdit_ReusesNestedSiblingInsideCSharpBlock()
    {
        const string oldCode =
            "state bool isOpen = false;\n" +
            "\n" +
            "if(isOpen)\n" +
            "{\n" +
            "    <StackPanel>\n" +
            "        <TextBlock Text=\"Old\"/>\n" +
            "        <Button Text=\"Save\"/>\n" +
            "    </StackPanel>\n" +
            "}";
        const string newCode =
            "state bool isOpen = false;\n" +
            "\n" +
            "if(isOpen)\n" +
            "{\n" +
            "    <StackPanel>\n" +
            "        <TextBlock Text=\"New\"/>\n" +
            "        <Button Text=\"Save\"/>\n" +
            "    </StackPanel>\n" +
            "}";

        var oldSyntax = Parse(oldCode);
        var changeStart = oldCode.IndexOf("Old");
        var change = new TextChangeRange(new TextSpan(changeStart, "Old".Length), "New".Length);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var oldConditional = Assert.IsType<GreenCSharpStatementSyntax>(oldSyntax.Members[1]);
        var newConditional = Assert.IsType<GreenCSharpStatementSyntax>(incremental.Members[1]);
        var oldMarkup = Assert.IsType<GreenMarkupRootSyntax>(oldConditional.Body!.Tokens[0]);
        var newMarkup = Assert.IsType<GreenMarkupRootSyntax>(newConditional.Body!.Tokens[0]);
        var oldChildren = ElementContents(oldMarkup);
        var newChildren = ElementContents(newMarkup);

        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.NotSame(oldConditional, newConditional);
        Assert.NotSame(oldChildren[0], newChildren[0]);
        Assert.Equal(oldChildren[1].ToFullString(), newChildren[1].ToFullString());
        Assert.Equal(newCode, incremental.ToFullString());
    }

    private static GreenMarkupElementContentSyntax[] ElementContents(GreenMarkupRootSyntax markup)
    {
        var contents = new List<GreenMarkupElementContentSyntax>();

        for (var i = 0; i < markup.Element.Body.Count; i++)
        {
            if (markup.Element.Body[i] is GreenMarkupElementContentSyntax content)
            {
                contents.Add(content);
            }
        }

        return contents.ToArray();
    }

    private static (GreenMarkupRootSyntax OldMarkup, GreenMarkupRootSyntax NewMarkup) ParseMarkupIncremental(
        string newCode,
        string oldCode,
        int changeStart,
        int oldLength,
        int newLength)
    {
        var oldSyntax = Parse(oldCode);
        var oldMarkup = Assert.IsType<GreenMarkupRootSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(new TextSpan(changeStart, oldLength), newLength);

        var syntax = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(1, syntax.Members.Count);
        return (oldMarkup, Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[0]));
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
