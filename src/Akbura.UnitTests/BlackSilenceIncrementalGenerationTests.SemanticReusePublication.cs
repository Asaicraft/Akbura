using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
#if AKBURA_SEMANTIC_REUSE
    [Fact]
    public async Task SemanticReusePublication_ConcurrentBranchesPublishOnlyTheNewestCompleteGeneration()
    {
        var fixture = CreateSemanticPublicationFixture();
        var left = SemanticPublicationRequest(fixture.State, fixture.Options, "20");
        var right = SemanticPublicationRequest(fixture.State, fixture.Options, "30");
        var batches = await Task.WhenAll(Task.Run(() => BlackSilenceDocumentBatch.Generate(left, default)),
            Task.Run(() => BlackSilenceDocumentBatch.Generate(right, default)));
        Assert.Equal(right.Version, fixture.State.TryGetSnapshot(fixture.Options)!.Version);
        Assert.Equal(right.Version, fixture.State.ProbeStore!.Latest!.Version);
        for (var index = 0; index < batches.Length; index++)
        {
            var freshState = new BlackSilenceProjectState(fixture.State.CSharpCompilation);
            var fresh = BlackSilenceDocumentBatch.Generate(SemanticPublicationRequest(freshState, fixture.Options, index == 0 ? "20" : "30"), default);
            Assert.Equal(fresh.Components.Select(source => source.SourceText.ToString()), batches[index].Components.Select(source => source.SourceText.ToString()));
        }
        BlackSilenceDocumentBatch.Generate(left, default);
        Assert.Equal(right.Version, fixture.State.ProbeStore.Latest.Version);
    }

#if STATS
    [Theory]
    [InlineData("validation")]
    [InlineData("binding")]
    [InlineData("publication")]
    public void SemanticReusePublication_CancellationCannotPublishPrivateSemanticEntries(string point)
    {
        var fixture = CreateSemanticPublicationFixture();
        var snapshot = fixture.State.TryGetSnapshot(fixture.Options);
        var generation = fixture.State.ProbeStore!.Latest;
        using var cancellation = new CancellationTokenSource();
        var request = SemanticPublicationRequest(fixture.State, fixture.Options, "20", cancellation.Token,
            point == "binding" ? SemanticReuseBody.Replace("FirstName", "LastName") : null);
        long? candidateProbeCount = null;
        using (GenerationStatistics.BeginMeasurement(() =>
        {
            var stats = GenerationStatistics.Current!.GetSnapshot();
            var candidate = stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionBindingCandidate) > 0;
            var probes = stats.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated);
            if (candidate && candidateProbeCount == null)
            {
                candidateProbeCount = probes;
            }
            if (point == "validation" && candidate || point == "binding" && candidateProbeCount != null && probes > candidateProbeCount ||
                point == "publication" && stats.GetStageInvocationCount(GenerationStatisticStage.ArtifactCapture) > 0)
            {
                cancellation.Cancel();
            }
            return GC.GetAllocatedBytesForCurrentThread();
        }))
        {
            Assert.ThrowsAny<OperationCanceledException>(() => BlackSilenceDocumentBatch.Generate(request, cancellation.Token));
        }
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Same(snapshot, fixture.State.TryGetSnapshot(fixture.Options));
        Assert.Same(generation, fixture.State.ProbeStore.Latest);
        var recovered = BlackSilenceDocumentBatch.Generate(SemanticPublicationRequest(fixture.State, fixture.Options, "20"), default);
        var freshState = new BlackSilenceProjectState(fixture.State.CSharpCompilation);
        var fresh = BlackSilenceDocumentBatch.Generate(SemanticPublicationRequest(freshState, fixture.Options, "20"), default);
        Assert.Equal(fresh.Components.Select(source => source.SourceText.ToString()), recovered.Components.Select(source => source.SourceText.ToString()));
    }
#endif

    private static (BlackSilenceProjectState State, GeneratorProjectOptions Options) CreateSemanticPublicationFixture()
    {
        var project = new TestProject(publishDiagnostics: true);
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions));
        var state = new BlackSilenceProjectState(compilation);
        var options = new GeneratorProjectOptions("Demo", project.Directory);
        BlackSilenceDocumentBatch.Generate(SemanticPublicationRequest(state, options, "10"), default);
        return (state, options);
    }

    private static BlackSilenceGenerationRequest SemanticPublicationRequest(BlackSilenceProjectState state, GeneratorProjectOptions options,
        string margin, CancellationToken cancellationToken = default, string? body = null)
    {
        var tree = ComponentSyntaxTree.ParseText(Component((body ?? SemanticReuseBody).Replace("10", margin)), Path.Combine(options.ProjectDirectory, "Page.akbura"));
        return BlackSilenceGenerationRequestBuilder.Create([DocumentSyntaxVersion.Create(tree)], state, options, cancellationToken)!;
    }
#endif
}
