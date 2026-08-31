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
    public void PropertyElementRawStringExpressionEdit_PreservesSemicolonAndReusesBraces()
    {
        const string oldCode =
            """"
            <FeatureView>
                <FeatureView.Code>
                    {"""
                    state int count = 0;
                    """;}
                </FeatureView.Code>
            </FeatureView>
            """";
        const string newCode =
            """"
            <FeatureView>
                <FeatureView.Code>
                    {"""
                    state int count = 1;
                    """;}
                </FeatureView.Code>
            </FeatureView>
            """";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("count = 0", StringComparison.Ordinal),
            oldLength: "count = 0".Length,
            newLength: "count = 1".Length);
        var oldProperty = Assert.IsType<GreenMarkupElementContentSyntax>(
            oldMarkup.Element.Body[0]);
        var newProperty = Assert.IsType<GreenMarkupElementContentSyntax>(
            newMarkup.Element.Body[0]);
        var oldInline = Assert.IsType<GreenMarkupInlineExpressionSyntax>(
            oldProperty.Element.Body[0]).Expression;
        var newInline = Assert.IsType<GreenMarkupInlineExpressionSyntax>(
            newProperty.Element.Body[0]).Expression;

        Assert.Same(oldInline.OpenBrace, newInline.OpenBrace);
        Assert.NotSame(oldInline.Expression, newInline.Expression);
        Assert.Same(oldInline.Semicolon, newInline.Semicolon);
        Assert.Same(oldInline.CloseBrace, newInline.CloseBrace);
        Assert.Equal(SyntaxKind.SemicolonToken, newInline.Semicolon!.Kind);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void MarkupExtensionExpressionEdit_ReusesUnaffectedArguments()
    {
        const string oldCode = "<Button Content=${MyMx 123, Property={mystate + 1}, Binding=${Binding Hello}} class=\"primary\"/>";
        const string newCode = "<Button Content=${MyMx 123, Property={mystate + 2}, Binding=${Binding Hello}} class=\"primary\"/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("mystate + 1"),
            oldLength: "mystate + 1".Length,
            newLength: "mystate + 2".Length);

        var oldContent = Assert.IsType<GreenMarkupPlainAttributeSyntax>(oldMarkup.Element.StartTag!.Attributes[0]);
        var newContent = Assert.IsType<GreenMarkupPlainAttributeSyntax>(newMarkup.Element.StartTag!.Attributes[0]);
        var oldValue = Assert.IsType<GreenMarkupExtensionAttributeValueSyntax>(oldContent.Value);
        var newValue = Assert.IsType<GreenMarkupExtensionAttributeValueSyntax>(newContent.Value);
        var oldArguments = oldValue.Extension.Arguments;
        var newArguments = newValue.Extension.Arguments;

        Assert.NotSame(oldContent, newContent);
        Assert.Same(oldContent.Name.Identifier, newContent.Name.Identifier);
        Assert.Same(oldContent.EqualsToken, newContent.EqualsToken);
        Assert.Same(oldValue.Extension.Type, newValue.Extension.Type);
        Assert.Same(oldArguments[0], newArguments[0]);
        Assert.NotSame(oldArguments[2], newArguments[2]);
        Assert.Same(oldArguments[4], newArguments[4]);
        Assert.Same(oldMarkup.Element.StartTag.Attributes[1], newMarkup.Element.StartTag.Attributes[1]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void BindingDotPathEdit_RemainsAPositionalArgument()
    {
        // Avalonia path: .
        const string oldCode =
            "<TextBlock Text=$" +
            "{Binding Name}/>";
        const string newCode =
            "<TextBlock Text=$" +
            "{Binding .}/>";

        var (oldMarkup, newMarkup) =
            ParseMarkupIncremental(
                newCode,
                oldCode,
                oldCode.IndexOf(
                    "Name",
                    StringComparison.Ordinal),
                oldLength: "Name".Length,
                newLength: 1);
        var oldAttribute =
            Assert.IsType<GreenMarkupPlainAttributeSyntax>(
                oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute =
            Assert.IsType<GreenMarkupPlainAttributeSyntax>(
                newMarkup.Element.StartTag!.Attributes[0]);
        var oldValue =
            Assert.IsType<
                GreenMarkupExtensionAttributeValueSyntax>(
                oldAttribute.Value);
        var newValue =
            Assert.IsType<
                GreenMarkupExtensionAttributeValueSyntax>(
                newAttribute.Value);

        Assert.Same(
            oldValue.Extension.Type,
            newValue.Extension.Type);
        Assert.Equal(
            "Binding",
            newValue.Extension.Type
                .ToFullString()
                .Trim());
        Assert.Equal(
            1,
            newValue.Extension.Arguments.Count);

        var argument =
            Assert.IsType<
                GreenMarkupExtensionPositionalArgumentSyntax>(
                newValue.Extension.Arguments[0]);
        var literal =
            Assert.IsType<
                GreenMarkupExtensionLiteralValueSyntax>(
                argument.Value);

        Assert.Equal(
            ".",
            literal.Value.ToFullString().Trim());
        Assert.Equal(
            newCode,
            newMarkup.ToFullString());
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
    public void TailwindAlphaNumericSegmentEdit_PreservesSingleIdentifierSegment()
    {
        const string oldCode = "<TextBlock text-2xl/>";
        const string newCode = "<TextBlock text-3xl/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("2xl", StringComparison.Ordinal),
            oldLength: "2xl".Length,
            newLength: "3xl".Length);

        Assert.Equal(1, oldMarkup.Element.StartTag!.Attributes.Count);
        Assert.Equal(1, newMarkup.Element.StartTag!.Attributes.Count);
        var oldAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            oldMarkup.Element.StartTag.Attributes[0]);
        var newAttribute = Assert.IsType<GreenTailwindFullAttributeSyntax>(
            newMarkup.Element.StartTag.Attributes[0]);
        Assert.Equal(1, oldAttribute.Segments.Count);
        Assert.Equal(1, newAttribute.Segments.Count);
        var oldSegment = Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(
            oldAttribute.Segments[0]);
        var newSegment = Assert.IsType<GreenTailwindIdentifierSegmentSyntax>(
            newAttribute.Segments[0]);

        Assert.Same(oldAttribute.Name.Identifier, newAttribute.Name.Identifier);
        Assert.Same(oldAttribute.Minus, newAttribute.Minus);
        Assert.Equal("2xl", oldSegment.Name.Identifier.ValueText);
        Assert.Equal("3xl", newSegment.Name.Identifier.ValueText);
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
    public void TailwindMarkupExtensionEdit_ReusesUtilityName()
    {
        const string oldCode =
            "<Border p-${GalleryPadding 1, Value={spacing + 1}, Other=2} rounded-xl/>";
        const string newCode =
            "<Border p-${GalleryPadding 1, Value={spacing + 2}, Other=2} rounded-xl/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("spacing + 1", StringComparison.Ordinal),
            oldLength: "spacing + 1".Length,
            newLength: "spacing + 2".Length);
        var oldAttribute =
            Assert.IsType<GreenTailwindFullAttributeSyntax>(
                oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute =
            Assert.IsType<GreenTailwindFullAttributeSyntax>(
                newMarkup.Element.StartTag!.Attributes[0]);
        var oldSegment =
            Assert.IsType<GreenTailwindMarkupExtensionSegmentSyntax>(
                oldAttribute.Segments[0]);
        var newSegment =
            Assert.IsType<GreenTailwindMarkupExtensionSegmentSyntax>(
                newAttribute.Segments[0]);

        Assert.Same(
            oldAttribute.Name.Identifier,
            newAttribute.Name.Identifier);
        Assert.Same(
            oldSegment.Extension.Arguments[0],
            newSegment.Extension.Arguments[0]);
        Assert.NotSame(
            oldSegment.Extension.Arguments[2],
            newSegment.Extension.Arguments[2]);
        Assert.Same(
            oldSegment.Extension.Arguments[4],
            newSegment.Extension.Arguments[4]);
        Assert.NotSame(oldSegment, newSegment);
        Assert.Same(
            oldMarkup.Element.StartTag.Attributes[1],
            newMarkup.Element.StartTag.Attributes[1]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TailwindMarkupExtensionPrefixEdit_ReusesUtilityBody()
    {
        const string oldCode = "<Border ${md}:p-5 rounded-xl/>";
        const string newCode = "<Border ${lg}:p-5 rounded-xl/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("md", StringComparison.Ordinal),
            oldLength: 2,
            newLength: 2);
        var oldAttribute =
            Assert.IsType<GreenTailwindFullAttributeSyntax>(
                oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute =
            Assert.IsType<GreenTailwindFullAttributeSyntax>(
                newMarkup.Element.StartTag!.Attributes[0]);
        var oldPrefix =
            Assert.IsType<GreenMarkupExtensionConditionalPrefixSyntax>(
                oldAttribute.Prefix);
        var newPrefix =
            Assert.IsType<GreenMarkupExtensionConditionalPrefixSyntax>(
                newAttribute.Prefix);

        Assert.NotSame(oldPrefix, newPrefix);
        Assert.Same(
            oldAttribute.Name,
            newAttribute.Name);
        Assert.Same(
            oldAttribute.Segments[0],
            newAttribute.Segments[0]);
        Assert.Same(
            oldMarkup.Element.StartTag.Attributes[1],
            newMarkup.Element.StartTag.Attributes[1]);
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
    public void AttachedPropertyValueEdit_PreservesAttachedPropertyShapeAndReusesSiblingAttribute()
    {
        const string oldCode = "<TextBlock global::MyControls.AttachedGeneric{int}.Nested.A={1} Text=\"Hi\"/>";
        const string newCode = "<TextBlock global::MyControls.AttachedGeneric{int}.Nested.A={2} Text=\"Hi\"/>";

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("1"),
            oldLength: "1".Length,
            newLength: "2".Length);

        var oldAttribute = Assert.IsType<GreenMarkupAttachedPropertyAttributeSyntax>(oldMarkup.Element.StartTag!.Attributes[0]);
        var newAttribute = Assert.IsType<GreenMarkupAttachedPropertyAttributeSyntax>(newMarkup.Element.StartTag!.Attributes[0]);

        Assert.NotSame(oldAttribute, newAttribute);
        Assert.Equal("global::MyControls.AttachedGeneric{int}.Nested", newAttribute.OwnerType.ToFullString());
        Assert.Equal("A", newAttribute.Name.Identifier.ValueText);
        Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(newAttribute.Value);
        Assert.Same(oldMarkup.Element.StartTag.Attributes[1], newMarkup.Element.StartTag.Attributes[1]);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void RemovingPlainAttributeValue_ProducesIncompleteAttribute()
    {
        const string oldCode = "<Button Hello=\"World\" Role=\"Action\"/>";
        const string removed = "\"World\"";
        var changeStart = oldCode.IndexOf(removed, StringComparison.Ordinal);
        var newCode = oldCode.Remove(changeStart, removed.Length);

        var (oldMarkup, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            changeStart,
            oldLength: removed.Length,
            newLength: 0);

        Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            oldMarkup.Element.StartTag!.Attributes[0]);
        var incomplete = Assert.IsType<GreenIncompleteAttributeSyntax>(
            newMarkup.Element.StartTag!.Attributes[0]);
        Assert.Equal("Hello", incomplete.Name.Identifier.ValueText);
        Assert.True(incomplete.ContainsDiagnostics);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void RemovingPrefixedUtility_ProducesIncompletePrefixedAttribute()
    {
        const string oldCode = "<Button {condition}:p-5/>";
        const string removed = "p-5";
        var changeStart = oldCode.IndexOf(removed, StringComparison.Ordinal);
        var newCode = oldCode.Remove(changeStart, removed.Length);

        var (_, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            changeStart,
            oldLength: removed.Length,
            newLength: 0);

        Assert.True(
            newMarkup.Element.StartTag!.Attributes.Count > 0,
            $"No attribute was parsed from '{newMarkup.ToFullString()}'; " +
            $"close token: '{newMarkup.Element.StartTag.CloseToken.ToFullString()}'.");
        var incomplete = Assert.IsType<GreenIncompletePrefixedAttributeSyntax>(
            newMarkup.Element.StartTag.Attributes[0]);
        Assert.IsType<GreenExpressionConditionalPrefixSyntax>(incomplete.Prefix);
        Assert.True(incomplete.ContainsDiagnostics);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void RemovingMarkupExtensionPrefixedUtility_ProducesIncompletePrefixedAttribute()
    {
        const string oldCode = "<Button ${md}:p-5/>";
        const string removed = "p-5";
        var changeStart = oldCode.IndexOf(removed, StringComparison.Ordinal);
        var newCode = oldCode.Remove(changeStart, removed.Length);

        var (_, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            changeStart,
            oldLength: removed.Length,
            newLength: 0);

        var incomplete = Assert.IsType<GreenIncompletePrefixedAttributeSyntax>(
            newMarkup.Element.StartTag!.Attributes[0]);
        Assert.IsType<GreenMarkupExtensionConditionalPrefixSyntax>(incomplete.Prefix);
        Assert.True(incomplete.ContainsDiagnostics);
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Theory]
    [InlineData("<Button />", "<Button $/>")]
    [InlineData("<Button $/>", "<Button ${/>")]
    [InlineData("<Button ${/>", "<Button ${m/>")]
    [InlineData("<Button ${m/>", "<Button ${md/>")]
    [InlineData("<Button ${md/>", "<Button ${md}/>")]
    [InlineData("<Button ${md}/>", "<Button ${md}:/>")]
    public void TypingMarkupExtensionPrefix_DoesNotCrash(
        string oldCode,
        string newCode)
    {
        var change = GetSingleChange(oldCode, newCode);

        var (_, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            change.Span.Start,
            change.Span.Length,
            change.NewLength);

        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void TypingDynamicAttributeExpression_ExtendsCSharpExpression()
    {
        var code = "<Button Click={}/>";
        var syntax = Parse(code);
        var insertionPosition = code.IndexOf('}');

        foreach (var character in "count++")
        {
            var newCode = code.Insert(
                insertionPosition,
                character.ToString());
            var change = new TextChangeRange(
                new TextSpan(insertionPosition, 0),
                newLength: 1);

            syntax = ParseIncremental(
                newCode,
                syntax,
                [change]);

            Assert.Equal(newCode, syntax.ToFullString());
            code = newCode;
            insertionPosition++;
        }

        var markup = Assert.IsType<GreenMarkupRootSyntax>(
            syntax.Members[0]);
        Assert.Equal(1, markup.Element.StartTag!.Attributes.Count);
        var attribute = Assert.IsType<GreenMarkupPlainAttributeSyntax>(
            markup.Element.StartTag.Attributes[0]);
        var value = Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(
            attribute.Value);

        Assert.Equal("{count++}", value.Expression.ToFullString());
        Assert.False(value.Expression.ContainsDiagnostics);
    }

    [Fact]
    public void SkippedTextBeforeAttributeWithLeadingTrivia_DoesNotCrash()
    {
        const string code = "<Button ! Content=\"Hello\"/>";

        var syntax = Parse(code);
        var markup = Assert.IsType<GreenMarkupRootSyntax>(syntax.Members[0]);
        var attribute = markup.Element.StartTag!.Attributes[0];

        Assert.NotNull(attribute);
        Assert.True(attribute.ContainsSkippedText);
        Assert.Equal(code, markup.ToFullString());
    }

    [Fact]
    public void InsertingUnmatchedEndTag_ProducesIncompleteTag()
    {
        const string oldCode = "<StackPanel><Button/></StackPanel>";
        const string inserted = "</Button>";
        var changeStart = oldCode.IndexOf("<Button", StringComparison.Ordinal);
        var newCode = oldCode.Insert(changeStart, inserted);

        var (_, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            changeStart,
            oldLength: 0,
            newLength: inserted.Length);

        Assert.Equal(2, newMarkup.Element.Body.Count);
        Assert.IsType<GreenIncompleteTagSyntax>(newMarkup.Element.Body[0]);
        Assert.IsType<GreenMarkupElementContentSyntax>(newMarkup.Element.Body[1]);
        Assert.Equal("StackPanel", newMarkup.Element.EndTag!.Name.ToFullString());
        Assert.Equal(newCode, newMarkup.ToFullString());
    }

    [Fact]
    public void RemovingStartTagCloseToken_PreservesNestedElement()
    {
        const string oldCode = "<StackPanel gap-3><Button/></StackPanel>";
        var changeStart = oldCode.IndexOf('>');
        var newCode = oldCode.Remove(changeStart, 1);

        var (_, newMarkup) = ParseMarkupIncremental(
            newCode,
            oldCode,
            changeStart,
            oldLength: 1,
            newLength: 0);

        Assert.True(newMarkup.Element.StartTag!.CloseToken.IsMissing);
        Assert.Equal(1, newMarkup.Element.Body.Count);
        Assert.IsType<GreenMarkupElementContentSyntax>(newMarkup.Element.Body[0]);
        Assert.Equal("StackPanel", newMarkup.Element.EndTag!.Name.ToFullString());
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

    [Fact]
    public void CompletingStateInitializerBeforeExistingMarkup_ProducesSeparateMarkupRoot()
    {
        const string oldCode =
            "state int hi = \n\n\n" +
            "<Border>\n" +
            "</Border>";
        const string inserted = "1;";
        var insertionPosition =
            "state int hi = ".Length;
        var newCode = oldCode.Insert(
            insertionPosition,
            inserted);
        var oldSyntax = Parse(oldCode);

        Assert.Equal(1, oldSyntax.Members.Count);

        var oldState =
            Assert.IsType<GreenStateDeclarationSyntax>(
                oldSyntax.Members[0]);
        var oldInitializer =
            Assert.IsType<GreenSimpleStateInitializerSyntax>(
                oldState.Initializer);

        Assert.True(oldState.Semicolon.IsMissing);
        Assert.Contains(
            "<Border>",
            oldInitializer.Expression.ToFullString());

        var incremental = ParseIncremental(
            newCode,
            oldSyntax,
            [
                new TextChangeRange(
                    new TextSpan(
                        insertionPosition,
                        length: 0),
                    inserted.Length),
            ]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(2, incremental.Members.Count);

        var state =
            Assert.IsType<GreenStateDeclarationSyntax>(
                incremental.Members[0]);
        var initializer =
            Assert.IsType<GreenSimpleStateInitializerSyntax>(
                state.Initializer);

        Assert.Equal(
            "1",
            initializer.Expression.ToFullString());
        Assert.False(state.Semicolon.IsMissing);

        var markup =
            Assert.IsType<GreenMarkupRootSyntax>(
                incremental.Members[1]);

        Assert.Equal(
            "Border",
            markup.Element.StartTag!.Name.ToFullString());
        Assert.Equal(
            "Border",
            markup.Element.EndTag!.Name.ToFullString());
        AssertSameTree(
            Parse(newCode),
            incremental);
    }

    [Fact]
    public void TypingComponentAndInsertingClosingTag_ProducesCompleteRoot()
    {
        var code = "<";
        var syntax = Parse(code);

        foreach (var character in "Border>")
        {
            var newCode =
                code + character;

            var change =
                new TextChangeRange(
                    new TextSpan(
                        code.Length,
                        length: 0),
                    newLength: 1);

            syntax = ParseIncremental(
                newCode,
                syntax,
                [change]);

            code = newCode;

            Assert.Equal(
                code,
                syntax.ToFullString());

            AssertSameTree(
                Parse(code),
                syntax);
        }

        const string closingTag =
            "</Border>";

        var codeWithClosingTag =
            code + closingTag;

        syntax = ParseIncremental(
            codeWithClosingTag,
            syntax,
            [
                new TextChangeRange(
                new TextSpan(
                    code.Length,
                    length: 0),
                closingTag.Length),
            ]);

        Assert.Equal(
            1,
            syntax.Members.Count);

        var markup =
            Assert.IsType<GreenMarkupRootSyntax>(
                syntax.Members[0]);

        Assert.NotNull(markup.Element.StartTag);

        var startTag = markup.Element.StartTag;

        Assert.Equal(
            "Border",
            startTag.Name.ToFullString());

        Assert.Equal(
            0,
            startTag.Attributes.Count);

        Assert.NotNull(markup.Element.EndTag);

        var endTag = markup.Element.EndTag;

        Assert.Equal(
            "Border",
            endTag.Name.ToFullString());

        Assert.False(
            markup.ContainsDiagnostics);

        Assert.Equal(
            codeWithClosingTag,
            syntax.ToFullString());

        AssertSameTree(
            Parse(codeWithClosingTag),
            syntax);
    }

    [Fact]
    public void
        TypingChildBeforeExistingEndTag_MatchesFullParse()
    {
        var code =
            "<StackPanel>\r\n" +
            "\r\n" +
            "</StackPanel>";

        var syntax = Parse(code);
        var insertionPosition =
            code.IndexOf(
                "</StackPanel>",
                StringComparison.Ordinal);

        const string inserted =
            "\t<Button></Button>\r\n";

        foreach (var character in inserted)
        {
            var newCode = code.Insert(
                insertionPosition,
                character.ToString());

            syntax = ParseIncremental(
                newCode,
                syntax,
                [
                    new TextChangeRange(
                        new TextSpan(
                            insertionPosition,
                            length: 0),
                        newLength: 1),
                ]);

            code = newCode;
            insertionPosition++;

            var fullSyntax = Parse(code);

            Assert.Equal(
                code,
                fullSyntax.ToFullString());
            Assert.Equal(
                code,
                syntax.ToFullString());

            AssertSameTree(
                fullSyntax,
                syntax);
        }

        Assert.False(
            syntax.ContainsDiagnostics);
    }

    [Fact]
    public void
        ConvertingSelfClosingChildToPairedElement_MatchesFullParse()
    {
        var code =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "state int a = 0;\r\n" +
            "\r\n" +
            "<StackPanel>\r\n" +
            "\t<Button/>\r\n" +
            "</StackPanel>";

        var syntax = Parse(code);

        void ApplyChange(
            string updatedCode,
            TextChangeRange change)
        {
            syntax = ParseIncremental(
                updatedCode,
                syntax,
                [change]);

            code = updatedCode;

            Assert.Equal(
                code,
                syntax.ToFullString());

            AssertSameTree(
                Parse(code),
                syntax);
        }

        var slashPosition =
            code.IndexOf(
                "/>",
                StringComparison.Ordinal);

        var updatedCode =
            code.Insert(
                slashPosition,
                " ");

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    slashPosition,
                    length: 0),
                newLength: 1));

        var spacePosition =
            code.IndexOf(
                " />",
                StringComparison.Ordinal);

        updatedCode =
            code.Remove(
                spacePosition,
                count: 1);

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    spacePosition,
                    length: 1),
                newLength: 0));

        slashPosition =
            code.IndexOf(
                "/>",
                StringComparison.Ordinal);

        updatedCode =
            code.Remove(
                slashPosition,
                count: 1);

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    slashPosition,
                    length: 1),
                newLength: 0));

        var insertionPosition =
            code.IndexOf(
                "</StackPanel>",
                StringComparison.Ordinal);

        const string blankLine =
            "\r\n";

        updatedCode =
            code.Insert(
                insertionPosition,
                blankLine);

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    insertionPosition,
                    length: 0),
                blankLine.Length));

        insertionPosition +=
            blankLine.Length;

        const string indentationAndLess =
            "\t\t<";

        updatedCode =
            code.Insert(
                insertionPosition,
                indentationAndLess);

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    insertionPosition,
                    length: 0),
                indentationAndLess.Length));

        insertionPosition +=
            indentationAndLess.Length;

        foreach (var character in "/Button>")
        {
            updatedCode =
                code.Insert(
                    insertionPosition,
                    character.ToString());

            ApplyChange(
                updatedCode,
                new TextChangeRange(
                    new TextSpan(
                        insertionPosition,
                        length: 0),
                    newLength: 1));

            insertionPosition++;
        }

        updatedCode =
            code.Insert(
                insertionPosition,
                "\r\n");

        ApplyChange(
            updatedCode,
            new TextChangeRange(
                new TextSpan(
                    insertionPosition,
                    length: 0),
                newLength: 2));

        Assert.False(
            syntax.ContainsDiagnostics);
    }

    [Fact]
    public void
        TypingMarkupExpressionWithObjectInitializers_MatchesFullParse()
    {
        const string prefix =
            "using Avalonia.Controls;\r\n" +
            "\r\n" +
            "state int count = 1;\r\n" +
            "\r\n" +
            "<Border>\r\n" +
            "\t";

        const string expressionBody =
            "count % 2 == 0 \r\n" +
            "\t\t? new Button() " +
            "{ Content = $\"Increment {count}\" }\r\n" +
            "\t\t: new Border() " +
            "{ Width = 100, Height = 100 }\r\n" +
            "\t";

        const string suffix =
            "\r\n" +
            "</Border>";

        var code =
            prefix +
            "{}" +
            suffix;

        var syntax = Parse(code);
        var insertionPosition =
            prefix.Length + 1;

        foreach (var character in expressionBody)
        {
            var newCode = code.Insert(
                insertionPosition,
                character.ToString());

            var change =
                new TextChangeRange(
                    new TextSpan(
                        insertionPosition,
                        length: 0),
                    newLength: 1);

            syntax = ParseIncremental(
                newCode,
                syntax,
                [change]);

            code = newCode;
            insertionPosition++;

            var fullSyntax = Parse(code);

            Assert.Equal(
                code,
                fullSyntax.ToFullString());
            Assert.Equal(
                code,
                syntax.ToFullString());

            AssertSameTree(
                fullSyntax,
                syntax);
        }

        Assert.False(
            syntax.ContainsDiagnostics);

        var markup =
            Assert.IsType<GreenMarkupRootSyntax>(
                syntax.Members[
                    syntax.Members.Count - 1]);

        var inlineExpressions =
            new List<GreenMarkupInlineExpressionSyntax>();
        for (var i = 0; i < markup.Element.Body.Count; i++)
        {
            if (markup.Element.Body[i] is
                GreenMarkupInlineExpressionSyntax expression)
            {
                inlineExpressions.Add(expression);
            }
        }

        var inlineExpression =
            Assert.Single(inlineExpressions);

        Assert.False(
            inlineExpression
                .Expression
                .CloseBrace
                .IsMissing);

        Assert.Equal(
            expressionBody,
            inlineExpression
                .Expression
                .Expression
                .ToFullString());
    }

    private static void AssertSameTree(
        GreenNode expected,
        GreenNode actual)
    {
        if (ReferenceEquals(expected, actual))
        {
            return;
        }

        Assert.Equal(
            expected.Kind,
            actual.Kind);

        Assert.Equal(
            expected.FullWidth,
            actual.FullWidth);

        Assert.Equal(
            expected.SlotCount,
            actual.SlotCount);

        Assert.Equal(
            expected.IsMissing,
            actual.IsMissing);

        Assert.Equal(
            expected
                .GetDiagnostics()
                .Select(static diagnostic =>
                    diagnostic.Code),
            actual
                .GetDiagnostics()
                .Select(static diagnostic =>
                    diagnostic.Code));

        if (expected.SlotCount == 0)
        {
            Assert.Equal(
                expected.ToFullString(),
                actual.ToFullString());

            return;
        }

        for (var i = 0;
             i < expected.SlotCount;
             i++)
        {
            var expectedChild =
                expected.GetSlot(i);

            var actualChild =
                actual.GetSlot(i);

            Assert.Equal(
                expectedChild == null,
                actualChild == null);

            if (expectedChild == null ||
                actualChild == null)
            {
                continue;
            }

            AssertSameTree(
                expectedChild,
                actualChild);
        }
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

    private static TextChangeRange GetSingleChange(string oldText, string newText)
    {
        var start = 0;
        while (start < oldText.Length &&
               start < newText.Length &&
               oldText[start] == newText[start])
        {
            start++;
        }

        var oldEnd = oldText.Length;
        var newEnd = newText.Length;
        while (oldEnd > start &&
               newEnd > start &&
               oldText[oldEnd - 1] == newText[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        return new TextChangeRange(
            new TextSpan(start, oldEnd - start),
            newEnd - start);
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
