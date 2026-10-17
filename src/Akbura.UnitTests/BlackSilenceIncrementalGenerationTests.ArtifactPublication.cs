using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    [Fact]
    public void DetachedArtifacts_SchemaMismatchRequiresRebinding()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var owner = initial.Request.State.Artifacts;
        owner.Publish(owner.Latest! with { Version = owner.GetNextVersion(), Schema = DetachedProjectArtifacts.CurrentSchema + 1 },
            initial.Request.State, initial.Snapshot, CancellationToken.None);
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 2"), project.Options.ParseOptions, tree.FilePath));
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Equal("ArtifactSchemaChanged", current.Request.ArtifactValidation.Reason);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);
    }

    [Fact]
    public void DetachedArtifacts_OrdinaryMarkupEditSharesOnlyCurrentDetachedCSharpFacts()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), project.Compilation);
        var changed = file.Replace("10", "20");
        var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(file, changed), project.Compilation);
        Assert.Same(initial.Request.ArtifactInputs, current.Request.ArtifactInputs);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, project.Compilation, changed);
    }
    [Fact]
    public void DetachedArtifacts_ReplacingAssemblyIdentityForcesCurrentCompilationBinding()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Dependency.Api.Read().ToString()} />"));
        var first = Library("DependencyOne");
        var second = Library("DependencyTwo");
        var baseline = project.Compilation.AddReferences(first);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var compilation = baseline.ReplaceReference(first, second);
        var current = ArtifactRun(project, initial.Driver, compilation);
        Assert.Null(current.Request.Components.Single().Previous);
        AssertArtifactsEqualToFresh(project, current, compilation, file);

        PortableExecutableReference Library(string name)
        {
            var library = CSharpCompilation.Create(name,
                [CSharpSyntaxTree.ParseText("namespace Dependency; public static class Api { public static int Read() => 1; }", project.Options.ParseOptions)],
                project.Compilation.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var stream = new MemoryStream();
            Assert.True(library.Emit(stream).Success);
            return MetadataReference.CreateFromImage(stream.ToArray());
        }
    }

    [Fact]
    public void DetachedArtifacts_IndependentDriversSharingGeneratorInstanceHaveSeparateStores()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var generator = new AkburaBlackSilenceGenerator().AsSourceGenerator();
        var left = ArtifactRun(project, Driver(), project.Compilation);
        var right = ArtifactRun(project, Driver(), project.Compilation);
        Assert.NotSame(left.Request.State.Artifacts, right.Request.State.Artifacts);
        Assert.NotSame(left.Request.State.Artifacts.Latest, right.Request.State.Artifacts.Latest);

        GeneratorDriver Driver() => CSharpGeneratorDriver.Create([generator], [file], project.Options.ParseOptions,
            project.Options, driverOptions: new GeneratorDriverOptions(default, true));
    }

    [Fact]
    public void DetachedArtifacts_OlderCompletionCannotReplaceNewerArtifacts()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var owner = initial.Request.State.Artifacts;
        var older = Request(2);
        var newer = Request(3);
        var newerBatch = BlackSilenceDocumentBatch.GenerateSafely(newer, CancellationToken.None);
        var published = owner.Latest;
        Assert.Equal(newer.Version, published!.Version);
        var olderBatch = BlackSilenceDocumentBatch.GenerateSafely(older, CancellationToken.None);
        Assert.Same(published, owner.Latest);
        Assert.Equal(newerBatch.Components.Single().SourceText.ToString(), olderBatch.Components.Single().SourceText.ToString());
        Assert.True(owner.Read()!.Identities.Length <= published.CSharpInputs.References.Length + published.CSharpInputs.Trees.Length * 2 + 1);
        AssertDetachedGraph(published);

        BlackSilenceGenerationRequest Request(int value)
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> " + value), project.Options.ParseOptions, tree.FilePath));
            return BlackSilenceGenerationRequestBuilder.Create(initial.Request.Documents,
                new BlackSilenceProjectState(compilation, owner), initial.Request.Options, CancellationToken.None)!;
        }
    }

    [Fact]
    public void DetachedArtifacts_ConcurrentBranchesRemainCorrectAndCollectible()
    {
        var result = RunConcurrentArtifactBranches();
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner.Latest!);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunConcurrentArtifactBranches()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<Border Width=\"10\" />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var tasks = Enumerable.Range(2, 4).Select(value => Task.Run(() =>
        {
            var changed = project.File("Page.akbura", Component("<Border Width=\"" + value + "\" />"));
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> " + value), project.Options.ParseOptions, tree.FilePath));
            using var measurement = GenerationStatistics.BeginMeasurement(trackInvalidation: true);
            var branch = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(file, changed), compilation);
            AssertArtifactsEqualToFresh(project, branch, compilation, changed);
            var weak = new List<(string Kind, WeakReference Reference)>();
            CaptureArtifactWeakObjects(branch, weak);
            return weak;
        })).ToArray();
        Task.WaitAll(tasks);
        var all = tasks.SelectMany(static task => task.Result).ToList();
        CaptureArtifactWeakObjects(initial, all);
        return new ArtifactLifetimeResult(initial.Request.State.Artifacts, all);
    }

