using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Xunit.Abstractions;

namespace Akbura.UnitTests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ConcurrentDictionaryMeasurements
{
    public const string Name = "Concurrent dictionary lifecycle measurements";
}

// Measurements use the existing test runner and output. Timings are observations,
// not pass/fail thresholds; run this class alone when comparing versions.
[Collection(ConcurrentDictionaryMeasurements.Name)]
public sealed class ConcurrentDictionaryAllocationTests
{
    private readonly ITestOutputHelper _output;

    public ConcurrentDictionaryAllocationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void MeasureCompleteDictionaryLifecycles()
    {
        _output.WriteLine($"{RuntimeInformation.FrameworkDescription}; {RuntimeInformation.OSDescription}; " +
            $"CPU={Environment.ProcessorCount}; ServerGC={System.Runtime.GCSettings.IsServerGC}");
        foreach (var workers in new[] { 1, 4 })
        foreach (var count in new[] { 0, 1, 4, 16, 64, 256, 1024 })
        {
            var keys = Enumerable.Range(0, count).Select(static index => "key-" + index).ToArray();
            var pool = PooledConcurrentDictionary<string, byte>.CreatePool(StringComparer.Ordinal);
            var owner = new ConcurrentDictionary<string, byte>(PooledConcurrentDictionary<string, byte>.ConcurrencyLevel,
                PooledConcurrentDictionary<string, byte>.InitialCapacity, StringComparer.Ordinal);
            Func<int>[] variants =
            [
                () => PopulateAndSnapshot(new ConcurrentDictionary<string, byte>(StringComparer.Ordinal), keys, workers),
                () =>
                {
                    using var dictionary = PooledConcurrentDictionary<string, byte>.GetInstanceFromPool(pool);
                    return PopulateAndSnapshot(dictionary, keys, workers);
                },
                () =>
                {
                    try { return PopulateAndSnapshot(owner, keys, workers); }
                    finally { owner.Clear(); }
                },
            ];
            MeasureVariants(["original", "pooled", "owner-local"], variants, count, workers,
                workers == 1 ? Math.Max(128, 32768 / (count + 1)) : 128);
        }
    }

    private static int PopulateAndSnapshot(ConcurrentDictionary<string, byte> dictionary, string[] keys, int workers)
    {
        if (workers == 1)
        {
            for (var index = 0; index < keys.Length; index++)
            {
                dictionary.TryAdd(keys[index], 0);
                dictionary.TryAdd(keys[index], 0);
            }
        }
        else
        {
            Parallel.For(0, workers, worker =>
            {
                for (var index = worker; index < keys.Length; index += workers)
                {
                    dictionary.TryAdd(keys[index], 0);
                    dictionary.TryAdd(keys[index], 0);
                }
            });
        }

        return dictionary.ToArray().Length;
    }

    [Fact]
    public void MeasureBindingDiagnosticBagLifecycle()
    {
        var syntax = AkburaSyntaxTree.ParseText("state int value = 0;", "Measure.akbura").GetRoot().Members[0];
        var descriptor = new DiagnosticDescriptor("MEASURE", "Title", "Message {0}", "Test", DiagnosticSeverity.Error, true);
        foreach (var count in new[] { 0, 4, 16, 64 })
        {
            var semantic = Enumerable.Range(0, count).Select(index =>
                new AkburaSemanticDiagnostic(syntax, ErrorCodes.ERR_SyntaxError, [index.ToString()])).ToArray();
            var csharp = Enumerable.Range(0, count).Select(index =>
                Diagnostic.Create(descriptor, Microsoft.CodeAnalysis.Location.None, index)).ToArray();
            MeasureVariants(["binding-bag"], [() =>
            {
                var bag = BindingDiagnosticBag.GetInstance();
                try
                {
                    for (var index = 0; index < count; index++)
                    {
                        bag.Add(semantic[index]);
                        bag.Add(semantic[index]);
                        bag.AddCSharp(csharp[index]);
                        bag.AddCSharp(csharp[index]);
                    }
                    var snapshot = bag.ToReadOnly();
                    return snapshot.SemanticDiagnostics.Length + snapshot.CSharpDiagnostics.Length;
                }
                finally { bag.Free(); }
            }], count * 2, 1, 256);
        }
    }

