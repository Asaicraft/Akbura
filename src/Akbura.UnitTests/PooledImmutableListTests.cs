using Akbura.Pools;
using System.Collections;

namespace Akbura.UnitTests;

public sealed class PooledImmutableListTests
{
    [Fact]
    public void Create_CopiesItemsAndUsesSmallBuffer()
    {
        var source = new[] { 1, 2, 3 };
        var list = PooledImmutableList<int>.Create(source);

        try
        {
            Assert.Equal(3, list.Count);
            Assert.Equal(3, list.Length);
            Assert.Equal(16, list.Capacity);
            Assert.Equal(1, list[0]);
            Assert.Equal(2, list[1]);
            Assert.Equal(3, list[2]);
            Assert.Equal(source, list.AsSpan().ToArray());
        }
        finally
        {
            list.ReturnToPool();
        }
    }

    [Fact]
    public void Create_UsesLargeBufferForMoreThanSixteenItems()
    {
        var source = Enumerable.Range(0, 17).ToArray();
        var list = PooledImmutableList<int>.Create(source);

        try
        {
            Assert.Equal(17, list.Count);
            Assert.Equal(64, list.Capacity);
            Assert.Equal(source, list.AsSpan().ToArray());
        }
        finally
        {
            list.ReturnToPool();
        }
    }

    [Fact]
    public void Create_UsesSharedArrayPoolForMoreThanSixtyFourItems()
    {
        var source = Enumerable.Range(0, 65).ToArray();
        var list = PooledImmutableList<int>.Create(source);

        try
        {
            Assert.Equal(65, list.Count);
            Assert.True(list.Capacity >= 65);
            Assert.Equal(source, list.AsSpan().ToArray());
        }
        finally
        {
            list.ReturnToPool();
        }
    }

    [Fact]
    public void Create_EmptySpanReturnsDefaultList()
    {
        var list = PooledImmutableList<int>.Create([]);

        Assert.Empty(list);
        Assert.Equal(0, list.Capacity);
        Assert.True(list.IsEmpty);
        Assert.True(list.IsDefaultOrEmpty);
    }

    [Fact]
    public void Enumerator_CurrentThrowsBeforeStartAndAfterEnd()
    {
        var list = PooledImmutableList<int>.Create([10, 20]);
        var enumerator = list.GetEnumerator();

        try
        {
            Assert.Throws<InvalidOperationException>(() => enumerator.Current);

            Assert.True(enumerator.MoveNext());
            Assert.Equal(10, enumerator.Current);

            Assert.True(enumerator.MoveNext());
            Assert.Equal(20, enumerator.Current);

            Assert.False(enumerator.MoveNext());
            Assert.Throws<InvalidOperationException>(() => enumerator.Current);
        }
        finally
        {
            enumerator.Dispose();
            list.ReturnToPool();
        }
    }

    [Fact]
    public void InterfaceEnumerationPreservesOrder()
    {
        var list = PooledImmutableList<int>.Create([3, 2, 1]);

        try
        {
            Assert.Equal([3, 2, 1], [.. list.Cast<int>()]);
        }
        finally
        {
            list.ReturnToPool();
        }
    }

    [Fact]
    public void AsSpan_ReturnsRequestedRange()
    {
        var list = PooledImmutableList<int>.Create([1, 2, 3, 4]);

        try
        {
            Assert.Equal([2, 3], list.AsSpan(1, 2).ToArray());
        }
        finally
        {
            list.ReturnToPool();
        }
    }
}
