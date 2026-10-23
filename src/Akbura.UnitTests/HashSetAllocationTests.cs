using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class HashSetMeasurements
{
    public const string Name = "HashSet lifecycle measurements";
}

// Run this collection alone in Release. Timing is observational; assertions
// verify results and ownership independently of measurement noise.
[Collection(HashSetMeasurements.Name)]
public sealed class HashSetAllocationTests
{
    private readonly ITestOutputHelper _output;

    public HashSetAllocationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MeasureSetLifecycles()
    {
        _output.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; " +
            $"CPU={Environment.ProcessorCount}; ServerGC={System.Runtime.GCSettings.IsServerGC}");
        foreach (var count in new[] { 0, 1, 4, 16, 64, 256, 1024, 4096 })
        {
            var keys = Enumerable.Range(0, count).Select(static index => "key-" + index).ToArray();
            var ordinalPool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal);
            var ignoreCasePool = PooledHashSet<string>.CreatePool(StringComparer.OrdinalIgnoreCase);
            Measure(["new-" + count, "pooled-default-" + count, "pooled-ordinal-" + count, "pooled-ignorecase-" + count], [
                () => Populate(new HashSet<string>(StringComparer.Ordinal), keys),
                () =>
                {
                    var set = PooledHashSet<string>.GetInstance();
                    try { return Populate(set, keys); }
                    finally { set.Free(); }
                },
                () =>
                {
                    var set = ordinalPool.Allocate();
                    try { return Populate(set, keys); }
                    finally { set.Free(); }
                },
                () =>
                {
                    var set = ignoreCasePool.Allocate();
                    try { return Populate(set, keys); }
                    finally { set.Free(); }
                },
            ], count, Math.Max(256, 65536 / (count + 1)));
        }
    }

    private static int Populate(HashSet<string> set, string[] keys)
    {
        foreach (var key in keys)
        {
            set.Add(key);
            set.Add(key);
            if (!set.Contains(key)) throw new InvalidOperationException();
        }
        if (keys.Length != 0)
        {
            set.Remove(keys[0]);
            set.Add(keys[0]);
        }
        return set.Count;
    }

    [Fact]
    public void MeasurePoolCapacityAndConcurrentLifecycles()
    {
        var keys = Enumerable.Range(0, 64).Select(static index => "key-" + index).ToArray();
        foreach (var capacity in new[] { 8, 16, 32, 128 })
        {
            foreach (var active in new[] { 8, 32 })
            {
                var pool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, capacity);
                var sets = new PooledHashSet<string>[active];
                Measure([$"capacity-{capacity}-burst-{active}"], [() =>
                {
                    var rented = 0;
                    var sum = 0;
                    try
                    {
                        for (var index = 0; index < active; index++)
                        {
                            sets[index] = pool.Allocate();
                            rented++;
                            sum += Populate(sets[index], keys);
                        }
                        return sum;
                    }
                    finally
                    {
                        for (var index = 0; index < rented; index++)
                        {
                            sets[index].Free();
                            sets[index] = null!;
                        }
                    }
                }], active * 64, 256, active);
            }
            var concurrentPool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, capacity);
            Measure([$"capacity-{capacity}-parallel"], [() =>
            {
                var total = 0;
                Parallel.For(0, 256, _ =>
                {
                    var set = concurrentPool.Allocate();
                    try { Interlocked.Add(ref total, Populate(set, keys)); }
                    finally { set.Free(); }
                });
                return total;
            }], 256 * 64, 32, 256, concurrent: true);
        }
    }

    [Fact]
    public void MeasureColdAndRetainedStorage()
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        var cold = PooledHashSet<ColdKey>.GetInstance();
        try { cold.Add(new ColdKey(42)); }
        finally { cold.Free(); }
        _output.WriteLine($"cold-first-generic: {Stopwatch.GetElapsedTime(start).TotalNanoseconds:F1} ns, " +
            $"{GC.GetAllocatedBytesForCurrentThread() - before} B (includes static initialization/JIT)");

        foreach (var size in new[] { 64, 4096, 65536 })
        foreach (var capacity in new[] { 8, 16, 32, 128 })
        {
            var (alive, delta) = MeasureRetainedBurst(capacity, size);
            _output.WriteLine($"retention capacity={capacity}, size={size}, burst=128: " +
                $"alive={alive}, managed-delta={delta} B");
            Assert.Equal(size <= PooledHashSet<int>.MaxPooledCount ? capacity : 0, alive);
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (int Alive, long Delta) MeasureRetainedBurst(int capacity, int size)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var memory = GC.GetTotalMemory(true);
        var (pool, references) = RentBurstAndRelease(capacity, size);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        var alive = references.Count(static reference => reference.IsAlive);
        var delta = GC.GetTotalMemory(true) - memory;
        GC.KeepAlive(pool);
        return (alive, delta);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (ObjectPool<PooledHashSet<int>> Pool, WeakReference[] References) RentBurstAndRelease(int capacity, int size)
    {
        var pool = PooledHashSet<int>.CreatePool(EqualityComparer<int>.Default, capacity);
        var sets = new PooledHashSet<int>[128];
        var references = new WeakReference[sets.Length];
        for (var index = 0; index < sets.Length; index++)
        {
            sets[index] = pool.Allocate();
            for (var value = 0; value < size; value++) sets[index].Add(value);
            references[index] = new WeakReference(sets[index]);
        }
        foreach (var set in sets) set.Free();
        return (pool, references);
    }

    [Fact]
    public void MeasureSmallSetsAfterLargeRental()
    {
        var keys = Enumerable.Range(0, 16).Select(static index => "small-" + index).ToArray();
        var originalPool = new ObjectPool<HashSet<string>>(
            static () => new HashSet<string>(StringComparer.Ordinal), 128);
        var boundedPool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal);
        var original = originalPool.Allocate();
        var bounded = boundedPool.Allocate();
        for (var index = 0; index < 65536; index++)
        {
            var key = "large-" + index;
            original.Add(key);
            bounded.Add(key);
        }
        original.Clear();
        originalPool.Free(original);
        bounded.Free();
        Measure(["small-new", "small-original-pool-after-large", "small-bounded-pool-after-large"], [
            () => Populate(new HashSet<string>(StringComparer.Ordinal), keys),
            () =>
            {
                var set = originalPool.Allocate();
                try { return Populate(set, keys); }
                finally { set.Clear(); originalPool.Free(set); }
            },
            () =>
            {
                var set = boundedPool.Allocate();
                try { return Populate(set, keys); }
                finally { set.Free(); }
            },
        ], keys.Length, 4096);
    }

    [Fact]
    public void MeasureRealAkburaOperations()
    {
        var references = SymbolTests.CreateAvaloniaReferences();
        var csharp = CSharpCompilation.Create("SetWorkloads", references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var tree = AkburaSyntaxTree.ParseText("using System; using System.Linq; using System; " +
            "state int alpha = 1; state int beta = 2; alpha = beta;", "SetWorkloads.akbura");
        var model = new AkburaCompilation(csharp, [tree]).GetSemanticModel(tree);
        var scope = tree.GetRoot().Members.Last();
        var binder = model.BindingSession.GetCSharpProbeBinder(scope);
        var expression = SyntaxFactory.ParseExpression("alpha + beta + alpha");
        var expected = binder.CreateProbeScope(scope, expression);
        Measure(["probe-scope"], [() =>
        {
            var result = binder.CreateProbeScope(scope, expression);
            return result.MemberDeclarations.Length + result.LocalStatements.Length;
        }], expected.MemberDeclarations.Length + expected.LocalStatements.Length, 4096);
        Measure(["using-aggregation"], [() => model.GetCSharpUsingDirectivesCore().Length],
            model.GetCSharpUsingDirectivesCore().Length, 8192);

        foreach (var count in new[] { 0, 4, 32 })
        {
            var members = string.Join(" ", Enumerable.Range(0, count).Select(static index => $"public int P{index} {{ get; set; }}"));
            var attributes = string.Concat(Enumerable.Range(0, count).Select(static index => $"[Avalonia.Metadata.DependsOn(\"P{index}\")][Avalonia.Metadata.DependsOn(\"P{index}\")][Avalonia.Metadata.DependsOn(\"Missing{index}\")]"));
            var metadataCompilation = csharp.AddSyntaxTrees(CSharpSyntaxTree.ParseText($"public class Metadata {{ {members} {attributes} public int Value {{ get; set; }} }}"));
            var property = metadataCompilation.GetTypeByMetadataName("Metadata")!.GetMembers("Value").Single();
            Measure(["metadata-" + count], [() =>
            {
                var metadata = new MarkupPropertyMetadataReader().GetMetadata(property);
                return metadata.Dependencies.Length + metadata.InvalidDependencies.Length;
            }], count * 2, 1024);
        }
        foreach (var count in new[] { 0, 8, 128 })
        {
            var compilation = csharp.AddSyntaxTrees(Enumerable.Range(0, count).Select(static index =>
                CSharpSyntaxTree.ParseText($"namespace Demo; public class Tree{index} {{ }}", path: $"Tree{index}.cs")));
            var expectedCount = CSharpEnvironmentSnapshot.Create(compilation, []).Compilation.SyntaxTrees.Count();
            Measure(["environment-" + count], [() => CSharpEnvironmentSnapshot.Create(compilation, []).Compilation.SyntaxTrees.Count()],
                expectedCount, 256);
        }
    }

    private void Measure(string[] names, Func<int>[] variants, int expected, int iterations, int operationsPerIteration = 1, bool concurrent = false)
    {
        var samples = variants.Select(static _ => new List<(double Ns, double Bytes, int Gen0)>()).ToArray();
        foreach (var variant in variants)
            for (var warmup = 0; warmup < 32; warmup++) Assert.Equal(expected, variant());
        for (var round = 0; round < 7; round++)
        for (var step = 0; step < variants.Length; step++)
        {
            var index = (round + step) % variants.Length;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var bytes = concurrent ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread();
            var gen0 = GC.CollectionCount(0);
            var start = Stopwatch.GetTimestamp();
            var checksum = 0;
            for (var operation = 0; operation < iterations; operation++) checksum += variants[index]();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            var allocated = (concurrent ? GC.GetTotalAllocatedBytes(true) : GC.GetAllocatedBytesForCurrentThread()) - bytes;
            Assert.Equal(expected * iterations, checksum);
            var operations = (double)iterations * operationsPerIteration;
            samples[index].Add((elapsed / operations, allocated / operations, GC.CollectionCount(0) - gen0));
        }
        for (var index = 0; index < names.Length; index++)
        {
            var time = samples[index].Select(static sample => sample.Ns).Order().ToArray();
            var bytes = samples[index].Select(static sample => sample.Bytes).Order().ToArray();
            var gen0 = samples[index].Select(static sample => sample.Gen0).Order().ToArray();
            _output.WriteLine($"{names[index]}: {time[3]:F1} ns/op [{time[0]:F1}..{time[6]:F1}], " +
                $"{bytes[3]:F1} B/op [{bytes[0]:F1}..{bytes[6]:F1}], Gen0={gen0[3]}/{iterations * operationsPerIteration}");
        }
    }

    private readonly record struct ColdKey(int Value);
}
