#if STATS
using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    [Fact]
    public void InvalidationTrace_UnchangedCompilationSkipsCallbacks()
    {
        var expectedRoslyn = Environment.GetEnvironmentVariable("AKBURA_TEST_EXPECTED_ROSLYN_VERSION");
        if (expectedRoslyn != null)
        {
            Assert.Equal(expectedRoslyn, typeof(CSharpCompilation).Assembly.GetName().Version!.ToString());
        }
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var initial = Run(project, CreateDriver(project.Options, file));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver);
        var statistics = measurement.GetSnapshot();
        Assert.DoesNotContain(statistics.InvalidationEvents, item => item.Kind is "CSharpEnvironment" or "ProjectState" or "GenerationDecision" or "DiagnosticDecision");
        Assert.Equal(0, statistics.ComponentGeneratedCount);
        Assert.All(updated.Driver.GetRunResult().Results.Single().TrackedSteps["BlackSilence.CSharpEnvironment"],
            step => Assert.All(step.Outputs, output => Assert.Equal(IncrementalStepRunReason.Cached, output.Reason)));
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file)), updated);
    }

    [Fact]
    public void InvalidationTrace_SourceReadPrecedesCanonicalDocumentComparison()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var initial = Run(project, CreateDriver(project.Options, file));
        var changed = project.File("Page.akbura", file.Text + "\r\n   ");
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(file, changed));
        var events = measurement.GetSnapshot().InvalidationEvents;
        Assert.Contains(events, item => item.Kind == "SourceRead" && item.Data["Source"] == BlackSilenceInvalidationTrace.Fingerprint(changed.Text.ToString()));
        Assert.Contains(events, item => item.Kind == "DocumentVersion" && item.Decision == "ConstructedBeforeComparison");
        Assert.DoesNotContain(events, item => item.Kind == "GenerationDecision" && item.Decision == "Generate");
        Assert.Equal(0, measurement.GetSnapshot().ComponentGeneratedCount);
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, changed)), updated);
    }

    [Fact]
    public void InvalidationTrace_UnrelatedMethodEditReportsTheActualReuseBoundary()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText("internal static class Unrelated { public static int Read() => 1; }", project.Options.ParseOptions, "Unrelated.cs");
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), compilation);
        var changed = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(tree.GetText().ToString().Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver, changed);
        var trace = measurement.GetSnapshot().InvalidationEvents;
        Assert.Contains(trace, observation => observation.Kind == "CSharpEnvironmentComparison" && observation.Reasons.Contains("TreeTextChanged"));
        var context = trace.Single(observation => observation.Kind == "ProjectContext");
        var decision = trace.Single(observation => observation.Kind == "GenerationDecision");
        if (context.Decision == "NoPreviousSnapshot")
        {
            Assert.Equal("False", decision.Data["EntryAvailable"]);
            Assert.Contains("PreviousSnapshotUnavailable", decision.Reasons);
        }
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), changed), updated);
    }

    [Fact]
    public void InvalidationTrace_ReferencedSignatureEditKeepsFreshParity()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("using Demo; <Border Width={Support.Read()} />"));
        var tree = CSharpSyntaxTree.ParseText("namespace Demo; public static class Support { public static int Read() => 1; }", project.Options.ParseOptions, "Support.cs");
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), compilation);
        var changed = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            "namespace Demo; public static class Support { public static string Read() => \"one\"; }", project.Options.ParseOptions, tree.FilePath));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver, changed);
        Assert.Contains(measurement.GetSnapshot().InvalidationEvents, observation => observation.Kind == "DiagnosticDecision" && observation.Decision == "Evaluate");
        Assert.Contains(measurement.GetSnapshot().InvalidationEvents, observation => observation.Kind == "CSharpTreeComparison" &&
            observation.Path == tree.FilePath && observation.Reasons.Contains("TreeTextChanged"));
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), changed), updated);
    }

    [Fact]
    public void InvalidationTrace_DiagnosticChangeIsNotHiddenByEqualGeneratedText()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("using Demo; <TextBlock Text={Support.Read()} />"));
        const string source = "namespace Demo; public static class Support { public static string Read() => \"one\"; }";
        var tree = CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, "Support.cs");
        var compilation = project.Compilation.AddSyntaxTrees(tree);
        var initial = RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), compilation);
        var changed = compilation.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(
            source.Replace("public static string", "[System.Obsolete(\"trace warning\")] public static string"), project.Options.ParseOptions, tree.FilePath));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var updated = RunProbeAllowingDiagnostics(project, initial.Driver, changed);
        var trace = measurement.GetSnapshot().InvalidationEvents;
        Assert.Equal(initial.Driver.GetRunResult().Results.Single().GeneratedSources.Select(item => item.SourceText.ToString()),
            updated.Driver.GetRunResult().Results.Single().GeneratedSources.Select(item => item.SourceText.ToString()));
        Assert.Contains(trace, observation => observation.Kind == "DiagnosticDecision" && observation.Decision == "Evaluate");
        Assert.Contains(trace, observation => observation.Kind == "OutputComparison" && observation.Decision == "Equal");
        Assert.NotEqual(GetDiagnostics(initial.Output.GetDiagnostics()), GetDiagnostics(updated.Output.GetDiagnostics()));
        AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, file), changed), updated);
    }

    [Fact]
    public void InvalidationTrace_RecordsMultipleEnvironmentChanges()
    {
        var project = new TestProject();
        var compilation = project.Compilation;
        var reference = compilation.References.First();
        var changed = compilation.WithOptions(compilation.Options.WithNullableContextOptions(NullableContextOptions.Disable))
            .ReplaceReference(reference, MetadataReference.CreateFromFile(reference.Display!));
        var before = CSharpEnvironmentSnapshot.Create(compilation, []);
        var after = CSharpEnvironmentSnapshot.Create(changed, []);
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        Assert.False(CSharpEnvironmentSnapshotComparer.Instance.Equals(before, after));
        var reasons = measurement.GetSnapshot().InvalidationEvents.Single(item => item.Kind == "CSharpEnvironmentComparison").Reasons;
        Assert.Contains("CompilationOptionsChanged", reasons);
        Assert.Contains("ReferenceIdentityChanged", reasons);
    }

    [Fact]
    public void InvalidationTrace_CancellationDoesNotPublishPartialSnapshotAndCanRetry()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("state int value = 1; <Border Width={value + 1} />"));
        var initial = Run(project, CreateDriver(project.Options, file));
        var changed = file.Replace("value = 1", "value = 2");
        var oldTree = Assert.IsType<ComponentSyntaxTree>(initial.Request.Documents.Single().SyntaxTree);
        var request = BlackSilenceGenerationRequestBuilder.Create(
            [DocumentSyntaxVersion.Create(oldTree.WithChangedText(changed.Text))], initial.Request.State, initial.Request.Options, CancellationToken.None)!;
        using var cancellation = new CancellationTokenSource();
        using (var measurement = GenerationStatistics.BeginMeasurement(() =>
        {
            if (GenerationStatistics.Current!.GetSnapshot().GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticQuery) > 0)
            {
                cancellation.Cancel();
            }
            return GC.GetAllocatedBytesForCurrentThread();
        }, trackInvalidation: true))
        {
            Assert.ThrowsAny<OperationCanceledException>(() => BlackSilenceDocumentBatch.GenerateSafely(request, cancellation.Token));
            Assert.DoesNotContain(measurement.GetSnapshot().InvalidationEvents, item => item.Kind == "SnapshotPublication");
            Assert.Contains(measurement.GetSnapshot().InvalidationEvents, item => item.Decision == "Canceled");
        }
        Assert.Null(GenerationStatistics.Current);
        Assert.Same(initial.Snapshot, initial.Request.State.TryGetSnapshot(initial.Request.Options));
        Assert.Single(BlackSilenceDocumentBatch.GenerateSafely(request, CancellationToken.None).Components);
        AssertParityWithFresh(project, Run(project, initial.Driver.ReplaceAdditionalText(file, changed)), project.Options, changed);
    }

    [Fact]
    public async Task InvalidationTrace_ConcurrentDriverBranchesHaveIndependentSessions()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var initial = Run(project, CreateDriver(project.Options, file));
        var results = await Task.WhenAll(Branch(20), Branch(30));
        Assert.NotEqual(results[0].Single(item => item.Kind == "ProjectContext").Data["CSharpCompilation"],
            results[1].Single(item => item.Kind == "ProjectContext").Data["CSharpCompilation"]);
        Assert.Null(GenerationStatistics.Current);

        Task<ImmutableArray<GenerationInvalidationEvent>> Branch(int value) => Task.Run(() =>
        {
            var changed = file.Replace("10", value.ToString());
            var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal static class Other { public const int Value = " + value + "; }", project.Options.ParseOptions));
            using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
            var updated = RunProbeAllowingDiagnostics(project, initial.Driver.ReplaceAdditionalText(file, changed), compilation);
            var statistics = measurement.GetSnapshot();
            var events = statistics.InvalidationEvents;
            Assert.Single(events.Where(item => item.Kind == "ProjectContext"));
            Assert.Equal(events.Count(item => item.Kind == "GenerationDecision" && item.Decision == "Generate"), statistics.ComponentGeneratedCount);
            Assert.Equal(events.Count(item => item.Kind == "DiagnosticDecision" && item.Decision == "Evaluate"), statistics.DiagnosticDocumentEvaluatedCount);
            using (var inner = GenerationStatistics.BeginMeasurement(trackInvalidation: true))
            {
                AssertProbeOutputEqual(RunProbeAllowingDiagnostics(project, CreateDriver(project.Options, changed), compilation), updated);
                Assert.NotEmpty(inner.GetSnapshot().InvalidationEvents);
            }
            Assert.Equal(events.Length, measurement.GetSnapshot().InvalidationEvents.Length);
            Assert.Equal(statistics.ComponentGeneratedCount, measurement.GetSnapshot().ComponentGeneratedCount);
            Assert.Equal(statistics.DiagnosticDocumentEvaluatedCount, measurement.GetSnapshot().DiagnosticDocumentEvaluatedCount);
            return events;
        });
    }

    [Fact]
    public void InvalidationTrace_DisabledGuardAllocatesNoDocumentEventsAndPreservesOutput()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var driver = CreateDriver(project.Options, file);
        (GeneratorDriver Driver, Compilation Output, ImmutableArray<Diagnostic> Diagnostics) enabled;
        using (var trace = GenerationStatistics.BeginMeasurement(trackInvalidation: true))
        {
            enabled = RunProbeAllowingDiagnostics(project, driver);
            Assert.NotEmpty(trace.GetSnapshot().InvalidationEvents);
        }
        using var measurement = GenerationStatistics.BeginMeasurement();
        var disabled = RunProbeAllowingDiagnostics(project, driver);
        Assert.Empty(measurement.GetSnapshot().InvalidationEvents);
        var start = GC.GetAllocatedBytesForCurrentThread();
        for (var index = 0; index < 1000; index++)
        {
            if (GenerationStatistics.IsInvalidationTrackingEnabled)
            {
                BlackSilenceInvalidationTrace.Record("Unused", file.Path, "Unused", []);
            }
        }
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - start);
        AssertProbeOutputEqual(enabled, disabled);
    }

    [Fact]
    public void InvalidationTrace_DocumentFilterRetainsProjectEvents()
    {
        var project = new TestProject();
        var first = project.File("First.akbura", Component("<Border />"));
        var second = project.File("Second.akbura", Component("<Border />"));
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true, documentFilter: "First");
        Run(project, CreateDriver(project.Options, first, second));
        var events = measurement.GetSnapshot().InvalidationEvents;
        Assert.Contains(events, item => item.Path.Length == 0);
        Assert.Contains(events, item => item.Path == first.Path);
        Assert.DoesNotContain(events, item => item.Path == second.Path);
    }

    [Fact]
    public void InvalidationTrace_DoesNotKeepCompilerGraphsAlive()
    {
        var (weakCompilation, snapshot) = CaptureWeakCompilation();
        for (var index = 0; index < 3 && weakCompilation.IsAlive; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Assert.False(weakCompilation.IsAlive);
        Assert.NotEmpty(snapshot.InvalidationEvents);
        GC.KeepAlive(snapshot);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Compilation, GenerationStatisticsSnapshot Snapshot) CaptureWeakCompilation()
    {
        using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
        var compilation = CSharpCompilation.Create("TraceLifetime", [CSharpSyntaxTree.ParseText("internal class Unused { }")]);
        var state = new BlackSilenceProjectState(compilation);
        BlackSilenceInvalidationTrace.Record("Lifetime", string.Empty, "Observed", [],
            ("Compilation", BlackSilenceInvalidationTrace.Id(compilation)), ("State", BlackSilenceInvalidationTrace.Id(state)));
        return (new WeakReference(compilation), measurement.GetSnapshot());
    }
}
#endif