    private void MeasureVariants(string[] names, Func<int>[] variants, int count, int workers, int iterations)
    {
        var samples = variants.Select(_ => new List<(double Ns, double Bytes, int Gen0)>()).ToArray();
        for (var index = 0; index < variants.Length; index++)
        {
            for (var warmup = 0; warmup < 64; warmup++)
            {
                Assert.Equal(count, variants[index]());
            }
        }
        for (var round = 0; round < 7; round++)
        for (var step = 0; step < variants.Length; step++)
        {
            var index = (round + step) % variants.Length;
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var before = workers == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true);
            var gen0 = GC.CollectionCount(0);
            var start = Stopwatch.GetTimestamp();
            var result = 0;
            for (var operation = 0; operation < iterations; operation++)
            {
                result += variants[index]();
            }
            var ticks = Stopwatch.GetTimestamp() - start;
            var bytes = workers == 1 ? GC.GetAllocatedBytesForCurrentThread() : GC.GetTotalAllocatedBytes(true);
            samples[index].Add((ticks * (1e9 / Stopwatch.Frequency) / iterations,
                (bytes - before) / (double)iterations, GC.CollectionCount(0) - gen0));
            Assert.Equal(count * iterations, result);
        }
        for (var index = 0; index < variants.Length; index++)
        {
            var data = samples[index];
            _output.WriteLine($"{names[index]}; count={count}; workers={workers}; n={iterations}; " +
                $"ns={data.Select(static s => s.Ns).Order().ElementAt(3):F1}; " +
                $"B/op={data.Select(static s => s.Bytes).Order().ElementAt(3):F1}; " +
                $"Gen0/batch={data.Select(static s => s.Gen0).Order().ElementAt(3)}; " +
                $"ns-range={data.Min(static s => s.Ns):F1}..{data.Max(static s => s.Ns):F1}");
        }
    }

    [Fact]
    public void MeasureRetainedMemoryAfterManyOwnersAndLargeWorkloads()
    {
        foreach (var size in new[] { 16, 16384 })
        foreach (var pooled in new[] { false, true })
        {
            var bytes = MeasureRetainedOwners(pooled, size);
            _output.WriteLine($"retained; pooled={pooled}; owners=128; count={size}; bytes={bytes}");
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static long MeasureRetainedOwners(bool pooled, int size)
    {
        var before = GC.GetTotalMemory(true);
        var retained = CreateRetainedOwners(pooled, size);
        var bytes = GC.GetTotalMemory(true) - before;
        GC.KeepAlive(retained);
        return bytes;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object CreateRetainedOwners(bool pooled, int size)
    {
        const int owners = 128;
        if (pooled)
        {
            var pool = PooledConcurrentDictionary<int, byte>.CreatePool();
            var dictionaries = Enumerable.Range(0, owners)
                .Select(_ => PooledConcurrentDictionary<int, byte>.GetInstanceFromPool(pool)).ToArray();
            foreach (var dictionary in dictionaries)
            {
                for (var key = 0; key < size; key++) dictionary.TryAdd(key, 0);
                dictionary.Free();
            }
            return pool;
        }

        var local = Enumerable.Range(0, owners).Select(_ => new ConcurrentDictionary<int, byte>(PooledConcurrentDictionary<int, byte>.ConcurrencyLevel,
                PooledConcurrentDictionary<int, byte>.InitialCapacity)).ToArray();
        foreach (var dictionary in local)
        {
            for (var key = 0; key < size; key++) dictionary.TryAdd(key, 0);
            dictionary.Clear();
        }
        return local;
    }
}
