using System;

namespace Akbura.Pools;

internal static class PooledStringHashSet
{
    private static readonly ObjectPool<PooledHashSet<string>> s_pool = PooledHashSet<string>.CreatePool(StringComparer.Ordinal, size: 32);

    internal static PooledHashSet<string> GetInstance() => s_pool.Allocate();
}
