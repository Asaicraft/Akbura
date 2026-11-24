using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using static Akbura.UnitTests.AkcssThemeParserTests;

namespace Akbura.UnitTests;

public sealed class AkcssThemeIncrementalParserTests
{
    private const string Fixture = """
        @mixin Button Helpers(string suffix = "Default") {
            Opacity: 1d;
        }
        @theme Button ThemeName {
            @based-on Hello.BaseTheme;
            @template {
                <ContentPresenter Name="PART_Initial">
                    before @template-bind Background, Content; after
                    <Border Width="40" />
                    @include Helpers("First");
                    <TextBlock />
                </ContentPresenter>
            }
            ^:pointerover /template/ ContentPresenter#PART_Initial {
                @include Helpers("State");
            }
            Width: 12d;
        }
        @theme Slider Following { Height: 8d; }
        """;

    public static IEnumerable<object[]> NarrowEdits()
    {
        foreach (var inline in new[] { false, true })
        {
            yield return new object[] { inline, "ThemeName", "NewTheme", 1, false };
            yield return new object[] { inline, "Button ThemeName", "global::Controls.Button ThemeName", 1, false };
            yield return new object[] { inline, "\"Default\"", "DefaultFor<First, Second>()", 0, false };
            yield return new object[] { inline, "\"First\"", "Build<First, Second>()", 1, true };
            yield return new object[] { inline, "Background, Content", "Foreground, Content", 1, true };
            yield return new object[] { inline, "Name=\"PART_Initial\"", "Name=\"PART_Updated\"", 1, true };
            yield return new object[] { inline, "Width=\"40\"", "Width=\"64\"", 1, false };
            yield return new object[] { inline, "pointerover", "focus-visible", 1, false };
            yield return new object[] { inline, "Hello.BaseTheme", "global::Hello.OtherTheme", 1, false };
        }
    }

    [Theory]
    [MemberData(nameof(NarrowEdits))]
    public void NarrowEditsReuseSiblingsInDirectAndPublicIncrementalParsers(
        bool inline, string original, string replacement, int editedMember, bool preservesBorder)
    {
        var source = inline ? WrapInline(Fixture) : Fixture;
        var (old, direct, actual) = ApplyEdits(source, inline, Replacement(source, original, replacement));
        Assert.False(direct.ContainsDiagnostics);
        Assert.False(actual.ContainsDiagnostics);
        var previousMembers = Members(old);
        var directMembers = Members(direct);
        var updatedMembers = Members(actual);
        Assert.Equal(previousMembers.Count, updatedMembers.Count);

        for (var index = 0; index < previousMembers.Count; index++)
        {
            if (index == editedMember)
            {
                Assert.NotSame(previousMembers[index].Green, directMembers[index].Green);
                Assert.NotSame(previousMembers[index].Green, updatedMembers[index].Green);
            }
            else
            {
                Assert.Same(previousMembers[index].Green, directMembers[index].Green);
                Assert.Same(previousMembers[index].Green, updatedMembers[index].Green);
            }
        }

        if (preservesBorder)
        {
            var previousBorder = FindMarkupElement(old, "Border");
            Assert.Same(previousBorder.Green, FindMarkupElement(direct, "Border").Green);
            Assert.Same(previousBorder.Green, FindMarkupElement(actual, "Border").Green);
        }
    }

    [Theory]
    [InlineData("@includeSomething Helpers();", "@include Helpers();", true, false)]
    [InlineData("@includ Helpers();", "@include Helpers();", true, false)]
    [InlineData("@include Helpers();", "@includeSomething Helpers();", false, false)]
    [InlineData("@template-binder Background;", "@template-bind Background;", false, true)]
    [InlineData("@template-bind Background;", "@template-binder Background;", false, false)]
    public void TemplateTextIsReclassifiedBeforeReusingOldMarkup(
        string original, string replacement, bool include, bool templateBind)
    {
        var source = "@theme Button Theme { @template { <Grid>before " + original +
            " after <Border/></Grid> } }\r\n@theme Button Following { }";
        var (old, direct, actual) = ApplyEdits(source, inline: false, Replacement(source, original, replacement));
        Assert.False(direct.ContainsDiagnostics);
        Assert.Equal(include ? 1 : 0, actual.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>().Count());
        Assert.Equal(templateBind ? 1 : 0, actual.DescendantNodes().OfType<MarkupTemplateBindDirectiveSyntax>().Count());
        Assert.Same(Members(old)[1].Green, Members(direct)[1].Green);
        Assert.Same(FindMarkupElement(old, "Border").Green, FindMarkupElement(direct, "Border").Green);
    }

