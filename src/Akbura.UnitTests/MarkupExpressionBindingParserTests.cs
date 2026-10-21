using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionBindingParserTests
{
    public static TheoryData<string> Expressions => new()
    {
        "Vm.Name",
        "Vm.Count + 1",
        "Vm.Age >= 18 ? \"Adult\" : \"Minor\"",
        "$\"{Vm.FirstName} {Vm.LastName}\"",
        "Vm.Person?.Name ?? \"Unknown\"",
        "Vm.Items[3].Name",
        "Format(Vm.Price, \"F2\")",
        "Vm.IsEnabled && Vm.Count > 0",
        "Vm.Count < 10 && Vm.Count >> 1 > 0",
        "new Person { Name = Vm.Name, Address = new() { City = Vm.City } }",
        "Vm.Count switch { 0 => \"Zero\", _ => \"Other\" }",
        "Vm.Person is { Address: { City: \"Osh\" } }",
        "new[] { 1, 2, 3 }[Vm.Index]",
        "(Vm.Name, Format(Vm.Price, \"F2\"))",
        "Vm.Items.Select(item => new { item.Name }).ToArray()",
        "((Func<int>)(() => { return Vm.Count + 1; }))()",
        "$\"{{{Format(Vm.Price, \"F2\")}}} { $\"{Vm.Name}\" }\"",
        "$@\"{{{Vm.Name}}}\"",
        "@$\"{Vm.Name}\"",
        "\"\"\"braces } { and /> inside raw text\"\"\"",
        "$$\"\"\"{ {{Vm.Name}} }\"\"\"",
        "'事' == '}' ? Vm.Name : \"/>\"",
        "Vm.Count /* } , ) ] /> */ + 1",
        "Vm.Count // } />\r\n + 1",
    };

    [Theory]
    [MemberData(nameof(Expressions))]
    public void FullParse_PreservesExpressionAndFollowingAttribute(string expression)
    {
        var code = "<TextBlock Text=${x.bind " + expression + "} Width=\"100\" />";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var binding = Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        var raw = binding.Expression.GetRawCSharpExpression();

        Assert.Equal(SyntaxKind.MarkupExpressionBindingSyntax, binding.Kind);
        Assert.Equal(SyntaxKind.DollarToken, binding.DollarToken.Kind);
        Assert.Equal(SyntaxKind.OpenBraceToken, binding.OpenBrace.Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, binding.XIdentifier.Identifier.Kind);
        Assert.Equal("x", binding.XIdentifier.Identifier.Text);
        Assert.Equal(SyntaxKind.DotToken, binding.DotToken.Kind);
        Assert.Equal(SyntaxKind.BindToken, binding.BindKeyword.Kind);
        Assert.Equal(SyntaxKind.CloseBraceToken, binding.CloseBrace.Kind);
        Assert.NotNull(raw);
        Assert.False(raw.ContainsDiagnostics, string.Join("\n", raw.GetDiagnostics()));
        Assert.Equal(expression, raw.ToFullString());
        Assert.Equal(expression, binding.Expression.ToFullString());
        Assert.Single(binding.Expression.Tokens);
        Assert.Equal(SyntaxKind.CSharpRawToken, binding.Expression.Tokens[0].Kind);
        Assert.Equal(new TextSpan(code.IndexOf(expression, StringComparison.Ordinal), expression.Length), binding.Expression.FullSpan);
        Assert.Equal(binding.Expression.EndPosition, binding.CloseBrace.Position);
        Assert.Equal("}", binding.CloseBrace.Text);
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(code.Length, root.FullWidth);
        Assert.Equal(code, root.ToFullString());
        Assert.Equal(new[] { "Text", "Width" }, root.DescendantNodes().OfType<MarkupPlainAttributeSyntax>().Select(a => a.Name.ToString()));
    }

    [Fact]
    public void IntroducerAndExpressionTrivia_RoundTripWithCorrectPositions()
    {
        const string code = "<TextBlock Text=$ /* dollar */ { /* brace */ x /* x */ . /* dot */ bind /* bind */\r\n Vm.Name /* end */ } />";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var binding = Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());

        Assert.Equal("x", binding.XIdentifier.Identifier.Text);
        Assert.Equal("$ /* dollar */ ", binding.DollarToken.ToFullString());
        Assert.Equal("bind /* bind */\r\n", binding.BindKeyword.ToFullString());
        Assert.Equal(" Vm.Name /* end */ ", binding.Expression.ToFullString());
        Assert.Equal(code.IndexOf(" Vm.Name", StringComparison.Ordinal), binding.Expression.Position);
        Assert.Equal(code.LastIndexOf('}'), binding.CloseBrace.SpanStart);
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());
    }

    [Theory]
    [InlineData("Binding FirstName")]
    [InlineData("CompiledBinding FirstName")]
    [InlineData("CustomExtension Value")]
    [InlineData("x.bindable FirstName")]
    [InlineData("x.other FirstName")]
    [InlineData("X.bind FirstName")]
    [InlineData("@x.bind FirstName")]
    public void SimilarIntroducers_RetainMarkupExtensionSyntax(string body)
    {
        var code = "<TextBlock Text=${" + body + "} />";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();

        Assert.Empty(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExtensionAttributeValueSyntax>());
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());
    }

    [Theory]
    [InlineData("${x.bind }", "", false)]
    [InlineData("${x.bind Vm.}", "Vm.", false)]
    [InlineData("${x.bind Vm.Name ", "Vm.Name ", true)]
    [InlineData("${x.bind (Vm.Count + 1}", "(Vm.Count + 1", false)]
    [InlineData("${x.bind Vm.Items[3}", "Vm.Items[3", false)]
    [InlineData("${x.bind Vm.Name) + 1}", "Vm.Name) + 1", false)]
    public void MalformedBinding_PreservesFollowingElementAndReportsCorrectLayer(
        string value, string expression, bool missingCloseBrace)
    {
        var code = "<StackPanel><TextBlock Text=" + value + "/>\r\n<Button Content=\"Next\" /></StackPanel>";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var binding = Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        var raw = binding.Expression.GetRawCSharpExpression()!;

        Assert.Equal(expression, binding.Expression.ToFullString());
        Assert.Equal(missingCloseBrace, binding.CloseBrace.IsMissing);
        Assert.Equal(expression.Length == 0 || missingCloseBrace, binding.ContainsDiagnostics);
        Assert.Equal(!missingCloseBrace, raw.ContainsDiagnostics);
        Assert.Equal(3, root.DescendantNodes().OfType<MarkupElementSyntax>().Count());
        Assert.Contains(root.DescendantNodes().OfType<MarkupPlainAttributeSyntax>(), a => a.Name.ToString() == "Content");
        Assert.Equal(code.Length, root.FullWidth);
        Assert.Equal(code, root.ToFullString());
    }

    [Theory]
    [InlineData("${x.bind")]
    [InlineData("${x.bind Vm.Name")]
    [InlineData("${x.bind /* comment with } ")]
    [InlineData("${x.bind \"unterminated")]
    public void IncompleteBindingAtEndOfFile_PreservesText(string value)
    {
        var code = "<TextBlock Text=" + value;
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var binding = Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());

        Assert.True(binding.CloseBrace.IsMissing);
        Assert.True(root.ContainsDiagnostics);
        Assert.Equal(code.Length, root.FullWidth);
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void BindingBeforeUnquotedWidth_PreservesExistingAttributeRecovery()
    {
        const string code = "<TextBlock Text=${x.bind Vm.Name} Width=100 />\r\n<Button Content=\"Next\" />";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();

        Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.Contains(root.DescendantNodes().OfType<IncompleteAttributeSyntax>(), a => a.Name.ToString() == "Width");
        Assert.Equal(2, root.Members.Count);
        Assert.Equal(code, root.ToFullString());
    }

    [Fact]
    public void Recognition_IsLimitedToAttributeValuesAndExistingConstructsRoundTrip()
    {
        const string code = """
            <Grid Text=${x.bind Vm.Name} bind:Value={Vm.Value} out:Result={Vm.Result}
                  Tag=${MyExtension Value=${Binding Name}, Other={Vm.Count + 1}}
                  p-${Spacing {Vm.Count + 1}} ${md}:p-4>
                {Vm.Name}
            </Grid>
            """;
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();

        Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.Equal(2, root.DescendantNodes().OfType<MarkupPrefixedAttributeSyntax>().Count());
        Assert.Single(root.DescendantNodes().OfType<TailwindMarkupExtensionSegmentSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExtensionConditionalPrefixSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExtensionNestedValueSyntax>());
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());

        using var parser = ParserHelper.MakeParser("p-${x.bind Value}");
        var tailwind = Assert.IsType<GreenTailwindFullAttributeSyntax>(parser.ParseMarkupAttributeSyntax());
        Assert.IsType<GreenTailwindMarkupExtensionSegmentSyntax>(tailwind.Segments[0]);
        Assert.Equal("p-${x.bind Value}", tailwind.ToFullString());
    }
}
