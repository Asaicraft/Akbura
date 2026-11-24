using Akbura.Language;
using Akbura.Language.Syntax;
using static Akbura.UnitTests.AkcssThemeParserTests;

namespace Akbura.UnitTests;

public sealed class AkcssSelectorParserTests
{
    private const string Selectors = """
        @utilities
        {
            Button.bg-red-300
            {
                ^ /template/ (global::Avalonia.Controls.Presenters.ContentPresenter)
                {
                    Background: Amx.DynamicResource<IBrush>("--color-red-300");
                }
            }
        }

        (global::Avalonia.Controls.Border) TextBlock
        {
            FontSize: 14d;
        }

        Border > TextBlock
        {
            FontSize: 16d;
        }

        Border.card
        {
            ^ TextBlock
            {
                FontSize: 14d;
            }

            ^.selected
            {
                Opacity: 1d;
            }
        }

        @mixin Button PresenterBackground(string resourceKey)
        {
            ^ /template/ ContentPresenter#PART_ContentPresenter
            {
                Background: Amx.DynamicResource<IBrush>(resourceKey);
            }
        }

        @theme Button MyTheme
        {
            @template
            {
                <ContentPresenter x.Name="PART_ContentPresenter">
                    @template-bind Background, BorderBrush, Padding, Content;
                </ContentPresenter>
            }

            ^:pointerover /template/ ContentPresenter#PART_ContentPresenter
            {
                Background: Amx.DynamicResource<IBrush>(
                    "ButtonBackgroundPointerOver");
            }
        }
        """;

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\r\n")]
    public void SelectorExamplesRoundTripInStandaloneAndInlineDocuments(bool inline, string newline)
    {
        var source = (inline ? WrapInline(Selectors) : Selectors).ReplaceLineEndings(newline);
        var root = Parse(source, inline);
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);

        var members = Members(root);
        Assert.Equal(6, members.Count);
        var utility = Assert.Single(Assert.IsType<AkcssUtilitiesSectionSyntax>(members[0]).Utilities);
        Assert.Equal("Button", utility.Selector.TargetType!.ToCSharp().ToString());
        Assert.Equal("bg-red-300", utility.Selector.Name.Identifier.ValueText);
        var traversal = Assert.IsType<AkcssTemplateTraversalSyntax>(Assert.Single(SingleNested(utility.Members).Selector.Steps));
        var type = Assert.IsType<AkcssTypeSelectorSyntax>(Assert.Single(traversal.Target.Components));
        var parenthesized = Assert.IsType<AkcssParenthesizedSelectorTypeSyntax>(type.Type);
        Assert.Equal("global::Avalonia.Controls.Presenters.ContentPresenter", parenthesized.Type.ToFullString());

        var descendant = Assert.IsType<AkcssSelectorRuleSyntax>(members[1]);
        var initial = Assert.IsType<AkcssCompoundSelectorSyntax>(descendant.Selector.Initial);
        var typedInitial = Assert.IsType<AkcssTypeSelectorSyntax>(Assert.Single(initial.Components));
        Assert.Equal("(global::Avalonia.Controls.Border)", typedInitial.Type.ToFullString().Trim());
        Assert.IsType<AkcssDescendantSelectorSyntax>(Assert.Single(descendant.Selector.Steps));

        var child = Assert.IsType<AkcssSelectorRuleSyntax>(members[2]);
        Assert.Equal(SyntaxKind.GreaterThanToken,
            Assert.IsType<AkcssChildSelectorSyntax>(Assert.Single(child.Selector.Steps)).GreaterThanToken.Kind);

        var card = Assert.IsType<AkcssStyleRuleSyntax>(members[3]);
        Assert.Equal("card", card.Selector.Name!.Identifier.ValueText);
        Assert.IsType<AkcssDescendantSelectorSyntax>(
            Assert.Single(Assert.IsType<AkcssNestedStyleRuleSyntax>(card.Members[0]).Selector.Steps));
        var selected = Assert.IsType<AkcssNestedStyleRuleSyntax>(card.Members[1]);
        var anchor = Assert.IsType<AkcssParentCompoundSelectorSyntax>(selected.Selector.Initial);
        Assert.Equal("selected", Assert.IsType<AkcssClassSelectorSyntax>(Assert.Single(anchor.Components)).Name.Identifier.ValueText);
        Assert.Empty(selected.Selector.Steps);

