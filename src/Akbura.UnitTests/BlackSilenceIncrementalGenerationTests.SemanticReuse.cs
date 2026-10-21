using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed partial class BlackSilenceIncrementalGenerationTests
{
    private const string SemanticReuseSupport = """
        namespace Demo;
        public class Person
        {
            public static Person Current { get; } = new();
            public string FirstName { get; set; } = "First";
            public string LastName { get; set; } = "Last";
        }
        """;
    private const string SemanticReuseBody = "<StackPanel><TextBlock Margin=\"10\" Text={global::Demo.Person.Current.FirstName} /><TextBlock Text={global::Demo.Person.Current.LastName} /></StackPanel>";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticReuseAudit_RetainedRoslynResultDoesNotRetainMarkupSemanticOwners(bool injected)
    {
        var (result, weak) = CaptureSemanticReuseOwnership(injected);
        AssertCollected(weak);
        Assert.Equal("FirstName", result.Symbol!.Name);
        Assert.NotNull(result.OperationDefinition.Operation!.SemanticModel);
        Assert.Equal(SpecialType.System_String, result.TypeSymbol!.SpecialType);
        GC.KeepAlive(result);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (CSharpBindingResult Result, List<(string Kind, WeakReference Reference)> Weak) CaptureSemanticReuseOwnership(bool injected)
    {
        var project = new TestProject(publishDiagnostics: true);
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions, "Person.cs"));
        var body = injected ? "inject Demo.Person Vm; " + SemanticReuseBody.Replace("global::Demo.Person.Current.", "Vm.") : SemanticReuseBody;
        var tree = ComponentSyntaxTree.ParseText(Component(body), Path.Combine(project.Directory, "Page.akbura"));
        var state = new BlackSilenceProjectState(compilation);
        var options = new GeneratorProjectOptions("Demo", project.Directory);
        var request = BlackSilenceGenerationRequestBuilder.Create([DocumentSyntaxVersion.Create(tree)], state, options, default)!;
        BlackSilenceDocumentBatch.Generate(request, default);
        var model = request.Index.Compilation.GetSemanticModel(tree);
        var scope = tree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupElementSyntax>().First(element => element.StartTag?.Name.ToString() == "TextBlock");
        var result = model.BindMarkupAttributeExpression(scope, CSharpSyntaxFactory.ParseExpression(injected ? "Vm.FirstName" : "global::Demo.Person.Current.FirstName"));
        Assert.Empty(result.Diagnostics);
        var component = Assert.IsAssignableFrom<IAkburaComponentSymbol>(model.GetSymbolInfo(tree.GetRootSyntax()).Symbol);
        var plan = ComponentPlanner.Create(component, model, request.Index.AkcssModuleTypeNames);
        var weak = new List<(string Kind, WeakReference Reference)>
        {
            ("AkburaSemanticModel", new WeakReference(model)),
            ("BindingSession", new WeakReference(model.BindingSession)),
            ("CSharpProbeContext", new WeakReference(model.BindingSession.ProbeContext)),
            ("CSharpProbeBinder", new WeakReference(model.BindingSession.GetCSharpProbeBinder(scope))),
            ("AkburaSyntaxTree", new WeakReference(tree)),
            ("BlackSilenceSnapshot", new WeakReference(state.TryGetSnapshot(options))),
            ("BlackSilenceState", new WeakReference(state)),
            ("ComponentPlanSyntax", new WeakReference(plan.Elements[0].Syntax)),
        };
        plan.ReturnToPool();
        return (result, weak);
    }

    [Theory]
    [InlineData("global::Demo.Person.Current.FirstName", false)]
    [InlineData("global::Demo.Person.Current.Missing", true)]
    public void SemanticReuseContribution_UsesOriginalCaptureAndReplaysExactPositiveAndNegativeFacts(string expression, bool negative)
    {
        var normal = SemanticReuseModel();
        var captured = SemanticReuseModel(normal.Compilation.CSharpCompilation);
        var replayed = SemanticReuseModel(normal.Compilation.CSharpCompilation);
        normal.BindMarkupAttributeExpression(normal.SyntaxTree.GetRootSyntax(), CSharpSyntaxFactory.ParseExpression(expression));
        var collector = captured.BindingSession.SemanticDependencies;
        DetachedCSharpDependencySet facts;
        using (var scope = collector.BeginProbeContribution(default)!)
        {
            captured.BindMarkupAttributeExpression(captured.SyntaxTree.GetRootSyntax(), CSharpSyntaxFactory.ParseExpression(expression));
            facts = scope.Complete();
        }
        Assert.Equal(!negative, facts.IsComplete);
        Assert.Contains(facts.Lookups, lookup => lookup.Name == (negative ? "Missing" : "FirstName") && lookup.IsNegative == negative);
        replayed.BindingSession.SemanticDependencies.Import(facts);
        Assert.Equal(DependencyJson(normal.BindingSession.SemanticDependencies.Detach()), DependencyJson(collector.Detach()));
        if (!negative)
        {
            Assert.Equal(DependencyJson(normal.BindingSession.SemanticDependencies.Detach()), DependencyJson(replayed.BindingSession.SemanticDependencies.Detach()));
        }
    }

    [Fact]
    public async Task SemanticReuseContribution_ParallelScopesDoNotMixIndependentProbeFacts()
    {
        var model = SemanticReuseModel();
        var facts = await Task.WhenAll(new[] { "FirstName", "LastName" }.Select(name => Task.Run(() =>
        {
            using var scope = model.BindingSession.SemanticDependencies.BeginProbeContribution(default)!;
            model.BindMarkupAttributeExpression(model.SyntaxTree.GetRootSyntax(), CSharpSyntaxFactory.ParseExpression("global::Demo.Person.Current." + name));
            return scope.Complete();
        })));
        Assert.Contains(facts[0].Lookups, lookup => lookup.Name == "FirstName");
        Assert.DoesNotContain(facts[0].Lookups, lookup => lookup.Name == "LastName");
        Assert.Contains(facts[1].Lookups, lookup => lookup.Name == "LastName");
        Assert.DoesNotContain(facts[1].Lookups, lookup => lookup.Name == "FirstName");
    }

    private static string DependencyJson(DetachedCSharpDependencySet facts) => System.Text.Json.JsonSerializer.Serialize(facts);

    [Theory]
    [InlineData("value.Read()")]
    [InlineData("value.Select(item => item.Name)")]
    [InlineData("value.Read<int>()")]
    [InlineData("from item in values select item.Name")]
    [InlineData("await value")]
    [InlineData("value?.Name")]
    [InlineData("value.Name + value.Other")]
    public void SemanticReuse_UnsupportedExpressionShapesNeverEnterTheSemanticFastPath(string expression)
    {
        Assert.False(ReusableProbeBinding.IsMemberChain(CSharpSyntaxFactory.ParseExpression(expression)));
    }

    private static AkburaSemanticModel SemanticReuseModel(CSharpCompilation? compilation = null)
    {
        var tree = ComponentSyntaxTree.ParseText(Component(SemanticReuseBody), "Page.akbura");
        if (compilation == null)
        {
            var project = new TestProject(publishDiagnostics: true);
            compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions));
        }
        return new AkburaCompilation(compilation, [tree], ImmutableArray<AkcssSyntaxTree>.Empty, rootNamespace: "Demo")
        {
            CollectCSharpSemanticDependencies = true,
        }.GetSemanticModel(tree);
    }

