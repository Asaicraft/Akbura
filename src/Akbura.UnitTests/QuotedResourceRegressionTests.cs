using System.Collections.Immutable;
using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class QuotedResourceRegressionTests
{
    private const string Prefix = "state int count = 0;\r\n\r\n<NavIcon Geometries=${StaticResource ";
    private const string Suffix = "} Tag=\"keep\" />";

    [Theory]
    [InlineData("Icon.Home")]
    [InlineData("\"Icon.Home\"")]
    [InlineData("'Icon.Home'")]
    public void FullDocument_PreservesResourceArgument(string argument)
    {
        var source = Prefix + argument + Suffix;
        var root = AkburaSyntaxTree.ParseText(source, "MainView.akbura").GetRoot();

        Assert.Equal(source, root.ToFullString());
        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, argument);
    }

    [Theory]
    [InlineData("\"Icon.Home\"")]
    [InlineData("'Icon.Home'")]
    [InlineData("Icon.Home")]
    public void CharacterwiseTyping_WithStatePrefix_MatchesFullParse(string argument)
    {
        var text = SourceText.From(Prefix + Suffix);
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        var position = Prefix.Length;
        foreach (var character in argument)
        {
            root = ApplyAndCompare(root, ref text,
                new TextChange(new TextSpan(position++, 0), character.ToString()));
        }

        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, argument);
    }

    [Fact]
    public void ClosingQuote_AtAggregateEnd_IsConsumedByTheArgument()
    {
        var text = SourceText.From(Prefix + "\"Icon.Home" + Suffix);
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        root = ApplyAndCompare(root, ref text,
            new TextChange(new TextSpan(Prefix.Length + "\"Icon.Home".Length, 0), "\""));

        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, "\"Icon.Home\"");
    }

    [Fact]
    public void QuoteDeletionAndReinsertion_DoesNotLeaveStaleDiagnostics()
    {
        var text = SourceText.From(Prefix + "\"Icon.Home\"" + Suffix);
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        var closingQuote = Prefix.Length + "\"Icon.Home".Length;
        root = ApplyAndCompare(root, ref text,
            new TextChange(new TextSpan(closingQuote, 1), string.Empty));
        root = ApplyAndCompare(root, ref text,
            new TextChange(new TextSpan(closingQuote, 0), "\""));

        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, "\"Icon.Home\"");
    }

    [Fact]
    public void Replacement_AtArgumentEnd_RelexesTheExtendedLiteral()
    {
        var text = SourceText.From(Prefix + "Icon" + Suffix);
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        // This is a replacement of '}', not an insertion. The old argument
        // ends at the start of the replacement and must not be reused.
        root = ApplyAndCompare(root, ref text,
            new TextChange(new TextSpan(Prefix.Length + "Icon".Length, 1), ".Home}"));

        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, "Icon.Home");
    }

    [Fact]
    public void DeletingAnArgumentSeparator_MatchesFullParse()
    {
        const string prefix = "<NavIcon Geometries=${MyExtension ";
        var text = SourceText.From(prefix + "Icon,Home} />");
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        root = ApplyAndCompare(root, ref text,
            new TextChange(new TextSpan(prefix.Length + "Icon".Length, 1), string.Empty));

        Assert.Empty(AllDiagnostics(root));
        AssertArgument(root, "IconHome");
    }

    [Theory]
    [InlineData("ERR_LbraceExpected", "'{' expected.")]
    [InlineData("ERR_RbraceExpected", "'}' expected.")]
    public void BraceDiagnostic_IsAValidCompositeFormat(string code, string expected)
    {
        // Report the actual embedded format before string.Format can obscure
        // a stale/unrebuilt resource with a FormatException stack trace.
        var expectedFormat = code == "ERR_LbraceExpected"
            ? "'{{' expected."
            : "'}}' expected.";
        var actualFormat = AkburaResources.ResourceManager.GetString(code);
        Assert.True(actualFormat == expectedFormat,
            $"Loaded resource {code}: '{actualFormat}'. Expected '{expectedFormat}'. " +
            $"Assembly: {typeof(AkburaDiagnostic).Assembly.Location}; " +
            $"MVID: {typeof(AkburaDiagnostic).Assembly.ManifestModule.ModuleVersionId}.");

        var diagnostic = new AkburaDiagnostic(
            ImmutableArray<object?>.Empty, code, AkburaDiagnosticSeverity.Error);
        Assert.Equal(expected, diagnostic.Message);
    }

    [Fact]
    public void ActuallyMissingBrace_IsStillReportedWithAReadableMessage()
    {
        var root = AkburaSyntaxTree.ParseText(Prefix + "\"Icon.Home\"", "MainView.akbura").GetRoot();
        var extension = Assert.Single(root.DescendantNodes().OfType<MarkupExtensionSyntax>());
        Assert.True(extension.CloseBrace.IsMissing);
        var diagnostic = Assert.Single(AllDiagnostics(root).Where(d => d.Code == "ERR_RbraceExpected"));
        Assert.Equal("'}' expected.", diagnostic.Message);
    }

    [Fact]
    public void EditingAnotherArgument_PreservesTheUnchangedLiteralNode()
    {
        const string source =
            "<NavIcon Geometries=${MyExtension \"Icon.Home\", Value={1}} Tag=\"keep\" />";
        var text = SourceText.From(source);
        var root = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        var before = Assert.Single(root.DescendantNodes().OfType<MarkupExtensionSyntax>());
        var position = source.IndexOf("{1}", StringComparison.Ordinal) + 1;
        root = ApplyAndCompare(root, ref text, new TextChange(new TextSpan(position, 1), "2"));
        var after = Assert.Single(root.DescendantNodes().OfType<MarkupExtensionSyntax>());

        Assert.Empty(AllDiagnostics(root));
        Assert.Same(before.Arguments[0].Green, after.Arguments[0].Green);
    }

    private static AkburaDocumentSyntax ApplyAndCompare(
        AkburaDocumentSyntax previous, ref SourceText text, TextChange change)
    {
        text = text.WithChanges(change);
        // Invoke the incremental parser directly. ComponentSyntaxTree's
        // full-width fallback must not turn this into a passing full parse.
        using var parser = new Parser(new Lexer(text), CancellationToken.None, previous,
            new[] { new TextChangeRange(change.Span, change.NewText?.Length ?? 0) });
        var actual = (AkburaDocumentSyntax)parser.ParseCompilationUnit().CreateRed();
        var expected = AkburaSyntaxTree.ParseText(text, "MainView.akbura").GetRoot();
        Assert.Equal(text.ToString(), actual.ToFullString());
        try
        {
            Assert.Equal(Shape(expected), Shape(actual));
        }
        catch (Xunit.Sdk.XunitException error)
        {
            throw new Xunit.Sdk.XunitException(
                $"Edit {change.Span}, inserted '{change.NewText}'.\n" +
                $"Source: {text.ToString().Replace("\r", "\\r").Replace("\n", "\\n")}\n" +
                error.Message);
        }
        return actual;
    }

    private static IEnumerable<AkburaDiagnostic> AllDiagnostics(AkburaSyntax root) =>
        root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
            .SelectMany(node => node.GetDiagnostics());

    private static object[] Shape(AkburaSyntax root) => root.DescendantNodesAndTokensAndSelf(descendIntoTrivia: true)
        .Select(node => (object)(node.RawKind, node.Span, node.FullSpan, node.IsMissing,
            string.Join("|", node.GetDiagnostics().Select(d => d.Code))))
        .ToArray();

    private static void AssertArgument(AkburaDocumentSyntax root, string expected)
    {
        var extension = Assert.Single(root.DescendantNodes().OfType<MarkupExtensionSyntax>());
        Assert.Equal(1, extension.Arguments.Count);
        var argument = Assert.IsType<MarkupExtensionPositionalArgumentSyntax>(extension.Arguments[0]);
        var literal = Assert.IsType<MarkupExtensionLiteralValueSyntax>(argument.Value);
        Assert.Equal(expected, literal.Value.ToFullString().Trim());
        Assert.False(extension.CloseBrace.IsMissing);
    }
}
