using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.TestUtilities;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.UnitTests;

public sealed class RealisticIncrementalParserTests
{
    [Fact]
    public void IncrementalCSharpStringExpressionAfterBlenderReset_RoundTrips()
    {
        var oldCode = BuildReusableStatePrefix(40) +
            "state string title = \"Old\";\n" +
            "<TextBlock Text={title}/>";
        var newCode = oldCode.Replace("\"Old\"", "\"New\"", StringComparison.Ordinal);
        var oldSyntax = Parse(oldCode);
        var changeStart = oldCode.IndexOf("\"Old\"", StringComparison.Ordinal);
        var change = new TextChangeRange(new TextSpan(changeStart, "\"Old\"".Length), "\"New\"".Length);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldSyntax.Members[0], incremental.Members[0]);
    }

    [Fact]
    public void IncrementalChangeAfterMarkupDynamicTailwindBoundary_RoundTrips()
    {
        const string oldCode =
            "state bool isBusy = false;\n" +
            "<StackPanel class=\"card\" {isBusy}:hidden>\n" +
            "\t<Button OnClick={isBusy = true}>Open</Button>\n" +
            "</StackPanel>\n" +
            "state int after = 0;";
        const string newCode =
            "state bool isBusy = false;\n" +
            "<StackPanel class=\"card\" {isBusy}:hidden>\n" +
            "\t<Button OnClick={isBusy = true}>Open</Button>\n" +
            "</StackPanel>\n" +
            "state int after = 1;";
        var oldSyntax = Parse(oldCode);
        Assert.Equal("state bool isBusy = false;\n".Length, oldSyntax.Members[0]!.FullWidth);
        var expectedMarkupWidth =
            oldCode.IndexOf("state int after", StringComparison.Ordinal) - "state bool isBusy = false;\n".Length;
        Assert.Equal(expectedMarkupWidth, oldSyntax.Members[1]!.FullWidth);
        var changeStart = oldCode.LastIndexOf("0;", StringComparison.Ordinal);
        var change = new TextChangeRange(new TextSpan(changeStart, 1), newLength: 1);

        var incremental = ParseIncremental(newCode, oldSyntax, [change]);

        var state = Assert.IsType<GreenStateDeclarationSyntax>(incremental.Members[2]);
        Assert.Equal("= ", state.EqualsToken.ToFullString());
        Assert.Equal("1", state.Initializer.ToFullString());
        Assert.Equal(";", state.Semicolon.ToFullString());
        Assert.Equal(newCode, incremental.ToFullString());
        Assert.Same(oldSyntax.Members[1], incremental.Members[1]);
    }

    [Fact]
    public void RealisticInvalidMultiEdit_ReusesMostOfOldTreeAndRoundTrips()
    {
        var scenario = RealisticIncrementalScenario.Create(stableSectionCount: 160);
        var oldSyntax = Parse(scenario.OldCode);

        Assert.False(oldSyntax.ContainsDiagnostics);

        var incremental = ParseIncremental(scenario.NewCode, oldSyntax, scenario.Changes);

        Assert.Equal(scenario.NewCode, incremental.ToFullString());
        Assert.Contains("var visible = ;", scenario.NewCode);
        Assert.Contains("var broken = ;", scenario.NewCode);
        Assert.True(HasTopLevelMember(incremental, SyntaxKind.InlineAkcssBlockSyntax));
        Assert.True(HasTopLevelMember(incremental, SyntaxKind.InjectDeclarationSyntax));
        Assert.True(HasTopLevelMember(incremental, SyntaxKind.CommandDeclarationSyntax));
        Assert.True(HasTopLevelMember(incremental, SyntaxKind.StateDeclarationSyntax));
        Assert.True(HasTopLevelMember(incremental, SyntaxKind.CSharpStatementSyntax));
        Assert.True(
            HasTopLevelMember(incremental, SyntaxKind.MarkupRootSyntax),
            "Actual top-level members: " + GetTopLevelMemberKindText(incremental));

        var topLevelReuseRatio = GetTopLevelReuseRatio(oldSyntax, incremental);
        Assert.True(
            topLevelReuseRatio >= 0.95,
            $"Expected at least 95% top-level reuse, actual {topLevelReuseRatio:P2}.");

        var treeReuseRatio = GetReusableSyntaxNodeRatio(oldSyntax, incremental);
        Assert.True(
            treeReuseRatio >= 0.95,
            $"Expected at least 95% tree-node reuse, actual {treeReuseRatio:P2}.");
    }

    private static string BuildReusableStatePrefix(int count)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            builder.Append("state int prefix");
            builder.Append(i);
            builder.Append(" = ");
            builder.Append(i);
            builder.AppendLine(";");
        }

        return builder.ToString();
    }

    private static GreenAkburaDocumentSyntax Parse(string code)
    {
        using var parser = ParserHelper.MakeParser(code);
        var syntax = parser.ParseCompilationUnit();
        AssertFullWidthMatchesText(code, syntax);
        return syntax;
    }

    private static GreenAkburaDocumentSyntax ParseIncremental(
        string code,
        GreenAkburaDocumentSyntax oldSyntax,
        IEnumerable<TextChangeRange>? changes)
    {
        var oldTree = (AkburaDocumentSyntax)oldSyntax.CreateRed();
        using var parser = ParserHelper.MakeIncrementalParser(code, oldTree, changes);
        var syntax = parser.ParseCompilationUnit();
        AssertFullWidthMatchesText(code, syntax);
        return syntax;
    }

    private static void AssertFullWidthMatchesText(string code, GreenAkburaDocumentSyntax syntax)
    {
        if (code.Length == syntax.FullWidth)
        {
            return;
        }

        var builder = new StringBuilder();
        builder.Append("Root FullWidth mismatch. ");
        builder.Append("Text length: ");
        builder.Append(code.Length);
        builder.Append(", FullWidth: ");
        builder.Append(syntax.FullWidth);
        builder.AppendLine(".");

        for (var i = 0; i < syntax.Members.Count; i++)
        {
            var member = syntax.Members[i];
            if (member == null)
            {
                continue;
            }

            var textLength = member.ToFullString().Length;
            if (textLength != member.FullWidth)
            {
                builder.Append("Member ");
                builder.Append(i);
                builder.Append(" ");
                builder.Append(member.Kind);
                builder.Append(" text length ");
                builder.Append(textLength);
                builder.Append(" full width ");
                builder.Append(member.FullWidth);
                builder.AppendLine(".");
            }
        }

        Assert.Fail(builder.ToString());
    }

    private static double GetTopLevelReuseRatio(
        GreenAkburaDocumentSyntax oldSyntax,
        GreenAkburaDocumentSyntax newSyntax)
    {
        var newMembers = new HashSet<GreenNode>(ReferenceEqualityComparer.Instance);
        for (var i = 0; i < newSyntax.Members.Count; i++)
        {
            var member = newSyntax.Members[i];
            if (member != null)
            {
                newMembers.Add(member);
            }
        }

        var reused = 0;
        for (var i = 0; i < oldSyntax.Members.Count; i++)
        {
            var member = oldSyntax.Members[i];
            if (member != null && newMembers.Contains(member))
            {
                reused++;
            }
        }

        return (double)reused / oldSyntax.Members.Count;
    }

    private static bool HasTopLevelMember(GreenAkburaDocumentSyntax syntax, SyntaxKind kind)
    {
        for (var i = 0; i < syntax.Members.Count; i++)
        {
            if (syntax.Members[i]?.Kind == kind)
            {
                return true;
            }
        }

        return false;
    }

    private static string GetTopLevelMemberKindText(GreenAkburaDocumentSyntax syntax)
    {
        var builder = new StringBuilder();
        for (var i = 0; i < syntax.Members.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            builder.Append(syntax.Members[i]?.Kind.ToString() ?? "<null>");
        }

        return builder.ToString();
    }

    private static double GetReusableSyntaxNodeRatio(GreenNode oldRoot, GreenNode newRoot)
    {
        var oldNodes = new List<GreenNode>();
        var newNodes = new HashSet<GreenNode>(ReferenceEqualityComparer.Instance);

        CollectReusableSyntaxNodes(oldRoot, oldNodes);
        CollectReusableSyntaxNodes(newRoot, newNodes);

        var reused = 0;
        foreach (var oldNode in oldNodes)
        {
            if (newNodes.Contains(oldNode))
            {
                reused++;
            }
        }

        return (double)reused / oldNodes.Count;
    }

    private static void CollectReusableSyntaxNodes(GreenNode node, ICollection<GreenNode> nodes)
    {
        if (node.IsToken || node.IsTrivia)
        {
            return;
        }

        if (!node.IsList && node.FullWidth > 0)
        {
            nodes.Add(node);
        }

        for (var i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetSlot(i);
            if (child != null)
            {
                CollectReusableSyntaxNodes(child, nodes);
            }
        }
    }

}
