using Akbura.Collections;
using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Collections.Concurrent;
using System.Reflection;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

[Collection(ConcurrentCacheMeasurements.Name)]
public sealed class ConcurrentCacheRegressionTests
{
    [Fact]
    public void ModuleLookup_PreservesCanonicalArraysDuplicateOrderAndNegativeResults()
    {
        var first = AkcssSyntaxTree.ParseText(".first { }", "First.akcss", "Shared.Styles.akcss");
        var second = AkcssSyntaxTree.ParseText(".second { }", "Second.akcss", "Shared.Styles.akcss");
        var compilation = new AkburaCompilation(CreateCSharp(), [], [first, second]);
        var cache = ModuleDictionary(compilation);
        var expected = compilation.LookupAkcssModulesByLogicalName(first.LogicalName);
        Assert.Equal(2, expected.Length);
        Assert.Same(first.GetRoot(), expected[0].DeclaringSyntax);
        Assert.Same(second.GetRoot(), expected[1].DeclaringSyntax);
        for (var repeat = 0; repeat < 32; repeat++)
        {
            Assert.Empty(compilation.LookupAkcssModulesByLogicalName("missing-" + repeat));
            Assert.True(cache.TryGetValue(first.LogicalName, out _));
            Assert.Empty(compilation.LookupAkcssModulesByLogicalName(first.LogicalName.ToLowerInvariant()));
            var actual = compilation.LookupAkcssModulesByLogicalName(first.LogicalName);
            Assert.True(expected == actual);
            Assert.Equal(expected.Length, actual.Length);
            for (var index = 0; index < actual.Length; index++) Assert.Same(expected[index], actual[index]);
            Assert.Same(first.GetRoot(), expected[0].DeclaringSyntax);
        }
        Assert.Empty(compilation.LookupAkcssModulesByLogicalName("missing"));
        Assert.True(cache.TryGetValue("missing", out var negative));
        Assert.False(negative.IsDefault);
        Assert.Empty(negative);
    }

