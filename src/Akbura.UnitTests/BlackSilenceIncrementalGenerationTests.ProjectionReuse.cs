using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    private const string ProjectionFixture = "<StackPanel><Border Margin=\"10\" /><TextBlock Text={1.ToString()} /><TextBlock Text={2.ToString()} /></StackPanel>";

    [Fact]
    public void ProjectionContract_RequiresExactContextAndStableSourcePositions()
    {
        var original = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        var contract = Assert.IsType<ProbeDocumentContract>(ProbeDocumentContract.Create(original));
        Assert.True(contract.Matches(ProbeDocumentContract.Create(original.WithChangedText(original.Text.ToString().Replace("10", "20")))!));
        foreach (var changed in new[]
        {
            original.Text.ToString().Replace("10", "200"),
            original.Text.ToString().Replace("10", "xx"),
            original.Text.ToString().Replace("<Border", " <Border"),
            original.Text.ToString().Replace("1.ToString()", "3.ToString()"),
            original.Text.ToString().Replace("Margin", "Width"),
            "using System; " + original.Text,
            original.Text.ToString().Replace("<Border Margin=\"10\" />", "<Border Margin=\"10\" x.Name=\"sibling\" />"),
        })
        {
            var next = ProbeDocumentContract.Create(original.WithChangedText(changed));
            Assert.True(next == null || !contract.Matches(next), changed);
        }
    }

    [Fact]
    public void ProjectionStore_RejectsCompilationTransitionsAndDeclarationChanges()
    {
        var project = new TestProject(publishDiagnostics: true);
        var owner = new CompilationBoundProbeStore(project.Compilation);
        var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        var first = PrepareProjection(owner, project.Compilation, tree, 1);
        owner.Publish(first.Session.Seal());
        Assert.Null(owner.Begin(project.Compilation.WithAssemblyName("Changed"), 2, [DocumentSyntaxVersion.Create(tree)], "Demo", "", default));
        foreach (var source in new[] { "state int count = 1; ", "param string Title; ", "using System; ", "namespace Other; " })
        {
            var next = PrepareProjection(owner, project.Compilation, ComponentSyntaxTree.ParseText(source + tree.Text, tree.FilePath), 2);
            Assert.False(next.Document.TryGetProjection(next.Scope, CSharpSyntaxFactory.ParseExpression("1.ToString()"), null, out _));
            next.Session.Discard();
        }
    }

    [Fact]
    public void ProjectionStore_KeysExpectedTypeSyntaxAndQueryRegionWithoutRetainingMarkupOwners()
    {
        var project = new TestProject(publishDiagnostics: true);
        var owner = new CompilationBoundProbeStore(project.Compilation);
        var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        var first = PrepareProjection(owner, project.Compilation, tree, 1);
        owner.Publish(first.Session.Seal());
        var next = PrepareProjection(owner, project.Compilation, tree.WithChangedText(tree.Text.ToString().Replace("10", "20")), 2, record: false);
        Assert.True(next.Document.TryGetProjection(next.Scope, CSharpSyntaxFactory.ParseExpression("1.ToString()"), null, out _));
        Assert.False(next.Document.TryGetProjection(next.Scope, CSharpSyntaxFactory.ParseExpression("1.ToString()"), project.Compilation.GetSpecialType(SpecialType.System_String), out _));
        Assert.False(next.Document.TryGetProjection(next.Scope, CSharpSyntaxFactory.ParseExpression("2.ToString()"), null, out _));
        Assert.False(next.Document.TryGetProjection(next.Scope, CSharpSyntaxFactory.ParseExpression("1.ToString()").WithAdditionalAnnotations(new SyntaxAnnotation("query")), null, out _));
        var other = tree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupElementSyntax>().Last();
        Assert.False(next.Document.TryGetProjection(other, CSharpSyntaxFactory.ParseExpression("1.ToString()"), null, out _));
        next.Session.Discard();
    }

    [Fact]
    public void ProjectionStore_PublicationIsLatestOnlyAndDeletedDocumentsAreDropped()
    {
        var project = new TestProject(publishDiagnostics: true);
        var owner = new CompilationBoundProbeStore(project.Compilation);
        var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        var initial = PrepareProjection(owner, project.Compilation, tree, 1).Session.Seal();
        var older = PrepareProjection(owner, project.Compilation, tree, 2).Session.Seal();
        var newer = PrepareProjection(owner, project.Compilation, tree, 3).Session.Seal();
        owner.Publish(initial);
        owner.Publish(newer);
        owner.Publish(older);
        Assert.Same(newer, owner.Latest);
        var deleted = owner.Begin(project.Compilation, 4, [], "Demo", "", default)!;
        owner.Publish(deleted.Seal());
        Assert.Empty(owner.Latest!.Documents);
        Assert.Equal(0, owner.Latest.EntryCount);
    }

    [Fact]
    public void ProjectionStore_CancellationAndDiscardNeverPublishPrivateCandidates()
    {
        var project = new TestProject(publishDiagnostics: true);
        var owner = new CompilationBoundProbeStore(project.Compilation);
        var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        using var cancellation = new CancellationTokenSource();
        var session = owner.Begin(project.Compilation, 1, [DocumentSyntaxVersion.Create(tree)], "Demo", "", cancellation.Token)!;
        session.GetDocument(tree);
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => session.Seal());
        Assert.Null(owner.Latest);
        session.Discard();
        Assert.Null(session.GetDocument(tree));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void ProjectionStore_LiveCurrentOwnerDoesNotRetainObsoleteMarkupOwners(int revisions)
    {
        var (owner, weak) = ProjectionRevisions(revisions);
        for (var attempt = 0; attempt < 10 && weak.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.All(weak, reference => Assert.False(reference.IsAlive));
        Assert.Equal(2, owner.Latest!.EntryCount);
        GC.KeepAlive(owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CompilationBoundProbeStore Owner, List<WeakReference> Weak) ProjectionRevisions(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true);
        var owner = new CompilationBoundProbeStore(project.Compilation);
        var weak = new List<WeakReference>();
        for (var index = 1; index <= revisions; index++)
        {
            var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture.Replace("10", (10 + index % 80).ToString())), "Page.akbura");
            var prepared = PrepareProjection(owner, project.Compilation, tree, index);
            var compilation = new AkburaCompilation(project.Compilation, [tree], ImmutableArray<AkcssSyntaxTree>.Empty,
                rootNamespace: "Demo", projectDirectory: "") { ProbeReuseSession = prepared.Session };
            var model = compilation.GetSemanticModel(tree);
            var result = model.BindMarkupAttributeExpression(prepared.Scope, CSharpSyntaxFactory.ParseExpression("2.ToString()"));
            weak.Add(new WeakReference(tree));
            weak.Add(new WeakReference(compilation));
            weak.Add(new WeakReference(model));
            weak.Add(new WeakReference(model.BindingSession));
            weak.Add(new WeakReference(prepared.Session));
            weak.Add(new WeakReference(result.OperationDefinition.Operation));
            if (owner.Latest != null)
            {
                weak.Add(new WeakReference(owner.Latest));
            }
            owner.Publish(prepared.Session.Seal());
        }
        return (owner, weak);
    }

    private static (CompilationBoundProbeStore.Session Session, CompilationBoundProbeStore.DocumentSession Document, MarkupElementSyntax Scope)
        PrepareProjection(CompilationBoundProbeStore owner, CSharpCompilation compilation, ComponentSyntaxTree tree, long version, bool record = true)
    {
        var session = owner.Begin(compilation, version, [DocumentSyntaxVersion.Create(tree)], "Demo", "", default)!;
        var document = Assert.IsType<CompilationBoundProbeStore.DocumentSession>(session.GetDocument(tree));
        var scope = tree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupElementSyntax>().First(element => element.StartTag?.Name.ToString() == "TextBlock");
        if (record)
        {
            document.Record(scope, CSharpSyntaxFactory.ParseExpression("1.ToString()"), null,
                CSharpSyntaxFactory.ParseCompilationUnit("class C { string M() { return 1.ToString(); } }"));
        }
        return (session, document, scope);
    }

