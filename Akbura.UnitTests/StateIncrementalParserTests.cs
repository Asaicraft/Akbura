using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class StateIncrementalParserTests
{
    [Fact]
    public void InitializerEdit_ReusesUnchangedStateSlots()
    {
        const string oldCode = "state int count = 0;";
        const string newCode = "state int count = 1;";

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("0;"),
            oldLength: 1,
            newLength: 1);

        Assert.NotSame(oldState, newState);
        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Same(oldState.Type, newState.Type);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.NotSame(oldState.Initializer, newState.Initializer);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    [Fact]
    public void NameEdit_ReusesTypeAndInitializer()
    {
        const string oldCode = "state int count = 0;";
        const string newCode = "state int total = 0;";

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("count"),
            oldLength: "count".Length,
            newLength: "total".Length);

        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Same(oldState.Type, newState.Type);
        Assert.NotSame(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.Same(oldState.Initializer, newState.Initializer);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    [Fact]
    public void TypeEdit_ReusesNameAndInitializer()
    {
        const string oldCode = "state int count = 0;";
        const string newCode = "state long count = 0;";

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("int"),
            oldLength: "int".Length,
            newLength: "long".Length);

        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.NotSame(oldState.Type, newState.Type);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.Same(oldState.Initializer, newState.Initializer);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    [Fact]
    public void BindingKeywordEdit_ReusesBindableExpression()
    {
        const string oldCode = "state bool busy = bind viewModel.IsBusy;";
        const string newCode = "state bool busy = out viewModel.IsBusy;";

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("bind"),
            oldLength: "bind".Length,
            newLength: "out".Length);

        var oldInitializer = Assert.IsType<GreenBindableStateInitializerSyntax>(oldState.Initializer);
        var newInitializer = Assert.IsType<GreenBindableStateInitializerSyntax>(newState.Initializer);

        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Same(oldState.Type, newState.Type);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.NotSame(oldInitializer, newInitializer);
        Assert.NotSame(oldInitializer.BindingKeyword, newInitializer.BindingKeyword);
        Assert.Same(oldInitializer.Expression, newInitializer.Expression);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    [Fact]
    public void BindableExpressionEdit_ReusesBindingKeyword()
    {
        const string oldCode = "state bool busy = bind viewModel.IsBusy;";
        const string newCode = "state bool busy = bind viewModel.IsReady;";

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("IsBusy"),
            oldLength: "IsBusy".Length,
            newLength: "IsReady".Length);

        var oldInitializer = Assert.IsType<GreenBindableStateInitializerSyntax>(oldState.Initializer);
        var newInitializer = Assert.IsType<GreenBindableStateInitializerSyntax>(newState.Initializer);

        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Same(oldState.Type, newState.Type);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.NotSame(oldInitializer, newInitializer);
        Assert.Same(oldInitializer.BindingKeyword, newInitializer.BindingKeyword);
        Assert.NotSame(oldInitializer.Expression, newInitializer.Expression);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    [Fact]
    public void InsertType_ReusesNameAndInitializer()
    {
        const string oldCode = "state count = 0;";
        const string inserted = "int ";
        var insertPosition = oldCode.IndexOf("count");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var (oldState, newState) = ParseStateIncremental(
            newCode,
            oldCode,
            insertPosition,
            oldLength: 0,
            newLength: inserted.Length);

        Assert.Same(oldState.StateKeyword, newState.StateKeyword);
        Assert.Null(oldState.Type);
        Assert.NotNull(newState.Type);
        Assert.Same(oldState.Name, newState.Name);
        Assert.Same(oldState.EqualsToken, newState.EqualsToken);
        Assert.Same(oldState.Initializer, newState.Initializer);
        Assert.Same(oldState.Semicolon, newState.Semicolon);
        Assert.Equal(newCode, newState.ToFullString());
    }

    private static (GreenStateDeclarationSyntax OldState, GreenStateDeclarationSyntax NewState) ParseStateIncremental(
        string newCode,
        string oldCode,
        int changeStart,
        int oldLength,
        int newLength)
    {
        var oldSyntax = ParseDocument(oldCode);
        var oldState = Assert.IsType<GreenStateDeclarationSyntax>(oldSyntax.Members[0]);

        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        var change = new TextChangeRange(new TextSpan(changeStart, oldLength), newLength);

        using var parser = ParserHelper.MakeIncrementalParser(newCode, oldTree, [change]);
        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(1, syntax.Members.Count);
        return (oldState, Assert.IsType<GreenStateDeclarationSyntax>(syntax.Members[0]));
    }

    private static GreenAkburaDocumentSyntax ParseDocument(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        return parser.ParseCompilationUnit();
    }
}