    [Fact]
    public void ModuleLookup_CancellationNeverPublishesAnUnfinishedLookup()
    {
        var styles = AkcssSyntaxTree.ParseText(".seed { }", "Styles.akcss");
        var compilation = new AkburaCompilation(CreateCSharp(), [], [styles]);
        var cache = ModuleDictionary(compilation);
        var complete = compilation.LookupAkcssModulesByLogicalName(styles.LogicalName);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => compilation.LookupAkcssModulesByLogicalName(styles.LogicalName, cancellation.Token));
        Assert.Throws<OperationCanceledException>(() => compilation.LookupAkcssModulesByLogicalName("missing", cancellation.Token));
        Assert.False(cache.TryGetValue("missing", out _));
        Assert.True(cache.TryGetValue(styles.LogicalName, out _));
        // Empty names retain their existing early return, including when
        // cancellation was already requested.
        Assert.Empty(compilation.LookupAkcssModulesByLogicalName(" ", cancellation.Token));
        Assert.Same(complete[0], compilation.LookupAkcssModulesByLogicalName(styles.LogicalName)[0]);
    }

    [Fact]
    public void ModuleLookup_ParallelNamesKeepCompilationOwnership()
    {
        var styles = AkcssSyntaxTree.ParseText(".seed { }", "Styles.akcss");
        var first = new AkburaCompilation(CreateCSharp(), [], [styles]);
        var otherStyles = AkcssSyntaxTree.ParseText(".other { }", "Styles.akcss");
        var second = new AkburaCompilation(CreateCSharp(), [], [otherStyles]);
        var expectedFirst = Assert.Single(first.LookupAkcssModulesByLogicalName(styles.LogicalName));
        var expectedSecond = Assert.Single(second.LookupAkcssModulesByLogicalName(styles.LogicalName));
        Assert.NotSame(expectedFirst, expectedSecond);
        Parallel.For(0, 256, index =>
        {
            var owner = index % 2 == 0 ? first : second;
            Assert.Empty(owner.LookupAkcssModulesByLogicalName("missing-" + index));
            Assert.Same(index % 2 == 0 ? expectedFirst : expectedSecond,
                Assert.Single(owner.LookupAkcssModulesByLogicalName(styles.LogicalName)));
        });
    }

    [Fact]
    public void ComponentTemplates_EvictionPreservesMembersImportsTriviaAndDiagnostics()
    {
        var (model, binder) = CreateModel();
        var context = model.BindingSession.ProbeContext;
        var cache = ReplaceCache<string, CSharp.CompilationUnitSyntax>(context, "_componentTemplates", StringComparer.Ordinal);
        var members = ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(SyntaxFactory.ParseMemberDeclaration("void First() { } // first\r\n")!);
        var imports = ImmutableArray.Create(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")));
        var first = context.CreateComponentCompilationUnit(binder, members, "First", imports);
        var expected = Snapshot(first);
        for (var repeat = 0; repeat < 32; repeat++)
        {
            context.CreateComponentCompilationUnit(binder,
                [SyntaxFactory.ParseMemberDeclaration("int Different = 42;")!], "Other", []);
            Assert.False(cache.TryGetValue("First", out _));
            Assert.Equal(expected, Snapshot(context.CreateComponentCompilationUnit(binder, members, "First", imports)));
            Assert.Equal(expected, Snapshot(first));
        }
        Assert.Contains("First", first.ToFullString());
        Assert.DoesNotContain("Different", first.ToFullString());
    }

    [Fact]
    public void ComponentTemplates_ParallelContendersReturnIndependentCompleteSyntax()
    {
        var (model, binder) = CreateModel();
        var context = model.BindingSession.ProbeContext;
        ReplaceCache<string, CSharp.CompilationUnitSyntax>(context, "_componentTemplates", StringComparer.Ordinal);
        Parallel.For(0, 256, index =>
        {
            var member = SyntaxFactory.ParseMemberDeclaration($"int Value{index} = {index};")!;
            var imports = ImmutableArray.Create(SyntaxFactory.UsingDirective(SyntaxFactory.ParseName("System")));
            var first = context.CreateComponentCompilationUnit(binder, [member], "Host" + index, imports);
            var expected = Snapshot(first);
            for (var repeat = 0; repeat < 4; repeat++)
                Assert.Equal(expected, Snapshot(context.CreateComponentCompilationUnit(binder, [member], "Host" + index, imports)));
            Assert.Contains("Value" + index, first.ToFullString());
        });
    }

    [Theory]
    [InlineData("System.Collections.Generic.List<Inputs>")]
    [InlineData("Inputs[]")]
    [InlineData("System.Int32")]
    [InlineData("missing-type")]
    public void TypeResolver_RetainsCanonicalResultsAcrossOtherNamesAndParallelReads(string name)
    {
        var resolver = new AkburaModuleTypeResolver(CreateCSharp());
        var expected = resolver.Resolve(name);
        for (var index = 0; index < 256; index++) resolver.Resolve("missing-" + index);
        Parallel.For(0, 128, _ => Assert.Same(expected.Symbol, resolver.Resolve(name).Symbol));
    }

    [Fact]
    public void MetadataReader_KeepsCanonicalOrderedArraysWhileOtherPropertiesAreRead()
    {
        var compilation = CreateCSharp();
        var type = compilation.GetTypeByMetadataName("Inputs")!;
        var property = Assert.Single(type.GetMembers("Payload"));
        var reader = new MarkupPropertyMetadataReader();
        var expected = reader.GetMetadata(property);
        Assert.True(expected.IsContent);
        Assert.True(expected.AssignBinding);
        Assert.Equal(["Alpha", "Zeta"], expected.Dependencies.Select(static symbol => symbol.Name));
        Assert.Equal<string>(["Missing"], expected.InvalidDependencies);
        foreach (var member in type.GetMembers().OfType<IPropertySymbol>()) reader.GetMetadata(member);
        Assert.Equal(expected.Dependencies, reader.GetMetadata(property).Dependencies);
        Assert.Equal(expected.InvalidDependencies, reader.GetMetadata(property).InvalidDependencies);
    }

    [Fact]
    public void ReferenceOwner_NormalizesAliasesAndKeepsTypeIdentityAndMissingResults()
    {
        var (model, _) = CreateModel();
        var expected = model.ResolveMarkupReferenceOwner("Avalonia.Controls.Border");
        Assert.NotNull(expected);
        Assert.Same(expected, model.ResolveMarkupReferenceOwner("Avalonia|Controls|Border"));
        Assert.Null(model.ResolveMarkupReferenceOwner("avalonia.controls.border"));
        Parallel.For(0, 128, _ => Assert.Same(expected, model.ResolveMarkupReferenceOwner("Avalonia.Controls.Border")));
    }

    [Fact]
    public void DiagnosticDescriptors_KeepCanonicalIdentityAcrossManyOtherIds()
    {
        var record = new AkburaDiagnosticRecord
        {
            Id = "CACHE_CANONICAL", Severity = AkburaDiagnosticSeverity.Warning,
            Message = "message", FilePath = "", Span = default, LineSpan = default,
        };
        var expected = AkburaDiagnosticAdapter.ToRoslyn(record).Descriptor;
        for (var index = 0; index < 512; index++) AkburaDiagnosticAdapter.ToRoslyn(record with { Id = "CACHE_OTHER_" + index });
        Assert.Same(expected, AkburaDiagnosticAdapter.ToRoslyn(record).Descriptor);
    }

    private static ConcurrentDictionary<string, ImmutableArray<Akbura.Language.Symbols.IAkcssModuleSymbol>> ModuleDictionary(AkburaCompilation owner) =>
        (ConcurrentDictionary<string, ImmutableArray<Akbura.Language.Symbols.IAkcssModuleSymbol>>)typeof(AkburaCompilation)
            .GetField("_akcssModuleLookup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(owner)!;

    private static ConcurrentCache<TKey, TValue> ReplaceCache<TKey, TValue>(object owner, string field, IEqualityComparer<TKey> comparer)
        where TKey : notnull
    {
        var cache = new ConcurrentCache<TKey, TValue>(16, new ConcurrentCacheTests.ConstantHashComparer<TKey>(comparer));
        owner.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(owner, cache);
        return cache;
    }

    private static string Snapshot(SyntaxNode node) => node.ToFullString() + "\0" + string.Join("|",
        node.DescendantTokens(descendIntoTrivia: true).Select(static token => $"{token.RawKind}:{token.Span}:{token.Text}:{token.ValueText}")) +
        "\0" + string.Join("|", node.GetDiagnostics().Select(static diagnostic => diagnostic.ToString()));

    private static (AkburaSemanticModel Model, CSharpProbeBinder Binder) CreateModel()
    {
        var tree = AkburaSyntaxTree.ParseText("using Avalonia.Controls; void Method() {} <Border />", "Cache.akbura");
        var model = new AkburaCompilation(CreateCSharp(), [tree]).GetSemanticModel(tree);
        return (model, model.BindingSession.GetCSharpProbeBinder(tree.GetRoot().Members[0]));
    }

    private static CSharpCompilation CreateCSharp() => CSharpCompilation.Create("CacheRegression",
        [CSharpSyntaxTree.ParseText("public class Inputs { public object Alpha { get; set; } public object Zeta { get; set; } " +
            "[Avalonia.Metadata.Content, Avalonia.Data.AssignBinding, Avalonia.Metadata.DependsOn(\"Zeta\"), " +
            "Avalonia.Metadata.DependsOn(\"Alpha\"), Avalonia.Metadata.DependsOn(\"Missing\")] public object Payload { get; set; } " +
            string.Concat(Enumerable.Range(0, 256).Select(static index => $"public int P{index} {{ get; set; }} ")) + "}")],
        SymbolTests.CreateAvaloniaReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
