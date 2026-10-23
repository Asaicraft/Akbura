using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Pools;

/// <summary>
/// A reusable set whose comparer belongs to its owning pool.
/// Callers must finish using the set before returning it exactly once with Free.
/// </summary>
internal sealed partial class PooledHashSet<T> : HashSet<T>
{
    internal const int DefaultPoolSize = 32;
    internal const int MaxPooledCount = 1024;

    private readonly ObjectPool<PooledHashSet<T>> _pool;

    private PooledHashSet(ObjectPool<PooledHashSet<T>> pool, IEqualityComparer<T> equalityComparer) :
        base(equalityComparer)
    {
        _pool = pool;
    }

    /// <summary>
    /// Clears the set and returns it to its pool when its current count is small enough.
    /// Larger transient sets are left for collection instead of retaining their storage.
    /// </summary>
    public void Free()
    {
        var reuse = Count <= MaxPooledCount;
        Clear();
        if (reuse)
        {
            _pool.Free(this);
        }
        else
        {
            _pool.ForgetTrackedObject(this);
        }
    }

    // global pool
    private static readonly ObjectPool<PooledHashSet<T>> s_poolInstance = CreatePool(EqualityComparer<T>.Default);

    // if someone needs to create a pool;
    public static ObjectPool<PooledHashSet<T>> CreatePool(IEqualityComparer<T> equalityComparer)
        => CreatePool(equalityComparer, DefaultPoolSize);

    public static ObjectPool<PooledHashSet<T>> CreatePool(IEqualityComparer<T> equalityComparer, int size)
    {
        ObjectPool<PooledHashSet<T>>? pool = null;
        pool = new ObjectPool<PooledHashSet<T>>(() => new PooledHashSet<T>(pool!, equalityComparer), size);
        return pool;
    }

    public static PooledHashSet<T> GetInstance()
    {
        var instance = s_poolInstance.Allocate();
        Debug.Assert(instance.Count == 0);
        return instance;
    }
}
