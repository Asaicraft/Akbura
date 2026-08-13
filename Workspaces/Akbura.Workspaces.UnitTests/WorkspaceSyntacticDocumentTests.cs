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
    [InlineData("|", "")]
    [InlineData("sta|", "sta")]
    [InlineData("\n    st|", "st")]
    [InlineData("<Button/>\nst|", "st")]
    public void SyntacticDocument_DetectsTopLevelKeywordCompletion(
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
            AkburaCompletionContextKind.TopLevel,
            context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(context.ApplicableSpan));
    }

    [Theory]
    [InlineData("<Button>st|</Button>")]
    [InlineData("var st| = 0;")]
    [InlineData("void Update()\n{\n    st|\n}")]
    [InlineData("// st|")]
    [InlineData("/* st| */")]
    public void SyntacticDocument_DoesNotCompleteStateOutsideTopLevelStart(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.NotEqual(
            AkburaCompletionContextKind.TopLevel,
            document.GetCompletionContext(position).Kind);
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

    [Theory]
    [InlineData(
        "<Button Content={ViewModel.Us|}/>",
        "ViewModel.Us")]
    [InlineData(
        "<Button Content={ViewModel.|}/>",
        "ViewModel.")]
    [InlineData(
        "<TextBlock Text={$\"Count {count.ToStr|}\"}/>",
        "$\"Count {count.ToStr}\"")]
    [InlineData(
        "<Button IsVisible={|true}/>",
        "true")]
    [InlineData(
        "<Button IsVisible={true|}/>",
        "true")]
    public void SyntacticDocument_DetectsCSharpExpressionCompletion(
        string sourceWithCaret,
        string expectedExpression)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var result = document.TryGetCSharpCompletionContext(
            position,
            out var context);

        Assert.True(result);
        Assert.Equal(
            AkburaCSharpCompletionContextKind.Expression,
            context.Kind);
        Assert.Equal(position, context.HostPosition);
        Assert.Equal(
            position - context.HostSpan.Start,
            context.RelativePosition);
        Assert.Equal(
            expectedExpression,
            document.Text.ToString(context.HostSpan));
    }

    [Theory]
    [InlineData(
        "state int maximum = Math.M|;",
        AkburaCSharpCompletionContextKind.Expression,
        "Math.M")]
    [InlineData(
        "param string Title = string.Em|;",
        AkburaCSharpCompletionContextKind.Expression,
        "string.Em")]
    [InlineData(
        "state int count = |;",
        AkburaCSharpCompletionContextKind.Expression,
        "")]
    [InlineData(
        "param string Title = |;",
        AkburaCSharpCompletionContextKind.Expression,
        "")]
    [InlineData(
        "var text = Title.ToUpp|;",
        AkburaCSharpCompletionContextKind.Statement,
        "var text = Title.ToUpp;")]
    [InlineData(
        "void Update(int value)\n{\n    var result = value.ToStr|;\n}",
        AkburaCSharpCompletionContextKind.Statement,
        "    var result = value.ToStr;\n")]
    [InlineData(
        "void Update(int value)\n{\n    if (value > Math.A|)\n    {\n    }\n}",
        AkburaCSharpCompletionContextKind.Statement,
        "    if (value > Math.A)\n    {\n    }\n")]
    [InlineData(
        "param List<UserMo|> Items;",
        AkburaCSharpCompletionContextKind.Type,
        "List<UserMo> ")]
    [InlineData(
        "inject ILogger<Count|> logger;",
        AkburaCSharpCompletionContextKind.Type,
        "ILogger<Count> ")]
    [InlineData(
        "state UserMo| model = new();",
        AkburaCSharpCompletionContextKind.Type,
        "UserMo ")]
    [InlineData(
        "command ValueTa| Save();",
        AkburaCSharpCompletionContextKind.Type,
        "ValueTa ")]
    [InlineData(
        "using System.Collections.Gen|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System.Collections.Gen")]
    [InlineData(
        "using Alias = System.Collections.Gen|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System.Collections.Gen")]
    [InlineData(
        "using static System.Ma|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System.Ma")]
    [InlineData(
        "global using System.Collections.Gen|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System.Collections.Gen")]
    [InlineData(
        "using unsafe Alias = System.IntP|*;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "System.IntP*")]
    [InlineData(
        "command void Save(UserMo| model);",
        AkburaCSharpCompletionContextKind.CommandParameterList,
        "(UserMo model)")]
    public void SyntacticDocument_DetectsEmbeddedCSharpCompletionContexts(
        string sourceWithCaret,
        AkburaCSharpCompletionContextKind expectedKind,
        string expectedHost)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var result = document.TryGetCSharpCompletionContext(
            position,
            out var context);

        Assert.True(result);
        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(position, context.HostPosition);
        Assert.Equal(
            position - context.HostSpan.Start,
            context.RelativePosition);
        Assert.Equal(
            expectedHost,
            document.Text.ToString(context.HostSpan));
    }

    [Theory]
    [InlineData("state co|unt = 0;")]
    [InlineData("<Button Text=\"DateTime.No|\"/>")]
    [InlineData("<Button Content=${Binding Path=Us|}/>")]
    [InlineData("<But|")]
    [InlineData("using Al| = System.Collections.Generic;")]
    public void SyntacticDocument_DoesNotProjectNonCSharpPositions(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.False(document.TryGetCSharpCompletionContext(
            position,
            out _));
    }

    [Theory]
    [InlineData("<Button Content=|{ViewModel.User}/>")]
    [InlineData("<Button Content={ViewModel.User}|/>")]
    [InlineData("<Button Content=\"ViewModel.Us|er\"/>")]
    public void SyntacticDocument_DoesNotProjectOutsideCSharpExpressions(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.False(document.TryGetCSharpCompletionContext(
            position,
            out _));
    }

    [Fact]
    public void SyntacticDocument_AkcssDoesNotProjectEmbeddedCSharp()
    {
        const string sourceWithCaret = """
            @utilities {
                Control.w-(double value) {
                    Width: Math.M|;
                }
            }
            """;
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        Assert.False(document.TryGetCSharpCompletionContext(
            position,
            out _));
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
