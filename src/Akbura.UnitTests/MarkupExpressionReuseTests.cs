using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.UnitTests;

public sealed class MarkupExpressionReuseTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedLiteralEdit_ChangesOnlyItsPropertyAndDerivedFingerprints(bool structural)
    {
        var mode = structural ? ComponentGenerationMode.DebugStructural : ComponentGenerationMode.ReleaseDirect;
        var before = Generate("10").Split('\n');
        var after = Generate("20").Split('\n');
        Assert.Equal(before.Length, after.Length);
        var changes = before.Select((line, index) => (Before: line.TrimEnd('\r'), After: after[index].TrimEnd('\r')))
            .Where(pair => pair.Before != pair.After).ToArray();
        output.WriteLine("GENERATED_LITERAL_DIFF " + System.Text.Json.JsonSerializer.Serialize(new { Mode = mode.ToString(), Changes = changes.Select(pair => new { pair.Before, pair.After }) }));
        Assert.Equal(structural ? 6 : 2, changes.Length);
        var values = changes.Where(pair => pair.Before.Contains("Thickness.Parse", StringComparison.Ordinal)).ToArray();
        Assert.Equal(2, values.Length);
        Assert.All(values, pair => Assert.Equal(pair.Before.Replace("\"10\"", "\"20\"", StringComparison.Ordinal), pair.After));
        // Both initialization/reconciliation paths emit the value. Structural
        // output additionally updates the existing render/value fingerprints.
        foreach (var pair in changes.Except(values))
        {
            Assert.Matches("^\\s*\"(?:[A-F0-9]{16}|[A-F0-9]{64})\"[,;]$", pair.Before);
            Assert.Matches("^\\s*\"(?:[A-F0-9]{16}|[A-F0-9]{64})\"[,;]$", pair.After);
        }

        string Generate(string margin)
        {
            var fixture = AkcssActivatorPlannerTests.CreateFixture("using Avalonia.Controls; state int count = 1; <StackPanel><TextBlock Margin=\"" + margin + "\" Text={count.ToString()} /><TextBlock Text={count.ToString()} /></StackPanel>");
            var component = Assert.IsAssignableFrom<Akbura.Language.Symbols.IAkburaComponentSymbol>(fixture.SemanticModel.GetSymbolInfo(fixture.ComponentTree.GetRoot()).Symbol);
            return ComponentDocumentWriter.Generate(component, fixture.SemanticModel, "Views/PlannerView.akbura",
                new Dictionary<Akbura.Language.Syntax.AkburaSyntax, string>(), mode: mode).ToString();
        }
    }

#if STATS
    [Fact]
    public void ExactPreparedExpressions_ExecuteOneProbeAndReuseOnlyTheCompletedResult()
    {
        var model = Model("state int count = 1; <StackPanel><TextBlock /><TextBlock /></StackPanel>");
        var attributes = model.SyntaxTree.GetRootSyntax().DescendantNodesAndSelf()
            .OfType<Akbura.Language.Syntax.MarkupElementSyntax>().Where(element => element.StartTag?.Name.ToString() == "TextBlock").ToArray();
        model.GetSemanticDiagnostics(model.SyntaxTree.GetRoot());
        using var measurement = GenerationStatistics.BeginMeasurement();
        var first = model.BindMarkupAttributeExpression(attributes[0], SyntaxFactory.ParseExpression("count + 1"));
        for (var index = 0; index < 20; index++)
        {
            var current = model.BindMarkupAttributeExpression(attributes[index % 2], SyntaxFactory.ParseExpression("count + 1"));
            Assert.Same(first.OperationDefinition.Operation, current.OperationDefinition.Operation);
            Assert.Equal(first.Diagnostics, current.Diagnostics);
        }
        var snapshot = measurement.GetSnapshot();
        Assert.Equal(21, snapshot.GetCounterCount(GenerationStatisticCounter.MarkupExpressionRequest));
        Assert.Equal(20, snapshot.GetCounterCount(GenerationStatisticCounter.MarkupExpressionCacheHit));
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeCompilationCreated));
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeSemanticModelAcquired));
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticQuery));
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionCacheEntryPublished));
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionKeyHash) >= 21);
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionKeySyntaxComparison) >= 20);
        Assert.Equal(21, snapshot.GetStageInvocationCount(GenerationStatisticStage.ExpressionProjectionConstruction));
        Assert.Equal(21, snapshot.GetStageInvocationCount(GenerationStatisticStage.ExpressionProjectionText));
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding));
    }

    [Fact]
    public void ConcurrentExactRequests_PublishOneEntryWithoutClaimingSingleFlight()
    {
        var context = Model(string.Empty).BindingSession.ProbeContext;
        var projection = SyntaxFactory.ParseCompilationUnit("class C { int M() { return 1; } }");
        var calls = 0;
        using var measurement = GenerationStatistics.BeginMeasurement();
        Parallel.For(0, 128, _ => context.BindMarkupExpression(projection, null, () =>
        {
            Interlocked.Increment(ref calls);
            return CSharpBindingResult.Empty;
        }));
        context.BindMarkupExpression(projection, null, () => throw new InvalidOperationException("Completed result not reused."));
        var snapshot = measurement.GetSnapshot();
        Assert.InRange(calls, 1, 128);
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionCacheEntryPublished));
        Assert.Equal(calls, snapshot.GetStageInvocationCount(GenerationStatisticStage.ExpressionFactoryBinding));
        Assert.Equal(129 - calls, snapshot.GetCounterCount(GenerationStatisticCounter.MarkupExpressionCacheHit));
    }
