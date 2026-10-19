using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Pools;
// HashSet that can be recycled via an object pool
// Each pool keeps its own equality comparer.
internal sealed partial class PooledHashSet<T> : HashSet<T>
{
    internal const int MaximumRetainedCount = 1024;
    private readonly ObjectPool<PooledHashSet<T>> _pool;

    private PooledHashSet(ObjectPool<PooledHashSet<T>> pool, IEqualityComparer<T> equalityComparer) :
        base(equalityComparer)
    {
        _pool = pool;
    }

    public void Free()
    {
        if (Count > MaximumRetainedCount)
        {
            Clear();
            _pool.ForgetTrackedObject(this);
            return;
        }
        // netstandard2.0 cannot inspect capacity. Compact before clearing so a
        // formerly large, now small set cannot retain its old bucket arrays.
        TrimExcess();
        Clear();
        _pool.Free(this);
    }

    // global pool
    private static readonly ObjectPool<PooledHashSet<T>> s_poolInstance = CreatePool(EqualityComparer<T>.Default);

    // if someone needs to create a pool;
    public static ObjectPool<PooledHashSet<T>> CreatePool(IEqualityComparer<T> equalityComparer, int size = 128)
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
