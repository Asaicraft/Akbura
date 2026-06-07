using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Akbura.TestUtilities;

internal readonly record struct RealisticIncrementalScenario(
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
            AppendStableSection(builder, "pre", i);
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

    private static void AppendStableSection(StringBuilder builder, string prefix, int index)
    {
        builder.AppendLine($"state int {prefix}Count{index} = {index};");
        builder.AppendLine();
        builder.AppendLine($"if({prefix}Count{index} >= 0)");
        builder.AppendLine("{");
        builder.AppendLine($"\tvar local{index} = {prefix}Count{index};");
        builder.AppendLine($"\t<TextBlock Text=\"{prefix} {index}\" class=\"status\"/>");
        builder.AppendLine("}");
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

    private readonly record struct Edit(int Start, int OldLength, string NewText);
}
