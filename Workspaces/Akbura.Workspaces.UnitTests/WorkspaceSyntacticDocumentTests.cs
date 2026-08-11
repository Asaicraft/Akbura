using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceSyntacticDocumentTests
{
    [Fact]
    public void SyntacticDocument_MarkupProvidesOutliningAndIndentation()
    {
        const string source = """
            <StackPanel>
                <Border>
                    <TextBlock/>
                </Border>
            </StackPanel>
            """;
        var text = SourceText.From(source);

        var document = AkburaSyntacticDocument.Parse(
            text,
            "Component.akbura");

        Assert.Equal(2, document.OutliningRegions.Length);
        Assert.All(
            document.OutliningRegions,
            static region => Assert.Equal("...", region.CollapsedText));
        Assert.Contains(
            "</StackPanel>",
            text.ToString(document.OutliningRegions[0].Span),
            StringComparison.Ordinal);
        Assert.Contains(
            "</Border>",
            text.ToString(document.OutliningRegions[1].Span),
            StringComparison.Ordinal);
        Assert.Equal(0, document.GetDesiredIndentationLevel(0));
        Assert.Equal(1, document.GetDesiredIndentationLevel(1));
        Assert.Equal(2, document.GetDesiredIndentationLevel(2));
        Assert.Equal(1, document.GetDesiredIndentationLevel(3));
        Assert.Equal(0, document.GetDesiredIndentationLevel(4));
    }

    [Fact]
    public void SyntacticDocument_AkcssProvidesOutliningAndIndentation()
    {
        const string source = """
            @utilities {
                .card-(double value) {
                    Width: value;
                }
            }
            """;
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Styles.akcss");

        Assert.Equal(2, document.OutliningRegions.Length);
        Assert.All(
            document.OutliningRegions,
            static region => Assert.Equal(
                "{ ... }",
                region.CollapsedText));
        Assert.Equal(0, document.GetDesiredIndentationLevel(0));
        Assert.Equal(1, document.GetDesiredIndentationLevel(1));
        Assert.Equal(2, document.GetDesiredIndentationLevel(2));
        Assert.Equal(1, document.GetDesiredIndentationLevel(3));
        Assert.Equal(0, document.GetDesiredIndentationLevel(4));
    }

    [Theory]
    [InlineData("<StackPanel>\n", "Incomplete.akbura", 1)]
    [InlineData("@utilities {\n", "Incomplete.akcss", 1)]
    public void SyntacticDocument_IncompleteBlockStillProvidesIndentation(
        string source,
        string filePath,
        int expectedIndentation)
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            filePath);

        Assert.Empty(document.OutliningRegions);
        Assert.Equal(
            expectedIndentation,
            document.GetDesiredIndentationLevel(1));
    }

    [Fact]
    public void SyntacticDocument_SelfClosingMarkupDoesNotIndentFollowingLine()
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From("<TextBlock/>\n"),
            "Component.akbura");

        Assert.Empty(document.OutliningRegions);
        Assert.Equal(0, document.GetDesiredIndentationLevel(1));
    }

    [Theory]
    [InlineData("<", AkburaCompletionContextKind.ComponentName, "")]
    [InlineData("<Sta", AkburaCompletionContextKind.ComponentName, "Sta")]
    [InlineData("<Card ", AkburaCompletionContextKind.AttributeName, "")]
    [InlineData("<Card Tit", AkburaCompletionContextKind.AttributeName, "Tit")]
    [InlineData("</", AkburaCompletionContextKind.ClosingComponentName, "")]
    public void SyntacticDocument_DetectsCompletionContext(
        string source,
        AkburaCompletionContextKind expectedKind,
        string expectedPrefix)
    {
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var context = document.GetCompletionContext(source.Length);

        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(context.ApplicableSpan));
    }

    [Theory]
    [InlineData("<Card Con|></Card>")]
    [InlineData("<Card Con|</Card>")]
    [InlineData("<Card Title=\"Hello\" Con|></Card>")]
    [InlineData("<Card\n    Con|></Card>")]
    public void SyntacticDocument_DetectsAttributeCompletionBeforeFollowingSyntax(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var context = document.GetCompletionContext(position);

        Assert.Equal(
            AkburaCompletionContextKind.AttributeName,
            context.Kind);
        Assert.Equal("Con", context.Prefix);
    }

    [Theory]
    [InlineData("<Button Content=${|}/>", "")]
    [InlineData("<Button Content=${Bin|}/>", "Bin")]
    [InlineData("<Button p-${Static|}/>", "Static")]
    [InlineData("<Button ${md|}:p-5/>", "md")]
    [InlineData(
        "<Button Content=${Outer Value=${Bin|}}/>",
        "Bin")]
    public void SyntacticDocument_DetectsMarkupExtensionTypeCompletion(
        string sourceWithCaret,
        string expectedPrefix)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var context = document.GetCompletionContext(position);

        Assert.Equal(
            AkburaCompletionContextKind.MarkupExtensionType,
            context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(context.ApplicableSpan));
    }

    [Fact]
    public void SyntacticDocument_DoesNotCompleteMarkupExtensionArgumentsAsTypes()
    {
        const string sourceWithCaret =
            "<Button Content=${Binding |}/>";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.NotEqual(
            AkburaCompletionContextKind.MarkupExtensionType,
            document.GetCompletionContext(position).Kind);
    }

    [Fact]
    public void SyntacticDocument_DoesNotTreatQuotedTextAsMarkupExtension()
    {
        const string sourceWithCaret =
            "<Button Content=\"${Bin|}\"/>";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.NotEqual(
            AkburaCompletionContextKind.MarkupExtensionType,
            document.GetCompletionContext(position).Kind);
    }

    [Fact]
    public void SyntacticDocument_AttributeCompletionContainsExistingAttributes()
    {
        const string source = "<Card Title=\"Hello\" Compact ";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var context = document.GetCompletionContext(source.Length);

        Assert.Equal(
            AkburaCompletionContextKind.AttributeName,
            context.Kind);
        Assert.Contains("Title", context.ExistingAttributeNames);
        Assert.Contains("Compact", context.ExistingAttributeNames);
    }

    [Theory]
    [InlineData("<Card>", "</Card>")]
    [InlineData("<Unknown>", "</Unknown>")]
    [InlineData("<Card.Title>", "</Card.Title>")]
    [InlineData("<Card/>", null)]
    [InlineData("</Card>", null)]
    [InlineData("<Card></Card>", null)]
    [InlineData("<Card Text=\"a>b\">", null)]
    [InlineData("<Card>\n<Border>", "</Border>")]
    public void SyntacticDocument_DeterminesAutoClosingTag(
        string source,
        string? expected)
    {
        var position = source.IndexOf(">", StringComparison.Ordinal) + 1;
        if (source.EndsWith(">", StringComparison.Ordinal) &&
            expected == "</Border>")
        {
            position = source.Length;
        }

        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.Equal(
            expected,
            document.GetAutoClosingTagText(position));
    }

    [Fact]
    public void SyntacticDocument_ClosesAfterQuotedGreaterThanAttribute()
    {
        const string source = "<Card Text=\"a>b\">";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.Equal(
            "</Card>",
            document.GetAutoClosingTagText(source.Length));
    }

    [Fact]
    public void SyntacticDocument_DoesNotUseSiblingEndTagForAutoClose()
    {
        const string source = """
            <StackPanel>
                <Page><Page></Page>
            </StackPanel>
            """;
        var position = source.IndexOf(
                "<Page>",
                StringComparison.Ordinal) +
            "<Page>".Length;
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.Equal(
            "</Page>",
            document.GetAutoClosingTagText(position));
    }

    [Fact]
    public void SyntacticDocument_AkcssDoesNotOfferMarkupEditing()
    {
        const string source = ".card { Value: value < Other; }";
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        Assert.True(
            document.GetCompletionContext(source.Length).IsDefault);
        Assert.Null(
            document.GetAutoClosingTagText(source.Length));
    }
}
