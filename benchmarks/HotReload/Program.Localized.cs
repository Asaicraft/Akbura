using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

internal static partial class Program
{
    private static string[] LocalizedScenarios =>
    [
        "localized-literal", "localized-property", "localized-akcss", "localized-binding-sibling", "localized-expression",
        "localized-property-add-remove", "localized-structure", "localized-name", "localized-diagnostic", "localized-trivia",
    ];

    // Edit an existing element of the real component, not an isolated synthetic consumer.
    // Inputs are reconstructed from the fixed original on every revision (including undo).
    private static string LocalizedSource(string source, string path, string scenario, int index)
    {
        var root = ComponentSyntaxTree.ParseText(SourceText.From(source), path).GetRootSyntax();
        var allTags = root.DescendantNodesAndSelf().OfType<MarkupStartTagSyntax>().ToArray();
        var tags = allTags.Where(tag => tag.Name.ToString().Trim() == "TextBlock").ToArray();
        Assert.NotEmpty(tags);
        // Padding utilities require a control with Padding; TextBlock has none.
        var tag = scenario == "localized-akcss" ? allTags.First(tag => tag.Name.ToString().Trim() is "Border" or "Button") :
            scenario is "localized-binding-sibling" or "localized-expression"
            ? tags.First(tag => tag.Attributes.OfType<MarkupPlainAttributeSyntax>().Any(attribute =>
                attribute.Name.ToString().Trim() == "Text" && attribute.Value is MarkupDynamicAttributeValueSyntax))
            : tags[0];
        var alternate = index % 2 != 0;
        var value = new[] { 10, 20, 30, 10, 40 }[index % 5];
        return scenario switch
        {
            "localized-literal" or "localized-binding-sibling" => SetProperty("Margin", "\"" + value + "\""),
            "localized-property" => SetProperty("Width", "\"" + (alternate ? 250 : 200) + "\""),
            "localized-akcss" => source.Insert(tag.Name.Span.End, alternate ? " p-5" : " p-4"),
            "localized-expression" => ChangeExpression(),
            "localized-property-add-remove" => alternate ? SetProperty("Opacity", "\"0.5\"") : source,
            "localized-structure" => alternate ? source.Insert(tag.Parent!.FullSpan.Start, "<Border Height=\"1\" />\r\n") : source,
            "localized-name" => source.Insert(tag.Name.Span.End, alternate ? " x.Name=\"FineEditB\"" : " x.Name=\"FineEditA\""),
            "localized-diagnostic" => alternate ? source.Insert(tag.Name.Span.End, " FineEditMissingProperty=\"1\"") : source,
            "localized-trivia" => alternate ? source.Insert(tag.Parent!.FullSpan.Start, "\r\n    ") : source,
            _ => throw new ArgumentException(scenario),
        };

        string SetProperty(string name, string valueText)
        {
            var attribute = tag.Attributes.OfType<MarkupPlainAttributeSyntax>().SingleOrDefault(attribute => attribute.Name.ToString().Trim() == name);
            return attribute == null ? source.Insert(tag.Name.Span.End, " " + name + "=" + valueText) :
                source.Remove(attribute.Span.Start, attribute.Span.Length).Insert(attribute.Span.Start, name + "=" + valueText);
        }

        string ChangeExpression()
        {
            var attribute = tag.Attributes.OfType<MarkupPlainAttributeSyntax>().Single(attribute => attribute.Name.ToString().Trim() == "Text");
            var expression = ((MarkupDynamicAttributeValueSyntax)attribute.Value!).Expression.ToString().Trim();
            // InlineExpression includes its braces; retain the exact original interior.
            Assert.StartsWith("{", expression);
            Assert.EndsWith("}", expression);
            var changed = "{(" + expression[1..^1] + ") + \" edit " + index + "\"}";
            return source.Remove(attribute.Value!.Span.Start, attribute.Value.Span.Length).Insert(attribute.Value.Span.Start, changed);
        }
    }

    private static object SyntaxReuse(AkburaSyntax? previous, AkburaSyntax current)
    {
        var oldGreen = previous?.DescendantNodesAndSelf().Select(node => node.Green).ToHashSet(ReferenceEqualityComparer.Instance) ?? [];
        var nodes = current.DescendantNodesAndSelf().ToArray();
        var reused = nodes.Count(node => oldGreen.Contains(node.Green));
        var embedded = nodes.OfType<CSharpExpressionSyntax>().ToArray();
        return new { Nodes = nodes.Length, SharedGreenNodes = reused, NewGreenNodes = nodes.Length - reused,
            EmbeddedExpressions = embedded.Length, SharedEmbeddedExpressions = embedded.Count(node => oldGreen.Contains(node.Green)),
            RedRootReused = ReferenceEquals(previous, current) };
    }
}
