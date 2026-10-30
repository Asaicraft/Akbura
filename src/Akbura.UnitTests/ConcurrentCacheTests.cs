using Akbura.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Akbura.UnitTests;

public sealed class ConcurrentCacheTests
{
    [Fact]
    public void EmptyCache_AllocatesBackingStorageOnlyOnFirstAccess()
    {
        var cache = new ConcurrentCache<string, int>(17, StringComparer.Ordinal);
        Assert.Null(BackingArray(cache));
        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue("missing", out var value));
        Assert.Equal(0, value);
        Assert.Equal(32, BackingArray(cache)!.Length);
        Assert.Equal(0, cache.Count);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(16, 16)]
    [InlineData(17, 32)]
    [InlineData(32, 32)]
    [InlineData(50, 64)]
    [InlineData(128, 128)]
    [InlineData(129, 256)]
    public void Storage_IsRoundedAndBoundedDespiteDistinctInsertions(int requested, int expected)
    {
        var cache = new ConcurrentCache<int, int>(requested);
        for (var key = 0; key < 4096; key++) cache.TryAdd(key, key);
        Assert.Equal(expected, BackingArray(cache)!.Length);
        Assert.Equal(expected, cache.Count);
        for (var key = 4096 - expected; key < 4096; key++)
        {
            Assert.True(cache.TryGetValue(key, out var value));
            Assert.Equal(key, value);
        }
    }

    [Fact]
    public void SameKeyInsertion_DoesNotReplaceTheExistingValueSerially()
    {
        var cache = new ConcurrentCache<string, int>(16, StringComparer.Ordinal);
        Assert.True(cache.TryAdd("key", 1));
        for (var repeat = 0; repeat < 100; repeat++) Assert.False(cache.TryAdd(new string("key".ToCharArray()), 2));
        Assert.True(cache.TryGetValue("key", out var value));
        Assert.Equal(1, value);
        Assert.Equal(1, cache.Count);
    }

    [Fact]
    public void EqualHashes_EvictDistinctKeysWithoutReturningAnotherKeysValue()
    {
        var cache = new ConcurrentCache<string, string>(32, new ConstantHashComparer<string>(StringComparer.Ordinal));
        for (var index = 0; index < 100; index++)
        {
            var key = "key-" + index;
            Assert.True(cache.TryAdd(key, "value-" + index));
            Assert.True(cache.TryGetValue(key, out var value));
            Assert.Equal("value-" + index, value);
            if (index > 0) Assert.False(cache.TryGetValue("key-" + (index - 1), out _));
        }
        Assert.Equal(1, cache.Count);
        Assert.Equal(32, BackingArray(cache)!.Length);
    }

    [Fact]
    public void CustomEquality_IsPreservedForHitsAndDuplicateInsertion()
    {
        var cache = new ConcurrentCache<string, int>(16, StringComparer.OrdinalIgnoreCase);
        Assert.True(cache.TryAdd("Name", 0));
        Assert.True(cache.TryGetValue("NAME", out var value));
        Assert.Equal(0, value);
        Assert.False(cache.TryAdd("name", 42));
        Assert.False(cache.TryGetValue("İ", out _));
        var ordinal = new ConcurrentCache<string, int>(16, StringComparer.Ordinal);
        Assert.True(ordinal.TryAdd("Name", 1));
        Assert.False(ordinal.TryGetValue("name", out _));
    }

    [Fact]
    public void ReferenceComparer_DistinguishesEqualTextObjects()
    {
        var cache = new ConcurrentCache<string, int>(16, ReferenceEqualityComparer.Instance);
        var first = new string('x', 3);
        var other = new string('x', 3);
        Assert.True(cache.TryAdd(first, 7));
        Assert.False(cache.TryGetValue(other, out _));
        Assert.True(cache.TryGetValue(first, out var value));
        Assert.Equal(7, value);
    }

    [Fact]
    public void DefaultAndNullValues_AreSuccessfulHits()
    {
        var integers = new ConcurrentCache<int, int>(16);
        Assert.True(integers.TryAdd(0, default));
        Assert.True(integers.TryGetValue(0, out var number));
        Assert.Equal(0, number);
        var references = new ConcurrentCache<int, string?>(16);
        Assert.True(references.TryAdd(-1, null));
        Assert.True(references.TryGetValue(-1, out var text));
        Assert.Null(text);
        Assert.False(references.TryGetValue(int.MinValue, out _));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ParallelPublication_NeverExposesPartiallyInitializedOrMismatchedEntries(bool collisions)
    {
        var cache = new ConcurrentCache<int, PublishedValue>(64,
            collisions ? new ConstantHashComparer<int>(EqualityComparer<int>.Default) : EqualityComparer<int>.Default);
        Parallel.For(0, 32768, index =>
        {
            var key = index % 257;
            cache.TryAdd(key, new PublishedValue(key));
            if (cache.TryGetValue(key, out var value))
            {
                Assert.Equal(key, value.Key);
                Assert.Equal(~key, value.Checksum);
                Assert.Equal(key.ToString(), value.Text);
            }
        });
        Assert.Equal(64, BackingArray(cache)!.Length);
        // Racing writes may lose entries or overcount occupied buckets.
        // Neither Count nor insertion uniqueness is asserted here.
    }

    [Fact]
    public void Eviction_ReleasesOldKeysAndValuesWhileCacheRemainsAlive()
    {
        var (cache, key, value) = CreateEvictedReferences();
        for (var attempt = 0; attempt < 8 && (key.IsAlive || value.IsAlive); attempt++)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
        }
        Assert.False(key.IsAlive);
        Assert.False(value.IsAlive);
        GC.KeepAlive(cache);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static (object Cache, WeakReference Key, WeakReference Value) CreateEvictedReferences()
    {
        var cache = new ConcurrentCache<object, object>(16, new ConstantHashComparer<object>(ReferenceEqualityComparer.Instance));
        var key = new object();
        var value = new object();
        cache.TryAdd(key, value);
        var references = (new WeakReference(key), new WeakReference(value));
        cache.TryAdd(new object(), new object());
        return (cache, references.Item1, references.Item2);
    }

    private static Array? BackingArray<TKey, TValue>(ConcurrentCache<TKey, TValue> cache) where TKey : notnull =>
        (Array?)typeof(CachingBase<ConcurrentCache<TKey, TValue>.Entry>)
            .GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache);

    private sealed class PublishedValue(int key)
    {
        public readonly int Key = key;
        public readonly int Checksum = ~key;
        public readonly string Text = key.ToString();
    }

    internal sealed class ConstantHashComparer<T>(IEqualityComparer<T> inner) : IEqualityComparer<T>
    {
        public bool Equals(T? left, T? right) => inner.Equals(left!, right!);
        public int GetHashCode(T value) => -1;
    }
}
