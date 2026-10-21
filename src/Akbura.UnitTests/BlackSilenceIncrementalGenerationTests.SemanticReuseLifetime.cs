using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
#if AKBURA_SEMANTIC_REUSE
    [Fact]
    public void SemanticReuseLifetime_TenRevisionOwnershipSmoke()
    {
        var transition = SemanticReuseTransition(10);
        AssertCollected(transition.Old);
        GC.KeepAlive(transition.State);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void SemanticReuseLifetime_LiveStateKeepsOnlyBoundedCurrentRoslynGraphsAndTransitionReleasesThem(int revisions)
    {
        var transition = SemanticReuseTransition(revisions);
        AssertCollected(transition.Old);
        if (Environment.GetEnvironmentVariable("AKBURA_SEMANTIC_EVIDENCE") is { Length: > 0 } directory)
        {
            Directory.CreateDirectory(directory);
            var evidence = System.Text.Json.JsonSerializer.Serialize(new
            {
                Revisions = revisions, LiveOwner = transition.LiveOwnerEvidence,
                ObsoleteObjectsAliveAfterCompilationTransition = transition.Old.Count(item => item.Reference.IsAlive),
                ExternalGeneratorDriverRetained = false,
            });
            File.WriteAllText(Path.Combine(directory, $"semantic-retention-{revisions}.json"), evidence + "\r\n");
        }
        GC.KeepAlive(transition.State);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object AssertLiveSemanticOwner(SemanticLifetimeResult retained)
    {
        AssertCollected(retained.Markup);
        var generation = retained.State.ProbeStore!.Latest!;
        Assert.InRange(generation.SemanticEntryCount, 1, Akbura.Language.Binder.CompilationBoundProbeStore.MaxSemanticEntriesPerProject);
        var liveGraphs = generation.Documents.Values.SelectMany(document => document.Entries.Values)
            .Where(entry => entry.Binding != null).Select(entry => entry.Binding!.Result.OperationDefinition.Operation!).ToArray();
        var operations = liveGraphs.ToHashSet(ReferenceEqualityComparer.Instance);
        var models = liveGraphs.Select(operation => operation.SemanticModel!).ToHashSet(ReferenceEqualityComparer.Instance);
        var compilations = liveGraphs.Select(operation => operation.SemanticModel!.Compilation).ToHashSet(ReferenceEqualityComparer.Instance);
        var trees = liveGraphs.Select(operation => operation.Syntax.SyntaxTree).ToHashSet(ReferenceEqualityComparer.Instance);
        foreach (var graph in retained.Graphs)
        {
            if (graph.Reference.Target is { } target)
            {
                Assert.True(graph.Kind switch
                {
                    "Operation" => operations.Contains(target),
                    "SemanticModel" => models.Contains(target),
                    "ProbeCompilation" => compilations.Contains(target),
                    "ProbeSyntaxTree" => trees.Contains(target),
                    _ => false,
                }, "Superseded Roslyn graph: " + graph.Kind);
            }
        }
        Assert.True(models.Count <= generation.SemanticEntryCount);
        Assert.True(compilations.Count <= generation.SemanticEntryCount);
        Assert.True(trees.Count <= generation.SemanticEntryCount);
        AssertDetachedGraph(retained.State.Artifacts);
        GC.KeepAlive(retained.State);
        return new { Entries = generation.EntryCount, SemanticEntries = generation.SemanticEntryCount,
            OperationRoots = operations.Count, ProbeSemanticModels = models.Count, ProbeCompilations = compilations.Count, ProbeSyntaxTrees = trees.Count,
            PostGcHeapBytes = GC.GetTotalMemory(true), retained.AllocatedBytes, AllocationIncludesFreshGenerationOracle = true,
            ObsoleteMarkupOwnersAlive = retained.Markup.Count(item => item.Reference.IsAlive) };
    }

    private sealed record SemanticLifetimeResult(BlackSilenceProjectState State,
        List<(string Kind, WeakReference Reference)> Markup, List<(string Kind, WeakReference Reference)> Graphs, long AllocatedBytes);

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static SemanticLifetimeResult SemanticReuseLifetime(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true, debugStructural: true);
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions, "Person.cs"));
        var state = new BlackSilenceProjectState(compilation);
        var options = new GeneratorProjectOptions("Demo", project.Directory, ComponentGenerationMode.DebugStructural);
        var markup = new List<(string Kind, WeakReference Reference)>();
        var graphs = new List<(string Kind, WeakReference Reference)>();
        var beforeBytes = GC.GetTotalAllocatedBytes(true);
        for (var revision = 1; revision <= revisions; revision++)
        {
            var body = SemanticReuseBody.Replace("10", (10 + revision % 80).ToString());
            if (revisions >= 500)
            {
                body = "<StackPanel>" + body + string.Concat(Enumerable.Repeat("<Border><TextBlock Text=\"label\" /></Border>", 8)) +
                    "$foreach (var item in new[] { 1, 2 }) { <TextBlock Text={item.ToString()} /> }</StackPanel>";
            }
            body = (revision % 10) switch
            {
                3 => body.Replace("FirstName", "LastName"),
                5 => body.Replace("<StackPanel>", "<StackPanel><Border />"),
                6 => body.Replace("FirstName", "Missing"),
                8 => body.Replace("LastName}", "LastName + \"!\"}"),
                _ => body,
            };
            var tree = ComponentSyntaxTree.ParseText(Component(body), Path.Combine(project.Directory, "Page.akbura"));
            var documents = System.Collections.Immutable.ImmutableArray.Create(DocumentSyntaxVersion.Create(tree));
            var request = BlackSilenceGenerationRequestBuilder.Create(documents, state, options, default)!;
            var batch = BlackSilenceDocumentBatch.Generate(request, default);
            var freshState = new BlackSilenceProjectState(compilation);
            var fresh = BlackSilenceDocumentBatch.Generate(BlackSilenceGenerationRequestBuilder.Create(documents,
                freshState, options, default)!, default);
            Assert.Equal(fresh.Components.Select(source => (source.HintName, source.SourceText.ToString())), batch.Components.Select(source => (source.HintName, source.SourceText.ToString())));
            Assert.Equal(System.Text.Json.JsonSerializer.Serialize(fresh.Diagnostics.Select(diagnostic => diagnostic with { DocumentVersion = 0 })),
                System.Text.Json.JsonSerializer.Serialize(batch.Diagnostics.Select(diagnostic => diagnostic with { DocumentVersion = 0 })));
            if (!fresh.Components.IsEmpty)
            {
                Assert.Equal(System.Text.Json.JsonSerializer.Serialize(freshState.TryGetSnapshot(options)!.Entries.Values.Single().SemanticDependencies),
                    System.Text.Json.JsonSerializer.Serialize(state.TryGetSnapshot(options)!.Entries.Values.Single().SemanticDependencies));
            }
            if (revision < revisions)
            {
                var model = request.Index.Compilation.GetSemanticModel(tree);
                markup.Add(("MarkupTree", new WeakReference(tree)));
                markup.Add(("SemanticModel", new WeakReference(model)));
                markup.Add(("BindingSession", new WeakReference(model.BindingSession)));
                markup.Add(("ProbeContext", new WeakReference(model.BindingSession.ProbeContext)));
                markup.Add(("Snapshot", new WeakReference(state.TryGetSnapshot(options))));
            }
            foreach (var entry in state.ProbeStore!.Latest?.Documents.Values.SelectMany(document => document.Entries.Values) ?? [])
            {
                if (entry.Binding?.Result.OperationDefinition.Operation is { } operation)
                {
                    graphs.Add(("Operation", new WeakReference(operation)));
                    graphs.Add(("SemanticModel", new WeakReference(operation.SemanticModel)));
                    graphs.Add(("ProbeCompilation", new WeakReference(operation.SemanticModel!.Compilation)));
                    graphs.Add(("ProbeSyntaxTree", new WeakReference(operation.Syntax.SyntaxTree)));
                }
            }
        }
        GC.Collect();
        System.Diagnostics.Trace.WriteLine($"Semantic reuse revisions={revisions} allocatedBytes={GC.GetTotalAllocatedBytes(true) - beforeBytes} postGcHeapBytes={GC.GetTotalMemory(true)} semanticEntries={state.ProbeStore!.Latest!.SemanticEntryCount}");
        return new SemanticLifetimeResult(state, markup, graphs, GC.GetTotalAllocatedBytes(true) - beforeBytes);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BlackSilenceProjectState State, List<(string Kind, WeakReference Reference)> Old, object LiveOwnerEvidence) SemanticReuseTransition(int revisions)
    {
        var previous = SemanticReuseLifetime(revisions);
        var evidence = AssertLiveSemanticOwner(previous);
        var old = previous.Markup.Concat(previous.Graphs).ToList();
        old.Add(("ProjectState A", new WeakReference(previous.State)));
        old.Add(("ProbeStore A", new WeakReference(previous.State.ProbeStore)));
        old.Add(("CSharpCompilation A", new WeakReference(previous.State.CSharpCompilation)));
        var replacement = new TestProject(publishDiagnostics: true);
        var next = new BlackSilenceProjectState(replacement.Compilation, previous.State.Artifacts);
        return (next, old, evidence);
    }
#endif
}
