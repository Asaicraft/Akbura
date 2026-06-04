using Akbura.Language;
using Akbura.Language.Syntax;

namespace Akbura.UnitTests;

public sealed class BlenderCursorTests
{
    [Fact]
    public void Cursor_MoveToFirstChild_WalksDepthFirstAndKeepsEndOfFile()
    {
        const string code =
            "state count = 0;\n" +
            "\n" +
            "<Button>{count}</Button>";

        var root = ParseRoot(code);
        var cursor = new Blender.Cursor(root);
        var kinds = new List<SyntaxKind>();

        for (var guard = 0; !cursor.IsFinished; guard++)
        {
            Assert.True(guard < 1_000);

            var current = cursor.CurrentNode;
            Assert.True(
                current.FullWidth > 0 || current.Kind == SyntaxKind.EndOfFileToken,
                $"Unexpected zero-width cursor item: {current.Kind}");

            kinds.Add(current.Kind);
            cursor = cursor.MoveToFirstChild();
        }

        Assert.Equal(SyntaxKind.AkburaDocumentSyntax, kinds[0]);
        Assert.Contains(SyntaxKind.StateDeclarationSyntax, kinds);
        Assert.Contains(SyntaxKind.MarkupRootSyntax, kinds);
        Assert.Equal(SyntaxKind.EndOfFileToken, kinds[^1]);
        Assert.True(kinds.IndexOf(SyntaxKind.StateKeyword) < kinds.IndexOf(SyntaxKind.MarkupRootSyntax));
    }

    [Fact]
    public void Cursor_Current_UsesSyntaxNodeOrToken()
    {
        const string code = "state count = 0;";

        var root = ParseRoot(code);
        var rootCursor = new Blender.Cursor(root);
        var state = rootCursor.MoveToFirstChild();
        var stateKeyword = state.MoveToFirstChild();
        var endOfFile = state.MoveToNextSibling();

        Assert.True(rootCursor.Current.IsNode);
        Assert.Equal(SyntaxKind.AkburaDocumentSyntax, rootCursor.Current.Kind);

        Assert.True(state.Current.IsNode);
        Assert.Equal(SyntaxKind.StateDeclarationSyntax, state.Current.Kind);

        Assert.True(stateKeyword.Current.IsToken);
        Assert.Equal(SyntaxKind.StateKeyword, stateKeyword.Current.Kind);

        Assert.True(endOfFile.Current.IsToken);
        Assert.Equal(SyntaxKind.EndOfFileToken, endOfFile.Current.Kind);
    }

    [Fact]
    public void Cursor_MoveToNextSibling_MovesAcrossTopLevelMembers()
    {
        const string code =
            "state count = 0;\n" +
            "<Button>{count}</Button>";

        var root = ParseRoot(code);
        var rootCursor = new Blender.Cursor(root);

        var state = rootCursor.MoveToFirstChild();
        Assert.Equal(SyntaxKind.StateDeclarationSyntax, state.CurrentNode.Kind);

        var markup = state.MoveToNextSibling();
        Assert.Equal(SyntaxKind.MarkupRootSyntax, markup.CurrentNode.Kind);

        var endOfFile = markup.MoveToNextSibling();
        Assert.Equal(SyntaxKind.EndOfFileToken, endOfFile.CurrentNode.Kind);

        Assert.True(endOfFile.MoveToNextSibling().IsFinished);
    }

    [Fact]
    public void Cursor_MoveToNextSibling_FromMarkupFirstToken_MovesToComponentName()
    {
        const string code = "<TextBlock Text=\"Hi\"/>";

        var root = ParseRoot(code);
        var firstToken = new Blender.Cursor(root).MoveToFirstChild();
        while (firstToken.Current.IsNode)
        {
            firstToken = firstToken.MoveToFirstChild();
        }

        var secondToken = firstToken.MoveToNextSibling();
        while (secondToken.Current.IsNode)
        {
            secondToken = secondToken.MoveToFirstChild();
        }

        Assert.Equal(SyntaxKind.LessThanToken, firstToken.Current.Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, secondToken.Current.Kind);
        Assert.Equal("TextBlock ", secondToken.Current.ToFullString());
    }

    [Fact]
    public void Cursor_MoveToParent_ReturnsContainingNode()
    {
        const string code = "state count = 0;";

        var root = ParseRoot(code);
        var state = new Blender.Cursor(root).MoveToFirstChild();
        var stateKeyword = state.MoveToFirstChild();

        Assert.Equal(SyntaxKind.StateKeyword, stateKeyword.CurrentNode.Kind);
        Assert.Same(state.CurrentNode, stateKeyword.MoveToParent().CurrentNode);
    }

    [Fact]
    public void Cursor_SkipsZeroWidthMissingNodesButKeepsEndOfFile()
    {
        const string code = "state count = ;";

        var root = ParseRoot(code);
        var cursor = new Blender.Cursor(root);
        var sawEndOfFile = false;

        for (var guard = 0; !cursor.IsFinished; guard++)
        {
            Assert.True(guard < 1_000);

            var current = cursor.CurrentNode;
            Assert.True(
                current.FullWidth > 0 || current.Kind == SyntaxKind.EndOfFileToken,
                $"Unexpected zero-width cursor item: {current.Kind}");

            sawEndOfFile |= current.Kind == SyntaxKind.EndOfFileToken;
            cursor = cursor.MoveToFirstChild();
        }

        Assert.True(sawEndOfFile);
    }

    private static AkburaDocumentSyntax ParseRoot(string code)
    {
        var green = ParserHelper.MakeParser(code).ParseCompilationUnit();
        return (AkburaDocumentSyntax)green.CreateRed();
    }
}
