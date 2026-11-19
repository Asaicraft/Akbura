using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using static Akbura.Language.Syntax.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed class AkcssThemeSyntaxFactoryTests
{
    [Theory]
    [InlineData("theme", (int)SyntaxKind.ThemeKeyword)]
    [InlineData("template", (int)SyntaxKind.TemplateKeyword)]
    [InlineData("mixin", (int)SyntaxKind.MixinKeyword)]
    [InlineData("include", (int)SyntaxKind.IncludeKeyword)]
    [InlineData("based", (int)SyntaxKind.BasedKeyword)]
    [InlineData("on", (int)SyntaxKind.OnKeyword)]
    public void DirectiveKeywordsRemainContextual(string text, int rawKind)
    {
        var kind = (SyntaxKind)rawKind;
        using var lexer = new Lexer(SourceText.From(text));

        var identifier = lexer.Lex(Lexer.LexerMode.TopLevel);
        Assert.Equal(SyntaxKind.IdentifierToken, identifier.Kind);
        Assert.Equal(kind, identifier.ContextualKind);
        Assert.False(SyntaxFacts.IsReservedKeyword(kind));
        Assert.Equal(text, Token(kind).ToFullString());
    }

    [Fact]
    public void MixinParameterListPreservesOptionalDefaultsAndSeparatorTrivia()
    {
        var parameters = AkcssMixinParameterListSyntax(
            Token(SyntaxKind.OpenParenToken),
            SeparatedList(
                new[]
                {
                    AkcssMixinParameterSyntax(TypeFragment("string "), IdentifierName("prefix"), null),
                    AkcssMixinParameterSyntax(
                        TypeFragment("string "),
                        IdentifierName("suffix"),
                        AkcssMixinParameterDefaultSyntax(
                            Token(SyntaxKind.EqualsToken),
                            CSharpExpressionSyntax(TokenList(CSharpRawToken("\"\""))))),
                },
                new[] { Token(SyntaxKind.CommaToken) }),
            Token(SyntaxKind.CloseParenToken));

        const string text = "(string prefix,string suffix=\"\")";
        Assert.Equal(text, parameters.ToFullString());
        Assert.Null(parameters.Parameters[0].Default);
        Assert.Equal("=\"\"", parameters.Parameters[1].Default!.ToFullString());
        Assert.Same(parameters, new IdentityRewriter().Visit(parameters));
        Assert.Same(parameters.Green, new GreenSyntaxRewriter().Visit(parameters.Green));
        Assert.Throws<ArgumentException>(() => parameters.WithCloseParen(Token(SyntaxKind.CloseBraceToken)));

        var rewritten = Assert.IsType<GreenAkcssMixinParameterListSyntax>(
            new SeparateWithSpaceRewriter().Visit(parameters.Green));
        Assert.Equal("(string prefix, string suffix=\"\")", rewritten.ToFullString());

        var empty = AkcssMixinParameterListSyntax(Token(SyntaxKind.OpenParenToken), default, Token(SyntaxKind.CloseParenToken));
        Assert.Empty(empty.Parameters);
        Assert.Same(empty, new IdentityRewriter().Visit(empty));
    }

    [Fact]
    public void IncludePayloadCanBeUsedInBothPlacementCategories()
    {
        var include = AkcssIncludeSyntax(
            Token(SyntaxKind.AtToken),
            TokenWithTrailingSpace(SyntaxKind.IncludeKeyword),
            IdentifierName("PresenterBrushes"),
            AkcssMixinArgumentListSyntax(Token(SyntaxKind.OpenParenToken), default, Token(SyntaxKind.CloseParenToken)),
            Token(SyntaxKind.SemicolonToken));

        var akcss = AkcssIncludeDirectiveSyntax(include);
        var markup = MarkupIncludeDirectiveSyntax(include);

        Assert.Equal("@include PresenterBrushes();", akcss.ToFullString());
        Assert.Equal(akcss.ToFullString(), markup.ToFullString());
        Assert.Same(akcss.Directive.Green, markup.Directive.Green);
        Assert.Empty(markup.Directive.Arguments.Arguments);
        Assert.Same(markup, new IdentityRewriter().Visit(markup));
    }

    [Fact]
    public void NestedSelectorPreservesAnchorTraversalAndCompoundOrder()
    {
        var initial = AkcssParentCompoundSelectorSyntax(
            AkcssParentSelectorSyntax(Token(SyntaxKind.AmpersandToken)),
            SingletonList<AkcssSelectorComponentSyntax>(AkcssPseudoClassSelectorSyntax(
                Token(SyntaxKind.ColonToken),
                IdentifierName(IdentifierWithTrailingSpace("pointerover")))));
        var target = AkcssCompoundSelectorSyntax(List<AkcssSelectorComponentSyntax>(
        [
            AkcssTypeSelectorSyntax(TypeFragment("ContentPresenter")),
            AkcssNameSelectorSyntax(Token(SyntaxKind.HashToken), IdentifierName("PART_ContentPresenter")),
        ]));
        var traversal = AkcssTemplateTraversalSyntax(
            Token(SyntaxKind.SlashToken),
            Token(SyntaxKind.TemplateKeyword),
            TokenWithTrailingSpace(SyntaxKind.SlashToken),
            target);
        var selector = AkcssNestedSelectorSyntax(initial, SingletonList(traversal));

        Assert.Equal("&:pointerover /template/ ContentPresenter#PART_ContentPresenter", selector.ToFullString());
        Assert.Equal(SyntaxKind.AmpersandToken, selector.Initial.Anchor.AmpersandToken.Kind);
        Assert.Same(selector.Initial, selector.Initial.Anchor.Parent);
        Assert.Same(selector, new IdentityRewriter().Visit(selector));

        var parenthesized = AkcssParenthesizedSelectorTypeSyntax(
            Token(SyntaxKind.OpenParenToken),
            TypeFragment("global::Controls.ContentPresenter"),
            Token(SyntaxKind.CloseParenToken));
        Assert.Equal("(global::Controls.ContentPresenter)", AkcssTypeSelectorSyntax(parenthesized).ToFullString());
        Assert.Throws<ArgumentException>(() => AkcssTypeSelectorSyntax(IdentifierName("ContentPresenter")));
    }

    private static CSharpTypeSyntax TypeFragment(string text) => CSharpTypeSyntax(TokenList(CSharpRawToken(text)));

    private sealed class IdentityRewriter : SyntaxRewriter;

    private sealed class SeparateWithSpaceRewriter : GreenSyntaxRewriter
    {
        public override GreenNode? VisitToken(GreenSyntaxToken? token) =>
            token?.Kind == SyntaxKind.CommaToken
                ? GreenSyntaxFactory.TokenWithTrailingSpace(SyntaxKind.CommaToken)
                : base.VisitToken(token);
    }
}
