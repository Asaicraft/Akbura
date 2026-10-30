using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.TestUtilities;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using static Akbura.TestUtilities.MetadataAkcssLookupFixture;

namespace Akbura.UnitTests;

public sealed class MetadataAkcssLookupContextTests
{
    [Theory]
    [InlineData("NoApply", 0)]
    [InlineData("EmptyApply", 0)]
    [InlineData("OneApply", 1)]
    [InlineData("MultipleModules", 1)]
    public void Initialization_BuildsOnlyOneLookupWhenResolutionIsRequired(string name, int constructions)
    {
        var compilation = Cases().Single(item => item.Name == name).Compilation;
        var modules = Modules(compilation);
        var marker = new CountingSymbol();
        var symbols = modules.SelectMany(module => module.AkcssSymbols).Append(marker).ToImmutableArray();
        var context = new MetadataAkcssLookupContext(symbols);
        Assert.Null(Lookup(context));
        foreach (var module in modules) module.InitializeOperations(compilation, context);
        Assert.Equal(constructions * 2, marker.MetadataNameReads);
        Assert.Equal(constructions != 0, Lookup(context) != null);
        var existing = Lookup(context);
        foreach (var module in modules) module.InitializeOperations(compilation, context);
        Assert.Same(existing, Lookup(context));
        Assert.Equal(constructions * 2, marker.MetadataNameReads);
        Assert.All(modules.SelectMany(module => module.AkcssSymbols).Cast<IMetadataAkcssSymbol>()
            .Where(symbol => !symbol.OperationAttributes.IsEmpty), symbol => Assert.NotEmpty(symbol.Operations));
    }

