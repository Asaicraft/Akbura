using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed class CSharpProbeDelegateProjectionTests
{
    [Theory]
    [InlineData("")]
    [InlineData("int @event")]
    [InlineData("int @event, /* preserve trivia */ string name")]
    public void LocalFunctionAndMethodParameters_KeepTheirExactHostOrigins(string parameters)
    {
        var source = "// source offset\r\nstate int prefix = 0;\r\nint Helper(" + parameters + ") { return 0; }";
        var tree = AkburaSyntaxTree.ParseText(source, "Origins.akbura");
        var owner = tree.GetRoot().Members.OfType<CSharpStatementSyntax>().Single();
        var original = Assert.IsType<CSharp.LocalFunctionStatementSyntax>(owner.GetRawCSharpStatement());
        var body = SyntaxFactory.Block(SyntaxFactory.ReturnStatement(SyntaxFactory.LiteralExpression(
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.NumericLiteralExpression, SyntaxFactory.Literal(42))));
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var projected = CSharpProbeBuilder.CreateProjectedLocalFunction(owner, original, body);
            Assert.Equal(original.ParameterList.ToFullString(), projected.ParameterList.ToFullString());
            Assert.Equal(body.ToFullString(), projected.Body!.ToFullString());
            Assert.Null(projected.ExpressionBody);
            Assert.Equal(0, projected.SemicolonToken.RawKind);
            var expected = original.ParameterList.Parameters.Select(static parameter => parameter.Identifier)
                .Append(original.Identifier).ToArray();
            AssertOrigins(source, projected, expected.Select(static identifier => identifier.ValueText).ToArray());
            var offset = owner.Tokens.FullSpan.Start - original.FullSpan.Start;
            foreach (var identifier in expected)
            {
                var origin = Origins(projected).Single(origin => origin.Name == identifier.ValueText);
                Assert.Equal(offset + identifier.Span.Start, origin.DeclarationSpan.Start);
                Assert.Equal(identifier.Span.Length, origin.DeclarationSpan.Length);
            }

            Assert.True(CSharpProbeBinder.TryCreateComponentMethodProbe(owner, out var method));
            Assert.Equal(original.ParameterList.ToFullString(), method.ParameterList.ToFullString());
            AssertOrigins(source, method, expected.Select(static identifier => identifier.ValueText).ToArray());
        }
    }

    [Theory]
    [InlineData("param object model = 1; <StackPanel>$if (model is int person) { <TextBlock Text={person.ToString()} /> }</StackPanel>", "person")]
    [InlineData("<StackPanel>$foreach (var item in new[] { 1 }) { int first = item, second = item + 1; <TextBlock Text={(first + second).ToString()} /> }</StackPanel>", "first,second")]
    [InlineData("<StackPanel>$foreach (var item in new[] { 1 }) { if (int.TryParse(\"1\", out var parsed)) { <TextBlock Text={parsed.ToString()} /> } }</StackPanel>", "parsed")]
    public void MarkupProjections_KeepDeclarationSpansAcrossRepeatedRentals(string content, string names)
    {
        var source = "using Avalonia.Controls; " + content;
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source);
        var expression = fixture.ComponentTree.GetRoot().DescendantNodes().OfType<CSharpExpressionSyntax>().Last();
        for (var iteration = 0; iteration < 8; iteration++)
        {
            var projection = fixture.SemanticModel.CreateCSharpCompletionProjection(expression, 3);
            Assert.Equal(expression.Tokens.ToFullString(), projection.Root.ToFullString().Substring(
                projection.ProjectedSpan.Start, projection.ProjectedSpan.Length));
            foreach (var name in names.Split(','))
            {
                var origin = Assert.Single(projection.SymbolOrigins, origin => origin.Name == name);
                Assert.Equal(name, source.Substring(origin.DeclarationSpan.Start, origin.DeclarationSpan.Length));
                Assert.Equal(Akbura.Language.Symbols.SymbolKind.CSharpSymbol, origin.Kind);
            }
            Assert.Equal(projection.SymbolOrigins.Length,
                projection.SymbolOrigins.Select(static origin => origin.AnnotationId).Distinct().Count());
        }
        Assert.Empty(fixture.SemanticModel.GetSemanticDiagnostics(fixture.ComponentTree.GetRoot()));
    }

    [Fact]
    public void CommandParameterProjection_KeepsCommandParameterKindsAndSpans()
    {
        const string source = "using Avalonia.Controls; command int Run(int @event, string name); <Border />";
        var fixture = AkcssActivatorPlannerTests.CreateFixture(source);
        var command = fixture.ComponentTree.GetRoot().Members.OfType<CommandDeclarationSyntax>().Single();
        var projection = fixture.SemanticModel.CreateCSharpCompletionProjection(command.Parameters, 3);
        foreach (var name in new[] { "event", "name" })
        {
            var origin = Assert.Single(projection.SymbolOrigins, origin => origin.Name == name);
            Assert.Equal(Akbura.Language.Symbols.SymbolKind.CommandParameter, origin.Kind);
            Assert.Equal(name, source.Substring(origin.DeclarationSpan.Start, origin.DeclarationSpan.Length).TrimStart('@'));
        }
    }

    private static CSharpProbeSymbolOrigin[] Origins(SyntaxNode node) => node.DescendantNodesAndSelf()
        .SelectMany(static node => node.GetAnnotations(CSharpProbeBinder.ProjectedSymbolAnnotationKind))
        .Select(static annotation =>
        {
            Assert.True(CSharpProbeSymbolOrigin.TryParse(annotation.Data, out var origin));
            Assert.True(Guid.TryParseExact(origin.AnnotationId, "N", out _));
            return origin;
        }).ToArray();

    private static void AssertOrigins(string source, SyntaxNode node, string[] names)
    {
        var origins = Origins(node);
        Assert.Equal(names.Order(), origins.Select(static origin => origin.Name).Order());
        Assert.Equal(origins.Length, origins.Select(static origin => origin.AnnotationId).Distinct().Count());
        foreach (var origin in origins)
        {
            Assert.Equal(Akbura.Language.Symbols.SymbolKind.CSharpSymbol, origin.Kind);
            Assert.Equal(origin.Name, source.Substring(origin.DeclarationSpan.Start, origin.DeclarationSpan.Length).TrimStart('@'));
        }
    }
}
