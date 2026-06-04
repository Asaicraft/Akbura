using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class IncrementalParserTests
{
    [Fact]
    public void NoChange_ReusesTopLevelMembersAndRoundTrips()
    {
        const string code =
            "using System;\n" +
            "global using static System.Math;\n" +
            "namespace Demo.App;";

        var oldSyntax = Parse(code);
        var incremental = ParseIncremental(code, oldSyntax, changes: null);

        Assert.Equal(code, incremental.ToFullString());
        Assert.Equal(oldSyntax.Members.Count, incremental.Members.Count);
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.Same(oldSyntax.Members[1], incremental.Members[1]);
        Assert.Same(oldSyntax.Members[2], incremental.Members[2]);
    }

    [Fact]
    public void StateEdit_ReparsesChangedMemberAndReusesNeighbors()
    {
        const string oldCode =
            "using System;\n" +
            "state int count = 0;\n" +
            "using Demo;";
        const string newCode =
            "using System;\n" +
            "state int count = 1;\n" +
            "using Demo;";

        var oldSyntax = Parse(oldCode);
        var changeStart = oldCode.IndexOf("0;");
        var change = new TextChangeRange(new TextSpan(changeStart, length: 1), newLength: 1);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.NotSame(oldSyntax.Members[1], incremental.Members[1]);
        Assert.Same(oldSyntax.Members[2], incremental.Members[2]);
    }

    [Fact]
    public void MarkupEdit_ReparsesChangedMarkupRootAndReusesNeighbors()
    {
        const string oldCode =
            "using System;\n" +
            "<TextBlock Text=\"Hi\"/>\n" +
            "using Demo;";
        const string newCode =
            "using System;\n" +
            "<TextBlock Text=\"Hello\"/>\n" +
            "using Demo;";

        var oldSyntax = Parse(oldCode);
        var changeStart = oldCode.IndexOf("Hi");
        var change = new TextChangeRange(new TextSpan(changeStart, length: 2), newLength: 5);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.NotSame(oldSyntax.Members[1], incremental.Members[1]);
        Assert.Same(oldSyntax.Members[2], incremental.Members[2]);
    }

    [Fact]
    public void InsertTopLevelMember_ReusesSurroundingMembers()
    {
        const string oldCode =
            "using System;\n" +
            "using Demo;";
        const string inserted = "state int b = 1;\n";
        var insertPosition = oldCode.IndexOf("using Demo");
        var newCode = oldCode.Insert(insertPosition, inserted);

        var oldSyntax = Parse(oldCode);
        var change = new TextChangeRange(new TextSpan(insertPosition, length: 0), inserted.Length);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(3, incremental.Members.Count);
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.IsType<GreenStateDeclarationSyntax>(incremental.Members[1]);
        Assert.Same(oldSyntax.Members[1], incremental.Members[2]);
    }

    [Fact]
    public void DeleteTopLevelMember_ReusesSurroundingMembers()
    {
        const string oldCode =
            "using System;\n" +
            "state int b = 1;\n" +
            "using Demo;";
        const string deleted = "state int b = 1;\n";
        var deletePosition = oldCode.IndexOf(deleted);
        var newCode = oldCode.Remove(deletePosition, deleted.Length);

        var oldSyntax = Parse(oldCode);
        var change = new TextChangeRange(new TextSpan(deletePosition, deleted.Length), newLength: 0);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(2, incremental.Members.Count);
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
        Assert.Same(oldSyntax.Members[2], incremental.Members[1]);
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
