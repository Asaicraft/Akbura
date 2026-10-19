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
    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void SemanticDependencies_MixedRevisionsKeepOnlyCurrentFactsAndReleaseEveryCompilerOwner(int revisions)
    {
        var result = RunSemanticDependencyLifetime(revisions);
        AssertCollected(result.WeakObjects);
        Assert.Equal(3, result.Owner.Latest!.Entries.Count);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunSemanticDependencyLifetime(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[]
        {
            project.File("Profile.akbura", Component("<TextBlock Text={global::Demo.UserViewModel.Current.GetName().ToString()} />")),
            project.File("Orders.akbura", Component("<TextBlock Text={global::Demo.OrderService.Current.GetOrders()} />")),
            project.File("Settings.akbura", Component("<TextBlock Text={global::Demo.SettingsViewModel.Current.Theme} />")),
        };
        var tree = CSharpSyntaxTree.ParseText(SemanticDependencySources, project.Options.ParseOptions, "Models.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var current = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(current, weak);
        for (var revision = 1; revision <= revisions; revision++)
        {
            var source = (revision % 7) switch
            {
                0 => SemanticDependencySources,
                1 => SemanticDependencySources.Replace("string GetName() => \"John\"", "int GetName() => 42"),
                2 => SemanticDependencySources.Replace("public string Name", "public string Added" + revision + "() => \"added\"; public string Name"),
                3 => SemanticDependencySources.Replace("public string Name { get => \"John\"; }", ""),
                4 => SemanticDependencySources.Replace("=> \"John\"", "=> \"body" + revision + "\""),
                5 => "global using System.Text;\n" + SemanticDependencySources,
                _ => SemanticDependencySources.Replace("string GetName() => \"John\"", "object GetName() => \"John\""),
            };
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            var driver = current.Driver;
            if (revision % 11 == 0)
            {
                var replacement = project.File("Orders.akbura", Component("<TextBlock Text={global::Demo.OrderService.Current.GetOrders() + \"" + revision + "\"} />"));
                driver = driver.ReplaceAdditionalText(files[1], replacement);
                files[1] = replacement;
            }
            current = ArtifactRun(project, driver, compilation);
            AssertArtifactsEqualToFresh(project, current, compilation, files);
            CaptureArtifactWeakObjects(current, weak);
            var artifacts = current.Request.State.Artifacts.Latest!;
            Assert.Equal(3, artifacts.Entries.Count);
            foreach (var artifact in artifacts.Entries.Values)
            {
                var facts = Assert.IsType<DetachedCSharpDependencySet>(artifact.SemanticDependencies);
                Assert.InRange(facts.SourceTypes.Length, 0, 3);
                Assert.Equal(facts.Symbols.Length, facts.Symbols.Distinct(StringComparer.Ordinal).Count());
                Assert.Equal(facts.Lookups.Length, facts.Lookups.Select(static lookup => lookup.Key).Distinct(StringComparer.Ordinal).Count());
                Assert.DoesNotContain(facts.Symbols, symbol => symbol.Contains("Added", StringComparison.Ordinal) &&
                    !symbol.Contains("Added" + revision + "(", StringComparison.Ordinal));
            }
        }
        return new ArtifactLifetimeResult(current.Request.State.Artifacts, weak);
    }

    [Fact]
    public void SemanticDependencies_ConcurrentSignatureBranchesUseOwnFactsAndReleaseAbandonedOwners()
    {
        var result = RunSemanticDependencyBranches();
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult RunSemanticDependencyBranches()
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("<TextBlock Text={global::Demo.Api.Read().ToString()} />")),
            project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var branches = Enumerable.Range(1, 4).Select(index => Task.Run(() =>
        {
            var source = DependencyApi.Replace("string Read() => \"one\"", index % 2 == 0 ? "int Read() => 1" : "object Read() => \"changed\"");
            var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(source, project.Options.ParseOptions, tree.FilePath));
            var current = ArtifactRun(project, initial.Driver, compilation);
            Assert.Null(current.Request.Components.Single(component => component.Identity.EndsWith("Page.akbura")).Previous);
            Assert.NotNull(current.Request.Components.Single(component => component.Identity.EndsWith("Other.akbura")).Previous);
            AssertArtifactsEqualToFresh(project, current, compilation, files);
            var weak = new List<(string Kind, WeakReference Reference)>();
            CaptureArtifactWeakObjects(current, weak);
            return weak;
        })).ToArray();
        Task.WaitAll(branches);
        var all = branches.SelectMany(static branch => branch.Result).ToList();
        CaptureArtifactWeakObjects(initial, all);
        return new ArtifactLifetimeResult(initial.Request.State.Artifacts, all);
    }

#if STATS
    [Theory]
    [InlineData("CSharpChangeSetConstruction")]
    [InlineData("SemanticDependencyCapture")]
    [InlineData("DependencySetDetachment")]
    [InlineData("DocumentDependencyValidation")]
    [InlineData("NegativeLookupValidation")]
    [InlineData("CandidateSetValidation")]
    [InlineData("DependencyGraphPropagation")]
    [InlineData("SelectiveArtifactReuse")]
    [InlineData("SelectiveDiagnosticReuse")]
    [InlineData("SemanticBinding")]
    [InlineData("DiagnosticSemantic")]
    [InlineData("ArtifactCapture")]
    [InlineData("ArtifactPublicationPreparation")]
    public void SemanticDependencies_CanceledStageNeverPublishesPartialFacts(string phase)
    {
        var result = CancelSemanticDependencyBranch(phase);
        AssertCollected(result.WeakObjects);
        AssertDetachedGraph(result.Owner);
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactLifetimeResult CancelSemanticDependencyBranch(string phase)
    {
        var project = new TestProject(publishDiagnostics: true);
        var files = new[] { project.File("Page.akbura", Component("<TextBlock Text={global::Demo.Api.Read().ToString()} />")),
            project.File("Other.akbura", Component("<Border />")) };
        var tree = CSharpSyntaxTree.ParseText(DependencyApi, project.Options.ParseOptions, "Api.cs");
        var baseline = project.Compilation.AddSyntaxTrees(tree);
        var initial = ArtifactRun(project, CreateDriver(project.Options, files), baseline);
        var owner = initial.Request.State.Artifacts;
        var publication = owner.Latest;
        var weak = new List<(string Kind, WeakReference Reference)>();
        CaptureArtifactWeakObjects(initial, weak);
        using var cancellation = new CancellationTokenSource();
        var compilation = baseline.ReplaceSyntaxTree(tree, CSharpSyntaxTree.ParseText(DependencyApi.Replace("string Read() => \"one\"", "int Read() => 1"), project.Options.ParseOptions, tree.FilePath));
        var state = new BlackSilenceProjectState(compilation, owner);
        weak.Add(("CanceledCompilation", new WeakReference(compilation)));
        weak.Add(("CanceledState", new WeakReference(state)));
        var stage = Enum.Parse<GenerationStatisticStage>(phase);
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
                var request = BlackSilenceGenerationRequestBuilder.Create(initial.Request.Documents, state, initial.Request.Options, cancellation.Token);
                BlackSilenceDocumentBatch.GenerateSafely(request!, cancellation.Token);
            });
            Assert.True(cancellation.IsCancellationRequested);
        }
        Assert.Same(publication, owner.Latest);
        Assert.Null(state.TryGetSnapshot(initial.Request.Options));
        return new ArtifactLifetimeResult(owner, weak);
    }
#endif
}