#if STATS
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticReuse_TwoPropertyBindingsSkipFirstRoslynWorkAndReplayExactDependencies(bool projections)
    {
        var baseline = SemanticReuseModel();
        var compilation = baseline.Compilation.CSharpCompilation;
        var store = new CompilationBoundProbeStore(compilation, projections, reuseBindings: true);
        var initial = SemanticReuseRevision(store, compilation, "10", 1);
        CSharpBindingResult[] first;
        GenerationStatisticsSnapshot initialStats;
        using (var measurement = GenerationStatistics.BeginMeasurement())
        {
            first = BindSemanticSiblings(initial.Model);
            initialStats = measurement.GetSnapshot();
        }
        store.Publish(initial.Session.Seal());
        Assert.True(store.Latest!.SemanticEntryCount >= 2, System.Text.Json.JsonSerializer.Serialize(new
        {
            Results = first.Select(result => new { result.CandidateReason, Symbol = result.Symbol?.ToString(), Kind = result.OperationDefinition.Kind,
                Eligible = ReusableProbeBinding.CanRetain(result), Diagnostics = result.Diagnostics.Select(diagnostic => diagnostic.ToString()) }),
            Facts = initial.Model.BindingSession.SemanticDependencies.Detach(),
            Candidates = initialStats.GetCounterCount(GenerationStatisticCounter.CrossRevisionBindingCandidate),
            Captured = initialStats.GetCounterCount(GenerationStatisticCounter.CrossRevisionSemanticContributionCaptured),
            Rejected = initialStats.GetCounterCount(GenerationStatisticCounter.CompilationBoundSemanticEntriesRejected),
        }));
        for (var revision = 2; revision <= 5; revision++)
        {
            var current = SemanticReuseRevision(store, compilation, revision % 2 == 0 ? "20" : "10", revision);
            CSharpBindingResult[] results;
            GenerationStatisticsSnapshot stats;
            using (var measurement = GenerationStatistics.BeginMeasurement())
            {
                results = BindSemanticSiblings(current.Model);
                stats = measurement.GetSnapshot();
            }
            Assert.Equal(2, stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
            Assert.Equal(2, stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionProbeCompilationSkipped));
            Assert.Equal(2, stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionProbeSemanticModelSkipped));
            Assert.Equal(0, stats.GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding));
            Assert.Equal(0, stats.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated));
            Assert.Equal(0, stats.GetCounterCount(GenerationStatisticCounter.ProbeSemanticModelAcquired));
            Assert.Equal(projections ? 2 : 0, stats.GetCounterCount(GenerationStatisticCounter.CrossRevisionProjectionConstructionSkipped));
            for (var index = 0; index < results.Length; index++)
            {
                Assert.Same(first[index].OperationDefinition.Operation, results[index].OperationDefinition.Operation);
                Assert.Equal(SpecialType.System_String, results[index].TypeSymbol!.SpecialType);
            }
            var fresh = SemanticReuseModel(compilation);
            BindSemanticSiblings(fresh);
            Assert.Equal(DependencyJson(fresh.BindingSession.SemanticDependencies.Detach()), DependencyJson(current.Model.BindingSession.SemanticDependencies.Detach()));
            using (var measurement = GenerationStatistics.BeginMeasurement())
            {
                BindSemanticSiblings(current.Model);
                Assert.Equal(0, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
                Assert.Equal(2, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.MarkupExpressionCacheHit));
            }
            store.Publish(current.Session.Seal());
        }
    }