#if STATS
    [Theory]
    [InlineData("lookup")]
    [InlineData("validation")]
    [InlineData("preparation")]
    public void DetachedArtifacts_RepeatedCancellationDoesNotPublishOrRetainTemporaryContexts(string phase)
    {
        var result = RunCanceledArtifactBranches(phase);
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner.Latest!);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunCanceledArtifactBranches(string phase)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("state int value = 1; <Border Width={value + 1} />"));
        var tree = CSharpSyntaxTree.ParseText(ArtifactLeaf, project.Options.ParseOptions, "Leaf.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var owner = initial.Request.State.Artifacts;
        var published = owner.Latest;
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(initial, weak);
        for (var index = 0; index < 5; index++)
        {
            using var cancellation = new CancellationTokenSource();
            var source = phase == "preparation" ? "internal static class IsolatedLeaf { public static int Read(int value) => value; }" : ArtifactLeaf.Replace("=> 1", "=> " + (index + 2));
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            var state = new BlackSilenceProjectState(compilation, owner);
            weak.Add(("CanceledCSharpCompilation", new WeakReference(compilation)));
            weak.Add(("CanceledProjectState", new WeakReference(state)));
            BlackSilenceGenerationRequest? request = null;
            using (var measurement = GenerationStatistics.BeginMeasurement(() =>
            {
                var observed = GenerationStatistics.Current!.GetSnapshot();
                var shouldCancel = phase switch
                {
                    "lookup" => observed.InvalidationEvents.Any(static item => item.Kind == "DetachedArtifactValidation"),
                    "validation" => observed.GetStageInvocationCount(GenerationStatisticStage.ArtifactValidation) > 0,
                    _ => observed.GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticQuery) > 0,
                };
                if (shouldCancel)
                {
                    cancellation.Cancel();
                }
                return GC.GetAllocatedBytesForCurrentThread();
            }, trackInvalidation: true))
            {
                Assert.ThrowsAny<OperationCanceledException>(() =>
                {
                    request = BlackSilenceGenerationRequestBuilder.Create(initial.Request.Documents, state, initial.Request.Options, cancellation.Token);
                    BlackSilenceDocumentBatch.GenerateSafely(request!, cancellation.Token);
                });
                Assert.True(cancellation.IsCancellationRequested);
            }
            Assert.Same(published, owner.Latest);
            Assert.Null(state.TryGetSnapshot(initial.Request.Options));
            if (request != null)
            {
                weak.Add(("CanceledAkburaCompilation", new WeakReference(request.Index.Compilation)));
                weak.Add(("CanceledProjectIndex", new WeakReference(request.Index)));
            }
        }
        var retryCompilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(ArtifactLeaf.Replace("=> 1", "=> 42"), project.Options.ParseOptions, tree.FilePath));
        var retry = ArtifactRun(project, initial.Driver, retryCompilation);
        AssertArtifactsEqualToFresh(project, retry, retryCompilation, file);
        return new ArtifactLifetimeResult(owner, weak);
    }
#endif
}
