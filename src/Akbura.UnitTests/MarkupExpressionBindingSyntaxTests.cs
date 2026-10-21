using System.Collections.Immutable;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionBindingSyntaxTests
{
    [Fact]
    public void GreenFactory_ConstructsDedicatedExpressionBindingNode()
    {
        var node = CreateGreenBinding("Vm.FirstName");

        Assert.Equal(SyntaxKind.MarkupExpressionBindingSyntax, node.Kind);
        Assert.Equal(7, node.SlotCount);
        Assert.Equal(SyntaxKind.DollarToken, node.DollarToken.Kind);
        Assert.Equal(SyntaxKind.OpenBraceToken, node.OpenBrace.Kind);
        Assert.Equal("x", node.XIdentifier.ToFullString());
        Assert.Equal(SyntaxKind.IdentifierToken, node.XIdentifier.Identifier.Kind);
        Assert.Equal(SyntaxKind.DotToken, node.DotToken.Kind);
        Assert.Equal(SyntaxKind.BindToken, node.BindKeyword.Kind);
        Assert.Equal("Vm.FirstName", node.Expression.ToFullString());
        Assert.Equal(SyntaxKind.CloseBraceToken, node.CloseBrace.Kind);
        Assert.Equal("${x.bind Vm.FirstName}", node.ToFullString());
    }

    [Fact]
    public void RedNode_ExposesChildrenWithParentsAndExactPositions()
    {
        var node = CreateBinding("Vm.FirstName");

        Assert.Equal(0, node.Position);
        Assert.Equal(0, node.DollarToken.Position);
        Assert.Equal(1, node.OpenBrace.Position);
        Assert.Equal(2, node.XIdentifier.Position);
        Assert.Equal(3, node.DotToken.Position);
        Assert.Equal(4, node.BindKeyword.Position);
        Assert.Equal(9, node.Expression.Position);
        Assert.Equal(9, node.Expression.Tokens[0].Position);
        Assert.Equal(21, node.CloseBrace.Position);

        Assert.Same(node, node.DollarToken.Parent);
        Assert.Same(node, node.OpenBrace.Parent);
        Assert.Same(node, node.XIdentifier.Parent);
        Assert.Same(node, node.DotToken.Parent);
        Assert.Same(node, node.BindKeyword.Parent);
        Assert.Same(node, node.Expression.Parent);
        Assert.Same(node, node.CloseBrace.Parent);
    }

    [Theory]
    [InlineData("Vm.FirstName")]
    [InlineData("$\"{Vm.FirstName} {Vm.LastName}\"")]
    [InlineData("Vm.Counter * 2")]
    public void ToFullString_RoundTripsConstructedBinding(string expression)
    {
        var node = CreateBinding(expression);

        Assert.Equal("${x.bind " + expression + "}", node.ToFullString());
        Assert.Equal(node.ToFullString().Length, node.FullWidth);
    }

    [Fact]
    public void WithExpression_ReplacesOnlyExpression()
    {
        var node = CreateBinding("Vm.FirstName");
        var replacement = CreateExpression("Vm.LastName");

        var unchanged = node.UpdateMarkupExpressionBindingSyntax(
            node.DollarToken,
            node.OpenBrace,
            node.XIdentifier,
            node.DotToken,
            node.BindKeyword,
            node.Expression,
            node.CloseBrace);
        var updated = node.WithExpression(replacement);

        Assert.Same(node, unchanged);
        Assert.NotSame(node, updated);
        Assert.Same(node.Green.DollarToken, updated.Green.DollarToken);
        Assert.Same(node.Green.OpenBrace, updated.Green.OpenBrace);
        Assert.Same(node.XIdentifier.Green, updated.XIdentifier.Green);
        Assert.Same(node.Green.DotToken, updated.Green.DotToken);
        Assert.Same(node.Green.BindKeyword, updated.Green.BindKeyword);
        Assert.Same(replacement.Green, updated.Expression.Green);
        Assert.Same(node.Green.CloseBrace, updated.Green.CloseBrace);
        Assert.Equal("${x.bind Vm.LastName}", updated.ToFullString());
    }

    [Fact]
    public void Rewriters_TraverseAndReplaceExpression()
    {
        var node = CreateBinding("Vm.FirstName");
        var replacement = CreateExpression("Vm.Counter * 2");
        var redRewriter = new ReplacingExpressionRewriter(replacement);
        var greenRewriter = new ReplacingGreenExpressionRewriter(replacement.Green);

        var rewritten = Assert.IsType<MarkupExpressionBindingSyntax>(redRewriter.Visit(node));
        var rewrittenGreen = Assert.IsType<GreenMarkupExpressionBindingSyntax>(greenRewriter.Visit(node.Green));

        Assert.Equal(1, redRewriter.VisitCount);
        Assert.Equal(1, greenRewriter.VisitCount);
        Assert.Equal("${x.bind Vm.Counter * 2}", rewritten.ToFullString());
        Assert.Equal("${x.bind Vm.Counter * 2}", rewrittenGreen.ToFullString());
        Assert.Same(node.XIdentifier.Green, rewritten.XIdentifier.Green);
        Assert.Same(node.XIdentifier.Green, rewrittenGreen.XIdentifier);
    }

    [Fact]
    public void Updates_PreserveDiagnosticsAndAnnotations_OnRedAndGreenNodes()
    {
        var diagnostic = new AkburaDiagnostic(
            ImmutableArray<object?>.Empty,
            "TEST_ExpressionBinding",
            AkburaDiagnosticSeverity.Warning);
        var annotation = new AkburaSyntaxAnnotation("expression-binding", "test");
        var replacement = CreateExpression("Vm.LastName");
        var decorated = CreateBinding("Vm.FirstName")
            .WithDiagnostics(ImmutableArray.Create(diagnostic))
            .WithAnnotations(ImmutableArray.Create(annotation));

        var updatedRed = decorated.WithExpression(replacement);
        var updatedGreen = decorated.Green.WithExpression(replacement.Green);

        Assert.Same(diagnostic, Assert.Single(updatedRed.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(updatedRed.GetAnnotations()));
        Assert.Same(diagnostic, Assert.Single(updatedGreen.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(updatedGreen.GetAnnotations()));
    }

    private static MarkupExpressionBindingSyntax CreateBinding(string expression)
    {
        return SyntaxFactory.MarkupExpressionBindingSyntax(
            dollarToken: SyntaxFactory.Token(SyntaxKind.DollarToken),
            openBrace: SyntaxFactory.Token(SyntaxKind.OpenBraceToken),
            xIdentifier: SyntaxFactory.IdentifierName("x"),
            dotToken: SyntaxFactory.Token(SyntaxKind.DotToken),
            bindKeyword: SyntaxFactory.TokenWithTrailingSpace(SyntaxKind.BindToken),
            expression: CreateExpression(expression),
            closeBrace: SyntaxFactory.Token(SyntaxKind.CloseBraceToken));
    }

    private static GreenMarkupExpressionBindingSyntax CreateGreenBinding(string expression)
    {
        var expressionSyntax = GreenSyntaxFactory.CSharpExpressionSyntax(
            GreenSyntaxFactory.List<GreenSyntaxToken>(GreenSyntaxFactory.CSharpRawToken(expression)));

        return GreenSyntaxFactory.MarkupExpressionBindingSyntax(
            dollarToken: GreenSyntaxFactory.Token(SyntaxKind.DollarToken),
            openBrace: GreenSyntaxFactory.Token(SyntaxKind.OpenBraceToken),
            xIdentifier: GreenSyntaxFactory.IdentifierName(GreenSyntaxFactory.Identifier("x")),
            dotToken: GreenSyntaxFactory.Token(SyntaxKind.DotToken),
            bindKeyword: GreenSyntaxFactory.TokenWithTrailingSpace(SyntaxKind.BindToken),
            expression: expressionSyntax,
            closeBrace: GreenSyntaxFactory.Token(SyntaxKind.CloseBraceToken));
    }

    private static CSharpExpressionSyntax CreateExpression(string expression)
    {
        return SyntaxFactory.CSharpExpressionSyntax(
            SyntaxFactory.TokenList(SyntaxFactory.CSharpRawToken(expression)));
    }

    private sealed class ReplacingExpressionRewriter(CSharpExpressionSyntax replacement) : SyntaxRewriter
    {
        public int VisitCount { get; private set; }

        public override AkburaSyntax VisitCSharpExpressionSyntax(CSharpExpressionSyntax node)
        {
            VisitCount++;
            return replacement;
        }
    }

    private sealed class ReplacingGreenExpressionRewriter(GreenCSharpExpressionSyntax replacement) : GreenSyntaxRewriter
    {
        public int VisitCount { get; private set; }

        public override GreenNode VisitCSharpExpressionSyntax(GreenCSharpExpressionSyntax node)
        {
            VisitCount++;
            return replacement;
        }
    }
}
