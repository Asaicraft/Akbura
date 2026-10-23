using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class PooledConcurrentDictionaryTests
{
    [Fact]
    public void RentFreeAndReuse_ResetContentsAndPreserveOrdinalComparer()
    {
        var pool = PooledConcurrentDictionary<string, object>.CreatePool(StringComparer.Ordinal);
        var first = PooledConcurrentDictionary<string, object>.GetInstanceFromPool(pool);
        Assert.Empty(first);
        var value = new object();
        Assert.True(first.TryAdd("key", value));
        Assert.True(first.TryAdd("KEY", new object()));
        Assert.False(first.TryAdd("key", new object()));
        Assert.True(first.TryGetValue("key", out var found));
        Assert.Same(value, found);
        first.Free();

        using var next = PooledConcurrentDictionary<string, object>.GetInstanceFromPool(pool);
        Assert.Same(first, next);
        Assert.Empty(next);
        Assert.Same(StringComparer.Ordinal, next.Comparer);
        Assert.True(next.TryAdd("key", new object()));
    }

    [Fact]
    public void SharedPools_PreserveDefaultAndOrdinalComparerAcrossRepeatedRentals()
    {
        for (var iteration = 0; iteration < 100; iteration++)
        {
            using var defaults = PooledConcurrentDictionary<string, int>.GetInstance();
            using var explicitDefault = PooledConcurrentDictionary<string, int>.GetInstance(null);
            using var ordinal = PooledConcurrentDictionary<string, int>.GetInstance(StringComparer.Ordinal);
            Assert.NotSame(defaults, explicitDefault);
            Assert.NotSame(defaults, ordinal);
            Assert.Empty(defaults);
            Assert.Empty(explicitDefault);
            Assert.Empty(ordinal);
            Assert.True(ordinal.TryAdd("name", iteration));
            Assert.True(ordinal.TryAdd("NAME", iteration));
            Assert.Equal(iteration, ordinal["name"]);
            Assert.Same(StringComparer.Ordinal, ordinal.Comparer);
        }
    }

    [Fact]
    public void CustomComparers_ArePreservedWithoutGlobalComparerPools()
    {
        using (var dictionary = PooledConcurrentDictionary<string, int>.GetInstance(StringComparer.OrdinalIgnoreCase))
        {
            Assert.True(dictionary.TryAdd("name", 1));
            Assert.False(dictionary.TryAdd("NAME", 2));
            Assert.Equal(1, dictionary["NAME"]);
            Assert.Same(StringComparer.OrdinalIgnoreCase, dictionary.Comparer);
        }

        var pool = PooledConcurrentDictionary<string, int>.CreatePool(StringComparer.OrdinalIgnoreCase);
        var first = PooledConcurrentDictionary<string, int>.GetInstanceFromPool(pool);
        first.TryAdd("first", 1);
        first.Dispose();
        using var next = PooledConcurrentDictionary<string, int>.GetInstanceFromPool(pool);
        Assert.Same(first, next);
        Assert.Empty(next);
        Assert.Same(StringComparer.OrdinalIgnoreCase, next.Comparer);
    }

    [Fact]
    public void ExplicitPool_SupportsSymbolComparers()
    {
        var compilation = CSharpCompilation.Create("Symbols", [CSharpSyntaxTree.ParseText("class C { }")]);
        var symbol = compilation.GetTypeByMetadataName("C")!;
        var pool = PooledConcurrentDictionary<ISymbol, int>.CreatePool(SymbolEqualityComparer.Default);
        using var dictionary = PooledConcurrentDictionary<ISymbol, int>.GetInstanceFromPool(pool);
        Assert.Same(SymbolEqualityComparer.Default, dictionary.Comparer);
        Assert.True(dictionary.TryAdd(symbol, 1));
        Assert.Equal(1, dictionary.GetOrAdd(symbol, 2));
    }

    [Fact]
    public void ConcurrentInsertionsAndGetOrAdd_PublishOneValuePerKey()
    {
        using var dictionary = PooledConcurrentDictionary<int, object>.GetInstance();
        var successes = 0;
        Parallel.For(0, 4096, index =>
        {
            if (dictionary.TryAdd(index % 256, new object()))
            {
                Interlocked.Increment(ref successes);
            }
        });
        Assert.Equal(256, successes);
        Assert.Equal(256, dictionary.Count);

        Parallel.For(256, 512, index => Assert.True(dictionary.TryAdd(index, new object())));
        Assert.Equal(512, dictionary.Count);

        var values = new object[256];
        Parallel.For(0, values.Length, index => values[index] = dictionary.GetOrAdd(1000, static _ => new object()));
        Assert.All(values, value => Assert.Same(values[0], value));
        Assert.Equal(513, dictionary.Count);
    }

    [Fact]
    public void ConcurrentRentals_NeverShareAnActiveDictionary()
    {
        var active = new ConcurrentDictionary<PooledConcurrentDictionary<int, int>, byte>();
        Parallel.For(0, 512, index =>
        {
            using var dictionary = PooledConcurrentDictionary<int, int>.GetInstance();
            Assert.True(active.TryAdd(dictionary, 0));
            Assert.Empty(dictionary);
            dictionary.TryAdd(index, index);
            Thread.Yield();
            Assert.Single(dictionary);
            Assert.Equal(index, dictionary[index]);
            Assert.True(active.TryRemove(dictionary, out _));
        });
        Assert.Empty(active);
    }

    [Fact]
    public async Task LazyPublication_ReturnsEveryLosingRentalWithoutClearingTheWinner()
    {
        const int contenders = 4;
        using var barrier = new Barrier(contenders);
        using var factoryTurn = new ThreadLocal<int>();
        using var turns = new PublicationTurns(contenders);
        var created = new ConcurrentQueue<PooledConcurrentDictionary<string, byte>>();
        var allocations = 0;
        ObjectPool<PooledConcurrentDictionary<string, byte>> pool = null!;
        pool = new ObjectPool<PooledConcurrentDictionary<string, byte>>(() =>
        {
            var dictionary = new PooledConcurrentDictionary<string, byte>(pool, StringComparer.Ordinal);
            created.Enqueue(dictionary);
            var turn = Interlocked.Increment(ref allocations) - 1;
            if (turn < contenders)
            {
                // Force all contenders to observe null and rent. Then serialize the
                // returns: ObjectPool deliberately allows concurrent Free to drop items.
                factoryTurn.Value = turn;
                Assert.True(barrier.SignalAndWait(TimeSpan.FromSeconds(20)));
                Assert.True(turns.Gates[turn].Wait(TimeSpan.FromSeconds(20)));
            }

            return dictionary;
        }, PooledConcurrentDictionary<string, byte>.PoolSize);
        PooledConcurrentDictionary<string, byte>? published = null;
        var tasks = Enumerable.Range(0, contenders).Select(_ => Task.Factory.StartNew(() =>
        {
            var dictionary = PooledConcurrentDictionary<string, byte>.GetOrCreate(ref published, pool);
            dictionary.TryAdd("winner", 0);
            var next = factoryTurn.Value + 1;
            if (next < contenders)
            {
                turns.Gates[next].Set();
            }
            return dictionary;
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        var results = await Task.WhenAll(tasks);
        var winner = results[0];
        var losers = new List<PooledConcurrentDictionary<string, byte>>();
        try
        {
            Assert.All(results, dictionary => Assert.Same(winner, dictionary));
            Assert.True(winner.ContainsKey("winner"));
            for (var index = 0; index < contenders - 1; index++)
            {
                var loser = PooledConcurrentDictionary<string, byte>.GetInstanceFromPool(pool);
                losers.Add(loser);
                Assert.NotSame(winner, loser);
                Assert.Contains(loser, created);
                Assert.Empty(loser);
            }

            Assert.Equal(contenders, allocations);
            Assert.Equal(contenders - 1, losers.Distinct().Count());
        }
        finally
        {
            foreach (var loser in losers)
            {
                loser.Free();
            }
            winner.Free();
        }
    }

    [Fact]
    public void Cancellation_AbandonsTheInstanceInsteadOfRecyclingIt()
    {
        var pool = PooledConcurrentDictionary<int, object>.CreatePool();
        var abandoned = PooledConcurrentDictionary<int, object>.GetInstanceFromPool(pool);
        abandoned.TryAdd(1, new object());
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        abandoned.Free(cancellation.Token);
        using var next = PooledConcurrentDictionary<int, object>.GetInstanceFromPool(pool);
        Assert.NotSame(abandoned, next);
        Assert.Empty(next);
        Assert.Throws<InvalidOperationException>(() => abandoned.Free());
    }

    [Fact]
    public void ExceptionDuringOwnership_ReturnsAnEmptyDictionaryViaDispose()
    {
        var pool = PooledConcurrentDictionary<int, int>.CreatePool();
        PooledConcurrentDictionary<int, int>? first = null;
        Assert.Throws<OperationCanceledException>((Action)(() =>
        {
            using var dictionary = PooledConcurrentDictionary<int, int>.GetInstanceFromPool(pool);
            first = dictionary;
            dictionary.TryAdd(1, 1);
            throw new OperationCanceledException();
        }));
        using var next = PooledConcurrentDictionary<int, int>.GetInstanceFromPool(pool);
        Assert.Same(first, next);
        Assert.Empty(next);
    }

    [Fact]
    public void DoubleRelease_IsRejectedBeforeItCanDuplicateAPoolEntry()
    {
        var pool = PooledConcurrentDictionary<int, int>.CreatePool();
        var dictionary = PooledConcurrentDictionary<int, int>.GetInstanceFromPool(pool);
        dictionary.Free();
        Assert.Throws<InvalidOperationException>(() => dictionary.Free());
        using var first = PooledConcurrentDictionary<int, int>.GetInstanceFromPool(pool);
        using var second = PooledConcurrentDictionary<int, int>.GetInstanceFromPool(pool);
        Assert.NotSame(first, second);
    }

    [Fact]
    public void Release_DoesNotKeepKeysOrValuesAlive()
    {
        var pool = PooledConcurrentDictionary<object, object>.CreatePool();
        var references = ReleasePayload(pool);
        Collect();
        Assert.All(references, reference => Assert.False(reference.IsAlive));
        using var next = PooledConcurrentDictionary<object, object>.GetInstanceFromPool(pool);
        Assert.Empty(next);
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] ReleasePayload(ObjectPool<PooledConcurrentDictionary<object, object>> pool)
    {
        using var dictionary = PooledConcurrentDictionary<object, object>.GetInstanceFromPool(pool);
        var key = new object();
        var value = new byte[8192];
        dictionary.TryAdd(key, value);
        return [new WeakReference(key), new WeakReference(value)];
    }

    [Fact]
    public void LargeWorkloads_ReleaseTheirPayloadAndThePoolCapacityIsBounded()
    {
        var pool = PooledConcurrentDictionary<int, object>.CreatePool();
        var large = ReleaseLargeDictionary(pool);
        var small = FillPool(pool);
        Collect();
        Assert.False(large.IsAlive);
        Assert.Equal(PooledConcurrentDictionary<int, object>.PoolSize, small.Count(static reference => reference.IsAlive));
        GC.KeepAlive(pool);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference ReleaseLargeDictionary(ObjectPool<PooledConcurrentDictionary<int, object>> pool)
    {
        var dictionary = PooledConcurrentDictionary<int, object>.GetInstanceFromPool(pool);
        var payload = new byte[8192];
        var reference = new WeakReference(payload);
        for (var index = 0; index < 16384; index++)
        {
            dictionary.TryAdd(index, payload);
        }
        dictionary.Free();
        return reference;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] FillPool(ObjectPool<PooledConcurrentDictionary<int, object>> pool)
    {
        var dictionaries = Enumerable.Range(0, PooledConcurrentDictionary<int, object>.PoolSize + 3)
            .Select(_ => PooledConcurrentDictionary<int, object>.GetInstanceFromPool(pool)).ToArray();
        var references = dictionaries.Select(static dictionary => new WeakReference(dictionary)).ToArray();
        foreach (var dictionary in dictionaries)
        {
            dictionary.Free();
        }
        return references;
    }

    private sealed class PublicationTurns : IDisposable
    {
        internal readonly ManualResetEventSlim[] Gates;

        internal PublicationTurns(int count)
        {
            Gates = Enumerable.Range(0, count).Select(_ => new ManualResetEventSlim()).ToArray();
            Gates[0].Set();
        }

        public void Dispose()
        {
            foreach (var gate in Gates)
            {
                gate.Dispose();
            }
        }
    }

    private static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
