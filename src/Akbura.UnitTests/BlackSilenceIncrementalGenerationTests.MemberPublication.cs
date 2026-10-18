using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    [Fact]
    public void MemberContracts_ConcurrentReferencedBodiesAndMarkupRemainIndependentAndCollectible()
    {
        var result = RunMemberBranches();
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunMemberBranches()
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var tasks = Enumerable.Range(1, 4).Select(index => Task.Run(() =>
        {
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return _name + \"" + index + "\";"), project.Options.ParseOptions, tree.FilePath));
            var changed = file.Replace("GetName()", "GetName() + \"" + index + "\"");
            var current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(file, changed), compilation);
            AssertArtifactsEqualToFresh(project, current, compilation, changed);
            var weak = new List<(string Kind, WeakReference Reference)>();
            CaptureArtifactWeakObjects(current, weak);
            return weak;
        })).ToArray();
        Task.WaitAll(tasks);
        var all = tasks.SelectMany(static task => task.Result).ToList();
        CaptureArtifactWeakObjects(initial, all);
        return new ArtifactLifetimeResult(initial.Request.State.Artifacts, all);
    }

#if STATS
    [Theory]
    [InlineData("DeclarationContractConstruction")]
    [InlineData("DeclarationContractValidation")]
    [InlineData("ArtifactSelection")]
    [InlineData("SemanticBinding")]
    [InlineData("ArtifactCapture")]
    [InlineData("ArtifactPublicationPreparation")]
    public void MemberContracts_CancellationAtEveryBoundaryNeverPublishesOrRetainsContexts(string phase)
    {
        var result = CancelMemberBranches(phase);
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult CancelMemberBranches(string phase)
    {
        var project = new TestProject(publishDiagnostics: true);
        var file = project.File("Page.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString()} />"));
        var tree = CSharpSyntaxTree.ParseText(MemberViewModel, project.Options.ParseOptions, "ViewModel.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, file), baseline);
        var owner = initial.Request.State.Artifacts;
        var published = owner.Latest;
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(initial, weak);
        var stage = Enum.Parse<GenerationStatisticStage>(phase);
        for (var index = 0; index < 3; index++)
        {
            using var cancellation = new CancellationTokenSource();
            var source = MemberViewModel.Replace("return _name;", "return \"Alex\";");
            if (phase == "SemanticBinding")
            {
                source = source.Replace("string GetName()", "object GetName()");
            }
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            var state = new BlackSilenceProjectState(compilation, owner);
            weak.Add(("CanceledCSharpCompilation", new WeakReference(compilation)));
            weak.Add(("CanceledState", new WeakReference(state)));
            BlackSilenceGenerationRequest? request = null;
            using (var measurement = GenerationStatistics.BeginMeasurement(() =>
            {
                if (GenerationStatistics.Current!.GetSnapshot().GetStageInvocationCount(stage) > 0)
                {
                    cancellation.Cancel();
                }
                return GC.GetAllocatedBytesForCurrentThread();
            }))
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
                weak.Add(("CanceledIndex", new WeakReference(request.Index)));
            }
        }
        var retryCompilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(MemberViewModel.Replace("return _name;", "return \"Retry\";"), project.Options.ParseOptions, tree.FilePath));
        var retry = ArtifactRun(project, initial.Driver, retryCompilation);
        AssertArtifactsEqualToFresh(project, retry, retryCompilation, file);
        return new ArtifactLifetimeResult(owner, weak);
    }
#endif
}
