using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    public static IEnumerable<object[]> ProbeWorkloads()
    {
        foreach (var workload in CreateProbeWorkloadSources())
        {
            yield return [workload[0], workload[1], false];
            yield return [workload[0], workload[1], true];
        }
    }

    private static IEnumerable<object[]> CreateProbeWorkloadSources()
    {
        yield return ["repeated targets", Component("state int count = 1;\r\n<StackPanel>" +
            string.Concat(Enumerable.Repeat("<TextBlock Text={count.ToString()} IsVisible={count > 0} /><Border Width={count} Height={count} />", 8)) +
            "</StackPanel>")];
        yield return ["nested loops", Component("""
            state int count = 1;
            <StackPanel>
                $foreach (var outer in new[] { 1, 2 })
                {
                    $foreach (var item in new[] { outer })
                    {
                        var local = item + count;
                        <TextBlock Text={local.ToString()} />
                    }
                    <TextBlock Text={outer.ToString()} />
                }
                <TextBlock Text={count.ToString()} />
            </StackPanel>
            """)];
        yield return ["conditional scopes", Component("""
            state int count = 1;
            <StackPanel>
                $if (count > 0)
                {
                    $if (count > 1) { <TextBlock Text={count.ToString()} /> }
                    $else { <TextBlock Text="one" /> }
                }
                $else if (count < 0) { <Border Width={-count} /> }
                $else { <TextBlock Text="zero" /> }
            </StackPanel>
            """)];
        yield return ["methods and lambdas", Component("""
            using System.Linq;
            state int count = 1;
            var offset = count;
            T Echo<T>(T value) where T : struct { return value; }
            string Format(int value)
            {
                int Local(int item) { return item + 1; }
                return (Local(value) + offset).ToString();
            }
            <StackPanel>
                <TextBlock Text={Format(Echo(count))} />
                <TextBlock Text={new[] { count }.Select(value => Format(value)).First()} />
            </StackPanel>
            """)];
        yield return ["property hook rewrites", Component("""
            using Akbura.Hooks;
            state int count = 1;
            state double width = useAvaloniaProperty(Width);
            <TextBlock Text={(width + count).ToString()} />
            """)];
    }

    [Theory]
    [MemberData(nameof(ProbeWorkloads))]
    public void ProbeEditsAndReversions_MatchFreshAfterEveryOneOfTwentyChanges(string category, string source, bool debugStructural)
    {
        Assert.NotEmpty(category);
        var project = new TestProject(publishDiagnostics: true, debugStructural);
        var original = project.File("Page.akbura", source);
        var previous = original;
        var run = Run(project, CreateDriver(project.Options, original));
        for (var index = 0; index < 20; index++)
        {
            var current = project.File("Page.akbura", index % 2 == 0 ? source.Replace("count", "count2", StringComparison.Ordinal) : source);
            run = Run(project, run.Driver.ReplaceAdditionalText(previous, current));
            AssertParityWithFresh(project, run, project.Options, current);
            previous = current;
        }
    }

    [Fact]
    public async Task ConcurrentProbeBranches_KeepTheirOwnHookContextsAndMatchFresh()
    {
        var project = new TestProject(publishDiagnostics: true);
        var source = (string)ProbeWorkloads().Last()[1];
        var original = project.File("Page.akbura", source);
        var initial = Run(project, CreateDriver(project.Options, original));
        var left = original.Replace("width + count", "width + count + 2");
        var right = original.Replace("width + count", "width + count + 3");
        var runs = await Task.WhenAll(
            Task.Run(() => Run(project, initial.Driver.ReplaceAdditionalText(original, left))),
            Task.Run(() => Run(project, initial.Driver.ReplaceAdditionalText(original, right))));
        AssertParityWithFresh(project, runs[0], project.Options, left);
        AssertParityWithFresh(project, runs[1], project.Options, right);
        Assert.NotEqual(runs[0].Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()),
            runs[1].Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()));
    }

    [Theory]
    [InlineData("state int value = 1;", "state string value = \"x\";", "value + 1")]
    [InlineData("param int value = 1;", "param string value = \"x\";", "value + 1")]
    [InlineData("inject System.IServiceProvider value;", "inject System.IFormatProvider value;", "value.GetHashCode()")]
    [InlineData("command void Execute(int value);", "command void Execute(string value);", "Execute.CanExecute(1) ? 1.0 : 0.0")]
    [InlineData("using Alias = System.Int32; state Alias value = 1;", "using Alias = System.Double; state Alias value = 1;", "value")]
    [InlineData("state System.Collections.Generic.List<int> value = [1];", "state System.Collections.Generic.List<double> value = [1];", "value[0]")]
    [InlineData("var value = 1;", "var value = 1.5;", "value")]
    [InlineData("int Value() { return 1; }", "double Value() { return 1; }", "Value()")]
    public void ProbeDeclarationChanges_RebindAndMatchFreshIncludingDiagnostics(string before, string after, string expression)
    {
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", Component(before + "\r\n<Border Width={" + expression + "} />"));
        var changed = original.Replace(before, after);
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, original));
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(original, changed));
        var fresh = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, project.File("Page.akbura", changed.Text.ToString())));
        AssertProbeOutputEqual(fresh, updated);
        Assert.NotEqual(initial.Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()),
            updated.Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => source.SourceText.ToString()));
    }

    [Theory]
    [InlineData("Missing(count) + Other(count)", "(count + 1).ToString()")]
    [InlineData("count.DoesNotExist()", "count.ToString()")]
    public void ProbeErrorsAndRepairs_KeepExactDiagnosticIdsMessagesAndMappedLocations(string broken, string repaired)
    {
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", Component("state int count = 1;\r\n<TextBlock Text={" + broken + "} />"));
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, original));
        Assert.Contains(initial.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var changed = original.Replace(broken, repaired);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(original, changed));
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, changed)), updated);
        Assert.DoesNotContain(updated.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        var reverted = RunProbeAllowingDiagnostics(project, updated.Driver.ReplaceAdditionalText(changed, original));
        AssertProbeOutputEqual(initial, reverted);
    }