#endif

    [Fact]
    public void SameExpressionTextInDifferentLocalScopes_IsNotAReuseProof()
    {
        var model = Model("""
            <StackPanel>
                $foreach (var value in new[] { 1 }) { <TextBlock /> }
                $foreach (var value in new[] { "x" }) { <TextBlock /> }
            </StackPanel>
            """);
        var elements = model.SyntaxTree.GetRootSyntax().DescendantNodesAndSelf().OfType<Akbura.Language.Syntax.MarkupElementSyntax>()
            .Where(element => element.StartTag?.Name.ToString() == "TextBlock").ToArray();
        var first = model.BindMarkupAttributeExpression(elements[0], SyntaxFactory.ParseExpression("value"));
        var second = model.BindMarkupAttributeExpression(elements[1], SyntaxFactory.ParseExpression("value"));
        Assert.Equal(SpecialType.System_Int32, first.TypeSymbol!.SpecialType);
        Assert.Equal(SpecialType.System_String, second.TypeSymbol!.SpecialType);
        Assert.NotSame(first.OperationDefinition.Operation, second.OperationDefinition.Operation);
    }

    [Fact]
    public void ContextualExpressions_KeepDifferentExpectedTypesAndConversions()
    {
        var model = Model(string.Empty);
        var root = model.SyntaxTree.GetRootSyntax();
        foreach (var specialType in new[] { SpecialType.System_Int32, SpecialType.System_String, SpecialType.System_Double })
        {
            var type = model.Compilation.CSharpProbeCompilation.CreateArrayTypeSymbol(model.Compilation.CSharpProbeCompilation.GetSpecialType(specialType));
            var result = model.BindMarkupAttributeExpression(root, SyntaxFactory.ParseExpression("[]"), type);
            Assert.Equal(specialType, Assert.IsAssignableFrom<IArrayTypeSymbol>(result.TypeSymbol).ElementType.SpecialType);
            Assert.Empty(result.Diagnostics);
        }
    }

    [Fact]
    public void CanceledAndFailedFactories_DoNotPublishReusableEntries()
    {
        var context = Model(string.Empty).BindingSession.ProbeContext;
        var projection = SyntaxFactory.ParseCompilationUnit("class C { int M() { return 1; } }");
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => context.BindMarkupExpression(projection, null, () =>
        {
            calls++;
            throw new OperationCanceledException();
        }));
        Assert.Throws<InvalidOperationException>(() => context.BindMarkupExpression(projection, null, () =>
        {
            calls++;
            throw new InvalidOperationException();
        }));
        context.BindMarkupExpression(projection, null, () => { calls++; return CSharpBindingResult.Empty; });
        context.BindMarkupExpression(projection, null, () => throw new InvalidOperationException("Completed result not reused."));
        Assert.Equal(3, calls);
    }

    [Theory]
    [InlineData("class C { int M() => ; }")]
    [InlineData("#nullable enable\r\nclass C { int M() => 1; }")]
    public void RecoveryAndDirectives_AlwaysUseTheNormalPath(string source)
    {
        var context = Model(string.Empty).BindingSession.ProbeContext;
        var projection = SyntaxFactory.ParseCompilationUnit(source);
        var calls = 0;
        for (var index = 0; index < 2; index++)
        {
            context.BindMarkupExpression(projection, null, () => { calls++; return CSharpBindingResult.Empty; });
        }
        Assert.Equal(2, calls);
    }

    [Fact]
    public void IdenticalTextWithDifferentSyntaxKinds_DoesNotReuse()
    {
        var context = Model(string.Empty).BindingSession.ProbeContext;
        var first = SyntaxFactory.ParseCompilationUnit("class C { int M() { return 1; } }");
        var type = first.DescendantNodes().OfType<CSharp.PredefinedTypeSyntax>().Single();
        var second = first.ReplaceNode(type, SyntaxFactory.IdentifierName("int").WithTriviaFrom(type));
        Assert.Equal(first.ToFullString(), second.ToFullString());
        var calls = 0;
#if STATS
        using var measurement = GenerationStatistics.BeginMeasurement();
#endif
        context.BindMarkupExpression(first, null, () => { calls++; return CSharpBindingResult.Empty; });
        context.BindMarkupExpression(second, null, () => { calls++; return CSharpBindingResult.Empty; });
        Assert.Equal(2, calls);
#if STATS
        var snapshot = measurement.GetSnapshot();
        Assert.Equal(2, snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionCacheEntryPublished));
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionKeyNonEqualComparison) > 0);
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ExpressionKeySyntaxComparison) > 0);
#endif
    }

    [Fact]
    public void IdenticalProjectionTextWithDifferentAnnotatedQueryPositions_DoesNotReuse()
    {
        var context = Model(string.Empty).BindingSession.ProbeContext;
        var root = SyntaxFactory.ParseCompilationUnit("class C { int M(bool flag) { if (flag) { return 1; } return 2; } }");
        var returns = root.DescendantNodes().OfType<CSharp.ReturnStatementSyntax>().ToArray();
        var first = root.ReplaceNode(returns[0], returns[0].WithAdditionalAnnotations(new SyntaxAnnotation("AkburaCSharpReturnProbe")));
        var second = root.ReplaceNode(returns[1], returns[1].WithAdditionalAnnotations(new SyntaxAnnotation("AkburaCSharpReturnProbe")));
        Assert.Equal(first.ToFullString(), second.ToFullString());
        var calls = 0;
        context.BindMarkupExpression(first, null, () => { calls++; return CSharpBindingResult.Empty; });
        context.BindMarkupExpression(second, null, () => { calls++; return CSharpBindingResult.Empty; });
        Assert.Equal(2, calls);
    }

    [Fact]
    public void ErrorResults_AreNotCachedAndKeepProbeRelativeLocations()
    {
        var model = Model(string.Empty);
        var root = model.SyntaxTree.GetRootSyntax();
        var first = model.BindMarkupAttributeExpression(root, SyntaxFactory.ParseExpression("Missing + 1"));
        var second = model.BindMarkupAttributeExpression(root, SyntaxFactory.ParseExpression("Missing + 1"));
        Assert.NotEmpty(first.Diagnostics);
        Assert.NotSame(first.OperationDefinition.Operation, second.OperationDefinition.Operation);
        Assert.Equal(first.Diagnostics.Select(Akbura.TestUtilities.DiagnosticParity.Key), second.Diagnostics.Select(Akbura.TestUtilities.DiagnosticParity.Key));
    }

    [Fact]
    public void LiteralEdit_CreatesAnIndependentCacheAndDoesNotReuseOldRoslynOwners()
    {
        var model = Model("state int count = 1; <TextBlock Margin=\"10\" />");
        var root = model.SyntaxTree.GetRootSyntax();
        var first = model.BindMarkupAttributeExpression(root, SyntaxFactory.ParseExpression("count + 1"));
        var tree = ((ComponentSyntaxTree)model.SyntaxTree).WithChangedText(SourceText.From(model.SyntaxTree.Text.ToString().Replace("10", "20")));
        var changed = model.Compilation.WithSyntaxTrees([tree]).GetSemanticModel(tree);
        var second = changed.BindMarkupAttributeExpression(tree.GetRootSyntax(), SyntaxFactory.ParseExpression("count + 1"));
        Assert.NotSame(model.BindingSession.ProbeContext, changed.BindingSession.ProbeContext);
        Assert.NotSame(first.OperationDefinition.Operation, second.OperationDefinition.Operation);
    }

    [Fact]
    public void ReusedExpressions_KeepTheSameDetachedCSharpDependencyFactsAsNormalBinding()
    {
        const string expression = "global::Demo.Fixture.Current.Name.ToString()";
        const string support = "namespace Demo; public class Fixture { public static Fixture Current => new(); public string Name => \"x\"; }";
        var cached = Model("<TextBlock />", support, collectDependencies: true);
        var normal = Model("<TextBlock />", support, collectDependencies: true);
        for (var index = 0; index < 3; index++)
        {
            cached.BindMarkupAttributeExpression(cached.SyntaxTree.GetRootSyntax(), SyntaxFactory.ParseExpression(expression));
            normal.BindingSession.GetCSharpProbeBinder(normal.SyntaxTree.GetRootSyntax()).BindExpression(normal.SyntaxTree.GetRootSyntax(), SyntaxFactory.ParseExpression(expression));
        }
        var expected = normal.BindingSession.SemanticDependencies.Detach();
        var actual = cached.BindingSession.SemanticDependencies.Detach();
        Assert.True(expected.IsComplete);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(expected), System.Text.Json.JsonSerializer.Serialize(actual));
    }

    [Theory]
    [InlineData(100)]
    [InlineData(500)]
    public void SequentialEdits_CurrentOwnerDoesNotRetainPreviousModelsOrProbeResults(int revisions)
    {
        var (current, weak) = Revisions(revisions);
        for (var attempt = 0; attempt < 10 && weak.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        Assert.All(weak, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(current);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (AkburaSemanticModel Current, List<WeakReference> Weak) Revisions(int revisions)
    {
        var model = Model("state int count = 1; <TextBlock Margin=\"0\" />");
        var weak = new List<WeakReference>();
        for (var index = 1; index <= revisions; index++)
        {
            var result = model.BindMarkupAttributeExpression(model.SyntaxTree.GetRootSyntax(), SyntaxFactory.ParseExpression("count + 1"));
            model.BindMarkupAttributeExpression(model.SyntaxTree.GetRootSyntax(), SyntaxFactory.ParseExpression("count + 1"));
            weak.Add(new WeakReference(model));
            weak.Add(new WeakReference(model.BindingSession.ProbeContext));
            weak.Add(new WeakReference(model.SyntaxTree));
            weak.Add(new WeakReference(model.Compilation));
            weak.Add(new WeakReference(result.OperationDefinition.Operation));
            var tree = ComponentSyntaxTree.ParseText("state int count = 1; <TextBlock Margin=\"" + index + "\" />", model.SyntaxTree.FilePath);
            model = model.Compilation.WithSyntaxTrees([tree]).GetSemanticModel(tree);
        }
        return (model, weak);
    }

    private static AkburaSemanticModel Model(string source, string? support = null, bool collectDependencies = false)
    {
        var tree = ComponentSyntaxTree.ParseText(source, "MarkupReuse.akbura");
        var compilation = CSharpCompilation.Create("MarkupReuse", syntaxTrees: support == null ? [] : [CSharpSyntaxTree.ParseText(support)], references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var akbura = new AkburaCompilation(compilation, [tree], ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo", projectDirectory: Environment.CurrentDirectory) { CollectCSharpSemanticDependencies = collectDependencies };
        return akbura.GetSemanticModel(tree);
    }
}
