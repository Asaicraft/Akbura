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
    [InlineData(
        "<Button/|",
        AkburaCompletionContextKind.ComponentName,
        "Button")]
    [InlineData(
        "<Button /|",
        AkburaCompletionContextKind.AttributeName,
        "")]
    [InlineData(
        "<Button Width=\"100\"/|",
        AkburaCompletionContextKind.AttributeName,
        "")]
    [InlineData(
        "<Button Width=\"100\" /|",
        AkburaCompletionContextKind.AttributeName,
        "")]
    [InlineData(
        "<Button Wid/|",
        AkburaCompletionContextKind.AttributeName,
        "Wid")]
    public void SyntacticDocument_DetectsSlashBoundaryContext(
        string sourceWithCaret,
        AkburaCompletionContextKind expectedKind,
        string expectedPrefix)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        var context = document.GetCompletionContext(position - 1);

        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
    }

    [Theory]
    [InlineData("|", "")]
    [InlineData("sta|", "sta")]
    [InlineData("\n    st|", "st")]
    [InlineData("<Button/>\nst|", "st")]
    [InlineData(
        "state int count = 0;\n\nstat|\n\n<StackPanel/>",
        "stat")]
    [InlineData(
        "state int count = 0;\n\nstat|\n\n<StackPanel gap-3>\n<Button Click={count--}/>\n</StackPanel>",
        "stat")]
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
    [InlineData("param |", "")]
    [InlineData("param b|", "b")]
    [InlineData("param ou|", "ou")]
    [InlineData("param bind|", "bind")]
    public void SyntacticDocument_DetectsParamModifierCompletion(
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
            AkburaCompletionContextKind.DeclarationModifier,
            context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(context.ApplicableSpan));
    }

    [Theory]
    [InlineData("state |")]
    [InlineData("param |")]
    [InlineData("param bind |")]
    [InlineData("param out |")]
    [InlineData("inject |")]
    [InlineData("command |")]
    public void SyntacticDocument_DoesNotTreatDeclarationTypeAsTopLevel(
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
        Assert.True(document.TryGetCSharpCompletionContext(
            position,
            out var csharpContext));
        Assert.Equal(
            AkburaCSharpCompletionContextKind.Type,
            csharpContext.Kind);
    }

    [Theory]
    [InlineData("<Button>st|</Button>")]
    [InlineData("var st| = 0;")]
    [InlineData("var text = \"st|\";")]
    [InlineData("void Update()\n{\n    st|\n}")]
    [InlineData("// st|")]
    [InlineData("/* st| */")]
    [InlineData("using |")]
    [InlineData("using Akbura.|")]
    [InlineData("global using Akbura.|")]
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
        "state |",
        AkburaCSharpCompletionContextKind.Type,
        "")]
    [InlineData(
        "param |",
        AkburaCSharpCompletionContextKind.Type,
        "")]
    [InlineData(
        "param bind |",
        AkburaCSharpCompletionContextKind.Type,
        "")]
    [InlineData(
        "inject |",
        AkburaCSharpCompletionContextKind.Type,
        "")]
    [InlineData(
        "param ObservableCollec|",
        AkburaCSharpCompletionContextKind.Type,
        "ObservableCollec")]
    [InlineData(
        "inject IUserSer|",
        AkburaCSharpCompletionContextKind.Type,
        "IUserSer")]
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
        "using Akbura.|",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "Akbura.")]
    [InlineData(
        "using Akbura.|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "Akbura.")]
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
    [InlineData("param Ti|tle = \"\";")]
    [InlineData("param Ti|tle;")]
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

    [Theory]
    [InlineData(
        "Button.card { Width: Math.Ro|; }",
        AkburaCSharpCompletionContextKind.Expression,
        "Math.Ro")]
    [InlineData(
        "Button.card { @if(IsPointerO|) { } }",
        AkburaCSharpCompletionContextKind.Expression,
        "IsPointerO")]
    [InlineData(
        "But|.card { }",
        AkburaCSharpCompletionContextKind.Type,
        "But")]
    [InlineData(
        "(global::Gallery.Car|).card { }",
        AkburaCSharpCompletionContextKind.Type,
        "global::Gallery.Car")]
    [InlineData(
        "@utilities { StackP|.gap { } }",
        AkburaCSharpCompletionContextKind.Type,
        "StackP")]
    [InlineData(
        "@utilities { .gap-(dou| value) { } }",
        AkburaCSharpCompletionContextKind.Type,
        "dou ")]
    [InlineData(
        "@intercept MyAkcssSty|;",
        AkburaCSharpCompletionContextKind.Type,
        "MyAkcssSty")]
    [InlineData(
        "@using Avalonia.Con|;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "Avalonia.Con")]
    [InlineData(
        "Button.card { Width: |; }",
        AkburaCSharpCompletionContextKind.Expression,
        "")]
    [InlineData(
        "Button.card { @if(|) { } }",
        AkburaCSharpCompletionContextKind.Expression,
        "")]
    [InlineData(
        "@intercept |;",
        AkburaCSharpCompletionContextKind.Type,
        "")]
    [InlineData(
        "@using |;",
        AkburaCSharpCompletionContextKind.UsingDirectiveName,
        "")]
    public void SyntacticDocument_DetectsAkcssEmbeddedCSharp(
        string sourceWithCaret,
        AkburaCSharpCompletionContextKind expectedKind,
        string expectedHost)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        Assert.True(document.TryGetCSharpCompletionContext(
            position,
            out var context));
        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(expectedHost, document.Text.ToString(context.HostSpan));
    }

    [Theory]
    [InlineData("Button.card { Wid| }")]
    [InlineData(".card { Grid.Ro| }")]
    [InlineData(".card { @apply gap-| }")]
    [InlineData("@using Shared.akcss|;")]
    public void SyntacticDocument_DoesNotProjectAkcssNativeSyntax(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        Assert.False(document.TryGetCSharpCompletionContext(
            position,
            out _));
    }

    [Theory]
    [InlineData("@akcss { .card { Wid| } }")]
    [InlineData("@akcss { .card { Grid.Ro| } }")]
    [InlineData("@akcss { .card { @apply gap-| } }")]
    [InlineData("@akcss { @using Shared.akcss|; }")]
    public void SyntacticDocument_DoesNotProjectInlineAkcssNativeSyntax(
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

    [Theory]
    [InlineData(
        "<Button/|",
        ">")]
    [InlineData(
        "<StackPanel/|",
        ">")]
    [InlineData(
        "<Button /|",
        ">")]
    [InlineData(
        "<Button Width=\"100\"/|",
        ">")]
    [InlineData(
        "<Button Width=\"100\" /|",
        ">")]
    [InlineData(
        "<Button Wid/|",
        null)]
    [InlineData(
        "<Button Width=/|",
        null)]
    [InlineData(
        "<Button Text=\"value/|",
        null)]
    [InlineData(
        "<Button/|>",
        null)]
    [InlineData(
        "<Button /|>",
        null)]
    [InlineData(
        "<Button>\n    </|",
        "Button>")]
    [InlineData(
        "<StackPanel>\n" +
        "    <Button></Button>\n" +
        "    </|",
        "StackPanel>")]
    [InlineData(
        "</|",
        null)]
    [InlineData(
        "state double value = 4 /|;",
        null)]
    [InlineData(
        "<Button Click={count /| 2 == 0}>",
        null)]
    [InlineData(
        "<Button Text=\"a /|\">",
        null)]
    public void
        SyntacticDocument_DeterminesSlashCompletion(
            string sourceWithCaret,
            string? expected)
    {
        var position =
            sourceWithCaret.IndexOf(
                '|',
                StringComparison.Ordinal);
        var source =
            sourceWithCaret.Remove(
                position,
                count: 1);
        var document =
            AkburaSyntacticDocument.Parse(
                SourceText.From(source),
                "Component.akbura");

        Assert.Equal(
            expected,
            document.GetSlashCompletionText(
                position));
    }

    [Theory]
    [InlineData(
        "<Button>\n    </|",
        0)]
    [InlineData(
        "<Grid>\n    <Button>\n        </|",
        1)]
    [InlineData(
        "<Grid><Button>\n        </|",
        1)]
    public void
        SyntacticDocument_DeterminesClosingTagIndentation(
            string sourceWithCaret,
            int expected)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.Equal(
            expected,
            document.GetSlashCompletionIndentationLevel(
                position));
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

    [Theory]
    [InlineData(
        "state double value = Amx.DynamicResource<double>|;")]
    [InlineData(
        "state Dictionary<string, List<int>> value = Create<Dictionary<string, List<int>>>|();")]
    [InlineData(
        "<Button Tag={GetValue<double>|()}/>")]
    public void SyntacticDocument_DoesNotAutoCloseCSharpGeneric(
        string sourceWithCaret)
    {
        var position = sourceWithCaret.IndexOf(
            '|',
            StringComparison.Ordinal);
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Component.akbura");

        Assert.Null(document.GetAutoClosingTagText(position));
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

        Assert.Equal(
            AkcssCompletionContextKind.SelectorSnippet,
            document.GetAkcssCompletionContext(source.Length).Kind);
        Assert.Null(
            document.GetAutoClosingTagText(source.Length));
    }

    [Theory]
    [InlineData(
        ".|",
        AkcssCompletionContextKind.SelectorSnippet,
        ".",
        "")]
    [InlineData(
        ".cl|",
        AkcssCompletionContextKind.SelectorSnippet,
        ".cl",
        "")]
    [InlineData(
        "@u|",
        AkcssCompletionContextKind.TopLevel,
        "@u",
        "")]
    [InlineData(
        "@using Gallery.St|;",
        AkcssCompletionContextKind.AkcssModuleName,
        "Gallery.St",
        "")]
    [InlineData(
        ".card {\n    Wid|\n}",
        AkcssCompletionContextKind.PropertyName,
        "Wid",
        "")]
    [InlineData(
        ".card {\n    Grid.Ro|\n}",
        AkcssCompletionContextKind.PropertyName,
        "Ro",
        "Grid")]
    [InlineData(
        ".card {\n    global::Grid.Ro|\n}",
        AkcssCompletionContextKind.PropertyName,
        "Ro",
        "global::Grid")]
    [InlineData(
        ".card {\n    Width: tr|;\n}",
        AkcssCompletionContextKind.PropertyValue,
        "tr",
        "")]
    [InlineData(
        ".card {\n    @apply ga|;\n}",
        AkcssCompletionContextKind.ApplyItem,
        "ga",
        "")]
    [InlineData(
        ".card {\n    @a|\n}",
        AkcssCompletionContextKind.BodyMember,
        "@a",
        "")]
    [InlineData(
        ".card {\n    |\n}",
        AkcssCompletionContextKind.BodyMember,
        "",
        "")]
    [InlineData(
        "Con|trol.card {\n}",
        AkcssCompletionContextKind.SelectorSnippet,
        "Con",
        "")]
    [InlineData(
        "@utilities {\n" +
        "    Control.gap-(double value) {\n" +
        "        @if(value > 0) {\n" +
        "            Wid|\n" +
        "        }\n" +
        "    }\n" +
        "}",
        AkcssCompletionContextKind.PropertyName,
        "Wid",
        "")]
    public void SyntacticDocument_DetectsAkcssCompletionContext(
        string sourceWithCaret,
        AkcssCompletionContextKind expectedKind,
        string expectedPrefix,
        string expectedQualifier)
    {
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        var context = document.GetAkcssCompletionContext(position);

        Assert.Equal(expectedKind, context.Kind);
        Assert.Equal(expectedPrefix, context.Prefix);
        Assert.Equal(expectedQualifier, context.Qualifier);
        Assert.Equal(
            expectedPrefix,
            document.Text.ToString(context.ApplicableSpan));

        const string inlinePrefix = "@akcss {\n";
        var inlineSourceWithCaret =
            inlinePrefix +
            sourceWithCaret +
            "\n}\n\n<Border/>";
        var inlinePosition = inlineSourceWithCaret.IndexOf('|');
        var inlineSource = inlineSourceWithCaret.Remove(
            inlinePosition,
            1);
        var inlineDocument = AkburaSyntacticDocument.Parse(
            SourceText.From(inlineSource),
            "Component.akbura");
        var inlineContext = inlineDocument
            .GetAkcssCompletionContext(inlinePosition);

        Assert.Equal(expectedKind, inlineContext.Kind);
        Assert.Equal(expectedPrefix, inlineContext.Prefix);
        Assert.Equal(expectedQualifier, inlineContext.Qualifier);
        Assert.Equal(
            expectedPrefix,
            inlineDocument.Text.ToString(
                inlineContext.ApplicableSpan));
    }

    [Fact]
    public void UsingEditService_InsertsInlineImportBeforeAkcssModules()
    {
        const string source =
            "@akcss {\r\n" +
            "    @using Imported.akcss;\r\n" +
            "\r\n" +
            "    Control.card { }\r\n" +
            "}\r\n" +
            "\r\n" +
            "<Border/>\r\n";
        var position = source.IndexOf(
            "Control",
            StringComparison.Ordinal);
        var text = SourceText.From(source);
        var document = AkburaSyntacticDocument.Parse(
            text,
            "Component.akbura");

        Assert.True(
            AkburaUsingEditService.TryCreateNamespaceImportChange(
                text,
                document.SyntaxTree,
                "Avalonia.Media",
                position,
                out var change));

        var changedText = text
            .WithChanges(change)
            .ToString();
        var inlineBlockIndex = changedText.IndexOf(
            "@akcss {",
            StringComparison.Ordinal);
        var namespaceImportIndex = changedText.IndexOf(
            "    @using Avalonia.Media;",
            StringComparison.Ordinal);
        var moduleImportIndex = changedText.IndexOf(
            "    @using Imported.akcss;",
            StringComparison.Ordinal);
        Assert.True(
            inlineBlockIndex >= 0 &&
            namespaceImportIndex > inlineBlockIndex &&
            moduleImportIndex > namespaceImportIndex,
            changedText);
        Assert.DoesNotContain(
            "\r\nusing Avalonia.Media;",
            changedText,
            StringComparison.Ordinal);
    }
    [Fact]
    public void SyntacticDocument_DoesNotOfferAkcssCompletionInsideComment()
    {
        const string sourceWithCaret =
            ".card {\n    // Wid|\n}";
        var position = sourceWithCaret.IndexOf('|');
        var source = sourceWithCaret.Remove(position, 1);
        var document = AkburaSyntacticDocument.Parse(
            SourceText.From(source),
            "Styles.akcss");

        Assert.True(
            document.GetAkcssCompletionContext(position).IsDefault);

        const string inlinePrefix = "@akcss {\n";
        var inlineSourceWithCaret =
            inlinePrefix +
            sourceWithCaret +
            "\n}\n\n<Border/>";
        var inlinePosition = inlineSourceWithCaret.IndexOf('|');
        var inlineSource = inlineSourceWithCaret.Remove(
            inlinePosition,
            1);
        var inlineDocument = AkburaSyntacticDocument.Parse(
            SourceText.From(inlineSource),
            "Component.akbura");

        Assert.True(
            inlineDocument.GetAkcssCompletionContext(
                inlinePosition).IsDefault);
    }
}
