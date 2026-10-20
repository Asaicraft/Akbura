using System.Collections.Immutable;
using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class ReactiveUtilityConditionSyntaxTests
{
    [Theory]
    [MemberData(nameof(MarkupExpressionBindingParserTests.Expressions), MemberType = typeof(MarkupExpressionBindingParserTests))]
    public void Parser_RetainsDedicatedBindingAndUtilityBoundaries(string expression)
    {
        var code = "<StackPanel><TextBlock ${x.bind " + expression + "}:hidden Width=\"100\"/>" +
            "<Button Content=\"Next\"/></StackPanel>";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var prefix = Assert.Single(root.DescendantNodes().OfType<TailwindExpressionBindingConditionalPrefixSyntax>());
        var attribute = Assert.IsType<TailwindFullAttributeSyntax>(prefix.Parent);
        Assert.Equal("hidden", attribute.Name.ToString());
        Assert.Equal(expression, prefix.Binding.Expression.ToFullString());
        Assert.Equal("}", prefix.Binding.CloseBrace.Text);
        Assert.Equal(":", prefix.Colon.Text);
        Assert.Equal(prefix.Binding.EndPosition, prefix.Colon.Position);
        Assert.Same(prefix, prefix.Binding.Parent);
        Assert.Same(prefix.Binding, prefix.Binding.Expression.Parent);
        Assert.Empty(root.DescendantNodes().OfType<MarkupExtensionConditionalPrefixSyntax>());
        Assert.Equal(3, root.DescendantNodes().OfType<MarkupElementSyntax>().Count());
        Assert.False(root.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());
        Assert.Equal(code.Length, root.FullWidth);
    }

    [Theory]
    [InlineData("${x.bind true}:hidden")]
    [InlineData("${x.bind width > 1000}:hidden")]
    [InlineData("${x.bind Vm.Enabled && Vm.Ready}:hidden")]
    [InlineData("${x.bind Vm.User?.Enabled == true}:font-bold")]
    [InlineData("${x.bind Vm.Items[Vm.SelectedIndex].Enabled}:bg-blue-500")]
    [InlineData("${x.bind (Vm.A && Vm.B) || Vm.C}:hidden")]
    [InlineData("$ /* dollar */ { x . bind /* separator */\r\n Vm.Enabled /* end */ }:hidden")]
    public void Parser_RequiredConditionsPreserveTokensAndTrivia(string condition)
    {
        var code = "<TextBlock " + condition + "/>";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        var prefix = Assert.Single(root.DescendantNodes().OfType<TailwindExpressionBindingConditionalPrefixSyntax>());
        Assert.False(root.ContainsDiagnostics);
        Assert.NotNull(prefix.Binding.Expression.GetRawCSharpExpression());
        Assert.Equal(code, root.ToFullString());
        Assert.Equal(code.IndexOf(':', code.LastIndexOf('}')), prefix.Colon.SpanStart);
    }

    [Fact]
    public void Factories_UpdatesAndRewritersPreserveMetadataAndSlotOrder()
    {
        var parsed = ComponentSyntaxTree.ParseText("<TextBlock ${x.bind true}:hidden/>").GetRoot();
        var binding = Assert.Single(parsed.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        var green = GreenSyntaxFactory.TailwindExpressionBindingConditionalPrefixSyntax(
            binding.Green, GreenSyntaxFactory.Token(SyntaxKind.ColonToken));
        Assert.Equal(2, green.SlotCount);
        Assert.Same(binding.Green, green.GetSlot(0));
        Assert.Same(green.Colon, green.GetSlot(1));
        Assert.Equal("${x.bind true}:", green.ToFullString());
        var red = SyntaxFactory.TailwindExpressionBindingConditionalPrefixSyntax(binding, SyntaxFactory.Token(SyntaxKind.ColonToken));
        Assert.Equal(red.Binding.FullWidth, red.Colon.Position);
        Assert.Same(red, red.Binding.Parent);
        Assert.Same(red, red.UpdateTailwindExpressionBindingConditionalPrefixSyntax(red.Binding, red.Colon));
        Assert.Same(green, green.UpdateTailwindExpressionBindingConditionalPrefixSyntax(green.Binding, green.Colon));

        var diagnostic = new AkburaDiagnostic(ImmutableArray<object?>.Empty, "TEST_ReactiveUtility", AkburaDiagnosticSeverity.Warning);
        var annotation = new AkburaSyntaxAnnotation("reactive-utility", "test");
        var decorated = red.WithDiagnostics(ImmutableArray.Create(diagnostic)).WithAnnotations(ImmutableArray.Create(annotation));
        var replacement = SyntaxFactory.CSharpExpressionSyntax(SyntaxFactory.TokenList(SyntaxFactory.CSharpRawToken("false")));
        var updated = decorated.WithBinding(decorated.Binding.WithExpression(replacement));
        var updatedGreen = decorated.Green.WithBinding(decorated.Binding.Green.WithExpression(replacement.Green));
        Assert.Equal("${x.bind false}:", updated.ToFullString());
        Assert.Equal(updated.ToFullString(), updatedGreen.ToFullString());
        Assert.Same(diagnostic, Assert.Single(updated.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(updated.GetAnnotations()));
        Assert.Same(diagnostic, Assert.Single(updatedGreen.GetDiagnostics()));
        Assert.Same(annotation, Assert.Single(updatedGreen.GetAnnotations()));
        Assert.Equal(updated.ToFullString(), new Rewriter(replacement).Visit(red)!.ToFullString());
        Assert.Equal(updatedGreen.ToFullString(), new GreenRewriter(replacement.Green).Visit(green)!.ToFullString());
    }

    [Theory]
    [InlineData("${x.bind")]
    [InlineData("${x.bind Vm.")]
    [InlineData("${x.bind Vm.Enabled}")]
    [InlineData("${x.bind Vm.Enabled}:")]
    public void IncompleteCondition_RoundTripsAndRetainsBinding(string condition)
    {
        var code = "<TextBlock " + condition;
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        Assert.Single(root.DescendantNodes().OfType<TailwindExpressionBindingConditionalPrefixSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.True(root.ContainsDiagnostics);
        Assert.Equal(code, root.ToFullString());
    }

    [Theory]
    [InlineData("Vm.Enabled", "Vm.Ready && Vm.Width > 1000")]
    [InlineData("Vm.Enabled", "Vm.User?.Enabled == true")]
    [InlineData("Vm.Enabled", "Vm.Items[Vm.SelectedIndex].Enabled")]
    [InlineData("Vm.Enabled", "(Vm.Enabled")]
    [InlineData("x.bind", "Binding")]
    [InlineData("x.bind", "hover")]
    [InlineData("x.bind", "x.bindable")]
    public void IncrementalEdits_MatchFreshSyntaxAndDiagnostics(string before, string after)
    {
        var code = "state int before = 1;\r\n<TextBlock ${x.bind Vm.Enabled}:hidden Width=\"100\"/>\r\n<Button/>";
        var tree = ComponentSyntaxTree.ParseText(code);
        var offset = code.IndexOf(before, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var updated = tree.WithChangedText(tree.Text.WithChanges(new TextChange(new TextSpan(offset, before.Length), after)));
        MarkupExpressionBindingIncrementalParserTests.AssertMatchesFreshParse(updated);
        var restored = updated.WithChangedText(updated.Text.WithChanges(new TextChange(new TextSpan(offset, after.Length), before)));
        MarkupExpressionBindingIncrementalParserTests.AssertMatchesFreshParse(restored);
    }

    [Theory]
    [InlineData(' ')]
    [InlineData('}')]
    [InlineData(':')]
    [InlineData('>')]
    public void IncrementalSingleCharacterEdits_IncludePrefixAndLexerBoundaries(char inserted)
    {
        const string prefix = "<TextBlock ";
        const string condition = "${x.bind Vm.Items[Vm.SelectedIndex].Enabled}:hidden";
        var tree = ComponentSyntaxTree.ParseText(prefix + condition + " Width=\"100\"/><Button/>");
        for (var offset = 0; offset <= condition.Length; offset++)
        {
            try
            {
                var changed = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length + offset, 0), inserted.ToString()));
                MarkupExpressionBindingIncrementalParserTests.AssertMatchesFreshParse(tree.WithChangedText(changed));
                if (offset < condition.Length)
                {
                    changed = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length + offset, 1), string.Empty));
                    MarkupExpressionBindingIncrementalParserTests.AssertMatchesFreshParse(tree.WithChangedText(changed));
                }
            }
            catch (Xunit.Sdk.XunitException exception)
            {
                Assert.Fail($"Condition offset {offset}, inserted '{inserted}': {exception}");
            }
        }
    }

    [Fact]
    public void IncrementalWhitespaceBoundary_PreservesFreshParseTriviaAcrossLineEndings()
    {
        foreach (var trivia in new[] { " ", "\t", "\r\n", "\n", "\r", "\u2028" })
        foreach (var inserted in new[] { " ", "\t", "\r\n", "\n" })
        {
            var prefix = "<TextBlock" + trivia;
            var tree = ComponentSyntaxTree.ParseText(prefix + "${x.bind Vm.Enabled}:hidden/>");
            var changed = tree.Text.WithChanges(new TextChange(new TextSpan(prefix.Length, 0), inserted));
            MarkupExpressionBindingIncrementalParserTests.AssertMatchesFreshParse(tree.WithChangedText(changed));
        }
    }

    [Fact]
    public void ExistingConditionalForms_KeepTheirOriginalSyntax()
    {
        const string code = "<TextBlock {width > 1000}:hidden ${hover}:bg-blue-500 Text=${x.bind Vm.Name} Tag=${Binding Name}/>";
        var root = ComponentSyntaxTree.ParseText(code).GetRoot();
        Assert.Single(root.DescendantNodes().OfType<ExpressionConditionalPrefixSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExtensionConditionalPrefixSyntax>());
        Assert.Single(root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>());
        Assert.Empty(root.DescendantNodes().OfType<TailwindExpressionBindingConditionalPrefixSyntax>());
        Assert.Equal(code, root.ToFullString());
    }

    private sealed class Rewriter(CSharpExpressionSyntax replacement) : SyntaxRewriter
    {
        public override AkburaSyntax VisitCSharpExpressionSyntax(CSharpExpressionSyntax node) => replacement;
    }

    private sealed class GreenRewriter(GreenCSharpExpressionSyntax replacement) : GreenSyntaxRewriter
    {
        public override GreenNode VisitCSharpExpressionSyntax(GreenCSharpExpressionSyntax node) => replacement;
    }
}
