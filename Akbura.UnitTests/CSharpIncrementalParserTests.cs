using Akbura.Language;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class CSharpIncrementalParserTests
{
    [Fact]
    public void TopLevelStatementIdentifierEdit_ReusesUnchangedTokensAndNeighbors()
    {
        const string oldCode =
            "using System;\n" +
            "var a = 11;\n" +
            "using Demo;";
        const string newCode =
            "using System;\n" +
            "var b = 11;\n" +
            "using Demo;";

        var oldSyntax = Parse(oldCode);
        var oldStatement = Assert.IsType<GreenCSharpStatementSyntax>(oldSyntax.Members[1]);
        var change = new TextChangeRange(
            new TextSpan(oldCode.IndexOf("a ="), length: 1),
            newLength: 1);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newStatement = Assert.IsType<GreenCSharpStatementSyntax>(incremental.Members[1]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.NotSame(oldStatement, newStatement);
        Assert.Same(oldSyntax.Members[2], incremental.Members[2]);

        Assert.Equal(5, oldStatement.Tokens.Count);
        Assert.Equal(5, newStatement.Tokens.Count);
        Assert.Same(oldStatement.Tokens[0], newStatement.Tokens[0]);
        Assert.NotSame(oldStatement.Tokens[1], newStatement.Tokens[1]);
        Assert.Same(oldStatement.Tokens[2], newStatement.Tokens[2]);
        Assert.Same(oldStatement.Tokens[3], newStatement.Tokens[3]);
        Assert.Same(oldStatement.Tokens[4], newStatement.Tokens[4]);
        Assert.Equal("var b = 11;\n", newStatement.ToFullString());
    }

    [Fact]
    public void CSharpBlockStatementIdentifierEdit_ReusesUnchangedBodyMembers()
    {
        const string oldCode =
            "if(isOpen)\n" +
            "{\n" +
            "    var a = 11;\n" +
            "    <TextBlock Text=\"Opened\"/>\n" +
            "    var c = 12;\n" +
            "}";
        const string newCode =
            "if(isOpen)\n" +
            "{\n" +
            "    var b = 11;\n" +
            "    <TextBlock Text=\"Opened\"/>\n" +
            "    var c = 12;\n" +
            "}";

        var oldSyntax = Parse(oldCode);
        var oldConditional = Assert.IsType<GreenCSharpStatementSyntax>(oldSyntax.Members[0]);
        var oldFirstStatement = Assert.IsType<GreenCSharpStatementSyntax>(oldConditional.Body!.Tokens[0]);
        var change = new TextChangeRange(
            new TextSpan(oldCode.IndexOf("a ="), length: 1),
            newLength: 1);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newConditional = Assert.IsType<GreenCSharpStatementSyntax>(incremental.Members[0]);
        var newFirstStatement = Assert.IsType<GreenCSharpStatementSyntax>(newConditional.Body!.Tokens[0]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.NotSame(oldConditional, newConditional);
        Assert.NotSame(oldConditional.Body, newConditional.Body);
        Assert.NotSame(oldFirstStatement, newFirstStatement);
        Assert.Same(oldConditional.Body.Tokens[1], newConditional.Body.Tokens[1]);
        Assert.Same(oldConditional.Body.Tokens[2], newConditional.Body.Tokens[2]);
        Assert.Same(oldFirstStatement.Tokens[0], newFirstStatement.Tokens[0]);
        Assert.NotSame(oldFirstStatement.Tokens[1], newFirstStatement.Tokens[1]);
        Assert.Same(oldFirstStatement.Tokens[2], newFirstStatement.Tokens[2]);
        Assert.Same(oldFirstStatement.Tokens[3], newFirstStatement.Tokens[3]);
        Assert.Same(oldFirstStatement.Tokens[4], newFirstStatement.Tokens[4]);
    }

    [Fact]
    public void CSharpBlockInsertStatement_ReusesSurroundingBodyMembers()
    {
        const string oldCode =
            "if(isOpen)\n" +
            "{\n" +
            "    var first = 1;\n" +
            "    var second = 2;\n" +
            "}";
        const string inserted = "    var inserted = 10;\n";
        var insertPosition = oldCode.IndexOf("    var second", StringComparison.Ordinal);
        var newCode = oldCode.Insert(insertPosition, inserted);

        var oldSyntax = Parse(oldCode);
        var oldConditional = Assert.IsType<GreenCSharpStatementSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(
            new TextSpan(insertPosition, length: 0),
            newLength: inserted.Length);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newConditional = Assert.IsType<GreenCSharpStatementSyntax>(incremental.Members[0]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(3, newConditional.Body!.Tokens.Count);
        Assert.Same(oldConditional.Body!.Tokens[0], newConditional.Body.Tokens[0]);
        Assert.IsType<GreenCSharpStatementSyntax>(newConditional.Body.Tokens[1]);
        Assert.Same(oldConditional.Body.Tokens[1], newConditional.Body.Tokens[2]);
    }

    [Fact]
    public void CSharpBlockDeleteStatement_ReusesSurroundingBodyMembers()
    {
        const string oldCode =
            "if(isOpen)\n" +
            "{\n" +
            "    var first = 1;\n" +
            "    var deleted = 10;\n" +
            "    var second = 2;\n" +
            "}";
        const string deleted = "    var deleted = 10;\n";
        var deletePosition = oldCode.IndexOf(deleted, StringComparison.Ordinal);
        var newCode = oldCode.Remove(deletePosition, deleted.Length);

        var oldSyntax = Parse(oldCode);
        var oldConditional = Assert.IsType<GreenCSharpStatementSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(
            new TextSpan(deletePosition, deleted.Length),
            newLength: 0);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newConditional = Assert.IsType<GreenCSharpStatementSyntax>(incremental.Members[0]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(2, newConditional.Body!.Tokens.Count);
        Assert.Same(oldConditional.Body!.Tokens[0], newConditional.Body.Tokens[0]);
        Assert.Same(oldConditional.Body.Tokens[2], newConditional.Body.Tokens[1]);
    }

    [Fact]
    public void RawExpressionIdentifierEdit_ReparsesCSharpRawWrapper()
    {
        const string oldCode = "state value = a + 1;";
        const string newCode = "state value = b + 1;";

        var oldSyntax = Parse(oldCode);
        var oldState = Assert.IsType<GreenStateDeclarationSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(
            new TextSpan(oldCode.IndexOf("a +"), length: 1),
            newLength: 1);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newState = Assert.IsType<GreenStateDeclarationSyntax>(incremental.Members[0]);
        var newInitializer = Assert.IsType<GreenSimpleStateInitializerSyntax>(newState.Initializer);
        var rawToken = Assert.IsType<GreenSyntaxToken.CSharpRawToken>(
            newInitializer.Expression.Tokens[0]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.NotSame(oldState.Initializer, newState.Initializer);
        Assert.IsType<CSharp.BinaryExpressionSyntax>(rawToken.RawNode);
        Assert.Equal("b + 1", rawToken.RawNode!.ToFullString());
    }

    [Fact]
    public void RawTypeIdentifierEdit_ReparsesCSharpRawWrapper()
    {
        const string oldCode = "inject List<Foo> service;";
        const string newCode = "inject List<Bar> service;";

        var oldSyntax = Parse(oldCode);
        var oldInject = Assert.IsType<GreenInjectDeclarationSyntax>(oldSyntax.Members[0]);
        var change = new TextChangeRange(
            new TextSpan(oldCode.IndexOf("Foo"), length: "Foo".Length),
            newLength: "Bar".Length);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);
        var newInject = Assert.IsType<GreenInjectDeclarationSyntax>(incremental.Members[0]);
        var rawToken = Assert.IsType<GreenSyntaxToken.CSharpRawToken>(
            newInject.Type.Tokens[0]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldInject.InjectKeyword, newInject.InjectKeyword);
        Assert.NotSame(oldInject.Type, newInject.Type);
        Assert.Same(oldInject.Name, newInject.Name);
        Assert.Same(oldInject.Semicolon, newInject.Semicolon);
        Assert.IsType<CSharp.GenericNameSyntax>(rawToken.RawNode);
        Assert.Equal("List<Bar> ", rawToken.RawNode!.ToFullString());
    }

    private static GreenAkburaDocumentSyntax Parse(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        return parser.ParseCompilationUnit();
    }

    private static GreenAkburaDocumentSyntax ParseIncremental(
        string code,
        GreenAkburaDocumentSyntax oldSyntax,
        IEnumerable<TextChangeRange>? changes)
    {
        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        using var parser = ParserHelper.MakeIncrementalParser(code, oldTree, changes);
        return parser.ParseCompilationUnit();
    }
}
