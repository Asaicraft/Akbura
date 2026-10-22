using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
#if STATS && AKBURA_SEMANTIC_REUSE
    [Fact]
    public void WatchObservation_ReportsActualSkippedWorkAndRestoresTheCallerMeasurement()
    {
        var (state, options) = CreateSemanticPublicationFixture();
        var path = Path.Combine(options.ProjectDirectory, "watch-observations", Guid.NewGuid().ToString("N"));
        var observer = new BlackSilenceWatchObservation(path);
        using var outer = GenerationStatistics.BeginMeasurement();
        foreach (var margin in new[] { "11", "12" })
        {
            var request = ObservedSemanticRequest(observer, state, options, margin);
            observer.Generate(request, default);
            Assert.Same(outer, GenerationStatistics.Current);
        }
        Assert.Equal(0, outer.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
        var records = ReadWatchRecords(path);
        var generations = records.Where(record => record["Kind"] == "Generation").ToArray();
        Assert.Equal(2, generations.Length);
        Assert.All(generations, record =>
        {
            Assert.Equal("2", record["counter.CrossRevisionFirstBindingSkipped"]);
            Assert.Equal(record["counter.CrossRevisionBindingHit"], record["counter.CrossRevisionSemanticContributionImported"]);
            Assert.Equal(record["Version"], record["PublishedGenerationVersion"]);
        });
        Assert.Equal("True", generations.Single(record => record["Version"] == generations.Select(item => long.Parse(item["Version"])).Max().ToString())
            ["SameProjectStateReference"]);
        Assert.Single(generations.Select(record => record["Compilation"]).Distinct());
        Assert.Single(generations.Select(record => record["ProbeStore"]).Distinct());
    }

    [Fact]
    public void WatchObservation_DiagnosticGenerationCannotPublishSemanticEntriesAndRepairIsObserved()
    {
        var (state, options) = CreateSemanticPublicationFixture();
        var path = Path.Combine(options.ProjectDirectory, "watch-observations", Guid.NewGuid().ToString("N"));
        var observer = new BlackSilenceWatchObservation(path);
        var validVersion = state.ProbeStore!.Latest!.Version;
        var invalid = ObservedSemanticRequest(observer, state, options, "11", SemanticReuseBody.Replace("FirstName", "Missing"));
        observer.Generate(invalid, default);
        Assert.Equal(validVersion, state.ProbeStore.Latest!.Version);
        var repaired = ObservedSemanticRequest(observer, state, options, "12");
        observer.Generate(repaired, default);
        Assert.Equal(repaired.Version, state.ProbeStore.Latest!.Version);
        var generations = ReadWatchRecords(path).Where(record => record["Kind"] == "Generation").ToArray();
        Assert.NotEqual("0", generations.Single(record => record["Version"] == invalid.Version.ToString())["ErrorDiagnostics"]);
        Assert.Equal(validVersion.ToString(), generations.Single(record => record["Version"] == invalid.Version.ToString())["PublishedGenerationVersion"]);
        Assert.Equal("0", generations.Single(record => record["Version"] == repaired.Version.ToString())["ErrorDiagnostics"]);
    }

    [Fact]
    public void WatchObservation_LiveRecorderDoesNotRetainCompilationStateOrItsProbeStore()
    {
        var (observer, weak) = CaptureWatchObservationOwnership();
        AssertCollected(weak);
        GC.KeepAlive(observer);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BlackSilenceWatchObservation Observer, List<(string Kind, WeakReference Reference)> Weak) CaptureWatchObservationOwnership()
    {
        var (state, options) = CreateSemanticPublicationFixture();
        var observer = new BlackSilenceWatchObservation(Path.Combine(options.ProjectDirectory, "watch-observations", Guid.NewGuid().ToString("N")));
        var request = ObservedSemanticRequest(observer, state, options, "11");
        observer.ObserveEnvironment(CSharpEnvironmentSnapshot.Create(state.CSharpCompilation, request.Documents, options), request.Documents);
        observer.Generate(request, default);
        return (observer, [("State", new WeakReference(state)), ("Compilation", new WeakReference(state.CSharpCompilation)),
            ("ProbeStore", new WeakReference(state.ProbeStore))]);
    }

    private static BlackSilenceGenerationRequest ObservedSemanticRequest(BlackSilenceWatchObservation observer, BlackSilenceProjectState state,
        GeneratorProjectOptions options, string margin, string? body = null)
    {
        var tree = ComponentSyntaxTree.ParseText(Component((body ?? SemanticReuseBody).Replace("10", margin)), Path.Combine(options.ProjectDirectory, "Page.akbura"));
        return observer.CreateRequest([DocumentSyntaxVersion.Create(tree)], state, options, default, computeDiagnostics: true)!;
    }

    private static Dictionary<string, string>[] ReadWatchRecords(string path) => Directory.GetFiles(path, "*.json")
        .Select(file => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!).ToArray();
#endif
}
