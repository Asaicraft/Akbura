using Akbura.Language;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class BlenderReaderTests
{
    [Fact]
    public void ReadToken_AtChangedUtilityCloseBrace_PreservesSelectorTokens()
    {
        const string oldCode =
            "@using System;\r\n" +
            "@utilities {\r\n" +
            "\tControl.w-(double width) { Width: width; }\r\n" +
            "\tControl.w-auto { Width: double.NaN; }\r\n" +
            "\tControl.w-full { HorizontalAlignment: Stretch; }\r\n" +
            "\tControl.w-px { Width: 1d; }\r\n" +
            "}\r\n";
        var insertPosition = oldCode.IndexOf(
            " }\r\n\tControl.w-px",
            StringComparison.Ordinal) + 1;
        const string insertedText = "\r\n";
        var newCode = oldCode.Insert(insertPosition, insertedText);
        using var lexer = new Lexer(SourceText.From(newCode));
        var oldTree = AkcssSyntaxTree.ParseText(
            SourceText.From(oldCode),
            "Styles.akcss").GetRoot();
        Assert.False(oldTree.ContainsDiagnostics);
        Assert.False(oldTree.Members[0].ContainsSkippedText);
        var change = new TextChangeRange(
            new TextSpan(insertPosition, 0),
            insertedText.Length);
        var blender = new Blender(lexer, oldTree, [change]);

        var usingDirective = blender.ReadNode(Lexer.LexerMode.InAkcss);
        Assert.NotNull(usingDirective.Node);
        blender = usingDirective.Blender;
        var section = blender.ReadNode(Lexer.LexerMode.InAkcss);
        Assert.Null(section.Node);

        var atToken = blender.ReadToken(Lexer.LexerMode.InAkcss);
        blender = atToken.Blender;
        var utilitiesToken = blender.ReadToken(Lexer.LexerMode.InAkcss);
        blender = utilitiesToken.Blender;
        var openBrace = blender.ReadToken(Lexer.LexerMode.InAkcss);
        blender = openBrace.Blender;
        var firstUtility = blender.ReadNode(Lexer.LexerMode.InAkcss);
        blender = firstUtility.Blender;
        var secondUtility = blender.ReadNode(Lexer.LexerMode.InAkcss);
        blender = secondUtility.Blender;
        var changedUtility = blender.ReadNode(Lexer.LexerMode.InAkcss);
        Assert.Null(changedUtility.Node);
        var selector = blender.ReadNode(Lexer.LexerMode.InAkcss);
        Assert.Null(selector.Node);

        var target = blender.ReadToken(Lexer.LexerMode.InAkcss);
        var dot = target.Blender.ReadToken(Lexer.LexerMode.InAkcss);

        Assert.Equal(SyntaxKind.CSharpRawToken, target.Token.Kind);
        Assert.EndsWith("Control", target.Token.ToFullString());
        Assert.Equal(SyntaxKind.DotToken, dot.Token.Kind);
    }

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

    [Fact]
    public void ReadNode_AfterMinusInsertionAtUtilityFlagBoundary_RejectsOldFlagAndReusesUnaffectedNodes()
    {
        const string oldCode = "using System;\r\n<Viewbox w><TextBlock /></Viewbox>";
        var oldTree = ParseRoot(oldCode);
        Assert.False(oldTree.ContainsDiagnostics);
        var oldFlag = Assert.Single(oldTree.DescendantNodes().OfType<TailwindFlagAttributeSyntax>());
        var oldBody = Assert.Single(oldTree.DescendantNodes().OfType<MarkupElementContentSyntax>());
        var insertion = oldCode.IndexOf("w>", StringComparison.Ordinal) + 1;
        Assert.Equal(insertion, oldFlag.FullSpan.End);
        var newCode = oldCode.Insert(insertion, "-");
        using var lexer = new Lexer(SourceText.From(newCode));
        var blender = new Blender(lexer, oldTree, [new TextChangeRange(new TextSpan(insertion, 0), 1)]);

        var unchangedUsing = blender.ReadNode(Lexer.LexerMode.TopLevel);
        Assert.Same(oldTree.Members[0].Green, unchangedUsing.Node!.Green);
        var open = unchangedUsing.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        var componentName = open.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        blender = componentName.Blender;

        // The identifier itself is still "w", but its flag syntax must be reparsed
        // because the appended '-' starts a full utility attribute.
        Assert.Null(blender.ReadNode(Lexer.LexerMode.TopLevel).Node);
        var utilityName = blender.ReadToken(Lexer.LexerMode.TopLevel);
        var minus = utilityName.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        var close = minus.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        var unchangedBody = close.Blender.ReadNode(Lexer.LexerMode.TopLevel);

        Assert.Equal(SyntaxKind.LessThanToken, open.Token.Kind);
        Assert.Equal("Viewbox ", componentName.Token.ToFullString());
        Assert.Equal(SyntaxKind.IdentifierToken, utilityName.Token.Kind);
        Assert.Equal("w", utilityName.Token.Text);
        Assert.Equal(SyntaxKind.MinusToken, minus.Token.Kind);
        Assert.Equal(SyntaxKind.GreaterThanToken, close.Token.Kind);
        Assert.Same(oldBody.Green, unchangedBody.Node!.Green);
    }

    [Theory]
    [InlineData("0", "30")]
    [InlineData(".5", "3.5")]
    [InlineData("d", "3d")]
    public void ReadNode_AfterNumericLiteralSuffixInsertion_RejectsOldLiteralAndReusesUnaffectedNodes(
        string insertedText,
        string expectedLiteral)
    {
        const string oldCode = "using System;\r\n<Viewbox w-3><TextBlock /></Viewbox>";
        var oldTree = ParseRoot(oldCode);
        Assert.False(oldTree.ContainsDiagnostics);
        var oldNumber = Assert.Single(oldTree.DescendantNodes().OfType<TailwindNumericSegmentSyntax>());
        var oldBody = Assert.Single(oldTree.DescendantNodes().OfType<MarkupElementContentSyntax>());
        var insertion = oldCode.IndexOf("3>", StringComparison.Ordinal) + 1;
        Assert.Equal(insertion, oldNumber.FullSpan.End);
        var newCode = oldCode.Insert(insertion, insertedText);
        var freshTree = ParseRoot(newCode);
        Assert.False(freshTree.ContainsDiagnostics);
        var freshNumber = Assert.Single(freshTree.DescendantNodes().OfType<TailwindNumericSegmentSyntax>());
        using var lexer = new Lexer(SourceText.From(newCode));
        var blender = new Blender(lexer, oldTree,
            [new TextChangeRange(new TextSpan(insertion, 0), insertedText.Length)]);

        var unchangedUsing = blender.ReadNode(Lexer.LexerMode.TopLevel);
        Assert.Same(oldTree.Members[0].Green, unchangedUsing.Node!.Green);
        var open = unchangedUsing.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        var componentName = open.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        blender = componentName.Blender;

        Assert.Null(blender.ReadNode(Lexer.LexerMode.TopLevel).Node);
        var utilityName = blender.ReadToken(Lexer.LexerMode.TopLevel);
        var minus = utilityName.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        blender = minus.Blender;

        // Extending '3' changes both the numeric segment and its terminal token.
        // An unaffected child after the edited opening tag must remain reusable.
        Assert.Null(blender.ReadNode(Lexer.LexerMode.TopLevel).Node);
        var number = blender.ReadToken(Lexer.LexerMode.TopLevel);
        var close = number.Blender.ReadToken(Lexer.LexerMode.TopLevel);
        var unchangedBody = close.Blender.ReadNode(Lexer.LexerMode.TopLevel);

        Assert.Equal(SyntaxKind.LessThanToken, open.Token.Kind);
        Assert.Equal("Viewbox ", componentName.Token.ToFullString());
        Assert.Equal("w", utilityName.Token.Text);
        Assert.Equal(SyntaxKind.MinusToken, minus.Token.Kind);
        Assert.Equal(SyntaxKind.NumericLiteralToken, number.Token.Kind);
        Assert.Equal(expectedLiteral, number.Token.Text);
        Assert.Equal(freshNumber.Number.Value, number.Token.Value);
        Assert.NotSame(oldNumber.Number.Node, number.Token.Node);
        Assert.Equal(SyntaxKind.GreaterThanToken, close.Token.Kind);
        Assert.Same(oldBody.Green, unchangedBody.Node!.Green);
    }

    private static AkburaDocumentSyntax ParseRoot(string code)
    {
        var green = ParserHelper.MakeParser(code).ParseCompilationUnit();
        return (AkburaDocumentSyntax)green.CreateRed();
    }
}