#if STATS
    [Fact]
    public void CancellationDuringProbeBinding_DoesNotPublishAndRetryMatchesFresh()
    {
        var project = new TestProject(publishDiagnostics: true);
        var original = project.File("Page.akbura", (string)ProbeWorkloads().First()[1]);
        var initial = Run(project, CreateDriver(project.Options, original));
        var changed = original.Replace("count = 1", "count = 2");
        var oldTree = Assert.IsType<ComponentSyntaxTree>(Assert.Single(initial.Request.Documents).SyntaxTree);
        var request = Assert.IsType<BlackSilenceGenerationRequest>(BlackSilenceGenerationRequestBuilder.Create(
            [DocumentSyntaxVersion.Create(oldTree.WithChangedText(changed.Text))], initial.Request.State, initial.Request.Options, CancellationToken.None));
        using var cancellation = new CancellationTokenSource();
        using (GenerationStatistics.BeginMeasurement(() =>
        {
            if (GenerationStatistics.Current!.GetSnapshot().GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticQuery) > 0)
            {
                cancellation.Cancel();
            }

            return GC.GetAllocatedBytesForCurrentThread();
        }))
        {
            Assert.ThrowsAny<OperationCanceledException>(() => BlackSilenceDocumentBatch.Generate(request, cancellation.Token));
        }

        Assert.True(cancellation.IsCancellationRequested);
        Assert.Same(initial.Snapshot, initial.Request.State.TryGetSnapshot(initial.Request.Options));
        Assert.Single(BlackSilenceDocumentBatch.Generate(request, CancellationToken.None).Components);
        var updated = Run(project, initial.Driver.ReplaceAdditionalText(original, changed));
        AssertParityWithFresh(project, updated, project.Options, changed);
    }
