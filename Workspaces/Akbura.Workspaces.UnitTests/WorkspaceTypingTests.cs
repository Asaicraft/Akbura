using Akbura.Workspaces.AutomaticPairing;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.UnitTests;

public sealed class WorkspaceTypingTests
{
    private static readonly AkburaTypingOptions Options = new(
        TabSize: 4,
        IndentSize: 4,
        InsertSpaces: true,
        NewLine: "\r\n");

    [Theory]
    [InlineData("|", '<', "<>", 1)]
    [InlineData("state object value = Call|;", '(', "()", 1)]
    [InlineData("state object value = items|;", '[', "[]", 1)]
    [InlineData("state object value = new List|;", '<', "<>", 1)]
    [InlineData("state string text = |;", '"', "\"\"", 1)]
    [InlineData("<Button Content=|/>", '"', "\"\"", 1)]
    [InlineData("<StackPanel>\r\n    |\r\n</StackPanel>", '<', "<>", 1)]
    public void TypeCreatesSyntaxAwareFixedPair(
        string sourceWithCaret,
        char character,
        string insertedText,
        int caretDelta)
    {
        var (document, position) = Parse(sourceWithCaret);

        var result = Type(document, position, character);

        Assert.True(result.Handled);
        Assert.Equal(insertedText, Assert.Single(result.Changes).NewText);
        Assert.Equal(position + caretDelta, result.NewPosition);
        Assert.NotNull(result.Session);
    }

    [Theory]
    [InlineData("// |", '{')]
    [InlineData("<Button Content=\"hello |world\"/>", '{')]
    [InlineData("<Button Content=\"hello |world\"/>", '<')]
    [InlineData("state string text = \"hello |world\";", '{')]
    [InlineData("state bool result = left | right;", '<')]
    public void TypeUsesInsertOnlyWhenPairIsSuppressed(
        string sourceWithCaret,
        char character)
    {
        var (document, position) = Parse(sourceWithCaret);

        var result = Type(document, position, character);

        Assert.True(result.Handled);
        Assert.Equal(character.ToString(), Assert.Single(result.Changes).NewText);
        Assert.Null(result.Session);
    }

    [Fact]
    public void GeneratedGreaterOvertypesAndAddsClosingTag()
    {
        var (document, position) = Parse("<Button|>");
        var session = new AkburaPairSession(
            AkburaPairSessionKind.MarkupAnglePair,
            new TextSpan(0, 1),
            new TextSpan(position, 1),
            "<",
            ">",
            1,
            0);

        var result = Type(document, position, '>', session);

        Assert.True(result.Handled);
        Assert.Equal("</Button>", Assert.Single(result.Changes).NewText);
        Assert.Equal(position + 1, result.NewPosition);
        Assert.Null(result.Session);
    }

    [Fact]
    public void GreaterThanCanDisableMatchingClosingTag()
    {
        var (document, position) = Parse("<Button|");
        var options = Options with { AutoClosingTags = false };

        var result = Type(
            document,
            position,
            '>',
            options: options);

        Assert.Equal(">", Assert.Single(result.Changes).NewText);
        Assert.Equal(position + 1, result.NewPosition);
    }

    [Fact]
    public void SlashReusesGeneratedGreaterThan()
    {
        var (document, position) = Parse("<Button|>");

        var result = Type(document, position, '/');

        Assert.Equal("/", Assert.Single(result.Changes).NewText);
        Assert.Equal(position + 2, result.NewPosition);
    }

    [Fact]
    public void BackspaceDeletesEmptyPair()
    {
        var (document, position) = Parse("{|}");
        var session = FixedSession(position - 1, '{', '}');
        var result = GetResult(
            document,
            new AkburaTypingCommand(
                AkburaTypingCommandKind.Backspace,
                position,
                string.Empty,
                Options,
                session));

        var change = Assert.Single(result.Changes);
        Assert.Equal(new TextSpan(position - 1, 2), change.Span);
        Assert.Equal(string.Empty, change.NewText);
        Assert.Equal(position - 1, result.NewPosition);
    }

    [Fact]
    public void TabMovesThroughGeneratedClose()
    {
        var (document, position) = Parse("{   |}");
        var session = new AkburaPairSession(
            AkburaPairSessionKind.FixedPair,
            new TextSpan(0, 1),
            new TextSpan(position, 1),
            "{",
            "}",
            1,
            0);

        var result = GetResult(
            document,
            new AkburaTypingCommand(
                AkburaTypingCommandKind.Tab,
                position - 3,
                string.Empty,
                Options,
                session));

        Assert.Empty(result.Changes);
        Assert.Equal(position + 1, result.NewPosition);
        Assert.Null(result.Session);
    }

    [Fact]
    public void ThirdQuoteCreatesRawStringSession()
    {
        var (document, position) = Parse(
            "state string text = \"\"|;");

        var result = Type(document, position, '"');

        Assert.Equal("\"\"\"\"", Assert.Single(result.Changes).NewText);
        Assert.Equal(AkburaPairSessionKind.RawStringQuotes, result.Session?.Kind);
        Assert.Equal(3, result.Session?.RequiredDelimiterLength);
    }