#endif

    private static (AkburaSemanticModel Model, CompilationBoundProbeStore.Session Session) SemanticReuseRevision(
        CompilationBoundProbeStore store, CSharpCompilation compilation, string margin, long version)
    {
        var tree = ComponentSyntaxTree.ParseText(Component(SemanticReuseBody.Replace("10", margin)), "Page.akbura");
        var session = store.Begin(compilation, version, [DocumentSyntaxVersion.Create(tree)], "Demo", "", default)!;
        var model = new AkburaCompilation(compilation, [tree], ImmutableArray<AkcssSyntaxTree>.Empty, rootNamespace: "Demo")
        {
            CollectCSharpSemanticDependencies = true,
            ProbeReuseSession = session,
        }.GetSemanticModel(tree);
        model.GetSymbolInfo(tree.GetRootSyntax());
        return (model, session);
    }

    private static CSharpBindingResult[] BindSemanticSiblings(AkburaSemanticModel model)
    {
        var scopes = model.SyntaxTree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupDynamicAttributeValueSyntax>().ToArray();
        return new[] { "FirstName", "LastName" }.Select((name, index) => model.BindMarkupAttributeExpression(scopes[index].Expression,
            CSharpSyntaxFactory.ParseExpression("global::Demo.Person.Current." + name))).ToArray();
    }

#if AKBURA_SEMANTIC_REUSE
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SemanticReuse_GeneratorReconstructsCurrentMarkupOwnersAndMatchesFreshDependencies(bool injected)
    {
        var project = new TestProject(publishDiagnostics: true, debugStructural: true);
        var compilation = project.Compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions, "Person.cs"));
        var body = injected ? "inject Demo.Person Vm; " + SemanticReuseBody.Replace("global::Demo.Person.Current.", "Vm.") : SemanticReuseBody;
        var previous = project.File("Page.akbura", Component(body));
        var current = ArtifactRun(project, CreateDriver(project.Options, previous), compilation);
        Assert.True(current.Request.State.ProbeStore!.Latest!.SemanticEntryCount >= 2);
        for (var revision = 0; revision < 4; revision++)
        {
            var next = project.File("Page.akbura", Component(body.Replace("10", revision % 2 == 0 ? "20" : "10")));
            using var measurement = GenerationStatistics.BeginMeasurement();
            current = ArtifactRun(project, current.Driver.ReplaceAdditionalText(previous, next), compilation);
#if STATS
            Assert.True(measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped) >= 2);
