using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class InjectIncrementalParserTests
{
    [Fact]
    public void TypeEdit_ReusesNameAndSemicolon()
    {
        const string oldCode = "inject ILogger<DashboardPage> log;";
        const string newCode = "inject ILogger<HomePage> log;";

        var (oldInject, newInject) = ParseInjectIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("DashboardPage"),
            oldLength: "DashboardPage".Length,
            newLength: "HomePage".Length);

        Assert.NotSame(oldInject, newInject);
        Assert.Same(oldInject.InjectKeyword, newInject.InjectKeyword);
        Assert.NotSame(oldInject.Type, newInject.Type);
        Assert.Same(oldInject.Name, newInject.Name);
        Assert.Same(oldInject.Semicolon, newInject.Semicolon);
        Assert.Equal(newCode, newInject.ToFullString());
    }

    [Fact]
    public void NameEdit_ReusesTypeAndSemicolon()
    {
        const string oldCode = "inject DashboardViewModel viewModel;";
        const string newCode = "inject DashboardViewModel model;";

        var (oldInject, newInject) = ParseInjectIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("viewModel"),
            oldLength: "viewModel".Length,
            newLength: "model".Length);

        Assert.Same(oldInject.InjectKeyword, newInject.InjectKeyword);
        Assert.Same(oldInject.Type, newInject.Type);
        Assert.NotSame(oldInject.Name, newInject.Name);
        Assert.Same(oldInject.Semicolon, newInject.Semicolon);
        Assert.Equal(newCode, newInject.ToFullString());
    }

    [Fact]
    public void VerbatimNameEdit_ReusesTypeAndSemicolon()
    {
        const string oldCode = "inject int @class;";
        const string newCode = "inject int @namespace;";

        var (oldInject, newInject) = ParseInjectIncremental(
            newCode,
            oldCode,
            oldCode.IndexOf("class"),
            oldLength: "class".Length,
            newLength: "namespace".Length);

        Assert.Same(oldInject.InjectKeyword, newInject.InjectKeyword);
        Assert.Same(oldInject.Type, newInject.Type);
        Assert.NotSame(oldInject.Name, newInject.Name);
        Assert.Same(oldInject.Semicolon, newInject.Semicolon);
        Assert.Equal(newCode, newInject.ToFullString());
    }

    private static (GreenInjectDeclarationSyntax OldInject, GreenInjectDeclarationSyntax NewInject) ParseInjectIncremental(
        string newCode,
        string oldCode,
        int changeStart,
        int oldLength,
        int newLength)
    {
        var oldSyntax = ParseDocument(oldCode);
        var oldInject = Assert.IsType<GreenInjectDeclarationSyntax>(oldSyntax.Members[0]);

        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        var change = new TextChangeRange(new TextSpan(changeStart, oldLength), newLength);

        using var parser = ParserHelper.MakeIncrementalParser(newCode, oldTree, [change]);
        var syntax = parser.ParseCompilationUnit();

        Assert.Equal(1, syntax.Members.Count);
        return (oldInject, Assert.IsType<GreenInjectDeclarationSyntax>(syntax.Members[0]));
    }

    private static GreenAkburaDocumentSyntax ParseDocument(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        return parser.ParseCompilationUnit();
    }
}
