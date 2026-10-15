using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using Microsoft.CodeAnalysis.Text;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

public sealed class CSharpProbeOptimizationTests
{
#if STATS
    [Fact]
    public void RepeatedTypeProbes_BindOnceWithinOneDocumentSnapshot()
    {
        var model = CreateModel("state int count = 1;");
        using var measurement = GenerationStatistics.BeginMeasurement();
        var first = model.BindCSharpType(SyntaxFactory.ParseTypeName("int"));
        for (var index = 0; index < 30; index++)
        {
            var repeated = model.BindCSharpType(SyntaxFactory.ParseTypeName("int"));
            Assert.Same(first.TypeSymbol, repeated.TypeSymbol);
            Assert.Equal(first.Diagnostics, repeated.Diagnostics);
        }

        var snapshot = measurement.GetSnapshot();
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeSemanticModelAcquired));
        Assert.Equal(30, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeBindingResultReused));
    }

    [Fact]
    public void RecursiveExpressionBinding_QueriesDiagnosticsOnceForTheCoveringExpression()
    {
        var model = CreateModel("state int count = 1;\r\ncount = count;");
        var scope = model.SyntaxTree.GetRoot().Members.Last();
        var binder = model.BindingSession.GetCSharpProbeBinder(scope);
        model.GetSemanticDiagnostics(model.SyntaxTree.GetRoot());
        using var measurement = GenerationStatistics.BeginMeasurement();
        var bound = binder.BindExpression(scope,
            SyntaxFactory.ParseExpression("(count + 1) * (count + 2) + count.ToString().Length"));

        Assert.NotNull(bound.Type);
        var snapshot = measurement.GetSnapshot();
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticsRequested) > 1);
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticQuery));
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ProbeDiagnosticsReused) > 0);
    }

    [Fact]
    public void HookAttempts_WithUnchangedInvocationReuseTheExactOwnedSemanticModel()
    {
        var model = CreateModel("state int count = 1;\r\ncount = count;",
            "namespace Hooks; public static class TestHooks { public static int useValue(int value) => value; }");
        model.GetSemanticDiagnostics(model.SyntaxTree.GetRoot());
        var syntax = model.SyntaxTree.GetRoot().Members.Last();
        var binder = model.BindingSession.GetCSharpProbeBinder(syntax);
        var invocation = (Microsoft.CodeAnalysis.CSharp.Syntax.InvocationExpressionSyntax)SyntaxFactory.ParseExpression("Hooks.TestHooks.useValue(count)");
        using var measurement = GenerationStatistics.BeginMeasurement();
        var first = binder.BindUseHookInvocation(syntax, invocation, ImmutableArray<INamedTypeSymbol>.Empty,
            injectSelf: false, rewritePropertyArguments: false);
        var rewritten = binder.BindUseHookInvocation(syntax, invocation, ImmutableArray<INamedTypeSymbol>.Empty,
            injectSelf: false, rewritePropertyArguments: true);
        Assert.NotNull(first.Method);
        Assert.Same(first.Method, rewritten.Method);
        Assert.Equal(first.BindingResult.Diagnostics, rewritten.BindingResult.Diagnostics);
        var snapshot = measurement.GetSnapshot();
        Assert.Equal(1, snapshot.GetCounterCount(GenerationStatisticCounter.ProbeSemanticModelAcquired));
        Assert.True(snapshot.GetCounterCount(GenerationStatisticCounter.ProbeContextReused) > 0);
    }
