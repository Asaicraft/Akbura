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

    [Fact]
    public void DeleteUsingSemicolon_PreservesFollowingStateAndMarkup()
    {
        const string oldCode =
            "using Akbura.Styles.akcss;\n" +
            "state int count = 0;\n" +
            "\n" +
            "<Button Content={count}/>";
        var semicolonPosition = oldCode.IndexOf(';');
        var newCode = oldCode.Remove(semicolonPosition, count: 1);
        var oldSyntax = Parse(oldCode);
        var change = new TextChangeRange(
            new TextSpan(semicolonPosition, length: 1),
            newLength: 0);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Equal(3, incremental.Members.Count);
        var usingDirective = Assert.IsType<GreenUsingDirectiveSyntax>(incremental.Members[0]);
        Assert.True(usingDirective.Semicolon.IsMissing);
        var state = Assert.IsType<GreenStateDeclarationSyntax>(incremental.Members[1]);
        Assert.Equal("count", state.Name.Identifier.ValueText);
        Assert.IsType<GreenMarkupRootSyntax>(incremental.Members[2]);
    }

    [Fact]
    public void TypingIncompleteTopLevelMembers_MatchesFullParse()
    {
        var cases = new[]
        {
            (
                Code:
                    "param int Count\n" +
                    "state int value = 0;",
                First: typeof(GreenParamDeclarationSyntax),
                Second: typeof(GreenStateDeclarationSyntax)),
            (
                Code:
                    "inject IService Service\n" +
                    "state int value = 0;",
                First: typeof(GreenInjectDeclarationSyntax),
                Second: typeof(GreenStateDeclarationSyntax)),
            (
                Code:
                    "command void Save()\n" +
                    "<Border/>",
                First: typeof(GreenCommandDeclarationSyntax),
                Second: typeof(GreenMarkupRootSyntax)),
            (
                Code:
                    "useEffect(() => { })\n" +
                    "<Border/>",
                First: typeof(GreenCSharpStatementSyntax),
                Second: typeof(GreenMarkupRootSyntax)),
        };

        foreach (var testCase in cases)
        {
            var code = string.Empty;
            var incremental = Parse(code);

            foreach (var character in testCase.Code)
            {
                var newCode = code + character;
                var change = new TextChangeRange(
                    new TextSpan(code.Length, length: 0),
                    newLength: 1);

                incremental = ParseIncremental(
                    newCode,
                    incremental,
                    [change]);
                var full = Parse(newCode);

                Assert.Equal(newCode, incremental.ToFullString());
                AssertSameTree(full, incremental, newCode);

                code = newCode;
            }

            Assert.Equal(2, incremental.Members.Count);
            var firstMember = incremental.Members[0]!;
            var secondMember = incremental.Members[1]!;
            Assert.Equal(
                testCase.First,
                firstMember.GetType());
            Assert.Equal(
                testCase.Second,
                secondMember.GetType());
            Assert.True(firstMember.ContainsDiagnostics);
            Assert.True(
                firstMember switch
                {
                    GreenParamDeclarationSyntax parameter =>
                        parameter.Semicolon.IsMissing,
                    GreenInjectDeclarationSyntax inject =>
                        inject.Semicolon.IsMissing,
                    GreenCommandDeclarationSyntax command =>
                        command.Semicolon.IsMissing,
                    GreenCSharpStatementSyntax => true,
                    _ => false,
                });
        }
    }

    private static void AssertSameTree(
        GreenNode expected,
        GreenNode actual,
        string source)
    {
        if (ReferenceEquals(expected, actual))
        {
            return;
        }

        Assert.True(
            expected.Kind == actual.Kind,
            $"Kind mismatch. Expected {expected.Kind}, " +
            $"actual {actual.Kind}, source '{source}'.");
        Assert.True(
            expected.FullWidth == actual.FullWidth,
            $"Tree width mismatch. Expected {expected.FullWidth}, " +
            $"actual {actual.FullWidth}, expected text " +
            $"'{expected.ToFullString()}', actual text " +
            $"'{actual.ToFullString()}', source '{source}'.");
        Assert.True(
            expected.SlotCount == actual.SlotCount,
            $"Slot-count mismatch for {expected.Kind}. " +
            $"Expected {expected.SlotCount}, actual {actual.SlotCount}, " +
            $"source '{source}'.");
        Assert.True(
            expected.IsMissing == actual.IsMissing,
            $"Missing-state mismatch for {expected.Kind}. " +
            $"Expected {expected.IsMissing}, actual {actual.IsMissing}, " +
            $"expected text '{expected.ToFullString()}', " +
            $"actual text '{actual.ToFullString()}', source '{source}'.");
        Assert.Equal(
            expected
                .GetDiagnostics()
                .Select(static diagnostic => diagnostic.Code),
            actual
                .GetDiagnostics()
                .Select(static diagnostic => diagnostic.Code));

        if (expected.SlotCount == 0)
        {
            Assert.Equal(
                expected.ToFullString(),
                actual.ToFullString());
            return;
        }

        for (var index = 0; index < expected.SlotCount; index++)
        {
            var expectedChild = expected.GetSlot(index);
            var actualChild = actual.GetSlot(index);

            Assert.Equal(
                expectedChild is null,
                actualChild is null);

            if (expectedChild is null ||
                actualChild is null)
            {
                continue;
            }

            AssertSameTree(expectedChild, actualChild, source);
        }
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
