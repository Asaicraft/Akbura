using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace Akbura.UnitTests;

public sealed class RealisticIncrementalParserTests
{
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

    private readonly record struct RealisticIncrementalScenario(
        string OldCode,
        string NewCode,
        TextChangeRange[] Changes)
    {
        public static RealisticIncrementalScenario Create(int stableSectionCount)
        {
            var oldCode = BuildOldCode(stableSectionCount);
            var edits = CreateEdits(oldCode);
            var newCode = ApplyEdits(oldCode, edits);
            var changes = new[] { CreateCollapsedChange(edits) };

            return new RealisticIncrementalScenario(oldCode, newCode, changes);
        }

        private static TextChangeRange CreateCollapsedChange(IReadOnlyCollection<Edit> edits)
        {
            var oldStart = edits.Min(edit => edit.Start);
            var oldEnd = edits.Max(edit => edit.Start + edit.OldLength);
            var delta = edits.Sum(edit => edit.NewText.Length - edit.OldLength);
            var oldLength = oldEnd - oldStart;

            return new TextChangeRange(new TextSpan(oldStart, oldLength), oldLength + delta);
        }

        private static Edit[] CreateEdits(string oldCode)
        {
            var newline = Environment.NewLine;
            const string stateDeclaration = "state bool isOpen = false;";
            var stateDeclarationStart = IndexOfRequired(oldCode, stateDeclaration);

            const string useEffectStart = "useEffect(UserId, Search)";
            var largeInsertionStart = IndexOfRequired(oldCode, useEffectStart);

            var visibleLine = "    var visible = isOpen;" + newline;
            var visibleLineStart = IndexOfRequired(oldCode, visibleLine);
            var invalidDeleteStart = IndexOfRequired(oldCode, "isOpen", visibleLineStart);
            var invalidSyntaxInsertionStart = visibleLineStart + visibleLine.Length;

            var taskListLine = "    <TaskList Items={tasks} out:Selected={SelectedTask}/>" + newline;
            var validDeleteStart = IndexOfRequired(oldCode, taskListLine);

            return
            [
                new Edit(stateDeclarationStart, stateDeclaration.Length, "state bool isPanelOpen = false;"),
                new Edit(largeInsertionStart, 0, BuildLargeTopLevelInsertion()),
                new Edit(validDeleteStart, taskListLine.Length, string.Empty),
                new Edit(invalidDeleteStart, "isOpen".Length, string.Empty),
                new Edit(invalidSyntaxInsertionStart, 0, "    var broken = ;" + newline)
            ];
        }

        private static string BuildOldCode(int stableSectionCount)
        {
            var builder = new StringBuilder();

            builder.AppendLine("using System;");
            builder.AppendLine("global using static System.Math;");
            builder.AppendLine("namespace Demo.App;");
            builder.AppendLine();
            builder.AppendLine("@akcss {");
            builder.AppendLine("    .card {");
            builder.AppendLine("        Padding: 12;");
            builder.AppendLine("        Background: \"White\";");
            builder.AppendLine();
            builder.AppendLine("        @if(IsHovered) {");
            builder.AppendLine("            Background: \"AliceBlue\";");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    @utilities {");
            builder.AppendLine("        .gap-(double value) {");
            builder.AppendLine("            RowGap: value * Spacing;");
            builder.AppendLine("        }");
            builder.AppendLine();
            builder.AppendLine("        .accent {");
            builder.AppendLine("            BorderBrush: \"DodgerBlue\";");
            builder.AppendLine("        }");
            builder.AppendLine("    }");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("inject ILogger<DashboardPage> log;");
            builder.AppendLine("inject DashboardViewModel viewModel;");
            builder.AppendLine();
            builder.AppendLine("param int UserId = 1;");
            builder.AppendLine("param bind string Search = \"\";");
            builder.AppendLine("param out SelectedTask;");
            builder.AppendLine();
            builder.AppendLine("state bool isBusy = bind viewModel.IsBusy;");
            builder.AppendLine();

            for (var i = 0; i < stableSectionCount; i++)
            {
                AppendStableSection(builder, "pre", i, includeMarkup: false);
            }

            builder.AppendLine("state int stableBoundary = 0;");
            builder.AppendLine();
            builder.AppendLine("namespace Feature.Area;");
            builder.AppendLine();
            builder.AppendLine("state bool isOpen = false;");
            builder.AppendLine("state ReactList tasks = bind viewModel.Tasks;");
            builder.AppendLine();
            builder.AppendLine("command Task Refresh(int userId);");
            builder.AppendLine();
            builder.AppendLine("useEffect(UserId, Search) {");
            builder.AppendLine("    log.LogInformation(\"Loading user\");");
            builder.AppendLine();
            builder.AppendLine("    if(UserId < 0) {");
            builder.AppendLine("        return;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    <TextBlock Text=\"Effect loaded\" class=\"status\"/>");
            builder.AppendLine("}");
            builder.AppendLine("cancel {");
            builder.AppendLine("    log.LogInformation(\"Cancelled\");");
            builder.AppendLine("}");
            builder.AppendLine("finally {");
            builder.AppendLine("    log.LogInformation(\"Done\");");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("if(isOpen)");
            builder.AppendLine("{");
            builder.AppendLine("    Console.WriteLine(\"Panel opened\");");
            builder.AppendLine("    var visible = isOpen;");
            builder.AppendLine();
            builder.AppendLine("    <TextBlock Text=\"Opened!\" class=\"status\"/>");
            builder.AppendLine("    <Border class=\"box\" IsVisible={isOpen}/>");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("state int afterConditionalBoundary = 0;");
            builder.AppendLine();
            builder.AppendLine("<StackPanel class=\"card\" gap-4 p-4 {isOpen}:opacity-50>");
            builder.AppendLine("    <TextBlock Text=\"Dashboard\" class=\"title\"/>");
            builder.AppendLine("    <Input bind:Value={Search} Placeholder=\"Search tasks\"/>");
            builder.AppendLine("    <Button OnClick={isOpen = true} class=\"primary\" w-30>");
            builder.AppendLine("        Open");
            builder.AppendLine("    </Button>");
            builder.AppendLine("    <TaskList Items={tasks} out:Selected={SelectedTask}/>");
            builder.AppendLine("    <Border class=\"box\" IsVisible={isOpen}/>");
            builder.AppendLine("</StackPanel>");
            builder.AppendLine();

            return builder.ToString();
        }

        private static void AppendStableSection(StringBuilder builder, string prefix, int index, bool includeMarkup)
        {
            builder.AppendLine($"state int {prefix}Count{index} = {index};");
            builder.AppendLine();
            builder.AppendLine($"if({prefix}Count{index} >= 0)");
            builder.AppendLine("{");
            builder.AppendLine($"\tvar local{index} = {prefix}Count{index};");
            builder.AppendLine($"\t<TextBlock Text=\"{prefix} {index}\" class=\"status\"/>");
            builder.AppendLine("}");
            builder.AppendLine();

            if (!includeMarkup)
            {
                return;
            }

            builder.AppendLine($"<StackPanel class=\"card\" gap-2 p-{index % 8} {{isBusy}}:opacity-50>");
            builder.AppendLine($"\t<TextBlock Text=\"{prefix} item {index}\" class=\"label\"/>");
            builder.AppendLine($"\t<Button OnClick={{{prefix}Count{index}++}} class=\"primary\" w-30>");
            builder.AppendLine("\t\tIncrement");
            builder.AppendLine("\t</Button>");
            builder.AppendLine("</StackPanel>");
            builder.AppendLine();
        }

        private static string BuildLargeTopLevelInsertion()
        {
            var builder = new StringBuilder();

            builder.AppendLine("inject INotificationService notifications;");
            builder.AppendLine("command Task ArchiveTask(int taskId);");
            builder.AppendLine("state int insertedCount = 0;");
            builder.AppendLine("state string insertedTitle = \"Inserted\";");
            builder.AppendLine();
            builder.AppendLine("if(insertedCount >= 0)");
            builder.AppendLine("{");
            builder.AppendLine("    Console.WriteLine(\"Inserted block\");");
            builder.AppendLine();
            builder.AppendLine("    <TextBlock Text=\"Inserted\" class=\"inserted-status\"/>");
            builder.AppendLine("}");
            builder.AppendLine();
            builder.AppendLine("<StackPanel class=\"inserted-card\" gap-2>");
            builder.AppendLine("    <Button OnClick={insertedCount++} class=\"accent\">Inserted</Button>");
            builder.AppendLine("</StackPanel>");
            builder.AppendLine();

            return builder.ToString();
        }

        private static string ApplyEdits(string oldCode, IReadOnlyCollection<Edit> edits)
        {
            var newCode = oldCode;
            foreach (var edit in edits.OrderByDescending(edit => edit.Start))
            {
                newCode = newCode.Remove(edit.Start, edit.OldLength).Insert(edit.Start, edit.NewText);
            }

            return newCode;
        }

        private static int IndexOfRequired(string text, string value, int startIndex = 0)
        {
            var index = text.IndexOf(value, startIndex, StringComparison.Ordinal);
            if (index < 0)
            {
                throw new InvalidOperationException($"Could not find required text: {value}");
            }

            return index;
        }
    }

    private readonly record struct Edit(int Start, int OldLength, string NewText);
}
