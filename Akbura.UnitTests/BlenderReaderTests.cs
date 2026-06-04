using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class BlenderReaderTests
{
    [Fact]
    public void ReadToken_WithoutChanges_ReusesOldTreeTokens()
    {
        const string code = "state count = 0;";
        using var lexer = new Lexer(SourceText.From(code));
        var oldTree = ParseRoot(code);
        var blender = new Blender(lexer, oldTree, changes: null);

        var first = blender.ReadToken(Lexer.LexerMode.TopLevel);

        Assert.Null(first.Node);
        Assert.Equal(SyntaxKind.StateKeyword, first.Token.Kind);
        Assert.Equal("state ", first.Token.ToFullString());
    }

    [Fact]
    public void ReadToken_ReturnedBlenderContinuesFromNextToken()
    {
        const string code = "state count = 0;";
        using var lexer = new Lexer(SourceText.From(code));
        var oldTree = ParseRoot(code);
        var blender = new Blender(lexer, oldTree, changes: null);

        var first = blender.ReadToken(Lexer.LexerMode.TopLevel);
        var second = first.Blender.ReadToken(Lexer.LexerMode.TopLevel);

        Assert.Equal(SyntaxKind.StateKeyword, first.Token.Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, second.Token.Kind);
        Assert.Equal("count ", second.Token.ToFullString());
    }

    [Fact]
    public void ReadNode_WithoutChanges_ReusesOldTreeNode()
    {
        const string code = "state count = 0;";
        using var lexer = new Lexer(SourceText.From(code));
        var oldTree = ParseRoot(code);
        var blender = new Blender(lexer, oldTree, changes: null);

        var first = blender.ReadNode(Lexer.LexerMode.TopLevel);

        Assert.NotNull(first.Node);
        Assert.Equal(SyntaxKind.StateDeclarationSyntax, first.Node!.Kind);
        Assert.Equal(code, first.Node.ToFullString());
    }

    [Fact]
    public void ReadToken_InsideChangeRange_LexesNewToken()
    {
        const string oldCode = "state count = 0;";
        const string newCode = "param count = 0;";
        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = ParseRoot(oldCode);
        var change = new TextChangeRange(new TextSpan(0, "state".Length), "param".Length);
        var blender = new Blender(lexer, oldTree, [change]);

        var first = blender.ReadToken(Lexer.LexerMode.TopLevel);

        Assert.Null(first.Node);
        Assert.Equal(SyntaxKind.ParamKeyword, first.Token.Kind);
        Assert.Equal("param ", first.Token.ToFullString());
    }

    private static AkburaDocumentSyntax ParseRoot(string code)
    {
        var green = ParserHelper.MakeParser(code).ParseCompilationUnit();
        return (AkburaDocumentSyntax)green.CreateRed();
    }
}
