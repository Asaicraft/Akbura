using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class AkcssApplyItemFactsTests
{
    [Theory]
    [InlineData(".card { @apply surface; }", new[] { "surface" })]
    [InlineData(".card { @apply first second; }", new[] { "first", "second" })]
    [InlineData(".card { @apply first\tsecond\nthird; }", new[] { "first", "second", "third" })]
    [InlineData(".card { @apply first second }", new[] { "first", "second" })]
    [InlineData(".card { @apply first\u2003second; }", new[] { "first", "second" })]
    public void GetItems_UsesSharedWhitespaceProtocol(
        string source,
        string[] expected)
    {
        var tree = AkcssSyntaxTree.ParseText(SourceText.From(source));
        var apply = Assert.Single(
            tree.GetRoot().DescendantNodes()
                .OfType<AkcssApplyDirectiveSyntax>());

        var items = AkcssApplyItemFacts.GetItems(tree.Text, apply);

        Assert.Equal(expected, items.Select(static item => item.Text));
        Assert.All(items, item =>
            Assert.Equal(item.Text, tree.Text.ToString(item.Span)));
    }

    [Fact]
    public void ReferenceLookup_DoesNotIncludeTrailingWhitespace()
    {
        const string source = ".card { @apply surface  gap-4; }";
        var tree = AkcssSyntaxTree.ParseText(SourceText.From(source));
        var apply = Assert.Single(
            tree.GetRoot().DescendantNodes()
                .OfType<AkcssApplyDirectiveSyntax>());
        var whitespace = source.IndexOf("  gap", StringComparison.Ordinal) + 1;

        Assert.False(AkcssApplyItemFacts.TryGetReferenceItem(
            tree.Text,
            apply,
            whitespace,
            out _));
    }

    [Fact]
    public void CompletionLookup_IncludesItemEndAndEmptyItem()
    {
        const string source = ".card { @apply surface  ; }";
        var tree = AkcssSyntaxTree.ParseText(SourceText.From(source));
        var apply = Assert.Single(
            tree.GetRoot().DescendantNodes()
                .OfType<AkcssApplyDirectiveSyntax>());
        var surfaceEnd = source.IndexOf("surface", StringComparison.Ordinal) +
            "surface".Length;
        var emptyPosition = surfaceEnd + 1;

        Assert.True(AkcssApplyItemFacts.TryGetCompletionItem(
            tree.Text,
            apply,
            surfaceEnd,
            out var surface));
        Assert.Equal("surface", surface.Text);
        Assert.True(AkcssApplyItemFacts.TryGetCompletionItem(
            tree.Text,
            apply,
            emptyPosition,
            out var empty));
        Assert.Equal(new TextSpan(emptyPosition, 0), empty.Span);
        Assert.Equal(string.Empty, empty.Text);
    }

    [Fact]
    public void MissingSemicolon_DoesNotConsumeContainingCloseBrace()
    {
        const string source = """
            @utilities {
                .first {
                    @apply surface
                }

                .second {
                }
            }
            """;

        var tree = AkcssSyntaxTree.ParseText(source, "Styles.akcss");
        var root = Assert.IsType<AkcssDocumentSyntax>(
            tree.GetRootSyntax());
        var utilities = Assert.Single(
            root.Members
                .OfType<AkcssUtilitiesSectionSyntax>());

        Assert.Equal(2, utilities.Utilities.Count);
        Assert.Equal(
            "second",
            utilities.Utilities[1].Selector.Name.Identifier.ValueText);
        Assert.Single(
            utilities.Utilities[0]
                .Members
                .OfType<AkcssApplyDirectiveSyntax>());
    }
}
