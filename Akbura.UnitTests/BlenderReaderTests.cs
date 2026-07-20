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

    [Fact]
    public void ReadFreshToken_AfterChangedMember_ContinuesWithNextTokenOnce()
    {
        const string oldCode =
            "using System;\n" +
            "state int count = 0;\n" +
            "<TextBlock Text=\"Hi\"/>";
        const string newCode =
            "using System;\n" +
            "state int count = 1;\n" +
            "<TextBlock Text=\"Hi\"/>";

        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = ParseRoot(oldCode);
        var changeStart = oldCode.IndexOf("0;");
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);
        var blender = new Blender(lexer, oldTree, [change]);
        var kinds = new List<SyntaxKind>();

        for (var i = 0; i < 12; i++)
        {
            var current = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
            kinds.Add(current.Token.Kind);
            blender = current.Blender;
        }

        Assert.Equal(
            [
                SyntaxKind.UsingKeyword,
                SyntaxKind.IdentifierToken,
                SyntaxKind.SemicolonToken,
                SyntaxKind.StateKeyword,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.EqualsToken,
                SyntaxKind.NumericLiteralToken,
                SyntaxKind.SemicolonToken,
                SyntaxKind.LessThanToken,
                SyntaxKind.IdentifierToken,
                SyntaxKind.IdentifierToken,
            ],
            kinds);
    }

    [Fact]
    public void ReadToken_AfterInsertionInsideStatement_ResumesOldTokens()
    {
        const string oldCode =
            "state int count = 0;\n" +
            "useEffect(() => Console.WriteLine(count));\n" +
            "<TextBlock Text={count} />";
        const string inserted = " + 1";
        var insertion = oldCode.IndexOf("count));", StringComparison.Ordinal) + "count".Length;
        var newCode = oldCode.Insert(insertion, inserted);

        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = ParseRoot(oldCode);
        var change = new TextChangeRange(new TextSpan(insertion, 0), inserted.Length);
        var blender = new Blender(lexer, oldTree, [change]);
        var text = new System.Text.StringBuilder();
        var kinds = new List<SyntaxKind>();

        while (true)
        {
            var current = blender.ReadToken(Lexer.LexerMode.TopLevel);
            text.Append(current.Token.ToFullString());
            kinds.Add(current.Token.Kind);
            blender = current.Blender;

            if (current.Token.Kind == SyntaxKind.EndOfFileToken)
            {
                break;
            }
        }

        Assert.Equal(newCode, text.ToString());
        Assert.Contains(SyntaxKind.CloseParenToken, kinds);
    }

    [Fact]
    public void ReadNode_AfterInsertedMember_ReusesRightNode()
    {
        const string oldCode =
            "using System;\n" +
            "using Demo;";
        const string inserted = "state int b = 1;\n";
        var insertPosition = oldCode.IndexOf("using Demo");
        var newCode = oldCode.Insert(insertPosition, inserted);

        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = ParseRoot(oldCode);
        var change = new TextChangeRange(new TextSpan(insertPosition, 0), inserted.Length);
        var blender = new Blender(lexer, oldTree, [change]);

        var left = blender.ReadNode(Lexer.LexerMode.TopLevel);
        blender = left.Blender;
        var stateKeyword = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
        blender = stateKeyword.Blender;
        var type = blender.ReadFreshToken(Lexer.LexerMode.InTypeName);
        blender = type.Blender;
        var name = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
        blender = name.Blender;
        var equals = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
        blender = equals.Blender;
        _ = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
        var initializer = blender.ReadFreshToken(Lexer.LexerMode.InExpressionUntilSemicolon);
        blender = initializer.Blender;
        var semicolon = blender.ReadFreshToken(Lexer.LexerMode.TopLevel);
        blender = semicolon.Blender;

        var right = blender.ReadNode(Lexer.LexerMode.TopLevel);

        Assert.Same(oldTree.Members[0].Green, left.Node!.Green);
        Assert.Equal(SyntaxKind.StateKeyword, stateKeyword.Token.Kind);
        Assert.Equal(SyntaxKind.CSharpRawToken, type.Token.Kind);
        Assert.Equal(SyntaxKind.IdentifierToken, name.Token.Kind);
        Assert.Equal(SyntaxKind.EqualsToken, equals.Token.Kind);
        Assert.Equal(SyntaxKind.CSharpRawToken, initializer.Token.Kind);
        Assert.Equal(SyntaxKind.SemicolonToken, semicolon.Token.Kind);
        Assert.Same(oldTree.Members[1].Green, right.Node!.Green);
    }

    [Fact]
    public void ReadNode_AfterDeletedMember_ReusesRightNode()
    {
        const string oldCode =
            "using System;\n" +
            "state int b = 1;\n" +
            "using Demo;";
        const string deleted = "state int b = 1;\n";
        var deletePosition = oldCode.IndexOf(deleted);
        var newCode = oldCode.Remove(deletePosition, deleted.Length);

        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = ParseRoot(oldCode);
        var change = new TextChangeRange(new TextSpan(deletePosition, deleted.Length), newLength: 0);
        var blender = new Blender(lexer, oldTree, [change]);

        var left = blender.ReadNode(Lexer.LexerMode.TopLevel);
        var right = left.Blender.ReadNode(Lexer.LexerMode.TopLevel);

        Assert.Same(oldTree.Members[0].Green, left.Node!.Green);
        Assert.Same(oldTree.Members[2].Green, right.Node!.Green);
    }

    private static AkburaDocumentSyntax ParseRoot(string code)
    {
        var green = ParserHelper.MakeParser(code).ParseCompilationUnit();
        return (AkburaDocumentSyntax)green.CreateRed();
    }
}
