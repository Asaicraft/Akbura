using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;

namespace Akbura.UnitTests;

public sealed class AkcssThemeParserTests
{
    private const string Fixture = """
        @mixin ContentPresenter DefaultContentPresenterTBind() {
            @template-bind Background, BackgroundSizing, BorderBrush, BorderThickness, CornerRadius,
                Content, ContentTemplate, Padding, HorizontalContentAlignment, VerticalContentAlignment;
        }

        @mixin global::Demo.Controls.Presenter<First, Second> PresenterBrushes(string prefix, string suffix = "") {
            Background: Amx.DynamicResource(prefix + "Background" + suffix);
            BorderBrush: Amx.DynamicResource(prefix + "BorderBrush" + suffix);
            Foreground: Amx.DynamicResource(prefix + "Foreground" + suffix);
        }

        @mixin Button ButtonStates(string prefix) {
            &:pointerover /template/ ContentPresenter#PART_ContentPresenter {
                @include PresenterBrushes(prefix, "PointerOver");
            }
            &:pressed /template/ ContentPresenter#PART_ContentPresenter {
                @include PresenterBrushes(prefix, "Pressed");
            }
            &:disabled /template/ ContentPresenter#PART_ContentPresenter {
                @include PresenterBrushes(prefix, "Disabled");
            }
        }

        @theme Button MyTheme {
            Background: Amx.DynamicResource("ButtonBackground");
            @template {
                <ContentPresenter x.Name="PART_ContentPresenter">
                    @include DefaultContentPresenterTBind();
                </ContentPresenter>
            }
            & /template/ #PART_ContentPresenter { RecognizesAccessKey: true; }
            @include ButtonStates("Button");
            &.accent {
                @include ButtonStates("AccentButton");
                &:focus-visible /template/ (global::Demo.Controls.Presenter<First, Second>)#PART_ContentPresenter {
                    Background: Amx.DynamicResource("AccentButtonBackgroundPressed");
                }
            }
        }

        @theme Button CompactButton {
            @based-on global::HelloWorld.Hello.MyTheme;
            Padding: (8, 4);
        }
        """;