    [Fact]
    public void IntroducingAndRemovingTemplateChangesMarkupContext()
    {
        const string prefix = "@akcss { @theme Button T { @template { ";
        const string suffix = " } } }\n";
        const string panel = "<Grid>before @include Helpers(); after</Grid>\n";
        const string following = "<Border />\n";
        var plain = panel + following;
        var (_, _, inserted) = ApplyEdits(plain, inline: true,
            new TextChange(new TextSpan(0, 0), prefix),
            new TextChange(new TextSpan(panel.Length, 0), suffix));
        Assert.False(inserted.ContainsDiagnostics);
        Assert.Single(inserted.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());

        var wrapped = prefix + panel + suffix + following;
        var (_, removed, actual) = ApplyEdits(wrapped, inline: true,
            new TextChange(new TextSpan(0, prefix.Length), ""),
            new TextChange(new TextSpan(prefix.Length + panel.Length, suffix.Length), ""));
        Assert.False(actual.ContainsDiagnostics);
        Assert.Empty(removed.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());
        Assert.Single(removed.DescendantNodes().OfType<MarkupTextLiteralSyntax>());
    }

    [Theory]
    [InlineData("^:pointerover", "&:pointerover")]
    [InlineData("/template/", "template/")]
    [InlineData("/template/", "/template")]
    [InlineData("Helpers(\"First\");", "Helpers(\"First\";")]
    [InlineData("</ContentPresenter>", "")]
    [InlineData("</ContentPresenter>\n    }", "</ContentPresenter>")]
    public void BoundaryEditsRecoverAndCanBeCompletedAgain(string original, string malformed)
    {
        var fixture = Fixture.ReplaceLineEndings("\n");
        var (_, direct, actual) = ApplyEdits(fixture, inline: false, Replacement(fixture, original, malformed));
        Assert.True(direct.ContainsDiagnostics);
        Assert.True(actual.ContainsDiagnostics);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(Members(actual)[1]);
        Assert.Single(theme.Members.OfType<AkcssNestedStyleRuleSyntax>());
        Assert.Equal("Following",
            Assert.IsType<AkcssThemeDeclarationSyntax>(Members(actual).Last()).Name.Identifier.ValueText);

        var broken = actual.ToFullString();
        var start = fixture.IndexOf(original, StringComparison.Ordinal);
        var (_, restoredDirect, restored) = ApplyEdits(broken, inline: false,
            new TextChange(new TextSpan(start, malformed.Length), original));
        Assert.False(restoredDirect.ContainsDiagnostics);
        Assert.False(restored.ContainsDiagnostics);
        AssertExactSource(fixture, restored);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EofTriviaChangesRetainUnchangedDeclarations(bool inline)
    {
        var source = inline ? WrapInline(Fixture) : Fixture;
        var (old, direct, actual) = ApplyEdits(source, inline,
            new TextChange(new TextSpan(source.Length, 0), "\r\n\t// eof\r\n"));
        Assert.False(actual.ContainsDiagnostics);
        Assert.Same(Members(old)[0].Green, Members(direct)[0].Green);
        Assert.Same(Members(old)[0].Green, Members(actual)[0].Green);
    }

    public static IEnumerable<object[]> SelectorEdits()
    {
        (string Before, string After, bool Broken)[] pairs =
        {
            ("^:pointerover", "^ :pointerover", false),
            ("ContentPresenter#PART_Initial", "ContentPresenter #PART_Initial", false),
            ("Button.primary", "Button .primary", false),
            (" /template/ ContentPresenter", " ContentPresenter", false),
            ("Border TextBlock", "Border > TextBlock", false),
            ("<First, Second>", "<First, Third>", false),
            ("^:pointerover", "&:pointerover", true),
            ("Button.primary", "Button/* with spaces */.primary", false),
            ("Button.primary", "Button/* with spaces */ .primary", false),
            ("Second>) > TextBlock", "Second> > TextBlock", true),
            ("Grid > TextBlock { }", "Grid > TextBlock ;", true),
            ("</ContentPresenter>\n    }", "</ContentPresenter>", true),
        };

        foreach (var inline in new[] { false, true })
        {
            foreach (var (before, after, broken) in pairs)
            {
                yield return new object[] { inline, before, before, after, broken };
                yield return new object[] { inline, before, after, before, false };
            }
        }
    }

    [Theory]
    [MemberData(nameof(SelectorEdits))]
    public void SelectorEditsMatchFreshParseAndReuseUnchangedNeighbors(
        bool inline, string fixtureText, string before, string after, bool hasDiagnostics)
    {
        var body = Fixture + "\n\nButton.primary { }\n\nBorder TextBlock { }\n\n" +
            "(global::Demo.Border<First, Second>) > TextBlock { }\n\n" +
            "Grid > TextBlock { }\n\n.following { Width: 8d; }";
        var source = (inline ? WrapInline(body) : body).ReplaceLineEndings("\n");
        if (fixtureText != before)
        {
            var start = source.IndexOf(fixtureText, StringComparison.Ordinal);
            Assert.True(start >= 0);
            source = source.Remove(start, fixtureText.Length).Insert(start, before);
        }

        var edit = NarrowReplacement(source, before, after);
        var (old, direct, actual) = ApplyEdits(source, inline, edit);
        Assert.Equal(hasDiagnostics, direct.ContainsDiagnostics);
        Assert.Equal(hasDiagnostics, actual.ContainsDiagnostics);
        Assert.Same(Members(old)[0].Green, Members(direct)[0].Green);
        Assert.Same(Members(old).Last().Green, Members(direct).Last().Green);

        // The standalone tree API reparses when either tree has diagnostics.
        if (inline || !old.ContainsDiagnostics && !hasDiagnostics)
        {
            Assert.Same(Members(old)[0].Green, Members(actual)[0].Green);
            Assert.Same(Members(old).Last().Green, Members(actual).Last().Green);
        }
    }

    [Theory]
    [InlineData(false, "@theme global::Demo.Controls.Button FollowingTheme { }")]
    [InlineData(true, "@theme global::Demo.Controls.Button FollowingTheme { }")]
    [InlineData(false, "(global::Avalonia.Controls.Border) TextBlock { FontSize: 14d; }")]
    [InlineData(true, "(global::Avalonia.Controls.Border) TextBlock { FontSize: 14d; }")]
    public void TemplateDelimiterEditsPreserveTheFollowingDeclaration(bool inline, string following)
    {
        const string braces = "    }\n}\n";
        var body = "@theme Button T {\n    @template {\n        <Border />\n" + braces + following +
            "\n\n@mixin Button Unchanged() { }";
        var source = inline ? WrapInline(body) : body;
        var (old, direct, broken) = ApplyEdits(source, inline, Replacement(source, braces, ""));
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(Members(broken)[0]);
        Assert.True(Assert.IsType<AkcssTemplateDirectiveSyntax>(Assert.Single(theme.Members)).Body.CloseBraceToken.IsMissing);
        Assert.False(Members(broken)[1].ContainsDiagnostics);
        Assert.Equal(following, Members(broken)[1].ToFullString().Trim());
        Assert.Same(Members(old).Last().Green, Members(direct).Last().Green);
        if (inline)
        {
            Assert.Same(Members(old).Last().Green, Members(broken).Last().Green);
        }
        var start = source.IndexOf(braces, StringComparison.Ordinal);
        var (_, _, restored) = ApplyEdits(broken.ToFullString(), inline, new TextChange(new TextSpan(start, 0), braces));
        Assert.False(restored.ContainsDiagnostics);
        AssertExactSource(source, restored);
    }

    private static TextChange NarrowReplacement(string source, string original, string replacement)
    {
        var edit = Replacement(source, original, replacement);
        var start = 0;
        var oldEnd = original.Length;
        var newEnd = replacement.Length;
        while (start < oldEnd && start < newEnd && original[start] == replacement[start])
        {
            start++;
        }

        while (start < oldEnd && start < newEnd && original[oldEnd - 1] == replacement[newEnd - 1])
        {
            oldEnd--;
            newEnd--;
        }

        return new TextChange(new TextSpan(edit.Span.Start + start, oldEnd - start), replacement[start..newEnd]);
    }

    private static MarkupElementSyntax FindMarkupElement(AkburaSyntax root, string name) =>
        root.DescendantNodes().OfType<MarkupElementSyntax>()
            .Single(element => element.StartTag?.Name.ToFullString().Trim() == name);

    private static TextChange Replacement(string source, string original, string replacement)
    {
        var start = source.IndexOf(original, StringComparison.Ordinal);
        Assert.True(start >= 0);
        return new TextChange(new TextSpan(start, original.Length), replacement);
    }

    private static (AkburaSyntax Old, AkburaSyntax Direct, AkburaSyntax Public)
        ApplyEdits(string source, bool inline, params TextChange[] edits)
    {
        var oldText = SourceText.From(source);
        var newText = oldText.WithChanges(edits);
        var ranges = newText.GetChangeRanges(oldText);
        var newSource = newText.ToString();
        var expected = Parse(newSource, inline);

        AkburaSyntax old;
        AkburaSyntax actual;
        if (inline)
        {
            var tree = ComponentSyntaxTree.ParseText(oldText);
            old = tree.GetRoot();
            actual = tree.WithChangedText(newText, ranges).GetRoot();
        }
        else
        {
            var tree = AkcssSyntaxTree.ParseText(oldText);
            old = tree.GetRoot();
            actual = tree.WithChangedText(newText, ranges).GetRoot();
        }

        using var lexer = new Lexer(newText);
        using var parser = new Parser(lexer, default, old, ranges);
        var direct = inline
            ? parser.ParseCompilationUnit().CreateRed()
            : parser.ParseAkcssDocumentSyntax().CreateRed();

        AssertEquivalent(expected, direct, newSource);
        AssertEquivalent(expected, actual, newSource);
        return (old, direct, actual);
    }

    private static void AssertEquivalent(AkburaSyntax expected, AkburaSyntax actual, string source)
    {
        AssertExactSource(source, actual);
        var expectedParts = expected.DescendantNodesAndTokensAndSelf().ToArray();
        var actualParts = actual.DescendantNodesAndTokensAndSelf().ToArray();
        Assert.Equal(expectedParts.Select(part => (part.IsNode, part.Kind, part.FullSpan, part.Span)),
            actualParts.Select(part => (part.IsNode, part.Kind, part.FullSpan, part.Span)));
        Assert.Equal(expected.DescendantTokens().Select(DescribeToken), actual.DescendantTokens().Select(DescribeToken));
        Assert.Equal(DescribeDiagnostics(expectedParts), DescribeDiagnostics(actualParts));
    }

    private static (SyntaxKind Kind, string Text, string FullText, bool IsMissing) DescribeToken(SyntaxToken token) =>
        (token.Kind, token.Text, token.ToFullString(), token.IsMissing);

    private static IEnumerable<(string Code, int Position, int Width, string Arguments)> DescribeDiagnostics(
        IEnumerable<SyntaxNodeOrToken> parts) => parts.SelectMany(part => part.GetDiagnostics().Select(diagnostic =>
        {
            var syntax = diagnostic as SyntaxDiagnosticInfo;
            return (diagnostic.Code, part.FullSpan.Start + (syntax?.Position ?? 0),
                syntax?.Width ?? 0, string.Join("|", diagnostic.Parameters));
        }));
}
