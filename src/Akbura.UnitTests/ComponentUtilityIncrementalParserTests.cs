using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class ComponentUtilityIncrementalParserTests
{
    [Theory]
    [InlineData(" ", "${md}:page-desktop")]
    [InlineData("\r\n            ", "${md}:page-desktop")]
    [InlineData(" /* gap */ ", "${md}:page-desktop")]
    [InlineData(" ", "{isHighlighted}:page-desktop")]
    [InlineData("\r\n            ", "{isHighlighted}:page-desktop")]
    [InlineData(" /* gap */ ", "{isHighlighted}:page-desktop")]
    public void NestedLiteralEdit_AfterSeparatedUtilityPrefixes_MatchesFreshInBothDirections(string separator, string prefix)
    {
        var source =
            "<StackPanel page" + separator + prefix + ">\r\n" +
            "    <TextBlock Text=\"AKCSS\" />\r\n" +
            "</StackPanel>\r\n";

        AssertRoundTrip(source, containsDiagnostics: false);
    }

    [Theory]
    [InlineData(" ", "${md}:page-desktop")]
    [InlineData("\r\n            ", "${md}:page-desktop")]
    [InlineData(" /* gap */ ", "${md}:page-desktop")]
    [InlineData(" ", "{isHighlighted}:page-desktop")]
    [InlineData("\r\n            ", "{isHighlighted}:page-desktop")]
    [InlineData(" /* gap */ ", "{isHighlighted}:page-desktop")]
    public void FlagRenameBeforeSeparatedUtilityPrefix_MatchesFreshInBothDirections(string separator, string prefix)
    {
        var source = "<StackPanel page" + separator + prefix + " />\r\n";

        // Changing the flag itself prevents reusing its complete attribute node
        // and forces the incremental attribute-start classifier to run.
        AssertRoundTrip(source, false, "page" + separator, "shell" + separator);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void RealGalleryPageTitleEdit_WithDifferentChangeRanges_MatchesFreshInBothDirections(bool replaceWholeAttribute, bool trackChanges)
    {
        using var stream = typeof(ComponentUtilityIncrementalParserTests).Assembly.GetManifestResourceStream(
            "Akbura.UnitTests.Fixtures.AkcssPage.akbura");
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var source = reader.ReadToEnd();

        // The benchmark replaces the complete attribute, not just its value.
        // Untracked SourceText also covers the full-span fallback edit range.
        AssertRoundTrip(
            source,
            containsDiagnostics: false,
            replaceWholeAttribute ? "Text=\"AKCSS\"" : "AKCSS",
            replaceWholeAttribute ? "Text=\"AKCSS styles\"" : "AKCSS styles",
            trackChanges,
            uniqueAnchor: "Text=\"AKCSS\"");
    }

    [Fact]
    public void GalleryPageTitleEdit_WithNestedAndSiblingUtilities_MatchesFreshInBothDirections()
    {
        // Keep the relevant opening of Pages/AkcssPage.akbura as a standalone
        // parser fixture, without depending on repository files at test runtime.
        const string source =
            "using Akbura.FeatureGallery.Pages.Page.akcss;\r\n" +
            "using Akbura.Markup;\r\n" +
            "using Akbura.FeatureGallery.Models;\r\n" +
            "using Akbura.FeatureGallery.Markup;\r\n\r\n" +
            "<StackPanel page\r\n            ${md}:page-desktop>\r\n" +
            "    <StackPanel page-header>\r\n" +
            "        <TextBlock Text=\"AKCSS\" page-title ${md}:page-title-desktop />\r\n" +
            "        <TextBlock Text=\"Description\" page-description ${md}:page-description-desktop />\r\n" +
            "    </StackPanel>\r\n" +
            "    <Grid page-grid-3 ${lg}:page-grid-3-desktop>\r\n" +
            "        <Border page-cell-2 ${lg}:page-cell-2-desktop page-card />\r\n" +
            "    </Grid>\r\n" +
            "</StackPanel>\r\n";

        AssertRoundTrip(source, containsDiagnostics: false);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    [InlineData(true, false)]
    public void RootUtilityInsertion_WithNestedConditionalUtilities_MatchesFreshInBothDirections(
        bool replaceWholeOpeningTag,
        bool trackChanges)
    {
        const string source =
            "<Border>\r\n" +
            "    <Grid ColumnDefinitions=\"Auto, *\">\r\n" +
            "        <Border {IsActive}:bg-teal-300 {!IsActive}:bg-white rounded-xl>\r\n" +
            "            {Icon}\r\n" +
            "        </Border>\r\n" +
            "        <Border pr-3 {IsCollapsed}:hidden>\r\n" +
            "            <TextBlock {IsActive}:text-grey-700 {!IsActive}:text-grey-400 Text={Text} />\r\n" +
            "        </Border>\r\n" +
            "    </Grid>\r\n" +
            "</Border>\r\n";
        const string utilities = " px-4 py-3 rounded-2xl {IsActive}:bg-white";

        // An empty original value inserts at the root closing angle bracket;
        // the following Grid uniquely anchors that zero-width local edit.
        // The complete opening-tag replacement and untracked fallback cover
        // different change ranges without modifying any nested markup.
        AssertRoundTrip(
            source,
            containsDiagnostics: false,
            replaceWholeOpeningTag ? "<Border>" : "",
            replaceWholeOpeningTag ? "<Border" + utilities + ">" : utilities,
            trackChanges,
            uniqueAnchor: replaceWholeOpeningTag ? "<Border>" : ">\r\n    <Grid ColumnDefinitions=\"Auto, *\">");
    }

    [Theory]
    [InlineData("Viewbox", true)]
    [InlineData("Viewbox", false)]
    [InlineData("ViewBox", true)]
    [InlineData("ViewBox", false)]
    public void ViewboxWidthUtility_TypedCharacterByCharacter_ClearsIdentifierExpectedAndMatchesFresh(
        string componentName,
        bool trackChanges)
    {
        var source =
            "using System.Collections.ObjectModel;\r\n\r\n" +
            "namespace PurityUIDashboard;\r\n\r\n" +
            "param bool IsActive = false;\r\n" +
            "param ObservableCollection<StreamGeometry> Content;\r\n\r\n" +
            "<" + componentName + ">\r\n\r\n" +
            "</" + componentName + ">\r\n";
        var originalText = SourceText.From(source);
        var original = ComponentSyntaxTree.ParseText(originalText, "Components/NavIcon.akbura");
        var text = originalText;
        var tree = original;
        var openingTagStart = source.IndexOf("<" + componentName + ">", StringComparison.Ordinal);
        Assert.True(openingTagStart >= 0);
        var position = openingTagStart + 1 + componentName.Length;
        var typed = string.Empty;
        var failures = new List<string>();

        AssertMatchesFresh(tree, text, containsDiagnostics: false);

        // Simulate actual keystrokes: <Viewbox> -> <Viewbox > -> <Viewbox w>
        // -> <Viewbox w-> -> <Viewbox w-3> -> <Viewbox w-30>.
        foreach (var character in " w-30")
        {
            var changedText = text.WithChanges(new TextChange(new TextSpan(position, 0), character.ToString()));
            text = trackChanges ? changedText : SourceText.From(changedText.ToString());
            tree = tree.WithChangedText(text);
            position++;
            typed += character;

            var failure = Record.Exception(() => AssertTypingState(tree, text, typed));
            if (failure != null)
            {
                var diagnostics = string.Join(", ", GetTypingDiagnostics(tree).Select(static diagnostic => diagnostic.Code));
                failures.Add($"After inserting '{typed}' (diagnostics: {diagnostics}): {failure.Message}");
            }
        }

        Assert.Contains("<" + componentName + " w-30>", text.ToString(), StringComparison.Ordinal);
        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
        Assert.Empty(GetTypingDiagnostics(tree));

        // Backspace through the same intermediate states without replacing the parser tree.
        foreach (var character in " w-30".Reverse())
        {
            Assert.Equal(character, text[position - 1]);
            position--;
            var changedText = text.WithChanges(new TextChange(new TextSpan(position, 1), string.Empty));
            text = trackChanges ? changedText : SourceText.From(changedText.ToString());
            tree = tree.WithChangedText(text);
            typed = typed[..^1];

            AssertTypingState(tree, text, typed);
        }

        Assert.True(text.ContentEquals(originalText));
        AssertSameSyntax(new SyntaxNodeOrToken(original.GetRoot()), new SyntaxNodeOrToken(tree.GetRoot()));

        static void AssertTypingState(ComponentSyntaxTree incremental, SourceText currentText, string currentUtility)
        {
            var missingSegment = currentUtility == " w-";
            AssertMatchesFresh(incremental, currentText, containsDiagnostics: missingSegment);
            var diagnostics = GetTypingDiagnostics(incremental);

            if (missingSegment)
            {
                Assert.Contains(diagnostics, static diagnostic => diagnostic.Code == ErrorCodes.ERR_IdentifierExpected);
            }
            else
            {
                Assert.Empty(diagnostics);
            }
        }

        static AkburaDiagnostic[] GetTypingDiagnostics(ComponentSyntaxTree syntaxTree)
        {
            return syntaxTree.GetRoot().DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
                .SelectMany(static node => node.GetDiagnostics())
                .ToArray();
        }
    }

    [Theory]
    [InlineData("w", "-30")]
    [InlineData("w ", "-30")]
    [InlineData("w-3", "0")]
    [InlineData("w-3", ".5")]
    [InlineData("w-3", "d")]
    [InlineData("w-3", "auto")]
    [InlineData("w-3", "-0")]
    [InlineData("w-3 ", "-0")]
    [InlineData("hover", ":w-30")]
    [InlineData("hover ", ":w-30")]
    [InlineData("hover:w", "-30")]
    [InlineData("{IsActive}:w", "-30")]
    [InlineData("Text", "=\"A\"")]
    [InlineData("Text ", "=\"A\"")]
    public void AttributeBoundaryInsertion_MatchesFreshInBothDirections(string attribute, string suffix)
    {
        var source = "<Viewbox " + attribute + ">\r\n    <TextBlock Text=\"Body\" />\r\n</Viewbox>\r\n";

        AssertRoundTrip(source, false, "", suffix,
            uniqueAnchor: ">\r\n    <TextBlock Text=\"Body\" />");
    }

    [Fact]
    public void UtilityBoundaryReplacement_MatchesFreshInBothDirections()
    {
        // Replacing the closing angle bracket still extends the untouched flag on its left.
        AssertRoundTrip("<Viewbox w>\r\n</Viewbox>\r\n", false, ">", "-30>",
            uniqueAnchor: "<Viewbox w>");
    }

    [Fact]
    public void UtilitySegmentSeparatorDeletion_MergesNumericTokensAndMatchesFreshInBothDirections()
    {
        // Removing only the separator must re-lex 3 and 0 as one numeric segment, 30.
        AssertRoundTrip("<Viewbox w-3-0>\r\n</Viewbox>\r\n", false, "-", "",
            uniqueAnchor: "3-0");
    }

    [Theory]
    [InlineData("page ${md}:page-desktop Text=\"AKCSS\"")]
    [InlineData("page Text=\"AKCSS\" ${md}:page-desktop")]
    [InlineData("Text=\"AKCSS\" page ${md}:page-desktop")]
    [InlineData("page ${md}:page-desktop {isHighlighted}:highlight Text=\"AKCSS\" ${lg}:wide")]
    public void SameTagLiteralEdit_WithSurroundingUtilities_MatchesFreshInBothDirections(string attributes)
    {
        AssertRoundTrip("<TextBlock " + attributes + " />\r\n", containsDiagnostics: false);
    }

    [Theory]
    [InlineData("page${md}")]
    [InlineData("page{isHighlighted}")]
    public void AdjacentValueWithoutEquals_PreservesRecoveryDiagnosticsInBothDirections(string attribute)
    {
        AssertRoundTrip("<StackPanel " + attribute + " Text=\"AKCSS\" />\r\n", containsDiagnostics: true);
    }

    [Theory]
    [InlineData("Text = \"AKCSS\"")]
    [InlineData("Text\r\n    =\r\n    \"AKCSS\"")]
    [InlineData("Text /* before */ = /* after */ \"AKCSS\"")]
    public void ExplicitEqualsWithTrivia_RemainsPlainAttributeInBothDirections(string attribute)
    {
        AssertRoundTrip("<TextBlock page " + attribute + " ${md}:page-desktop />\r\n", containsDiagnostics: false);
    }

    private static void AssertRoundTrip(
        string source,
        bool containsDiagnostics,
        string originalValue = "AKCSS",
        string modifiedValue = "AKCSS styles",
        bool trackChanges = true,
        string? uniqueAnchor = null)
    {
        var anchor = uniqueAnchor ?? originalValue;
        var anchorStart = source.IndexOf(anchor, StringComparison.Ordinal);
        Assert.True(anchorStart >= 0);
        Assert.Equal(-1, source.IndexOf(anchor, anchorStart + anchor.Length, StringComparison.Ordinal));
        var offset = anchor.IndexOf(originalValue, StringComparison.Ordinal);
        Assert.True(offset >= 0);
        var start = anchorStart + offset;
        var originalText = SourceText.From(source);
        var original = ComponentSyntaxTree.ParseText(originalText, "Pages/AkcssPage.akbura");
        Assert.Equal(containsDiagnostics, original.GetRoot().ContainsDiagnostics);
        var modifiedText = originalText.WithChanges(new TextChange(new TextSpan(start, originalValue.Length), modifiedValue));
        if (!trackChanges)
        {
            modifiedText = SourceText.From(modifiedText.ToString());
            var change = Assert.Single(modifiedText.GetChangeRanges(originalText));
            Assert.Equal(new TextSpan(0, originalText.Length), change.Span);
            Assert.Equal(modifiedText.Length, change.NewLength);
        }

        var forward = original.WithChangedText(modifiedText);
        AssertMatchesFresh(forward, modifiedText, containsDiagnostics);
        var restoredText = modifiedText.WithChanges(new TextChange(new TextSpan(start, modifiedValue.Length), originalValue));
        if (!trackChanges)
        {
            restoredText = SourceText.From(restoredText.ToString());
            var change = Assert.Single(restoredText.GetChangeRanges(modifiedText));
            Assert.Equal(new TextSpan(0, modifiedText.Length), change.Span);
            Assert.Equal(originalText.Length, change.NewLength);
        }

        Assert.True(restoredText.ContentEquals(originalText));

        var restored = forward.WithChangedText(restoredText);
        AssertMatchesFresh(restored, restoredText, containsDiagnostics);
        AssertSameSyntax(new SyntaxNodeOrToken(original.GetRoot()), new SyntaxNodeOrToken(restored.GetRoot()));
    }

    private static void AssertMatchesFresh(ComponentSyntaxTree incremental, SourceText text, bool containsDiagnostics)
    {
        var fresh = ComponentSyntaxTree.ParseText(text, incremental.FilePath);
        Assert.Equal(text.ToString(), incremental.GetRoot().ToFullString());
        Assert.Equal(text.Length, incremental.GetRoot().FullWidth);
        Assert.Equal(containsDiagnostics, fresh.GetRoot().ContainsDiagnostics);
        Assert.Equal(fresh.GetRoot().ContainsDiagnostics, incremental.GetRoot().ContainsDiagnostics);
        Assert.Equal(fresh.GetRoot().ContainsSkippedText, incremental.GetRoot().ContainsSkippedText);
        AssertSameSyntax(new SyntaxNodeOrToken(fresh.GetRoot()), new SyntaxNodeOrToken(incremental.GetRoot()));
    }

    private static void AssertSameSyntax(SyntaxNodeOrToken expected, SyntaxNodeOrToken actual)
    {
        Assert.Equal(expected.Kind, actual.Kind);
        Assert.Equal(expected.IsToken, actual.IsToken);
        Assert.Equal(expected.IsMissing, actual.IsMissing);
        Assert.Equal(expected.Span, actual.Span);
        Assert.Equal(expected.FullSpan, actual.FullSpan);
        Assert.Equal(expected.ToFullString(), actual.ToFullString());
        Assert.Equal(DescribeDiagnostics(expected.GetDiagnostics()), DescribeDiagnostics(actual.GetDiagnostics()));

        if (expected.IsToken)
        {
            var expectedToken = expected.AsToken();
            var actualToken = actual.AsToken();
            Assert.Equal(expectedToken.ValueText, actualToken.ValueText);
            AssertSameTrivia(expectedToken.LeadingTrivia, actualToken.LeadingTrivia);
            AssertSameTrivia(expectedToken.TrailingTrivia, actualToken.TrailingTrivia);
            var expectedRaw = expectedToken.GetRawCSharpSyntax();
            var actualRaw = actualToken.GetRawCSharpSyntax();
            Assert.Equal(expectedRaw == null, actualRaw == null);
            if (expectedRaw != null && actualRaw != null)
            {
                Assert.Equal(
                    expectedRaw.DescendantNodesAndTokensAndSelf().Select(node => (node.RawKind, node.Span, node.FullSpan, node.ToFullString())).ToArray(),
                    actualRaw.DescendantNodesAndTokensAndSelf().Select(node => (node.RawKind, node.Span, node.FullSpan, node.ToFullString())).ToArray());
                Assert.Equal(
                    expectedRaw.GetDiagnostics().Select(diagnostic => (diagnostic.Id, diagnostic.Severity, diagnostic.Location.SourceSpan, diagnostic.GetMessage())).ToArray(),
                    actualRaw.GetDiagnostics().Select(diagnostic => (diagnostic.Id, diagnostic.Severity, diagnostic.Location.SourceSpan, diagnostic.GetMessage())).ToArray());
            }

            return;
        }

        var expectedChildren = expected.ChildNodesAndTokens();
        var actualChildren = actual.ChildNodesAndTokens();
        Assert.Equal(expectedChildren.Count, actualChildren.Count);
        for (var i = 0; i < expectedChildren.Count; i++)
        {
            AssertSameSyntax(expectedChildren[i], actualChildren[i]);
        }
    }

    private static void AssertSameTrivia(SyntaxTriviaList expected, SyntaxTriviaList actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (var i = 0; i < expected.Count; i++)
        {
            Assert.Equal(expected[i].Kind, actual[i].Kind);
            Assert.Equal(expected[i].Span, actual[i].Span);
            Assert.Equal(expected[i].FullSpan, actual[i].FullSpan);
            Assert.Equal(expected[i].ToFullString(), actual[i].ToFullString());
            Assert.Equal(DescribeDiagnostics(expected[i].GetDiagnostics()), DescribeDiagnostics(actual[i].GetDiagnostics()));
        }
    }

    private static (string Code, string Severity, string Message, int Position, int Width)[] DescribeDiagnostics(IEnumerable<AkburaDiagnostic> diagnostics)
    {
        return diagnostics.Select(diagnostic => (
            diagnostic.Code,
            diagnostic.Severity.ToString(),
            diagnostic.Message,
            diagnostic is SyntaxDiagnosticInfo position ? position.Position : 0,
            diagnostic is SyntaxDiagnosticInfo width ? width.Width : 0)).ToArray();
    }
}