    [Theory]
    [InlineData(false, "\n")]
    [InlineData(true, "\n")]
    [InlineData(false, "\r\n")]
    [InlineData(true, "\r\n")]
    public void RepresentativeFixtureProducesStructuredNodes(bool inline, string newline)
    {
        var source = (inline ? WrapInline(Fixture) : Fixture + "\n\n\t// End\n\t").ReplaceLineEndings(newline);
        var root = Parse(source, inline);
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var members = Members(root);
        Assert.Equal(5, members.Count);

        var bindingMixin = Assert.IsType<AkcssMixinDeclarationSyntax>(members[0]);
        Assert.Equal("ContentPresenter", bindingMixin.TargetType.ToFullString().Trim());
        Assert.Equal("DefaultContentPresenterTBind", bindingMixin.Name.Identifier.ValueText);
        Assert.Empty(bindingMixin.Parameters.Parameters);
        var deferred = Assert.IsType<AkcssTemplateBindDirectiveSyntax>(Assert.Single(bindingMixin.Members)).Directive;
        Assert.Equal(10, deferred.PropertyNames.Count);
        Assert.Equal(9, deferred.PropertyNames.SeparatorCount);
        Assert.Equal(SyntaxKind.MinusToken, deferred.MinusToken.Kind);
        Assert.Equal(SyntaxKind.BindToken, deferred.BindToken.Kind);

        var brushMixin = Assert.IsType<AkcssMixinDeclarationSyntax>(members[1]);
        Assert.Equal("global::Demo.Controls.Presenter<First, Second>", brushMixin.TargetType.ToFullString().Trim());
        Assert.Equal("prefix", brushMixin.Parameters.Parameters[0].Name.Identifier.ValueText);
        Assert.Null(brushMixin.Parameters.Parameters[0].Default);
        var suffix = brushMixin.Parameters.Parameters[1];
        Assert.Equal("string", suffix.Type.ToFullString().Trim());
        Assert.Equal("suffix", suffix.Name.Identifier.ValueText);
        Assert.Equal("\"\"", suffix.Default!.Expression.ToFullString().Trim());
        Assert.Equal(SyntaxKind.EqualsToken, suffix.Default.EqualsToken.Kind);
        Assert.Equal(SyntaxKind.CommaToken, brushMixin.Parameters.Parameters.GetSeparator(0).Kind);

        var states = Assert.IsType<AkcssMixinDeclarationSyntax>(members[2]);
        Assert.Equal(3, states.Members.Count);
        var firstRule = Assert.IsType<AkcssNestedStyleRuleSyntax>(states.Members[0]);
        Assert.Equal(SyntaxKind.AmpersandToken, firstRule.Selector.Initial.Anchor.AmpersandToken.Kind);
        Assert.Equal("pointerover",
            Assert.IsType<AkcssPseudoClassSelectorSyntax>(Assert.Single(firstRule.Selector.Initial.Components)).Name.Identifier.ValueText);
        var traversal = Assert.Single(firstRule.Selector.Traversals);
        Assert.Equal(SyntaxKind.SlashToken, traversal.OpenSlash.Kind);
        Assert.Equal(SyntaxKind.TemplateKeyword, traversal.TemplateKeyword.Kind);
        Assert.Equal(SyntaxKind.SlashToken, traversal.CloseSlash.Kind);
        Assert.Equal("ContentPresenter", Assert.IsType<AkcssTypeSelectorSyntax>(traversal.Target.Components[0]).Type.ToFullString());
        Assert.Equal("PART_ContentPresenter",
            Assert.IsType<AkcssNameSelectorSyntax>(traversal.Target.Components[1]).Name.Identifier.ValueText);
        var stateInclude = Assert.IsType<AkcssIncludeDirectiveSyntax>(Assert.Single(firstRule.Members)).Directive;
        Assert.Equal("PresenterBrushes", stateInclude.Name.Identifier.ValueText);
        Assert.Equal(new[] { "prefix", "\"PointerOver\"" },
            stateInclude.Arguments.Arguments.Select(argument => argument.ToFullString().Trim()));

        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(members[3]);
        Assert.Equal(SyntaxKind.ThemeKeyword, theme.ThemeKeyword.Kind);
        Assert.Equal("MyTheme", theme.Name.Identifier.ValueText);
        var template = Assert.IsType<AkcssTemplateDirectiveSyntax>(theme.Members[1]);
        var presenter = Assert.IsType<MarkupElementContentSyntax>(Assert.Single(template.Body.Content)).Element;
        Assert.Single(presenter.StartTag!.Attributes);
        var markupInclude = Assert.IsType<MarkupIncludeDirectiveSyntax>(Assert.Single(presenter.Body));
        Assert.Empty(markupInclude.Directive.Arguments.Arguments);
        Assert.Equal(SyntaxKind.SemicolonToken, markupInclude.Directive.Semicolon.Kind);
        Assert.Same(markupInclude, markupInclude.Directive.Parent);
        var accent = Assert.IsType<AkcssNestedStyleRuleSyntax>(theme.Members[4]);
        Assert.IsType<AkcssClassSelectorSyntax>(Assert.Single(accent.Selector.Initial.Components));
        var focus = Assert.IsType<AkcssNestedStyleRuleSyntax>(accent.Members[1]);
        Assert.Equal("focus-visible",
            Assert.IsType<AkcssPseudoClassSelectorSyntax>(Assert.Single(focus.Selector.Initial.Components)).Name.Identifier.ValueText);
        var typed = Assert.IsType<AkcssTypeSelectorSyntax>(Assert.Single(focus.Selector.Traversals).Target.Components[0]);
        Assert.IsType<AkcssParenthesizedSelectorTypeSyntax>(typed.Type);

        var compact = Assert.IsType<AkcssThemeDeclarationSyntax>(members[4]);
        var basedOn = Assert.IsType<AkcssBasedOnDirectiveSyntax>(compact.Members[0]);
        Assert.Equal(SyntaxKind.OnKeyword, basedOn.OnKeyword.Kind);
        Assert.Equal("global::HelloWorld.Hello.MyTheme", basedOn.Expression.ToFullString().Trim());
        Assert.Equal("(8, 4)", Assert.IsType<AkcssAssignmentSyntax>(compact.Members[1]).Expression.ToFullString().Trim());
        Assert.Same(root.Green, new GreenSyntaxRewriter().Visit(root.Green));

        if (inline)
        {
            var component = Assert.IsType<AkburaDocumentSyntax>(root);
            Assert.IsType<StateDeclarationSyntax>(component.Members[1]);
            Assert.IsType<MarkupRootSyntax>(component.Members[2]);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TemplateUsesExistingMarkupAndLeavesUnrelatedAtTextAlone(bool inline)
    {
        const string body = """
            @theme Button DirectBindingTheme {
                @template {
                    <StackPanel Note="@include Bad(); } </StackPanel> @template-bind X;">
                        before @other @includeSomething @template-binder middle
                        @template-bind Background, /* property */ Content, ContentTemplate, Padding;
                        @include Defaults(); @include Other();
                        after
                        <TextBlock Text=${x.bind Vm.Name}/>
                        <Border Tag=${Binding Name}>
                            {Choose("@include NotADirective();", @template)}
                        </Border>
                        $if (ready) { <Button/> }
                        $foreach (var item in items) { <TextBlock Text={item}/> }
                    </StackPanel>
                    <Button />
                }
            }
            """;
        var source = inline ? WrapInline(body) : body;
        var root = Parse(source, inline);
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var template = Assert.Single(root.DescendantNodes().OfType<AkcssTemplateDirectiveSyntax>());
        Assert.Equal(2, template.Body.Content.Count);
        var presenter = Assert.IsType<MarkupElementContentSyntax>(template.Body.Content[0]).Element;
        Assert.Contains("@includeSomething", presenter.Body.OfType<MarkupTextLiteralSyntax>().First().ToFullString());
        Assert.Contains("after", presenter.Body.OfType<MarkupTextLiteralSyntax>().Last().ToFullString());
        var binding = Assert.Single(presenter.Body.OfType<MarkupTemplateBindDirectiveSyntax>()).Directive;
        Assert.Equal(new[] { "Background", "Content", "ContentTemplate", "Padding" },
            binding.PropertyNames.Select(property => property.ToCSharp().ToString()));
        Assert.Equal(3, binding.PropertyNames.SeparatorCount);
        Assert.Contains("/* property */", binding.ToFullString());
        Assert.Equal(2, presenter.Body.OfType<MarkupIncludeDirectiveSyntax>().Count());
        Assert.Single(presenter.Body.OfType<MarkupIfStatementSyntax>());
        Assert.Single(presenter.Body.OfType<MarkupForeachStatementSyntax>());
        Assert.Single(presenter.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.Single(presenter.DescendantNodes().OfType<MarkupExtensionSyntax>());
        Assert.Single(presenter.DescendantNodes().OfType<MarkupInlineExpressionSyntax>());
    }

    [Fact]
    public void OrdinaryMarkupAndCSharpKeepVerbatimNames()
    {
        const string source = """
            state int @template = 1;
            <Panel>before @include Example(); @template-bind Background; after</Panel>
            """;
        var root = ComponentSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        Assert.Empty(root.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());
        Assert.Empty(root.DescendantNodes().OfType<MarkupTemplateBindDirectiveSyntax>());
        Assert.Contains("@include Example();", Assert.Single(root.DescendantNodes().OfType<MarkupTextLiteralSyntax>()).ToFullString());
    }

    [Fact]
    public void CSharpDelimitersDoNotSplitArgumentsOrDefaults()
    {
        var arguments = new[]
        {
            "Create<First, Second>()", "(left, right)", "values[index]", "left < right",
            "new[] { 1, 2, 3 }", "item => Transform(item, \"a,b\")",
            "item => { Transform(item, \");}\"); return item; }",
            "\"escaped \\\" delimiters ; } ) ,\"", "@\"verbatim ; } ) ,\"",
            "$\"interpolated {Create(1, 2)} ; )\"", "\"\"\"raw ; } ) , </Presenter>\"\"\"",
            "$$\"\"\"raw {{Create(1, 2)}} ; } ) ,\"\"\"", "'}'", "@template", "value is @theme control",
        };
        var source = "@mixin Button Layout(global::Demo.Pair<First, Second> pair = Create<First, Second>(), " +
            "string name = @\"default;)}\", double opacity = 1d) { }\n" +
            "@theme Button Theme { @include Layout(" + string.Join(", /* ;) } */ ", arguments) +
            "); Content: \"});\"; @based-on @template; }";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.False(root.ContainsDiagnostics);
        var mixin = Assert.IsType<AkcssMixinDeclarationSyntax>(root.Members[0]);
        Assert.Equal(3, mixin.Parameters.Parameters.Count);
        Assert.Equal("global::Demo.Pair<First, Second>", mixin.Parameters.Parameters[0].Type.ToFullString().Trim());
        Assert.Equal("Create<First, Second>()", mixin.Parameters.Parameters[0].Default!.Expression.ToFullString().Trim());
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[1]);
        var list = Assert.IsType<AkcssIncludeDirectiveSyntax>(theme.Members[0]).Directive.Arguments;
        Assert.Equal(arguments, list.Arguments.Select(expression => expression.GetRawCSharpExpression()!.ToString()));
        Assert.Equal(arguments.Length - 1, list.Arguments.SeparatorCount);
        Assert.Equal("\"});\"", Assert.IsType<AkcssAssignmentSyntax>(theme.Members[1]).Expression.ToFullString().Trim());
        Assert.Equal("@template", Assert.IsType<AkcssBasedOnDirectiveSyntax>(theme.Members[2]).Expression.ToFullString().Trim());
    }

    [Theory]
    [InlineData("@theme { }")]
    [InlineData("@theme Button { }")]
    [InlineData("@theme Button MissingOpen")]
    [InlineData("@theme Button MissingClose { Width: 1;")]
    [InlineData("@mixin Button Broken(string name { Width: 1; }")]
    [InlineData("@mixin Button Broken(string name = \"\", int width { Width: 1; }")]
    [InlineData("@mixin Button Broken { }")]
    [InlineData("@mixin Button Broken(string first, , string third) { }")]
    [InlineData("@mixin Button Broken([Attr] ref int value) { }")]
    [InlineData("@theme Button Broken { @include Mixin; }")]
    [InlineData("@theme Button Broken { @include Mixin(1, , 2); }")]
    [InlineData("@theme Button Broken { @include Mixin(name: 1, ref other); }")]
    [InlineData("@theme Button Broken { @include Mixin() }")]
    [InlineData("@theme Button Broken { @template-bind Background,; }")]
    [InlineData("@theme Button Broken { @template-bind; }")]
    [InlineData("@theme Button Broken { @template-bin Background; }")]
    [InlineData("@theme Button Broken { @based-o Hello.MyTheme; }")]
    [InlineData("@theme Button Broken { @based-on Hello.MyTheme }")]
    [InlineData("@theme Button Broken { & /template/ { } }")]
    [InlineData("@theme Button Broken { & :pointerover { } }")]
    [InlineData("@theme Button Broken { & > Child { } }")]
    [InlineData("@theme Button Broken { &:pointerover template/ #Part { } }")]
    public void MalformedSyntaxRoundTripsAndKeepsFollowingTheme(string malformed)
    {
        var source = malformed + "\r\n@theme Button FollowingTheme { Padding: (8, 4); }\r\n";
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.True(root.ContainsDiagnostics);
        var following = Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members.Last());
        Assert.Equal("FollowingTheme", following.Name.Identifier.ValueText);
        Assert.False(following.ContainsDiagnostics);
    }

    [Theory]
    [InlineData("</ContentPresenter>")]
    [InlineData("")]
    public void BrokenDirectiveLeavesTemplateAndLaterDeclarations(string closingTag)
    {
        var source = """
            @theme Button Broken {
                @template {
                    <ContentPresenter>
                        @include Defaults(
            """ + "\n" + closingTag + "\n" + """
                }

                &:pointerover { Opacity: 0.8d; }
            }
            @theme Button FollowingTheme { Padding: (8, 4); }
            """;
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.True(root.ContainsDiagnostics);
        var theme = Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[0]);
        Assert.IsType<AkcssNestedStyleRuleSyntax>(theme.Members[1]);
        var include = Assert.Single(theme.DescendantNodes().OfType<MarkupIncludeDirectiveSyntax>());
        Assert.True(include.Directive.Arguments.CloseParen.IsMissing);
        Assert.Equal("FollowingTheme", Assert.IsType<AkcssThemeDeclarationSyntax>(root.Members[1]).Name.Identifier.ValueText);
    }