#endif

    private static (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) RunProbeAllowingDiagnostics(
        TestProject project, GeneratorDriver driver, CSharpCompilation? compilation = null)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation ?? project.Compilation, out var output, out var diagnostics);
        Assert.Null(Assert.Single(driver.GetRunResult().Results).Exception);
        return (driver, output, diagnostics);
    }

    [Theory]
    [InlineData("nullable")]
    [InlineData("language")]
    [InlineData("options")]
    [InlineData("declaration")]
    [InlineData("unresolved")]
    [InlineData("hook overload")]
    [InlineData("target property")]
    [InlineData("assembly")]
    public void ProbeCompilationChanges_InvalidateCompletedContextsAndMatchFresh(string change)
    {
        var project = new TestProject(publishDiagnostics: true);
        var supportSource = "namespace Demo; public static class Support { public static int Read() => 1; } " +
            "public sealed class Target : Avalonia.Controls.Control { public double Value { get; set; } }";
        var source = Component("using Demo;\r\n<Target Value={Support.Read()} />");
        if (change == "hook overload")
        {
            supportSource += " public static class Hooks { [Akbura.CompilerAnotations.UseHook] public static Akbura.ComponentTree.State<int> useValue(int value) => null!; }";
            source = Component("using Demo;\r\nstate int value = useValue(1);\r\n<Border Width={value} />");
        }
        if (change == "unresolved")
        {
            source = Component("using Demo;\r\n<Target Value={Support.Missing()} />");
        }

        var tree = CSharpSyntaxTree.ParseText(supportSource, CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview), "Support.cs");
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        SyntaxTree ParseSupport(string text) => CSharpSyntaxTree.ParseText(text, (CSharpParseOptions)tree.Options, tree.FilePath);
        CSharpCompilation changed;
        switch (change)
        {
            case "nullable":
                changed = compilation.WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable));
                break;
            case "language":
                changed = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(compilation.SyntaxTrees.Select(syntax =>
                    CSharpSyntaxTree.ParseText(syntax.GetText(), ((CSharpParseOptions)syntax.Options).WithLanguageVersion(LanguageVersion.CSharp12), syntax.FilePath)));
                break;
            case "options":
                changed = compilation.WithOptions(compilation.Options.WithOptimizationLevel(OptimizationLevel.Release));
                break;
            case "declaration":
                changed = compilation.ReplaceSyntaxTree(tree, ParseSupport(supportSource.Replace("int Read()", "double Read()", StringComparison.Ordinal)));
                break;
            case "unresolved":
                changed = compilation.ReplaceSyntaxTree(tree, ParseSupport(supportSource.Replace("int Read()", "int Missing()", StringComparison.Ordinal)));
                break;
            case "hook overload":
                changed = compilation.ReplaceSyntaxTree(tree, ParseSupport(supportSource.Replace("useValue(int value)", "useValue(double value)", StringComparison.Ordinal)));
                break;
            case "target property":
                changed = compilation.ReplaceSyntaxTree(tree, ParseSupport(supportSource.Replace("double Value", "string Value", StringComparison.Ordinal)));
                break;
            default:
                var reference = CreateProbeReference(supportSource);
                compilation = project.Compilation.AddReferences(reference);
                changed = compilation.ReplaceReference(reference, CreateProbeReference(supportSource.Replace("int Read()", "double Read()", StringComparison.Ordinal)));
                break;
        }

        var file = project.File("Page.akbura", source);
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), compilation);
        var changedOptions = (CSharpParseOptions)changed.SyntaxTrees.First().Options;
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver.WithUpdatedParseOptions(changedOptions), changed);
        var fresh = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, project.File("Page.akbura", source)).WithUpdatedParseOptions(changedOptions), changed);
        AssertProbeOutputEqual(fresh, updated);
    }

    private static PortableExecutableReference CreateProbeReference(string source)
    {
        var compilation = CSharpCompilation.Create("ProbeSupport", [CSharpSyntaxTree.ParseText(source)], SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);
        Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
        return MetadataReference.CreateFromImage(stream.ToArray());
    }

    private static void AssertProbeOutputEqual(
        (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) expected,
        (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) actual)
    {
        Assert.Equal(expected.Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => (source.HintName, source.SourceText.ToString())),
            actual.Driver.GetRunResult().Results.Single().GeneratedSources.Select(source => (source.HintName, source.SourceText.ToString())));
        Assert.Equal(GetDiagnostics(expected.Diagnostics), GetDiagnostics(actual.Diagnostics));
        Assert.Equal(GetDiagnostics(expected.Output.GetDiagnostics()), GetDiagnostics(actual.Output.GetDiagnostics()));
    }
}