        var mixin = Assert.IsType<AkcssMixinDeclarationSyntax>(members[4]);
        var mixinTraversal = Assert.IsType<AkcssTemplateTraversalSyntax>(Assert.Single(SingleNested(mixin.Members).Selector.Steps));
        Assert.Equal("PART_ContentPresenter",
            Assert.IsType<AkcssNameSelectorSyntax>(mixinTraversal.Target.Components[1]).Name.Identifier.ValueText);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(members[5]);
        Assert.IsType<AkcssTemplateDirectiveSyntax>(theme.Members[0]);
        Assert.IsType<AkcssNestedStyleRuleSyntax>(theme.Members[1]);
    }

    [Theory]
    [InlineData("^", 0, null)]
    [InlineData("^:pointerover", 1, null)]
    [InlineData("^.accent", 1, null)]
    [InlineData("^#NamedControl", 1, null)]
    [InlineData("^ :pointerover", 0, nameof(SyntaxKind.AkcssDescendantSelectorSyntax))]
    [InlineData("^ .accent", 0, nameof(SyntaxKind.AkcssDescendantSelectorSyntax))]
    [InlineData("^ TextBlock", 0, nameof(SyntaxKind.AkcssDescendantSelectorSyntax))]
    [InlineData("^ > TextBlock", 0, nameof(SyntaxKind.AkcssChildSelectorSyntax))]
    [InlineData("^ /template/ ContentPresenter", 0, nameof(SyntaxKind.AkcssTemplateTraversalSyntax))]
    public void RelativeAnchorAndAdjacentSuffixesHaveDistinctStructure(string header, int suffixes, string? stepKind)
    {
        var source = ".parent { " + header + " { } }";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var selector = SingleNested(Assert.IsType<AkcssStyleRuleSyntax>(Assert.Single(root.Members)).Members).Selector;
        var anchor = Assert.IsType<AkcssParentCompoundSelectorSyntax>(selector.Initial);
        Assert.Equal(SyntaxKind.CaretToken, anchor.Anchor.CaretToken.Kind);
        Assert.False(anchor.Anchor.CaretToken.IsMissing);
        Assert.Equal(suffixes, anchor.Components.Count);
        if (stepKind == null)
        {
            Assert.Empty(selector.Steps);
        }
        else
        {
            Assert.Equal(stepKind, Assert.Single(selector.Steps).Kind.ToString());
        }
    }

    [Theory]
    [InlineData("Button.primary", false)]
    [InlineData("Button .primary", true)]
    [InlineData("Button/* comment */.primary", false)]
    [InlineData("Button/* line\n break */.primary", false)]
    [InlineData("Button /* comment */ .primary", true)]
    [InlineData("Button/* comment */ .primary", true)]
    [InlineData("Button\t.primary", true)]
    [InlineData("Button\n.primary", true)]
    [InlineData("ContentPresenter#Part", true)]
    [InlineData("ContentPresenter #Part", true)]
    public void OnlyWhitespaceOutsideCommentsSeparatesCompounds(string header, bool general)
    {
        var source = header + "\n{ }";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var rule = Assert.Single(root.Members);
        if (!general)
        {
            var named = Assert.IsType<AkcssStyleRuleSyntax>(rule);
            Assert.Equal("primary", named.Selector.Name!.Identifier.ValueText);
            Assert.Equal("Button", named.Selector.TargetType!.ToCSharp().ToString());
            return;
        }

        var selector = Assert.IsType<AkcssSelectorRuleSyntax>(rule).Selector;
        var initial = Assert.IsType<AkcssCompoundSelectorSyntax>(selector.Initial);
        if (header == "ContentPresenter#Part")
        {
            Assert.Empty(selector.Steps);
            Assert.IsType<AkcssNameSelectorSyntax>(initial.Components[1]);
            return;
        }

        Assert.Single(initial.Components);
        var descendant = Assert.IsType<AkcssDescendantSelectorSyntax>(Assert.Single(selector.Steps));
        Assert.Single(descendant.Target.Components);
        Assert.Equal(initial.FullSpan.End, descendant.FullSpan.Start);
    }

    [Fact]
    public void AbsoluteSelectorChainsRetainComponentAndStepOrder()
    {
        const string source = """
            #PART_ContentPresenter { }
            :focus-visible { }
            Button.accent.large { }
            Button.accent:pointerover { }
            Border.card Button:pointerover /template/ ContentPresenter > .selected /template/ #Part { }
            (global::Demo.Controls.Container<First, Second>).card TextBlock { }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var rules = root.Members.Select(member => Assert.IsType<AkcssSelectorRuleSyntax>(member)).ToArray();
        Assert.IsType<AkcssNameSelectorSyntax>(Assert.Single(Initial(rules[0]).Components));
        Assert.Equal("focus-visible",
            Assert.IsType<AkcssPseudoClassSelectorSyntax>(Assert.Single(Initial(rules[1]).Components)).Name.Identifier.ValueText);
        Assert.Equal(3, Initial(rules[2]).Components.Count);
        Assert.IsType<AkcssPseudoClassSelectorSyntax>(Initial(rules[3]).Components[2]);
        var mixed = rules[4].Selector;
        Assert.Equal(4, mixed.Steps.Count);
        var descendant = Assert.IsType<AkcssDescendantSelectorSyntax>(mixed.Steps[0]);
        Assert.Equal("pointerover",
            Assert.IsType<AkcssPseudoClassSelectorSyntax>(descendant.Target.Components[1]).Name.Identifier.ValueText);
        Assert.IsType<AkcssTemplateTraversalSyntax>(mixed.Steps[1]);
        Assert.IsType<AkcssChildSelectorSyntax>(mixed.Steps[2]);
        Assert.IsType<AkcssTemplateTraversalSyntax>(mixed.Steps[3]);
        var qualified = Assert.IsType<AkcssTypeSelectorSyntax>(Initial(rules[5]).Components[0]);
        Assert.Equal("global::Demo.Controls.Container<First, Second>",
            Assert.IsType<AkcssParenthesizedSelectorTypeSyntax>(qualified.Type).Type.ToCSharp().ToString());
        Assert.IsType<AkcssDescendantSelectorSyntax>(Assert.Single(rules[5].Selector.Steps));

        static AkcssCompoundSelectorSyntax Initial(AkcssSelectorRuleSyntax rule) =>
            Assert.IsType<AkcssCompoundSelectorSyntax>(rule.Selector.Initial);
    }

    [Fact]
    public void LegacyStylesAndUtilityDeclarationsKeepTheirTargetNameAndParameterFields()
    {
        const string source = """
            .card { }
            Button.primary { }
            (global::Avalonia.Controls.Border).panel { }
            (global::Avalonia.Controls.Border) { }
            Button { }
            @utilities
            {
                .flag { }
                .w-(double width) { }
                .some-name-(double width)-(Variant variant) { }
                Button.btn-(Variant variant) { }
                (global::Avalonia.Controls.Button).btn-(Variant variant) { }
            }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var styles = root.Members.Take(5).Select(member => Assert.IsType<AkcssStyleRuleSyntax>(member).Selector).ToArray();
        Assert.Equal(new[] { "card", "primary", "panel", null, null }, styles.Select(style => style.Name?.Identifier.ValueText));
        Assert.Equal(new[] { null, "Button", "global::Avalonia.Controls.Border", "global::Avalonia.Controls.Border", "Button" },
            styles.Select(style => style.TargetType?.ToCSharp().ToString()));
        Assert.Equal(SyntaxKind.None, styles[3].DotToken.Kind);
        Assert.Equal(SyntaxKind.None, styles[4].DotToken.Kind);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(root.Members[5]).Utilities;
        Assert.Equal(new[] { "flag", "w", "some-name", "btn", "btn" },
            utilities.Select(utility => utility.Selector.Name.Identifier.ValueText));
        Assert.Equal(new[] { null, null, null, "Button", "global::Avalonia.Controls.Button" },
            utilities.Select(utility => utility.Selector.TargetType?.ToCSharp().ToString()));
        Assert.Equal(new[] { 0, 1, 2, 1, 1 }, utilities.Select(utility => utility.Selector.Parameters.Count));
        Assert.Equal("double", utilities[2].Selector.Parameters[0].Type.ToCSharp().ToString().Trim());
        Assert.Equal("variant", utilities[2].Selector.Parameters[1].ParamName.Identifier.ValueText);
    }

    [Fact]
    public void NestedOrConditionalBodiesUseTheSameSelectorGrammar()
    {
        const string source = """
            Border > TextBlock
            {
                @if (isEnabled)
                {
                    ^ > .child
                    {
                        ^ :focus-visible { }
                    }
                }
            }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var general = Assert.IsType<AkcssSelectorRuleSyntax>(Assert.Single(root.Members));
        var conditional = Assert.IsType<AkcssIfDirectiveSyntax>(Assert.Single(general.Members));
        var child = SingleNested(conditional.Members);
        Assert.IsType<AkcssChildSelectorSyntax>(Assert.Single(child.Selector.Steps));
        Assert.IsType<AkcssDescendantSelectorSyntax>(Assert.Single(SingleNested(child.Members).Selector.Steps));
    }

    [Theory]
    [InlineData("&:pointerover")]
    [InlineData("^.")]
    [InlineData("^#")]
    [InlineData("^:")]
    [InlineData("^. name")]
    [InlineData("^# name")]
    [InlineData("^: name")]
    [InlineData("^ >")]
    [InlineData("^ /template/")]
    [InlineData("^ > > Child")]
    [InlineData("^ /template/ > Child")]
    [InlineData("^ > ()")]
    [InlineData("^ > (global:Demo.Button)")]
    [InlineData("^ > (global::Demo.Button")]
    [InlineData("^ /template Child")]
    [InlineData("^ template/ Child")]
    [InlineData("^ > Child ^:pressed")]
    [InlineData("^:not(.accent)")]
    [InlineData("^ [IsEnabled=true]")]
    [InlineData("^, TextBlock")]
    [InlineData("^ + TextBlock")]
    [InlineData("^ *")]
    [InlineData("^ prefix|TextBlock")]
    public void MalformedRelativeSelectorsKeepTheRuleBodyAndNextDeclarations(string header)
    {
        var source = ".first { " + header + " { Opacity: 0.8d; } }\n" +
            ".following { Width: 12d; }\n@theme Button Theme { }\n@mixin Button Mixin() { }";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.Equal(4, root.Members.Count);
        var nested = SingleNested(Assert.IsType<AkcssStyleRuleSyntax>(root.Members[0]).Members);
        Assert.True(nested.Selector.ContainsDiagnostics || nested.OpenBrace.ContainsDiagnostics);
        Assert.IsType<AkcssAssignmentSyntax>(Assert.Single(nested.Members));
        if (header.StartsWith('&'))
        {
            var anchor = Assert.IsType<AkcssParentCompoundSelectorSyntax>(nested.Selector.Initial).Anchor;
            Assert.True(anchor.CaretToken.IsMissing);
            Assert.Contains("&", anchor.CaretToken.ToFullString());
            Assert.True(anchor.ContainsSkippedText);
        }

        Assert.False(Assert.IsType<AkcssStyleRuleSyntax>(root.Members[1]).ContainsDiagnostics);
        Assert.False(Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[2]).ContainsDiagnostics);
        Assert.False(Assert.IsType<AkcssMixinDeclarationSyntax>(root.Members[3]).ContainsDiagnostics);
    }

    [Fact]
    public void MissingBracesBeforeANewUtilityKeepItsTypedParameters()
    {
        const string source = """
            @utilities
            {
                .first
                {
                    ^ > TextBlock
                    {
                        Opacity: 1d;
                .second-(double width) { Width: width; }
            }
            @theme Button FollowingTheme { }
            @mixin Button FollowingMixin() { }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(root.Members[0]).Utilities;
        Assert.Equal(2, utilities.Count);
        Assert.True(utilities[0].CloseBrace.IsMissing);
        Assert.True(SingleNested(utilities[0].Members).CloseBrace.IsMissing);
        Assert.Equal("second", utilities[1].Selector.Name.Identifier.ValueText);
        Assert.Equal("width", Assert.Single(utilities[1].Selector.Parameters).ParamName.Identifier.ValueText);
        Assert.False(utilities[1].ContainsDiagnostics);
        Assert.False(Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[1]).ContainsDiagnostics);
        Assert.False(Assert.IsType<AkcssMixinDeclarationSyntax>(root.Members[2]).ContainsDiagnostics);
    }

    [Theory]
    [InlineData("@theme global::Demo.Controls.Button FollowingTheme\n{ }", nameof(SyntaxKind.AkcssThemeDeclarationSyntax))]
    [InlineData("(global::Avalonia.Controls.Border) TextBlock\n{ FontSize: 14d; }", nameof(SyntaxKind.AkcssSelectorRuleSyntax))]
    [InlineData(".following\n{ }", nameof(SyntaxKind.AkcssStyleRuleSyntax))]
    [InlineData("@utilities\n{ .following { } }", nameof(SyntaxKind.AkcssUtilitiesSectionSyntax))]
    [InlineData("@mixin global::Demo.Controls.Button FollowingMixin()\n{ }", nameof(SyntaxKind.AkcssMixinDeclarationSyntax))]
    public void MissingTemplateBraceStopsAtACompleteFollowingDeclaration(string following, string kind)
    {
        var source = "@theme Button Broken {\n    @template {\n        <ContentPresenter />\n" + following;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.Equal(2, root.Members.Count);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[0]);
        var template = Assert.IsType<AkcssTemplateDirectiveSyntax>(Assert.Single(theme.Members));
        Assert.True(template.Body.CloseBraceToken.IsMissing);
        Assert.True(theme.CloseBrace.IsMissing);
        Assert.Equal(kind, root.Members[1].Kind.ToString());
        Assert.False(root.Members[1].ContainsDiagnostics);
    }

    [Theory]
    [InlineData("@utilities {\n#Unexpected { }\n}\n")]
    [InlineData("@utilities {\n@using Avalonia.Controls;\n")]
    [InlineData("@utilities {\n@utilities { .defined { } }\n")]
    public void InvalidUtilityBodiesMakeProgressAndLeaveFollowingRules(string prefix)
    {
        var source = prefix + ".following { Width: 1d; }";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.True(root.ContainsDiagnostics);
        var following = Assert.IsType<AkcssStyleRuleSyntax>(root.Members.Last());
        Assert.Equal("following", following.Selector.Name!.Identifier.ValueText);
        Assert.False(following.ContainsDiagnostics);
    }

    [Fact]
    public void MissingTemplateBraceStopsBeforeTheNextUtilityHeader()
    {
        const string source = """
            @utilities {
                .first {
                    @template {
                        <TextBlock />
                Button.second-(double width) { Width: width; }
            }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        var utilities = Assert.IsType<AkcssUtilitiesSectionSyntax>(Assert.Single(root.Members)).Utilities;
        Assert.Equal(2, utilities.Count);
        var template = Assert.IsType<AkcssTemplateDirectiveSyntax>(Assert.Single(utilities[0].Members));
        Assert.True(template.Body.CloseBraceToken.IsMissing);
        Assert.True(utilities[0].CloseBrace.IsMissing);
        Assert.Equal("second", utilities[1].Selector.Name.Identifier.ValueText);
        Assert.False(utilities[1].ContainsDiagnostics);
    }

    [Fact]
    public void SelectorLikeTextAndAttributesRemainInTemplateMarkup()
    {
        const string source = """
            @theme Button Demo {
                @template {
                    <Border Tag="^ /template/ { @theme X Y } &"/>
                    Border TextBlock
                    ^ and /template/ & @theme words
                    {Choose("^", "&")}
                    <TextBlock />
                }
            }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(Assert.Single(root.Members));
        var template = Assert.IsType<AkcssTemplateDirectiveSyntax>(Assert.Single(theme.Members));
        var text = Assert.Single(template.Body.Content.OfType<MarkupTextLiteralSyntax>());
        Assert.Contains("Border TextBlock", text.ToFullString());
        Assert.Contains("^ and /template/ & @theme words", text.ToFullString());
        Assert.Contains("Tag=\"^ /template/ { @theme X Y } &\"", template.Body.Content[0].ToFullString());
        Assert.Single(template.DescendantNodes().OfType<MarkupInlineExpressionSyntax>());
    }

    private static AkcssNestedStyleRuleSyntax SingleNested(SyntaxList<AkcssBodyMemberSyntax> members) =>
        Assert.IsType<AkcssNestedStyleRuleSyntax>(Assert.Single(members));
}
