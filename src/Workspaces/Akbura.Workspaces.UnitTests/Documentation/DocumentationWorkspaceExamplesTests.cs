using Akbura.Language.Syntax;
using Akbura.TestUtilities.Documentation;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class DocumentationWorkspaceExamplesTests
{
    public static IEnumerable<object[]> Examples() => DocumentationExampleCatalog.Positive
        .Select(example => new object[] { example.Id });

    public static IEnumerable<object[]> ControlFlowExamples() => DocumentationExampleCatalog.Positive
        .Where(example => example.Path == DocumentationExampleCatalog.ForeachPage ||
            example.Path == DocumentationExampleCatalog.ConditionalPage)
        .Select(example => new object[] { example.Id });

    public static IEnumerable<object[]> ExpressionExamples() => DocumentationExampleCatalog.Positive
        .Where(example => example.Path == DocumentationExampleCatalog.ExpressionBindingsPage)
        .Select(example => new object[] { example.Id });

    [Theory]
    [MemberData(nameof(ExpressionExamples))]
    public void DocumentedExpressions_KeepEmbeddedCSharpAndMarkupExtensionSpansDistinct(string id)
    {
        var source = Source(id);
        var document = Parse(source);
        var root = document.SyntaxTree.GetRootSyntax();
        using var workspace = new AkburaWorkspace();
        var spans = workspace.LanguageServices.Classification.GetSyntacticClassifications(document,
            new TextSpan(0, source.Length));
        var bindings = root.DescendantNodes().OfType<MarkupExpressionBindingSyntax>().ToArray();
        foreach (var binding in bindings)
        {
            Assert.Contains(spans, span => span.Span == binding.BindKeyword.Span && span.Kind == AkburaClassificationKind.Directive);
            Assert.Equal(binding.Expression.ToFullString(), source.Substring(
                binding.Expression.FullSpan.Start, binding.Expression.FullSpan.Length));
            Assert.True(document.TryGetCSharpCompletionContext(binding.Expression.Span.Start + 1, out var csharp));
            Assert.Equal(SyntaxKind.MarkupExpressionBindingSyntax, csharp.OwnerKind);
        }
        var extensions = root.DescendantNodes().OfType<MarkupExtensionSyntax>().ToArray();
        if (source.Contains("${Binding ", StringComparison.Ordinal))
        {
            Assert.NotEmpty(extensions);
            Assert.All(extensions, extension => Assert.Equal("Binding", extension.Type.Name.ToString().Trim()));
        }
        if (source.Contains("${x.bind ", StringComparison.Ordinal)) Assert.NotEmpty(bindings);
        var conditions = root.DescendantNodes().OfType<TailwindExpressionBindingConditionalPrefixSyntax>().ToArray();
        var expected = id switch
        {
            "expression-binding.utility-reactive-expression" or "expression-binding.utility-comparison" => 1,
            "expression-binding.utility-cascade" => 2,
            _ => 0,
        };
        Assert.Equal(expected, conditions.Length);
        Assert.All(conditions, condition => Assert.Contains(condition.Binding, bindings));
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void DocumentedSource_RoundTripsAndHasSyntacticClassifications(string id)
    {
        var source = Source(id);
        var document = Parse(source);
        Assert.Equal(source, document.SyntaxTree.GetRootSyntax().ToFullString());
        Assert.Empty(document.SyntaxTree.GetRootSyntax().GetDiagnostics());
        using var workspace = new AkburaWorkspace();
        var spans = workspace.LanguageServices.Classification.GetSyntacticClassifications(document,
            new TextSpan(0, source.Length));
        Assert.NotEmpty(spans);
        Assert.All(spans, span => Assert.InRange(span.Span.End, 0, source.Length));
    }

    [Theory]
    [MemberData(nameof(ControlFlowExamples))]
    public void DocumentedControlFlow_ClassifiesDirectiveAndKeywordAtTheirExactSpans(string id)
    {
        var source = Source(id);
        var document = Parse(source);
        using var workspace = new AkburaWorkspace();
        var spans = workspace.LanguageServices.Classification.GetSyntacticClassifications(document,
            new TextSpan(0, source.Length));
        var checkedTokens = 0;
        foreach (var token in document.SyntaxTree.GetRootSyntax().DescendantTokens())
        {
            if (token.IsMissing || token.Span.Length == 0) continue;
            var expected = token.Kind switch
            {
                SyntaxKind.DollarToken when token.Parent is MarkupForeachStatementSyntax or
                    MarkupIfStatementSyntax or MarkupElseIfClauseSyntax or MarkupElseClauseSyntax
                    => AkburaClassificationKind.Directive,
                SyntaxKind.ForeachKeyword or SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword
                    => AkburaClassificationKind.Keyword,
                _ => (AkburaClassificationKind?)null,
            };
            if (expected == null) continue;
            checkedTokens++;
            Assert.Contains(spans, span => span.Span == token.Span && span.Kind == expected);
        }
        Assert.True(checkedTokens >= 2, $"No control-flow tokens were checked in {id}.");
    }

    [Theory]
    [InlineData("foreach.guards", "\n")]
    [InlineData("foreach.guards", "\r\n")]
    [InlineData("foreach.nested", "\n")]
    [InlineData("foreach.nested", "\r\n")]
    public void DocumentedLoops_LinewiseEditsKeepFullAndIncrementalClassificationsEquivalent(string id, string newline)
    {
        // Newline variants are explicit editor scenarios. Compilation tests use
        // the original embedded Markdown block without newline normalization.
        var source = Source(id).Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", newline);
        var fullText = SourceText.From(source);
        var current = SourceText.From(string.Empty);
        var document = Parse(string.Empty);
        using var workspace = new AkburaWorkspace();
        foreach (var line in fullText.Lines)
        {
            var inserted = fullText.ToString(line.SpanIncludingLineBreak);
            if (inserted.Length == 0) continue;
            current = current.WithChanges(new TextChange(new TextSpan(current.Length, 0), inserted));
            document = document.WithText(current);
            var fresh = Parse(current.ToString());
            var requested = new TextSpan(0, current.Length);
            Assert.Equal(current.ToString(), document.SyntaxTree.GetRootSyntax().ToFullString());
            var expected = workspace.LanguageServices.Classification.GetSyntacticClassifications(fresh, requested)
                .Select(span => (span.Span, span.Kind)).ToArray();
            var actual = workspace.LanguageServices.Classification.GetSyntacticClassifications(document, requested)
                .Select(span => (span.Span, span.Kind)).ToArray();
            Assert.True(expected.SequenceEqual(actual),
                $"Classification mismatch in {id} after documentation source line {line.LineNumber + 1}; " +
                $"newline={(newline == "\n" ? "LF" : "CRLF")}.\n" + current);
        }
        Assert.Equal(source, document.Text.ToString());
    }

    private static string Source(string id)
    {
        var example = DocumentationExampleCatalog.Get(id);
        return example.CompleteSource(example.ReadBlock());
    }

    private static AkburaSyntacticDocument Parse(string source) =>
        AkburaSyntacticDocument.Parse(SourceText.From(source), "DocumentationExample.akbura");
}
