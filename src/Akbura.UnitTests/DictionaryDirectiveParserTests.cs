using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class DictionaryDirectiveParserTests
{
    [Theory]
    [InlineData("x.key=\"a b\"", false)]
    [InlineData("x.Key=\"Mixed CASE\"", false)]
    [InlineData("x.key={count + 1}", true)]
    [InlineData("x.Key={count + 1}", true)]
    public void DictionaryDirective_ReusesAttachedAttributeAndExistingValueKinds(
        string source,
        bool dynamic)
    {
        using var parser = ParserHelper.MakeParser(source);
        var attribute = Assert.IsType<GreenMarkupAttachedPropertyAttributeSyntax>(
            parser.ParseMarkupAttributeSyntax());

        Assert.Equal(source, attribute.ToFullString());
        Assert.Equal("x", attribute.OwnerType.ToFullString().Trim());
        if (dynamic)
        {
            Assert.IsType<GreenMarkupDynamicAttributeValueSyntax>(attribute.Value);
        }
        else
        {
            Assert.IsType<GreenMarkupLiteralAttributeValueSyntax>(attribute.Value);
        }
    }

    [Theory]
    [InlineData("\"a b\"", "{count + 1}")]
    [InlineData("{count + 1}", "\"a b\"")]
    public void DictionaryDirective_IncrementalValueKindChangeMatchesFullParse(
        string oldValue,
        string newValue)
    {
        var oldSource = "<Brush x.Key=" + oldValue + " Color=\"Red\"/>";
        var newSource = "<Brush x.Key=" + newValue + " Color=\"Red\"/>";
        var oldTree = Parse(oldSource);
        var oldRed = (AkburaDocumentSyntax)oldTree.CreateRed();
        var valueStart = oldSource.IndexOf(oldValue, StringComparison.Ordinal);
        using var parser = ParserHelper.MakeIncrementalParser(newSource, oldRed,
            [new TextChangeRange(new TextSpan(valueStart, oldValue.Length), newValue.Length)]);
        var incremental = parser.ParseCompilationUnit();
        var full = Parse(newSource);

        Assert.Equal(newSource, incremental.ToFullString());
        AssertTreeEqual(full, incremental);
    }

    [Theory]
    [InlineData("<Brush x.k")]
    [InlineData("<Brush x.Key=\"unfinished")]
    [InlineData("<Brush x.Key={count +")]
    public void DictionaryDirective_IncompleteInputRoundTrips(string source)
    {
        var tree = Parse(source);

        Assert.Equal(source, tree.ToFullString());
        Assert.NotEmpty(((AkburaDocumentSyntax)tree.CreateRed()).DescendantTokens());
    }

    private static GreenAkburaDocumentSyntax Parse(string source)
    {
        using var parser = ParserHelper.MakeParser(source);
        return parser.ParseCompilationUnit();
    }

    private static void AssertTreeEqual(GreenNode expected, GreenNode actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.ToFullString(), actual.ToFullString());
        Assert.Equal(expected.SlotCount, actual.SlotCount);
        Assert.Equal(expected.IsMissing, actual.IsMissing);
        Assert.Equal(
            expected.GetDiagnostics().Select(static diagnostic => diagnostic.Code), 
            actual.GetDiagnostics().Select(static diagnostic => diagnostic.Code));

        for (var i = 0; i < expected.SlotCount; i++)
        {
            var expectedChild = expected.GetSlot(i);
            var actualChild = actual.GetSlot(i);
            Assert.Equal(expectedChild == null, actualChild == null);
            if (expectedChild != null && actualChild != null)
            {
                AssertTreeEqual(expectedChild, actualChild);
            }
        }
    }
}
