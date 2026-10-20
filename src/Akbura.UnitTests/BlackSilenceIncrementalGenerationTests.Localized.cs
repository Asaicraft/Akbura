using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Akbura.TestUtilities;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    public static IEnumerable<object[]> LocalizedMarkupWorkloads()
    {
        foreach (var source in new[]
        {
            "state int count = 1; <StackPanel><TextBlock Margin=\"10\" Text={count.ToString()} /><TextBlock Text={count.ToString()} /><TextBlock Text={(count + 2).ToString()} /></StackPanel>",
            "param string Title = \"title\"; inject System.IServiceProvider Services; <StackPanel><Border Margin=\"10\"><StackPanel><TextBlock Text={Title} /><TextBlock Text={Services.GetHashCode().ToString()} /></StackPanel></Border></StackPanel>",
            "<StackPanel><TextBlock x.Name=\"first\" Text=\"one\" /><TextBlock Margin=\"10\" Text={first.Text} /><TextBlock Text={first.Text} /></StackPanel>",
            "<StackPanel>$foreach (var item in new[] { 1, 2 }) { var value = item + 1; <TextBlock Margin=\"10\" Text={value.ToString()} /><TextBlock Text={value.ToString()} /> }</StackPanel>",
            "state bool visible = true; <StackPanel>$if (visible) { <TextBlock Margin=\"10\" Text={visible.ToString()} /><TextBlock Text={visible.ToString()} /> } $else { <Border /> }</StackPanel>",
            "using Avalonia.Markup.Xaml.Templates; <DataTemplate><StackPanel><TextBlock Margin=\"10\" Text={\"item\"} /><TextBlock Text={\"item\"} /></StackPanel></DataTemplate>",
        })
        {
            yield return [source, false];
            yield return [source, true];
        }
    }

    [Theory]
    [MemberData(nameof(LocalizedMarkupWorkloads))]
    public void LocalizedEdits_UndoRedoAndRepeatedSourcesMatchFreshWithExactDiagnostics(string body, bool structural)
    {
        var project = new TestProject(publishDiagnostics: true, structural);
        var source = Component(body);
        var previous = project.File("Page.akbura", source);
        var current = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, previous));
        Assert.DoesNotContain(current.Diagnostics.Concat(current.Output.GetDiagnostics()), diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        foreach (var value in new[] { 20, 30, 10, 40, 40, 10, 20, 10 })
        {
            var replacement = project.File("Page.akbura", source.Replace("Margin=\"10\"", "Margin=\"" + value + "\"", StringComparison.Ordinal));
            using var measurement = GenerationStatistics.BeginMeasurement();
            current = RunProbeAllowingDiagnostics(project, current.Driver.ReplaceAdditionalText(previous, replacement));
            measurement.Dispose();
            var fresh = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, project.File("Page.akbura", replacement.Text.ToString())));
            AssertLocalizedParity(fresh, current);
            previous = replacement;
        }
    }

    [Theory]
    [InlineData("<TextBlock Margin=\"20\" Text={count.ToString()} />")]
    [InlineData("<TextBlock Margin=\"10\" Width=\"250\" Text={count.ToString()} />")]
    [InlineData("<TextBlock Text={count.ToString()} />")]
    [InlineData("<Border /><TextBlock Margin=\"10\" Text={count.ToString()} />")]
    [InlineData("<TextBlock Margin=\"10\" Text={(count + 1).ToString()} />")]
    [InlineData("<TextBlock x.Name=\"changed\" Margin=\"10\" Text={count.ToString()} />")]
    [InlineData("<TextBlock Margin=\"10\" Text={count.Missing()} />")]
    [InlineData("<TextBlock FineMissing=\"1\" Margin=\"10\" Text={count.ToString()} />")]
    [InlineData("<TextBlock Margin=\"bad\" Text={count.ToString()} />")]
    [InlineData("\r\n   <TextBlock Margin=\"10\" Text={count.ToString()} />")]
    public void LocalizedEditCategories_RepairAndRevertKeepCurrentMappings(string markup)
    {
        var project = new TestProject(publishDiagnostics: true);
        const string target = "<TextBlock Margin=\"10\" Text={count.ToString()} />";
        var source = Component("state int count = 1; <StackPanel>" + target + "<TextBlock Text={count.MissingAfterShift()} /></StackPanel>");
        var previous = project.File("Page.akbura", source);
        var current = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, previous));
        foreach (var text in new[] { source.Replace(target, markup), source, source.Replace("MissingAfterShift()", "ToString()"), source })
        {
            var replacement = project.File("Page.akbura", text);
            current = RunProbeAllowingDiagnostics(project, current.Driver.ReplaceAdditionalText(previous, replacement));
            AssertLocalizedParity(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, project.File("Page.akbura", text))), current);
            previous = replacement;
        }
    }

    [Theory]
    [InlineData("using Alias = System.Int32; state Alias value = 1;", "using Alias = System.Double; state Alias value = 1;")]
    [InlineData("param int value = 1;", "param string value = \"x\";")]
    [InlineData("state int value = 1;", "state string value = \"x\";")]
    public void LocalizedContextChanges_DoNotCarryOldExpressionResults(string before, string after)
    {
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", Component(before + "<StackPanel><TextBlock Text={value.ToString()} /><TextBlock Text={value.ToString()} /></StackPanel>"));
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, original));
        var changed = original.Replace(before, after);
        var current = RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(original, changed));
        AssertLocalizedParity(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, changed)), current);
    }

    [Fact]
    public async Task LocalizedConcurrentBranches_DoNotShareCompletedOrPartialCaches()
    {
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", Component("state int count = 1; <StackPanel><TextBlock Margin=\"10\" Text={count.ToString()} /><TextBlock Text={count.ToString()} /></StackPanel>"));
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, original));
        var files = new[] { original.Replace("10", "20"), original.Replace("10", "30") };
        var branches = await Task.WhenAll(files.Select(file => Task.Run(() => RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(original, file)))));
        for (var index = 0; index < files.Length; index++)
        {
            AssertLocalizedParity(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, files[index])), branches[index]);
        }
    }

    private static void AssertLocalizedParity(
        (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) expected,
        (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) actual)
    {
        Assert.Equal(expected.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(source => source.HintName)
            .Select(source => (source.HintName, source.SourceText.ToString())),
            actual.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(source => source.HintName)
                .Select(source => (source.HintName, source.SourceText.ToString())));
        Assert.Equal(expected.Diagnostics.Select(DiagnosticParity.Key).Order(), actual.Diagnostics.Select(DiagnosticParity.Key).Order());
        Assert.Equal(expected.Output.GetDiagnostics().Select(DiagnosticParity.Key).Order(), actual.Output.GetDiagnostics().Select(DiagnosticParity.Key).Order());
    }
}