#endif

    [Fact]
    public void IdenticalTypeText_DoesNotShareSymbolsAcrossCompilations()
    {
        var first = CreateModel("using Demo;", "namespace Demo; public sealed class Value { public int Item; }");
        var second = CreateModel("using Demo;", "namespace Demo; public sealed class Value { public string Item; }");
        var firstType = first.BindCSharpType(SyntaxFactory.ParseTypeName("Value")).TypeSymbol;
        var secondType = second.BindCSharpType(SyntaxFactory.ParseTypeName("Value")).TypeSymbol;

        Assert.NotSame(firstType, secondType);
        Assert.Equal(SpecialType.System_Int32, Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(firstType!.GetMembers("Item"))).Type.SpecialType);
        Assert.Equal(SpecialType.System_String, Assert.IsAssignableFrom<IFieldSymbol>(Assert.Single(secondType!.GetMembers("Item"))).Type.SpecialType);
    }

    [Fact]
    public void IdenticalExpressionSyntax_BindsIndependentlyForDifferentTargetTypes()
    {
        var model = CreateModel(string.Empty);
        var scope = model.SyntaxTree.GetRoot();
        var binder = model.BindingSession.GetCSharpProbeBinder(scope);
        var expression = SyntaxFactory.ParseExpression("[]");
        foreach (var specialType in new[] { SpecialType.System_Int32, SpecialType.System_String, SpecialType.System_Double })
        {
            var target = binder.CSharpCompilation.CreateArrayTypeSymbol(binder.CSharpCompilation.GetSpecialType(specialType));
            var result = Assert.IsType<BoundConversionExpression>(binder.BindExpression(scope, expression, target));
            Assert.False(result.HasErrors);
            Assert.Equal(specialType, Assert.IsAssignableFrom<IArrayTypeSymbol>(result.Operand.Type).ElementType.SpecialType);
            Assert.Equal(specialType, Assert.IsAssignableFrom<IArrayTypeSymbol>(result.Type).ElementType.SpecialType);
        }
    }

    [Fact]
    public void DefaultImports_AreSharedWithoutChangingPrefixCompletionScope()
    {
        var model = CreateModel("using System;\r\nusing Alias = System.Int32;");
        Assert.True(model.GetCSharpUsingDirectives() == model.GetCSharpUsingDirectives());
        var lastUsing = model.SyntaxTree.GetRoot().Members.OfType<UsingDirectiveSyntax>().Last();
        Assert.DoesNotContain(model.GetCSharpUsingDirectivesBefore(lastUsing), directive => directive.Alias?.Name.Identifier.ValueText == "Alias");
        Assert.Contains(model.GetCSharpUsingDirectives(), directive => directive.Alias?.Name.Identifier.ValueText == "Alias");
    }

    [Fact]
    public void AliasEdit_CreatesANewProbeContextWithoutMutatingThePreviousSnapshot()
    {
        var model = CreateModel("using Alias = One.Value;",
            "namespace One { public class Value { } } namespace Two { public class Value { } }");
        var type = SyntaxFactory.ParseTypeName("Alias");
        Assert.Equal("One", model.BindCSharpType(type).TypeSymbol!.ContainingNamespace.ToDisplayString());
        var changedTree = Assert.IsType<ComponentSyntaxTree>(model.SyntaxTree).WithChangedText(SourceText.From("using Alias = Two.Value;"));
        var changed = model.Compilation.WithSyntaxTrees([changedTree]).GetSemanticModel(changedTree);
        Assert.Same(model.Compilation.CSharpCompilation, changed.Compilation.CSharpCompilation);
        Assert.NotSame(model.BindingSession.ProbeContext, changed.BindingSession.ProbeContext);
        Assert.Equal("Two", changed.BindCSharpType(type).TypeSymbol!.ContainingNamespace.ToDisplayString());
        Assert.Equal("One", model.BindCSharpType(type).TypeSymbol!.ContainingNamespace.ToDisplayString());
    }

    [Theory]
    [InlineData("class C { int M() => Missing + Missing; }")]
    [InlineData("class C { int M() => \"bad\"; int N() => Other; }")]
    [InlineData("class C { int M() { int x = ; return x + Missing; } }")]
    [InlineData("class C { int M() { int x = 0; int x = 1; return x; } }")]
    [InlineData("class C { int M() => Call(null); int Call(string s) => 1; int Call(int[] s) => 2; }")]
    [InlineData("#nullable enable\r\nclass C { string M(string? s) => s.ToString(); }")]
    [InlineData("class C { T M<T>() where T : struct { return null; } }")]
    [InlineData("class C { void M() { if (true) { int x = 1; } else { x = 2; } } }")]
    public void CoveringDiagnostics_MatchRoslynForEveryNestedSpanIncludingMissingTokens(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create("DiagnosticRanges", [tree], SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var model = compilation.GetSemanticModel(tree);
        var cache = new CSharpProbeDiagnostics(model);
        cache.GetDiagnostics(tree.GetRoot().FullSpan);
        var spans = tree.GetRoot().DescendantNodesAndTokens().Select(node => node.Span)
            .Concat(tree.GetRoot().DescendantTokens().Select(token => new TextSpan(token.Span.End, 0))).Distinct();
        foreach (var span in spans)
        {
            Assert.Equal(DiagnosticKeys(model.GetDiagnostics(span)), DiagnosticKeys(cache.GetDiagnostics(span)));
        }
    }

    [Fact]
    public void TypeProbeCache_DistinguishesImportOrderAliasesGenericsAndExactTrivia()
    {
        var model = CreateModel("using System;",
            "namespace One { public class Value { } } namespace Two { public class Value { } }");
        var firstUsings = ImmutableArray.Create(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("One")));
        var secondUsings = ImmutableArray.Create(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("Two")));
        var first = model.BindCSharpType(SyntaxFactory.ParseTypeName("Value"), firstUsings);
        var second = model.BindCSharpType(SyntaxFactory.ParseTypeName("Value"), secondUsings);
        Assert.Equal("One", first.TypeSymbol!.ContainingNamespace.ToDisplayString());
        Assert.Equal("Two", second.TypeSymbol!.ContainingNamespace.ToDisplayString());
        var firstAlias = ImmutableArray.Create((Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax)SyntaxFactory.ParseCompilationUnit("using Alias = One.Value;").Usings.Single());
        var secondAlias = ImmutableArray.Create((Microsoft.CodeAnalysis.CSharp.Syntax.UsingDirectiveSyntax)SyntaxFactory.ParseCompilationUnit("using Alias = Two.Value;").Usings.Single());
        Assert.Equal("One", model.BindCSharpType(SyntaxFactory.ParseTypeName("Alias"), firstAlias).TypeSymbol!.ContainingNamespace.ToDisplayString());
        Assert.Equal("Two", model.BindCSharpType(SyntaxFactory.ParseTypeName("Alias"), secondAlias).TypeSymbol!.ContainingNamespace.ToDisplayString());
        Assert.Null(model.BindCSharpType(SyntaxFactory.ParseTypeName("Value"), firstUsings.AddRange(secondUsings)).TypeSymbol);
        Assert.Null(model.BindCSharpType(SyntaxFactory.ParseTypeName("Value"), secondUsings.AddRange(firstUsings)).TypeSymbol);
        var invalid = model.BindCSharpType(SyntaxFactory.ParseTypeName("Missing"));
        Assert.NotEmpty(invalid.Diagnostics);
        Assert.Null(invalid.TypeSymbol);
        var generic = model.BindCSharpType(SyntaxFactory.ParseTypeName("System.Collections.Generic.List<int>"));
        var otherGeneric = model.BindCSharpType(SyntaxFactory.ParseTypeName("System.Collections.Generic.List<string>"));
        Assert.NotEqual(generic.TypeSymbol!.ToDisplayString(), otherGeneric.TypeSymbol!.ToDisplayString());
        var trivia = model.BindCSharpType(SyntaxFactory.ParseTypeName("/* offset */Missing"));
        Assert.NotEqual(invalid.Diagnostics[0].Location.SourceSpan, trivia.Diagnostics[0].Location.SourceSpan);
    }

    [Fact]
    public void CanceledTypeProbe_DoesNotPublishAnIncompleteResult()
    {
        var model = CreateModel(string.Empty);
        var context = model.BindingSession.ProbeContext;
        var type = SyntaxFactory.ParseTypeName("int");
        var calls = 0;
        Assert.Throws<OperationCanceledException>(() => context.BindType(type, default, () =>
        {
            calls++;
            throw new OperationCanceledException();
        }));
        context.BindType(type, default, () =>
        {
            calls++;
            return CSharpBindingResult.Empty;
        });
        context.BindType(type, default, () => throw new InvalidOperationException("Completed result was not reused."));
        Assert.Equal(2, calls);
    }

    [Fact]
    public void TypeProbes_WithIdenticalTextButDifferentSyntaxKindsRemainIndependent()
    {
        var model = CreateModel(string.Empty);
        var keyword = SyntaxFactory.ParseTypeName("int");
        var identifier = SyntaxFactory.IdentifierName("int");
        Assert.Equal(keyword.ToFullString(), identifier.ToFullString());
        Assert.Equal(SpecialType.System_Int32, model.BindCSharpType(keyword).TypeSymbol!.SpecialType);
        Assert.Null(model.BindCSharpType(identifier).TypeSymbol);
    }

    [Fact]
    public async Task ConcurrentTypeProbes_ReturnOneCompletedResultOwnedByTheirDocument()
    {
        var model = CreateModel("using System;");
        var results = await Task.WhenAll(Enumerable.Range(0, 16).Select(_ => Task.Run(() =>
            model.BindCSharpType(SyntaxFactory.ParseTypeName("System.Collections.Generic.List<int>")))));
        Assert.All(results, result => Assert.Same(results[0].TypeSymbol, result.TypeSymbol));
        Assert.All(results, result => Assert.Empty(result.Diagnostics));
    }

    [Fact]
    public void DiagnosticCache_DoesNotKeepUnusedProbeModelsAliveWhileDocumentRemainsAlive()
    {
        var model = CreateModel(string.Empty);
        var references = CreateReleasedDiagnosticProbe(model.BindingSession.ProbeContext, model.Compilation.CSharpCompilation);
        for (var attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive));
        GC.KeepAlive(model);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateReleasedDiagnosticProbe(CSharpProbeContext context, CSharpCompilation compilation)
    {
        var tree = CSharpSyntaxTree.ParseText("class TemporaryProbe { int Read() => Missing; }");
        var derived = compilation.AddSyntaxTrees(tree);
        var model = derived.GetSemanticModel(tree);
        context.GetDiagnostics(model, tree.GetRoot().FullSpan);
        return [new WeakReference(tree), new WeakReference(derived), new WeakReference(model)];
    }

    private static string[] DiagnosticKeys(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(diagnostic =>
        diagnostic.Id + "|" + diagnostic.Severity + "|" + diagnostic.GetMessage(System.Globalization.CultureInfo.InvariantCulture) +
        "|" + diagnostic.Location.SourceSpan + "|" + diagnostic.Location.GetMappedLineSpan()).ToArray();

    [Fact]
    public void ReleasedProbeSnapshot_DoesNotRetainItsCompilation()
    {
        var references = CreateReleasedSnapshot();
        for (var attempt = 0; attempt < 10 && references.Any(reference => reference.IsAlive); attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }

        Assert.All(references, reference => Assert.False(reference.IsAlive));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] CreateReleasedSnapshot()
    {
        var model = CreateModel("state int count = 1;");
        model.BindCSharpType(SyntaxFactory.ParseTypeName("int"));
        model.BindCSharpType(SyntaxFactory.ParseTypeName("int"));
        var binder = model.BindingSession.GetCSharpProbeBinder(model.SyntaxTree.GetRoot());
        binder.BindExpression(model.SyntaxTree.GetRoot(), SyntaxFactory.ParseExpression("count + 1"));
        return [new WeakReference(model), new WeakReference(model.Compilation), new WeakReference(model.Compilation.CSharpCompilation)];
    }

    private static AkburaSemanticModel CreateModel(string source, string? csharp = null)
    {
        var tree = AkburaSyntaxTree.ParseText(source, "Probe.akbura");
        var compilation = CSharpCompilation.Create("ProbeTests",
            csharp == null ? [] : [CSharpSyntaxTree.ParseText(csharp)],
            SymbolTests.CreateAvaloniaReferences(),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        return new AkburaCompilation(compilation, [tree], ImmutableArray<AkcssSyntaxTree>.Empty,
            rootNamespace: "Demo", projectDirectory: Environment.CurrentDirectory).GetSemanticModel(tree);
    }
}
