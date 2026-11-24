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
            AkcssParentSelectorSyntax(Token(SyntaxKind.CaretToken)),
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
        var selector = AkcssSelectorSyntax(initial, SingletonList<AkcssSelectorStepSyntax>(traversal));

        Assert.Equal("^:pointerover /template/ ContentPresenter#PART_ContentPresenter", selector.ToFullString());
        var parent = Assert.IsType<AkcssParentCompoundSelectorSyntax>(selector.Initial);
        Assert.Equal(SyntaxKind.CaretToken, parent.Anchor.CaretToken.Kind);
        Assert.Same(parent, parent.Anchor.Parent);
        Assert.Same(parent.Anchor, parent.Anchor.WithCaretToken(parent.Anchor.CaretToken));
        Assert.Throws<ArgumentException>(() => parent.Anchor.WithCaretToken(Token(SyntaxKind.AmpersandToken)));
        Assert.Same(selector, new IdentityRewriter().Visit(selector));

        var parenthesized = AkcssParenthesizedSelectorTypeSyntax(
            Token(SyntaxKind.OpenParenToken),
            TypeFragment("global::Controls.ContentPresenter"),
            Token(SyntaxKind.CloseParenToken));
        Assert.Equal("(global::Controls.ContentPresenter)", AkcssTypeSelectorSyntax(parenthesized).ToFullString());
        Assert.Throws<ArgumentException>(() => AkcssTypeSelectorSyntax(IdentifierName("ContentPresenter")));
    }

    [Fact]
    public void SharedSelectorStepsKeepSlotsPositionsAndMetadataThroughUpdates()
    {
        var initial = AkcssCompoundSelectorSyntax(SingletonList<AkcssSelectorComponentSyntax>(
            AkcssClassSelectorSyntax(Token(SyntaxKind.DotToken), IdentifierName(IdentifierWithTrailingSpace("card")))));
        var target = AkcssCompoundSelectorSyntax(SingletonList<AkcssSelectorComponentSyntax>(
            AkcssClassSelectorSyntax(Token(SyntaxKind.DotToken), IdentifierName(IdentifierWithTrailingSpace("label")))));
        var descendant = AkcssDescendantSelectorSyntax(target);
        var child = AkcssChildSelectorSyntax(TokenWithTrailingSpace(SyntaxKind.GreaterThanToken),
            AkcssCompoundSelectorSyntax(SingletonList<AkcssSelectorComponentSyntax>(AkcssTypeSelectorSyntax(TypeFragment("TextBlock ")))));
        var selector = AkcssSelectorSyntax(initial, List<AkcssSelectorStepSyntax>([descendant, child]));
        var rule = AkcssSelectorRuleSyntax(selector, Token(SyntaxKind.OpenBraceToken), default, Token(SyntaxKind.CloseBraceToken));

        const string text = ".card .label > TextBlock {}";
        Assert.Equal(text, rule.ToFullString());
        Assert.Equal(new TextSpan(0, text.Length), rule.FullSpan);
        Assert.Same(rule, rule.Selector.Parent);
        var parsedDescendant = Assert.IsType<AkcssDescendantSelectorSyntax>(rule.Selector.Steps[0]);
        var parsedChild = Assert.IsType<AkcssChildSelectorSyntax>(rule.Selector.Steps[1]);
        Assert.Same(parsedDescendant, parsedDescendant.Target.Parent);
        Assert.Same(parsedChild, parsedChild.Target.Parent);
        Assert.Same(parsedChild, parsedChild.GreaterThanToken.Parent);
        Assert.Equal(rule.Selector.Initial.FullSpan.End, parsedDescendant.FullSpan.Start);
        Assert.Equal(parsedDescendant.FullSpan.End, parsedChild.GreaterThanToken.Span.Start);
        Assert.Equal(parsedChild.GreaterThanToken.FullSpan.End, parsedChild.Target.FullSpan.Start);
        Assert.Equal(parsedChild.Target.FullSpan.End, rule.OpenBrace.FullSpan.Start);

        Assert.Same(rule, new IdentityRewriter().Visit(rule));
        Assert.Same(rule.Green, new GreenSyntaxRewriter().Visit(rule.Green));
        Assert.Same(parsedChild, parsedChild.UpdateAkcssChildSelectorSyntax(parsedChild.GreaterThanToken, parsedChild.Target));
        Assert.Throws<ArgumentException>(() => parsedChild.WithGreaterThanToken(Token(SyntaxKind.SlashToken)));

        var diagnostic = new SyntaxDiagnosticInfo(0, 1, ErrorCodes.ERR_SyntaxError, ["selector"]);
        var annotation = new AkburaSyntaxAnnotation("selector test");
        var decorated = child.WithDiagnostics(diagnostic).WithAnnotations(annotation);
        var updated = decorated.WithTarget(target);
        Assert.Same(diagnostic, Assert.Single(updated.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(updated.GetAnnotations()));
        var rewritten = Assert.IsType<GreenAkcssChildSelectorSyntax>(new ReplaceChildTokenRewriter().Visit(decorated.Green));
        Assert.Same(diagnostic, Assert.Single(rewritten.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(rewritten.GetAnnotations()));
        Assert.Equal(" >", rewritten.GreaterThanToken.ToFullString());
    }

    private sealed class ReplaceChildTokenRewriter : GreenSyntaxRewriter
    {
        public override GreenNode? VisitToken(GreenSyntaxToken? token) =>
            token?.Kind == SyntaxKind.GreaterThanToken
                ? GreenSyntaxFactory.Token(GreenSyntaxFactory.Whitespace(" "), SyntaxKind.GreaterThanToken, null)
                : base.VisitToken(token);
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
