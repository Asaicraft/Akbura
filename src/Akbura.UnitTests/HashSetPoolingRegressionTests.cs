using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.UnitTests;

[Collection(HashSetMeasurements.Name)]
public sealed class HashSetPoolingRegressionTests
{
    [Fact]
    public void UsingAggregation_PreservesFirstOccurrenceOrderAndIndependentSnapshots()
    {
        var tree = AkburaSyntaxTree.ParseText("using System; using System.Linq; using System; " +
            "using Alias = System.Text.StringBuilder;", "Usings.akbura");
        var csharp = CreateCSharp("global using System.Collections.Generic;");
        var model = new AkburaCompilation(csharp, [tree]).GetSemanticModel(tree);
        var before = model.GetCSharpUsingDirectivesCore();
        var expected = before.Select(static directive => directive.ToString()).ToArray();
        Assert.Equal(["using System.Collections.Generic;", "usingSystem;", "usingSystem.Linq;",
            "usingAlias=System.Text.StringBuilder;"], expected);

        for (var iteration = 0; iteration < 16; iteration++)
        {
            ContaminateThenReleaseStringStorage();
            Assert.Equal(expected, model.GetCSharpUsingDirectivesCore().Select(static directive => directive.ToString()));
            Assert.Equal(expected, before.Select(static directive => directive.ToString()));
        }
    }

    [Fact]
    public void MetadataDependencies_KeepOrdinalSortingInheritedDuplicatesAndInvalidNames()
    {
        var compilation = CreateCSharp("""
            public class Base
            {
                public int First { get; set; }
                [Avalonia.Metadata.DependsOn("First")]
                [Avalonia.Metadata.DependsOn("Missing")]
                public virtual int Value { get; set; }
            }
            public class Derived : Base
            {
                public int Second { get; set; }
                [Avalonia.Metadata.DependsOn("Second")]
                [Avalonia.Metadata.DependsOn("First")]
                [Avalonia.Metadata.DependsOn("first")]
                public override int Value { get; set; }
            }
            """);
        var property = compilation.GetTypeByMetadataName("Derived")!.GetMembers("Value").Single();
        for (var iteration = 0; iteration < 16; iteration++)
        {
            ContaminateThenReleaseStringStorage();
            var metadata = new MarkupPropertyMetadataReader().GetMetadata(property);
            Assert.Equal(["First", "Second"], metadata.Dependencies.Select(static symbol => symbol.Name));
            Assert.Equal<string>(["Missing", "first"], metadata.InvalidDependencies);
        }
    }

    [Fact]
    public void CancelledComponentCompletion_DoesNotPublishPartialResultsOrContaminateNextQuery()
    {
        var tree = AkburaSyntaxTree.ParseText("using Avalonia.Controls; <Border />", "Completion.akbura");
        var model = new AkburaCompilation(CreateCSharp("namespace Extra; public class Candidate : Avalonia.Controls.Border { }"),
            [tree]).GetSemanticModel(tree);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => model.LookupMarkupComponentCompletionImports(cancellation.Token));
        var result = model.LookupMarkupComponentCompletionImports();
        Assert.Contains(result, static candidate => candidate.NamespaceName == "Extra" && candidate.TypeDisplay.Contains("Candidate"));
        Assert.True(result == model.LookupMarkupComponentCompletionImports());
        ContaminateThenReleaseStringStorage();
        Assert.True(result == model.LookupMarkupComponentCompletionImports());
    }

    [Fact]
    public void ProbeScopes_KeepExcludedNamesAndRemainIndependentDuringParallelCompilation()
    {
        var compilation = CreateCSharp();
        Parallel.For(0, 16, index =>
        {
            var tree = AkburaSyntaxTree.ParseText($"state int value{index} = 1; state int other{index} = 2; value{index} = other{index};",
                $"Scope{index}.akbura");
            var model = new AkburaCompilation(compilation, [tree]).GetSemanticModel(tree);
            var syntax = tree.GetRoot().Members.Last();
            var binder = model.BindingSession.GetCSharpProbeBinder(syntax);
            var expression = SyntaxFactory.ParseExpression($"value{index} + other{index} + value{index}");
            var before = binder.CreateProbeScope(syntax, expression);
            var expected = before.LocalStatements.Select(static declaration => declaration.ToFullString()).ToArray();
            Assert.Empty(before.MemberDeclarations);
            Assert.Equal(2, expected.Length);
            for (var repeat = 0; repeat < 16; repeat++)
            {
                var scope = binder.CreateProbeScope(syntax, expression);
                Assert.Equal(expected, scope.LocalStatements.Select(static declaration => declaration.ToFullString()));
                var excluded = binder.CreateProbeScope(syntax, expression, [$"other{index}"]);
                Assert.Single(excluded.LocalStatements);
                Assert.Equal(expected, before.LocalStatements.Select(static declaration => declaration.ToFullString()));
            }
        });
    }

    private static void ContaminateThenReleaseStringStorage()
    {
        var names = PooledHashSet<string>.GetInstance();
        try
        {
            for (var index = 0; index < 4096; index++) names.Add("unrelated-name-" + index);
            names.Remove("unrelated-name-3");
            names.Add("unrelated-name-3");
        }
        finally { names.Free(); }
    }

    private static CSharpCompilation CreateCSharp(string source = "") => CSharpCompilation.Create("HashSetRegression",
        source.Length == 0 ? [] : [CSharpSyntaxTree.ParseText(source)], SymbolTests.CreateAvaloniaReferences(),
        new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
