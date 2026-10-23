using Akbura.Collections;
using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConcurrentCacheMeasurements
{
    public const string Name = "ConcurrentCache measurements";
}

// Full operations are measured. Performance numbers are observational;
// assertions verify cache behavior rather than timing thresholds.
[Collection(ConcurrentCacheMeasurements.Name)]
public sealed class ConcurrentCacheAllocationTests(ITestOutputHelper output)
{
    private static int s_sink;

    [Fact]
    public void MeasureCacheSizesAndDistributions()
    {
        output.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; CPU={Environment.ProcessorCount}");
        foreach (var workingSet in new[] { 1, 8, 32, 128, 1024 })
        foreach (var distribution in new[] { "sequential", "hot", "collisions" })
        {
            var comparer = distribution == "collisions"
                ? (IEqualityComparer<int>)new ConcurrentCacheTests.ConstantHashComparer<int>(EqualityComparer<int>.Default)
                : EqualityComparer<int>.Default;
            var keys = Enumerable.Range(0, Math.Max(256, workingSet)).Select(index => distribution == "hot" && index % 10 != 0
                ? 0 : index % workingSet).ToArray();
            var scenarios = new[] { 0, 16, 32, 64, 128, 256 }.Select(size =>
                new Scenario<int, Value>(size, comparer, static key => new Value(key))).ToArray();
            Measure(scenarios.Select(scenario => $"{distribution}-ws{workingSet}-size{scenario.Size}").ToArray(),
                scenarios.Select(scenario => (Func<int>)(() =>
                {
                    var sum = 0;
                    foreach (var key in keys) sum ^= scenario.Get(key).Key;
                    return sum;
                })).ToArray(), scenarios.Select(scenario => (Func<int>)(() => scenario.TakeFactories())).ToArray(),
                iterations: 128, operationsPerIteration: keys.Length);
        }
    }

    [Fact]
    public void MeasureActualFactoriesAndCandidateSizes()
    {
        var compilation = CreateCompilation();
        var readMetadata = typeof(MarkupPropertyMetadataReader).GetMethod("ReadMetadata", BindingFlags.Static | BindingFlags.NonPublic)!
            .CreateDelegate<Func<ISymbol, MarkupPropertyMetadata>>();
        var properties = compilation.GetTypeByMetadataName("Inputs")!.GetMembers().OfType<IPropertySymbol>()
            .Where(static property => property.Name.StartsWith("P", StringComparison.Ordinal)).Cast<ISymbol>().ToArray();
        var statements = AkburaSyntaxTree.ParseText(string.Concat(Enumerable.Range(0, 128)
            .Select(static index => $"int Method{index}() => {index};\r\n"))).GetRoot().Members.OfType<CSharpStatementSyntax>().ToArray();
        Assert.Equal(128, statements.Length);
        foreach (var workingSet in new[] { 1, 8, 32, 128 })
        {
            var metadata = new[] { 0, 16, 32, 64, 128, 256 }.Select(size =>
                new Scenario<ISymbol, MarkupPropertyMetadata>(size, SymbolEqualityComparer.Default, readMetadata)).ToArray();
            Measure(metadata.Select(scenario => $"metadata-ws{workingSet}-size{scenario.Size}").ToArray(),
                metadata.Select(scenario => (Func<int>)(() =>
                {
                    var sum = 0;
                    for (var index = 0; index < workingSet; index++) sum += scenario.Get(properties[index]).Dependencies.Length;
                    return sum;
                })).ToArray(), metadata.Select(scenario => (Func<int>)(() => scenario.TakeFactories())).ToArray(),
                iterations: 128, operationsPerIteration: workingSet);

            var methods = new[] { 0, 16, 32, 64, 128, 256 }.Select(size =>
                new Scenario<CSharpStatementSyntax, ParsedMethod>(size, EqualityComparer<CSharpStatementSyntax>.Default,
                    static statement => new ParsedMethod(SyntaxFactory.ParseMemberDeclaration(statement.ToFullString())))).ToArray();
            Measure(methods.Select(scenario => $"method-ws{workingSet}-size{scenario.Size}").ToArray(),
                methods.Select(scenario => (Func<int>)(() =>
                {
                    var sum = 0;
                    for (var index = 0; index < workingSet; index++) sum += scenario.Get(statements[index]).Node!.RawKind;
                    return sum;
                })).ToArray(), methods.Select(scenario => (Func<int>)(() => scenario.TakeFactories())).ToArray(),
                iterations: 128, operationsPerIteration: workingSet);
        }
    }