    [Fact]
    public void RawStringCompletionCanBeDisabled()
    {
        var (document, position) = Parse(
            "state string text = \"\"|;");
        var options = Options with { RawStringCompletion = false };

        var result = Type(
            document,
            position,
            '"',
            options: options);

        Assert.Equal("\"", Assert.Single(result.Changes).NewText);
        Assert.Null(result.Session);
    }

    [Fact]
    public void RawQuoteGrowsBothDelimiters()
    {
        var (document, position) = Parse(
            "state string text = \"\"\"|\"\"\";");
        Assert.True(document.TryGetRawStringInfo(position, out var info));
        var session = new AkburaPairSession(
            AkburaPairSessionKind.RawStringQuotes,
            info.OpeningSpan,
            info.ClosingSpan,
            "\"\"\"",
            "\"\"\"",
            3,
            0);

        var result = Type(document, position, '"', session);

        Assert.Equal(2, result.Changes.Length);
        Assert.Equal(4, result.Session?.RequiredDelimiterLength);
        Assert.Equal(position + 1, result.NewPosition);
    }

    [Fact]
    public void RawReturnCreatesMultilineLiteral()
    {
        var (document, position) = Parse(
            "state string text = \"\"\"|\"\"\";");

        var result = GetResult(
            document,
            new AkburaTypingCommand(
                AkburaTypingCommandKind.Return,
                position,
                string.Empty,
                Options,
                Session: null));

        Assert.Equal("\r\n    \r\n", Assert.Single(result.Changes).NewText);
        Assert.Equal(position + 6, result.NewPosition);
        Assert.Equal(AkburaPairSessionKind.RawStringQuotes, result.Session?.Kind);
    }

    [Theory]
    [InlineData(".card |", "Styles.akcss")]
    [InlineData("@akcss {\r\n    .card |\r\n}", "Component.akbura")]
    public void StructuralAkcssBraceCreatesPair(
        string sourceWithCaret,
        string filePath)
    {
        var (document, position) = Parse(
            sourceWithCaret,
            filePath);

        var result = Type(document, position, '{');

        Assert.True(result.Handled);
        Assert.Equal("{}", Assert.Single(result.Changes).NewText);
        Assert.Equal(position + 1, result.NewPosition);
        Assert.Equal(
            AkburaPairSessionKind.FixedPair,
            result.Session?.Kind);
    }

    [Fact]
    public void RawInterpolationUsesDollarArity()
    {
        var (document, position) = Parse(
            "state string text = $$\"\"\"{|\"\"\";");

        var result = Type(document, position, '{');

        Assert.True(result.Handled);
        Assert.Equal("{}}", Assert.Single(result.Changes).NewText);
        Assert.Equal(
            AkburaPairSessionKind.InterpolationBraces,
            result.Session?.Kind);
        Assert.Equal(2, result.Session?.RequiredDelimiterLength);
    }
    [Fact]
    public void SecondNormalInterpolationBraceEscapesGeneratedPair()
    {
        var (document, position) = Parse(
            "state string text = $\"{|}\";");
        var session = new AkburaPairSession(
            AkburaPairSessionKind.InterpolationBraces,
            new TextSpan(position - 1, 1),
            new TextSpan(position, 1),
            "{",
            "}",
            1,
            0);

        var result = Type(document, position, '{', session);

        var change = Assert.Single(result.Changes);
        Assert.Equal(new TextSpan(position, 1), change.Span);
        Assert.Equal("{", change.NewText);
        Assert.Equal(position + 1, result.NewPosition);
        Assert.Null(result.Session);
    }

    private static AkburaTypingResult Type(
        AkburaSyntacticDocument document,
        int position,
        char character,
        AkburaPairSession? session = null,
        AkburaTypingOptions? options = null)
    {
        return GetResult(
            document,
            new AkburaTypingCommand(
                AkburaTypingCommandKind.Type,
                position,
                character.ToString(),
                options ?? Options,
                session));
    }

    private static AkburaTypingResult GetResult(
        AkburaSyntacticDocument document,
        AkburaTypingCommand command)
    {
        using var workspace = new AkburaWorkspace();
        return workspace.LanguageServices.Typing.GetResult(
            document,
            command);
    }

    private static AkburaPairSession FixedSession(
        int start,
        char opening,
        char closing)
    {
        return new AkburaPairSession(
            AkburaPairSessionKind.FixedPair,
            new TextSpan(start, 1),
            new TextSpan(start + 1, 1),
            opening.ToString(),
            closing.ToString(),
            1,
            0);
    }

    private static (AkburaSyntacticDocument Document, int Position) Parse(
        string sourceWithCaret,
        string filePath = "Component.akbura")
    {
        var position = sourceWithCaret.IndexOf('|');
        Assert.True(position >= 0);
        var source = sourceWithCaret.Remove(position, 1);
        return (
            AkburaSyntacticDocument.Parse(
                SourceText.From(source),
                filePath),
            position);
    }
}
