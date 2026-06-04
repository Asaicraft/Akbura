using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class CommandIncrementalParserTests
{
    [Fact]
    public void ReturnTypeEdit_ReusesNameParametersAndSemicolon()
    {
        const string oldCode = "command Task Refresh(int userId);";
        const string newCode = "command ValueTask Refresh(int userId);";

        var (oldCommand, newCommand) = ParseCommandIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Task"),
            oldLength: "Task".Length,
            newLength: "ValueTask".Length);

        Assert.NotSame(oldCommand, newCommand);
        Assert.Same(oldCommand.CommandKeyword, newCommand.CommandKeyword);
        Assert.NotSame(oldCommand.ReturnType, newCommand.ReturnType);
        Assert.Same(oldCommand.Name, newCommand.Name);
        Assert.Same(oldCommand.Parameters, newCommand.Parameters);
        Assert.Same(oldCommand.Semicolon, newCommand.Semicolon);
        Assert.Equal(newCode, newCommand.ToFullString());
    }

    [Fact]
    public void NameEdit_ReusesReturnTypeAndParameters()
    {
        const string oldCode = "command Task Refresh(int userId);";
        const string newCode = "command Task Reload(int userId);";

        var (oldCommand, newCommand) = ParseCommandIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("Refresh"),
            oldLength: "Refresh".Length,
            newLength: "Reload".Length);

        Assert.Same(oldCommand.CommandKeyword, newCommand.CommandKeyword);
        Assert.Same(oldCommand.ReturnType, newCommand.ReturnType);
        Assert.NotSame(oldCommand.Name, newCommand.Name);
        Assert.Same(oldCommand.Parameters, newCommand.Parameters);
        Assert.Same(oldCommand.Semicolon, newCommand.Semicolon);
        Assert.Equal(newCode, newCommand.ToFullString());
    }

    [Fact]
    public void ParametersEdit_ReusesReturnTypeAndName()
    {
        const string oldCode = "command Task Refresh(int userId);";
        const string newCode = "command Task Refresh(long userId, bool force);";

        var (oldCommand, newCommand) = ParseCommandIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("(int userId)"),
            oldLength: "(int userId)".Length,
            newLength: "(long userId, bool force)".Length);

        Assert.Same(oldCommand.CommandKeyword, newCommand.CommandKeyword);
        Assert.Same(oldCommand.ReturnType, newCommand.ReturnType);
        Assert.Same(oldCommand.Name, newCommand.Name);
        Assert.NotSame(oldCommand.Parameters, newCommand.Parameters);
        Assert.Same(oldCommand.Semicolon, newCommand.Semicolon);
        Assert.Equal(newCode, newCommand.ToFullString());
    }

    [Fact]
    public void InsertParameter_ReusesReturnTypeAndName()
    {
        const string oldCode = "command Task Refresh();";
        const string inserted = "int userId";
        var insertPosition = oldCode.IndexOf(")");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldCommand, newCommand) = ParseCommandIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        Assert.Same(oldCommand.CommandKeyword, newCommand.CommandKeyword);
        Assert.Same(oldCommand.ReturnType, newCommand.ReturnType);
        Assert.Same(oldCommand.Name, newCommand.Name);
        Assert.NotSame(oldCommand.Parameters, newCommand.Parameters);
        Assert.Same(oldCommand.Semicolon, newCommand.Semicolon);
        Assert.Equal(newCode, newCommand.ToFullString());
    }

    private static (GreenCommandDeclarationSyntax OldCommand, GreenCommandDeclarationSyntax NewCommand) ParseCommandIncremental(
        string newCode,
        string oldCode,
        int changeStart,
        int oldLength,
        int newLength)
    {
        var oldSyntax = ParseDocument(oldCode);
        var oldCommand = Assert.IsType<GreenCommandDeclarationSyntax>(oldSyntax.Members[0]);

        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        var change = new TextChangeRange(new TextSpan(changeStart, oldLength), newLength);

        using var parser = ParserHelper.MakeIncrementalParser(newCode, oldTree, [change]);
        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(1, syntax.Members.Count);
        return (oldCommand, Assert.IsType<GreenCommandDeclarationSyntax>(syntax.Members[0]));
    }

    private static GreenAkburaDocumentSyntax ParseDocument(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        return parser.ParseCompilationUnit();
    }
}