    [Fact]
    public void MultipleApplies_PreserveOperationOrderAndTargetIdentityAcrossModules()
    {
        var modules = Load("MultipleModules");
        var seed = modules[0].AkcssSymbols[0];
        var owner = modules[0].AkcssSymbols[1];
        var operations = owner.Operations.Cast<MetadataAkcssApplyOperation>().ToArray();
        Assert.Equal(new[] { 0, 1 }, operations.Select(operation => operation.Order));
        Assert.All(operations, operation => { Assert.Same(seed, Assert.Single(operation.AppliedSymbols)); Assert.False(operation.HasErrors); });
        Assert.Same(seed, Assert.Single(Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(modules[1].AkcssSymbols[0].Operations)).AppliedSymbols));
    }

    [Fact]
    public void DuplicateNames_PreserveCandidateOrderSameModulePreferenceAndAmbiguity()
    {
        var compilation = Cases().Single(item => item.Name == "Duplicates").Compilation;
        var modules = Modules(compilation);
        var symbols = modules.SelectMany(module => module.AkcssSymbols).ToImmutableArray();
        var context = new MetadataAkcssLookupContext(symbols);
        Assert.True(context.TryGetCandidates("Shared", out var candidates));
        Assert.Equal(new IAkcssSymbol[] { modules[0].AkcssSymbols[0], modules[1].AkcssSymbols[0], modules[3].AkcssSymbols[0], modules[3].AkcssSymbols[1] }, candidates);
        foreach (var module in modules) module.InitializeOperations(compilation, context);
        for (var i = 0; i < 2; i++)
        {
            var operation = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(modules[i].AkcssSymbols[1].Operations));
            Assert.Same(modules[i].AkcssSymbols[0], Assert.Single(operation.AppliedSymbols));
            Assert.False(operation.HasErrors);
        }
        foreach (var owner in new[] { modules[2].AkcssSymbols[0], modules[3].AkcssSymbols[2] })
        {
            var operation = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(owner.Operations));
            Assert.Empty(operation.AppliedSymbols);
            Assert.True(operation.HasErrors);
        }
    }

    [Fact]
    public void MissingReferences_PreservePartialResolutionRepeatedTargetsAndErrors()
    {
        var module = Assert.Single(Load("Missing"));
        foreach (var index in new[] { 1, 3 })
        {
            var operation = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(module.AkcssSymbols[index].Operations));
            Assert.Empty(operation.AppliedSymbols);
            Assert.True(operation.HasErrors);
        }
        var partial = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(module.AkcssSymbols[2].Operations));
        Assert.Equal(new[] { "Seed", "Missing", "Seed" }, partial.AppliedSymbolMetadataNames);
        Assert.Equal(2, partial.AppliedSymbols.Length);
        Assert.All(partial.AppliedSymbols, target => Assert.Same(module.AkcssSymbols[0], target));
        Assert.True(partial.HasErrors);
    }

    [Fact]
    public void NonApplyOperations_PreserveTreeAndEmptyApplyIsValid()
    {
        var module = Load("NoApply")[0];
        var operations = module.AkcssSymbols[0].Operations;
        Assert.Equal(2, operations.Length);
        var condition = Assert.IsType<MetadataAkcssIfOperation>(operations[0]);
        var setter = Assert.IsType<MetadataAkcssPropertySetterOperation>(Assert.Single(condition.Children));
        Assert.Same(condition, setter.Parent);
        Assert.IsType<MetadataAkcssInterceptOperation>(operations[1]);
        var empty = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(Load("EmptyApply")[0].AkcssSymbols[0].Operations));
        Assert.Empty(empty.AppliedSymbols);
        Assert.False(empty.HasErrors);
    }

    [Fact]
    public void IndependentLoadingPasses_ResolveToTheirOwnSymbols()
    {
        var reference = Emit(Cases().Single(item => item.Name == "MultipleModules").Compilation);
        var first = LoadedModules(Consumer(reference));
        var second = LoadedModules(Consumer(reference));
        Assert.NotSame(first[0], second[0]);
        Assert.NotSame(first[0].AkcssSymbols[0], second[0].AkcssSymbols[0]);
        foreach (var cohort in new[] { first, second })
        {
            var target = cohort[0].AkcssSymbols[0];
            var apply = Assert.IsType<MetadataAkcssApplyOperation>(Assert.Single(cohort[1].AkcssSymbols[0].Operations));
            Assert.Same(target, Assert.Single(apply.AppliedSymbols));
        }
    }

    [Fact]
    public void CompletedOperations_DoNotRetainTheLookupContext()
    {
        var fixture = InitializeAndReleaseContext();
        for (var attempt = 0; attempt < 10 && fixture.Context.IsAlive; attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(fixture.Context.IsAlive);
        Assert.NotEmpty(fixture.Modules[1].AkcssSymbols[0].Operations);
        GC.KeepAlive(fixture.Modules);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (WeakReference Context, MetadataAkcssModuleSymbol[] Modules) InitializeAndReleaseContext()
    {
        var compilation = Cases().Single(item => item.Name == "MultipleModules").Compilation;
        var modules = Modules(compilation);
        var context = new MetadataAkcssLookupContext(modules.SelectMany(module => module.AkcssSymbols).ToImmutableArray());
        foreach (var module in modules) module.InitializeOperations(compilation, context);
        return (new WeakReference(context), modules);
    }

    private static MetadataAkcssModuleSymbol[] Load(string name) => LoadedModules(Consumer(Emit(Cases().Single(item => item.Name == name).Compilation)));
    private static MetadataAkcssModuleSymbol[] LoadedModules(CSharpCompilation compilation) => AkburaReferencedModule.Load(compilation)
        .SelectMany(module => module.GetAkcssModuleNames().SelectMany(name => module.GetAkcssModuleSymbolsByLogicalName(name))).Cast<MetadataAkcssModuleSymbol>().ToArray();
    private static MetadataAkcssModuleSymbol[] Modules(CSharpCompilation compilation) => compilation.Assembly.GetAttributes().Select(attribute =>
    {
        var type = (Microsoft.CodeAnalysis.INamedTypeSymbol)attribute.ConstructorArguments[0].Value!;
        Assert.True(MetadataAkcssModuleSymbol.TryCreate(type, out var module));
        return module;
    }).ToArray();
    private static object? Lookup(MetadataAkcssLookupContext context) => typeof(MetadataAkcssLookupContext)
        .GetField("_lookup", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(context);

    private sealed class CountingSymbol : Symbol, IAkcssSymbol
    {
        public int MetadataNameReads { get; private set; }
        public override string MetadataName { get { MetadataNameReads++; return "CounterSentinel"; } }
        public override string Name => "CounterSentinel";
        public override Akbura.Language.Symbols.SymbolKind Kind => Akbura.Language.Symbols.SymbolKind.AkcssClass;
        public override SymbolLanguage Language => SymbolLanguage.Akcss;
        public AkburaSyntax? DeclarationSyntax => null;
        public ImmutableArray<IAkcssOperation> Operations => [];
        public string? ClassName => null;
        public bool HasTargetType => false;
        public CSharpSymbolDefinition TargetType => default;
        public bool IsIntercepted => false;
        public CSharpSymbolDefinition InterceptType => default;
    }
}