    [Fact]
    public void MeasureCompilerOperations()
    {
        var csharp = CreateCompilation();
        var resolver = new AkburaModuleTypeResolver(csharp);
        var tree = AkburaSyntaxTree.ParseText("using Avalonia.Controls; void First() {} int Second() => 2; <Border />", "Cache.akbura");
        var styles = AkcssSyntaxTree.ParseText("@using Avalonia.Controls; Border.seed { Width: 12; }", "Styles.akcss");
        var compilation = new AkburaCompilation(csharp, [tree], [styles]);
        var model = compilation.GetSemanticModel(tree);
        var context = model.BindingSession.ProbeContext;
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot().Members[0]);
        var statement = tree.GetRoot().Members.OfType<CSharpStatementSyntax>().First();
        var names = new[] { styles.LogicalName, "missing" };
        var typeNames = new[] { "System.Int32", "Inputs", "System.Collections.Generic.List<Inputs>", "missing-type" };
        Measure(["resolve-metadata", "resolve-generic", "resolve-missing", "reference-owner", "template", "method-body", "method-signature", "captured-function", "module-hit", "module-negative"], [
            () => resolver.Resolve(typeNames[0]).Name.Length,
            () => resolver.Resolve(typeNames[2]).Name.Length,
            () => resolver.Resolve(typeNames[3]).Name.Length,
            () => model.ResolveMarkupReferenceOwner("Avalonia.Controls.Border")?.Name.Length ?? 0,
            () => context.CreateComponentCompilationUnit(binder, [], "Host", default).FullSpan.Length,
            () => context.GetMethodBody(statement)?.FullSpan.Length ?? 0,
            () => context.GetMethodSignature(statement)?.FullSpan.Length ?? 0,
            () => context.GetCapturedFunction(statement)?.FullSpan.Length ?? 0,
            () => compilation.LookupAkcssModulesByLogicalName(names[0]).Length,
            () => compilation.LookupAkcssModulesByLogicalName(names[1]).Length,
        ], null, 4096);
        Measure(["cold-context", "cold-resolver"], [
            () => new CSharpProbeContext(model).CreateComponentCompilationUnit(binder, [], "Host", default).FullSpan.Length,
            () => new AkburaModuleTypeResolver(csharp).Resolve("System.Int32").Name.Length,
        ], null, 256);
        Measure(["cold-module-lookup"], [
            () => new AkburaCompilation(csharp, [tree], [styles]).LookupAkcssModulesByLogicalName("missing").Length,
        ], null, 1);
        var distinctNames = Enumerable.Range(0, 256).Select(static index => "missing-module-" + index).ToArray();
        Measure(["module-mixed"], [() =>
        {
            var sum = 0;
            foreach (var name in distinctNames) sum += compilation.LookupAkcssModulesByLogicalName(name).Length;
            sum += compilation.LookupAkcssModulesByLogicalName(names[0]).Length;
            return sum;
        }], null, 128, distinctNames.Length + 1);
        foreach (var count in new[] { 4, 512 })
        {
            var diagnostics = Enumerable.Range(0, count).Select(static index => new AkburaDiagnosticRecord
            {
                Id = "CACHE_MEASURE_" + index, Severity = AkburaDiagnosticSeverity.Warning,
                Message = "message", FilePath = "", Span = default, LineSpan = default,
            }).ToArray();
            Measure(["diagnostics-" + count], [() =>
            {
                var sum = 0;
                foreach (var diagnostic in diagnostics) sum ^= AkburaDiagnosticAdapter.ToRoslyn(diagnostic).Descriptor.Id.Length;
                return sum;
            }], null, 16, count);
        }
    }

    [Fact]
    public void MeasureTemplateCacheSizes()
    {
        var tree = AkburaSyntaxTree.ParseText("using Avalonia.Controls; void Method() {} <Border />", "Sizes.akbura");
        var styles = AkcssSyntaxTree.ParseText(".seed { }", "Styles.akcss");
        var compilation = new AkburaCompilation(CreateCompilation(), [tree], [styles]);
        var model = compilation.GetSemanticModel(tree);
        var context = model.BindingSession.ProbeContext;
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot().Members[0]);
        var templateField = typeof(CSharpProbeContext).GetField("_componentTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        foreach (var workingSet in new[] { 1, 4, 16, 32 })
        {
            var sizes = new[] { 16, 32, 64, 128, 256 };
            var templates = sizes.Select(size => new ConcurrentCache<string, CSharp.CompilationUnitSyntax>(size, StringComparer.Ordinal)).ToArray();
            var keys = Enumerable.Range(0, 256).Select(index => index % workingSet == 0
                ? styles.LogicalName : "missing-" + index % workingSet).ToArray();
            Measure(sizes.Select(size => $"template-profile-ws{workingSet}-size{size}").ToArray(),
                sizes.Select((size, index) => (Func<int>)(() =>
                {
                    templateField.SetValue(context, templates[index]);
                    var sum = 0;
                    foreach (var key in keys) sum += context.CreateComponentCompilationUnit(binder, [], key, default).FullSpan.Length;
                    return sum;
                })).ToArray(), null, 16, keys.Length);
        }
    }

    [Fact]
    public void MeasureColdRetentionAndParallelThroughput()
    {
        foreach (var size in new[] { 0, 16, 32, 64, 128, 256 })
        {
            Measure([$"cold-size{size}"], [() =>
            {
                var scenario = new Scenario<int, Value>(size, EqualityComparer<int>.Default, static key => new Value(key));
                return scenario.Get(1).Key;
            }], null, 4096);
            var (owner, references) = CreateRetainedValues(size);
            var retainedHeap = GC.GetTotalMemory(true);
            var live = references.Count(static reference => reference.IsAlive);
            output.WriteLine($"RETAIN size={size}; values={live}; payloadBytes={live * 1024}; heap={retainedHeap}");
            GC.KeepAlive(owner);
        }
        foreach (var size in new[] { 0, 32, 256 })
        foreach (var collisions in new[] { false, true })
        {
            var scenario = new Scenario<int, Value>(size, collisions
                ? new ConcurrentCacheTests.ConstantHashComparer<int>(EqualityComparer<int>.Default)
                : EqualityComparer<int>.Default, static key => new Value(key));
            Parallel.For(0, 1024, index => scenario.Get(index % 32));
            scenario.TakeFactories();
            var bytes = GC.GetTotalAllocatedBytes(true);
            var gen0 = GC.CollectionCount(0);
            var started = Stopwatch.GetTimestamp();
            Parallel.For(0, 32768, index => Assert.Equal(index % 32, scenario.Get(index % 32).Key));
            var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            output.WriteLine($"PARALLEL size={size}; collisions={collisions}; B/op={(GC.GetTotalAllocatedBytes(true) - bytes) / 32768.0:F2}; " +
                $"ns/op={elapsed / 32768:F1}; factories={scenario.TakeFactories()}; gen0={GC.CollectionCount(0) - gen0}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (object Owner, WeakReference[] Values) CreateRetainedValues(int size)
    {
        var scenario = new Scenario<int, byte[]>(size, EqualityComparer<int>.Default, static _ => new byte[1024]);
        var references = new WeakReference[4096];
        for (var index = 0; index < references.Length; index++) references[index] = new WeakReference(scenario.Get(index));
        return (scenario, references);
    }

    private void Measure(string[] names, Func<int>[] operations, Func<int>[]? factories, int iterations, int operationsPerIteration = 1)
    {
        var samples = new List<(double Bytes, double Ns, int Factories, int Gen0)>[operations.Length];
        for (var index = 0; index < operations.Length; index++)
        {
            samples[index] = [];
            for (var warmup = 0; warmup < 16; warmup++) s_sink ^= operations[index]();
            factories?[index]();
        }
        for (var round = 0; round < 5; round++)
        for (var position = 0; position < operations.Length; position++)
        {
            var index = (round + position) % operations.Length;
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            factories?[index]();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var gen0 = GC.CollectionCount(0);
            var started = Stopwatch.GetTimestamp();
            var sum = 0;
            for (var iteration = 0; iteration < iterations; iteration++) sum ^= operations[index]();
            var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
            var allocated = GC.GetAllocatedBytesForCurrentThread() - bytes;
            var calls = factories?[index]() ?? -1;
            s_sink ^= sum;
            var count = iterations * operationsPerIteration;
            samples[index].Add((allocated / (double)count, elapsed / count, calls, GC.CollectionCount(0) - gen0));
        }
        for (var index = 0; index < operations.Length; index++)
        {
            var sorted = samples[index].OrderBy(static sample => sample.Ns).ToArray();
            output.WriteLine($"MEASURE {names[index]}; B/op={samples[index].Average(static sample => sample.Bytes):F2}; " +
                $"ns/op={sorted[2].Ns:F1}; range={sorted[0].Ns:F1}..{sorted[^1].Ns:F1}; " +
                $"factories={samples[index].Sum(static sample => sample.Factories)}; gen0={samples[index].Sum(static sample => sample.Gen0)}; " +
                $"operations=5x{iterations * operationsPerIteration}");
        }
    }

    private sealed class Scenario<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentCache<TKey, TValue>? _cache;
        private readonly ConcurrentDictionary<TKey, TValue>? _dictionary;
        private readonly Func<TKey, TValue> _factory;
        private int _factories;
        public readonly int Size;
        public Scenario(int size, IEqualityComparer<TKey> comparer, Func<TKey, TValue> factory)
        {
            Size = size;
            if (size == 0) _dictionary = new(comparer);
            else _cache = new(size, comparer);
            _factory = factory;
        }
        public TValue Get(TKey key)
        {
            if (_dictionary != null)
            {
                if (_dictionary.TryGetValue(key, out var cached)) return cached;
                Interlocked.Increment(ref _factories);
                return _dictionary.GetOrAdd(key, _factory(key));
            }
            if (_cache!.TryGetValue(key, out var value)) return value;
            Interlocked.Increment(ref _factories);
            value = _factory(key);
            _cache.TryAdd(key, value);
            return value;
        }
        public int TakeFactories() => Interlocked.Exchange(ref _factories, 0);
    }

    private sealed record Value(int Key);
    private sealed record ParsedMethod(SyntaxNode? Node);

    private static CSharpCompilation CreateCompilation() => CSharpCompilation.Create("CacheMeasurements",
        [CSharpSyntaxTree.ParseText("public class Inputs { public object Anchor { get; set; } " +
            string.Concat(Enumerable.Range(0, 128).Select(static index =>
                $"[Avalonia.Metadata.DependsOn(\"Anchor\")] public object P{index} {{ get; set; }} ")) + "}")],
        SymbolTests.CreateAvaloniaReferences(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
}