#if STATS
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProjectionParserInstrumentation_DistinguishesWholeAndPreciseChangeRanges(bool precise)
    {
        var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture), "Page.akbura");
        var start = tree.Text.ToString().IndexOf("10", StringComparison.Ordinal);
        var changed = precise ? tree.Text.WithChanges(new TextChange(new TextSpan(start, 2), "20")) : SourceText.From(tree.Text.ToString().Replace("10", "20"));
        using var measurement = GenerationStatistics.BeginMeasurement();
        var next = tree.WithChangedText(changed);
        Assert.Equal(changed.ToString(), next.GetRootSyntax().ToFullString());
        var snapshot = measurement.GetSnapshot();
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.IncrementalParseChangeRangeComputation));
        Assert.Equal(precise ? 1 : 0, snapshot.GetCounterCount(GenerationStatisticCounter.IncrementalParsePreciseChangeRange));
        Assert.Equal(precise ? 0 : 1, snapshot.GetCounterCount(GenerationStatisticCounter.IncrementalParseWholeDocumentChangeRange));
    }

#if AKBURA_PROJECTION_REUSE
    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void ProjectionProjectState_MarkupRevisionsReleasePreviousSnapshotsAndCSharpTransitionReleasesStore(int revisions)
    {
        var (current, old) = ProjectionStateTransition(revisions);
        AssertCollected(old);
        Assert.NotNull(current.ProbeStore);
        AssertDetachedGraph(current.Artifacts);
        GC.KeepAlive(current);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (BlackSilenceProjectState State, List<(string Kind, WeakReference Reference)> Weak) ProjectionStateTransition(int revisions)
    {
        var project = new TestProject(publishDiagnostics: true);
        var state = new BlackSilenceProjectState(project.Compilation);
        var options = new GeneratorProjectOptions("Demo", project.Directory);
        var weak = new List<(string Kind, WeakReference Reference)>
        {
            ("ProjectState A", new WeakReference(state)),
            ("ProbeStore A", new WeakReference(state.ProbeStore!)),
            ("CSharpCompilation A", new WeakReference(project.Compilation)),
        };
        for (var revision = 1; revision <= revisions; revision++)
        {
            var tree = ComponentSyntaxTree.ParseText(Component(ProjectionFixture.Replace("10", (10 + revision % 80).ToString())), Path.Combine(project.Directory, "Page.akbura"));
            var request = BlackSilenceGenerationRequestBuilder.Create([DocumentSyntaxVersion.Create(tree)], state, options, default)!;
            var batch = BlackSilenceDocumentBatch.Generate(request, default);
            var fresh = BlackSilenceDocumentBatch.Generate(BlackSilenceGenerationRequestBuilder.Create(
                [DocumentSyntaxVersion.Create(tree)], new BlackSilenceProjectState(project.Compilation), options, default)!, default);
            Assert.Equal(fresh.Components.Select(source => source.SourceText.ToString()), batch.Components.Select(source => source.SourceText.ToString()));
            Assert.Equal(fresh.Diagnostics, batch.Diagnostics);
            var model = request.Index.Compilation.GetSemanticModel(tree);
            weak.Add(("Markup tree", new WeakReference(tree)));
            weak.Add(("Markup semantic model", new WeakReference(model)));
            weak.Add(("Binding session", new WeakReference(model.BindingSession)));
            weak.Add(("Project snapshot", new WeakReference(state.TryGetSnapshot(options)!)));
        }
        var replacement = new TestProject(publishDiagnostics: true);
        return (new BlackSilenceProjectState(replacement.Compilation, state.Artifacts), weak);
    }

    [Theory]
    [MemberData(nameof(LocalizedMarkupWorkloads))]
    public void ProjectionReuse_SameWidthLiteralEditsSkipConstructionButStillBindAndMatchFresh(string body, bool structural)
    {
        var project = new TestProject(publishDiagnostics: true, structural);
        var original = project.File("Page.akbura", Component(body));
        var run = Run(project, CreateDriver(project.Options, original));
        Assert.NotNull(run.Request.State.ProbeStore);
        Assert.True(run.Request.State.ProbeStore.Latest!.EntryCount > 0);
        var previous = original;
        foreach (var value in new[] { "20", "30", "10", "20" })
        {
            var next = project.File("Page.akbura", Component(body.Replace("10", value)));
            GenerationStatisticsSnapshot stats;
            using (var measurement = GenerationStatistics.BeginMeasurement())
            {
                run = Run(project, run.Driver.ReplaceAdditionalText(previous, next));
                stats = measurement.GetSnapshot();
            }
            Assert.True(stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionProjectionConstructionSkipped) > 0);
            Assert.True(stats.GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding) > 0);
            Assert.True(stats.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated) > 0);
            AssertParityWithFresh(project, run, project.Options, next);
            previous = next;
        }
    }
#endif
#endif
}