    [Theory]
    [InlineData("@theme")]
    [InlineData("@mixin Button M(")]
    [InlineData("@theme Button T { @template { <Grid")]
    [InlineData("@theme Button T { @template { <Grid>text @template-bind Background,")]
    [InlineData("@theme Button T { & /template/")]
    public void TruncatedAtEofPreservesSource(string source)
    {
        var root = AkcssSyntaxTree.ParseText(source).GetRoot();
        AssertExactSource(source, root);
        Assert.True(root.ContainsDiagnostics);
    }

    internal static string WrapInline(string akcss) => "@akcss {\n" + akcss + "\n}\nstate int counter = 1;\n<Panel />\n";

    internal static AkburaSyntax Parse(string source, bool inline) => inline
        ? ComponentSyntaxTree.ParseText(source).GetRoot()
        : AkcssSyntaxTree.ParseText(source).GetRoot();

    internal static SyntaxList<AkcssTopLevelMemberSyntax> Members(AkburaSyntax root) =>
        root is AkburaDocumentSyntax document
            ? Assert.IsType<InlineAkcssBlockSyntax>(document.Members[0]).Members
            : Assert.IsType<AkcssDocumentSyntax>(root).Members;

    internal static void AssertExactSource(string source, AkburaSyntax root)
    {
        Assert.Equal(source, root.ToFullString());
        Assert.Equal(source.Length, root.FullSpan.Length);
    }
}