#endif
            var fresh = ArtifactRun(project, CreateDriver(project.Options, next), compilation);
            AssertSemanticReuseParity(fresh, current);
            var tree = current.Request.Documents[0].SyntaxTree;
            var model = current.Request.Index.Compilation.GetSemanticModel(tree);
            var element = tree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupElementSyntax>().First();
            var operation = Assert.IsAssignableFrom<Akbura.Language.Operations.IOperation>(model.GetOperation(element));
            Assert.Same(element, operation.Syntax);
            AssertCurrentOperationSyntax(operation, tree.GetRootSyntax());
            previous = next;
        }
    }

    [Theory]
    [InlineData("width")]
    [InlineData("expression")]
    [InlineData("import")]
    [InlineData("scope")]
    [InlineData("name")]
    [InlineData("recovery")]
    [InlineData("diagnostic")]
    [InlineData("compilation")]
    [InlineData("signature")]
    public void SemanticReuse_UnsupportedChangesFallBackAndMatchFresh(string change)
    {
        var project = new TestProject(publishDiagnostics: true);
        var support = CSharpSyntaxTree.ParseText(SemanticReuseSupport, project.Options.ParseOptions, "Person.cs");
        var compilation = project.Compilation.AddSyntaxTrees(support);
        var original = project.File("Page.akbura", Component(SemanticReuseBody));
        var initial = ArtifactRun(project, CreateDriver(project.Options, original), compilation);
        var source = change switch
        {
            "width" => Component(SemanticReuseBody.Replace("10", "200")),
            "expression" => Component(SemanticReuseBody.Replace("FirstName", "LastName")),
            "import" => "using System;\r\n" + Component(SemanticReuseBody),
            "scope" => Component(SemanticReuseBody.Replace("<TextBlock Margin", "$foreach (var item in new[] { 1 }) { <TextBlock Margin").Replace("FirstName} />", "FirstName} /> }")),
            "name" => Component(SemanticReuseBody.Replace("<StackPanel>", "<StackPanel x.Name=\"named\">")),
            "recovery" => Component(SemanticReuseBody.Replace("FirstName}", "FirstName + }")),
            "diagnostic" => Component(SemanticReuseBody.Replace("FirstName", "Missing")),
            _ => original.Text.ToString(),
        };
        if (change is "compilation" or "signature")
        {
            compilation = compilation.ReplaceSyntaxTree(support, CSharpSyntaxTree.ParseText(change == "signature"
                ? SemanticReuseSupport.Replace("string FirstName", "object FirstName") : SemanticReuseSupport + "\r\npublic class Other { }", project.Options.ParseOptions, "Person.cs"));
        }
        var changed = project.File("Page.akbura", source);
        TestRun current;
        using (var measurement = GenerationStatistics.BeginMeasurement())
        {
            current = ArtifactRun(project, initial.Driver.ReplaceAdditionalText(original, changed), compilation);
#if STATS
            Assert.Equal(0, measurement.GetSnapshot().GetCounterCount(GenerationStatisticCounter.CrossRevisionFirstBindingSkipped));
#endif
        }
        AssertSemanticReuseParity(ArtifactRun(project, CreateDriver(project.Options, changed), compilation), current);
    }

    private static void AssertSemanticReuseParity(TestRun expected, TestRun actual)
    {
        Assert.Equal(expected.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(source => source.HintName).Select(source => (source.HintName, source.SourceText.ToString())),
            actual.Driver.GetRunResult().Results.Single().GeneratedSources.OrderBy(source => source.HintName).Select(source => (source.HintName, source.SourceText.ToString())));
        Assert.Equal(ArtifactDiagnosticKeys(expected.Output.GetDiagnostics()), ArtifactDiagnosticKeys(actual.Output.GetDiagnostics()));
        Assert.Equal(ArtifactDiagnosticKeys(expected.Driver.GetRunResult().Diagnostics), ArtifactDiagnosticKeys(actual.Driver.GetRunResult().Diagnostics));
        Assert.Equal(expected.Snapshot.Entries.OrderBy(pair => pair.Key).Select(pair => (pair.Key, System.Text.Json.JsonSerializer.Serialize(pair.Value.SemanticDependencies))),
            actual.Snapshot.Entries.OrderBy(pair => pair.Key).Select(pair => (pair.Key, System.Text.Json.JsonSerializer.Serialize(pair.Value.SemanticDependencies))));
    }

    private static void AssertCurrentOperationSyntax(Akbura.Language.Operations.IOperation operation, AkburaSyntax root)
    {
        if (operation.Syntax != null)
        {
            Assert.Same(root, operation.Syntax.Root);
        }
        foreach (var child in operation.Children)
        {
            AssertCurrentOperationSyntax(child, root);
        }
    }
#endif
}
