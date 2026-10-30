using Akbura.Pools;
using System.Collections.Concurrent;

namespace Akbura.UnitTests;

public sealed class PooledStringBuilderTests
{
    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("Привет 世界 😀 e\u0301")]
    [InlineData("a\0b\0")]
    [InlineData("a\r\nb\nc\r")]
    public void Materialization_PreservesTextAndIndependentResults(string text)
    {
        var pooled = PooledStringBuilder.GetInstance();
        Assert.Equal(0, pooled.Length);
        pooled.Builder.Append(text);
        var result = pooled.ToStringAndFree();
        var next = PooledStringBuilder.GetInstance();
        Assert.Equal(0, next.Length);
        next.Builder.Append("unrelated content");
        next.Free();
        Assert.Equal(text, result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(32)]
    [InlineData(128)]
    [InlineData(512)]
    [InlineData(1024)]
    public void ExplicitFree_ReusesSmallBuildersWithoutStaleText(int capacity)
    {
        var pool = PooledStringBuilder.CreatePool(1);
        var first = pool.Allocate();
        first.Builder.EnsureCapacity(capacity);
        first.Builder.Append('x', capacity);
        var retainedCapacity = first.Builder.Capacity;
        first.Free();
        for (var index = 0; index < 32; index++)
        {
            var next = pool.Allocate();
            Assert.Same(first, next);
            Assert.Equal(0, next.Length);
            Assert.Equal(retainedCapacity, next.Builder.Capacity);
            next.Builder.Append(index);
            next.Free();
        }
    }

    [Theory]
    [InlineData(1025)]
    [InlineData(4096)]
    [InlineData(16384)]
    public void OversizedBuilders_AreDiscardedEvenWhenLengthIsSmall(int capacity)
    {
        var pool = PooledStringBuilder.CreatePool(1);
        var large = pool.Allocate();
        large.Builder.EnsureCapacity(capacity);
        large.Builder.Append('x', capacity);
        Assert.Equal(new string('x', capacity), large.ToStringAndFree());
        var next = pool.Allocate();
        Assert.NotSame(large, next);
        Assert.Equal(0, next.Length);
        next.Builder.EnsureCapacity(capacity);
        next.Builder.Append('x');
        next.Free();
        var replacement = pool.Allocate();
        Assert.NotSame(next, replacement);
        Assert.InRange(replacement.Builder.Capacity, 0, 1024);
        replacement.Free();
    }

    [Theory]
    [InlineData(0, 0, "")]
    [InlineData(0, 2, "ab")]
    [InlineData(2, 4, "\0\r\n世")]
    [InlineData(7, 0, "")]
    public void SliceMaterialization_ReleasesAndPreservesExactRange(int start, int length, string expected)
    {
        var pooled = PooledStringBuilder.GetInstance();
        pooled.Builder.Append("ab\0\r\n世界");
        Assert.Equal(expected, pooled.ToStringAndFree(start, length));
        var next = PooledStringBuilder.GetInstance();
        Assert.Equal(0, next.Length);
        next.Free();
    }

    [Fact]
    public void NestedAndSimultaneousRentals_AreIndependent()
    {
        var builders = new PooledStringBuilder[64];
        for (var index = 0; index < builders.Length; index++)
        {
            builders[index] = PooledStringBuilder.GetInstance();
            Assert.DoesNotContain(builders.Take(index), builder => ReferenceEquals(builder, builders[index]));
            builders[index].Builder.Append("outer-").Append(index);
            var inner = PooledStringBuilder.GetInstance();
            Assert.NotSame(builders[index], inner);
            inner.Builder.Append("inner-").Append(index);
            Assert.Equal("inner-" + index, inner.ToStringAndFree());
        }
        for (var index = 0; index < builders.Length; index++)
            Assert.Equal("outer-" + index, builders[index].ToStringAndFree());
    }

    [Fact]
    public void ParallelRentals_DoNotShareActiveStorage()
    {
        var active = new ConcurrentDictionary<PooledStringBuilder, byte>();
        Parallel.For(0, 4096, index =>
        {
            var pooled = PooledStringBuilder.GetInstance();
            Assert.True(active.TryAdd(pooled, 0));
            Assert.Equal(0, pooled.Length);
            var text = $"rental-{index}\0世界\r\n";
            pooled.Builder.Append(text);
            Assert.Equal(text, pooled.Builder.ToString());
            Assert.True(active.TryRemove(pooled, out _));
            Assert.Equal(text, pooled.ToStringAndFree());
        });
        Assert.Empty(active);
    }
}
