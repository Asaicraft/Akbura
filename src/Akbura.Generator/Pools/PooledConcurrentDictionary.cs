using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Akbura.Pools;

/// <summary>
/// A concurrently populated dictionary with an exclusive owner for its rental lifetime.
/// All consumers must finish before Free, Dispose, or publication to another owner.
/// Inherited dictionary operations do not check ownership or make concurrent release safe.
/// </summary>
internal sealed class PooledConcurrentDictionary<TKey, TValue> : ConcurrentDictionary<TKey, TValue>, IDisposable
    where TKey : notnull
{
    internal const int PoolSize = 4;
    internal const int ConcurrencyLevel = 8;
    internal const int InitialCapacity = 31;

    private static readonly ObjectPool<PooledConcurrentDictionary<TKey, TValue>> s_defaultPool = CreatePool();
    private static readonly Lazy<ObjectPool<PooledConcurrentDictionary<TKey, TValue>>> s_ordinalPool =
        new(() => CreatePool(StringComparer.Ordinal as IEqualityComparer<TKey>));

    private readonly ObjectPool<PooledConcurrentDictionary<TKey, TValue>>? _pool;
    private int _rented;

    internal PooledConcurrentDictionary(ObjectPool<PooledConcurrentDictionary<TKey, TValue>>? pool,
        IEqualityComparer<TKey>? comparer)
        // Clear resets buckets but keeps the locks. A growing lock array can make every
        // subsequent small rental repeatedly resize its buckets. Fixed stripes bound
        // this overhead while supporting concurrently populated, short-lived accumulators.
        : base(ConcurrencyLevel, InitialCapacity, comparer ?? EqualityComparer<TKey>.Default)
    {
        _pool = pool;
    }

    public static PooledConcurrentDictionary<TKey, TValue> GetInstance() => GetInstanceFromPool(s_defaultPool);

    public static PooledConcurrentDictionary<TKey, TValue> GetInstance(IEqualityComparer<TKey>? comparer)
    {
        if (comparer == null || ReferenceEquals(comparer, EqualityComparer<TKey>.Default))
        {
            return GetInstance();
        }

        if (ReferenceEquals(comparer, StringComparer.Ordinal))
        {
            return GetInstanceFromPool(s_ordinalPool.Value);
        }

        // Arbitrary comparers are not rooted in a global registry. Frequent callers can
        // retain a bounded CreatePool(comparer) and rent through GetInstanceFromPool.
        return new PooledConcurrentDictionary<TKey, TValue>(pool: null, comparer).BeginRental();
    }

    /// <summary>
    /// Rent from the returned pool through GetInstanceFromPool, not Allocate.
    /// </summary>
    public static ObjectPool<PooledConcurrentDictionary<TKey, TValue>> CreatePool(IEqualityComparer<TKey>? comparer = null)
        => new(pool => new PooledConcurrentDictionary<TKey, TValue>(pool, comparer), PoolSize);

    internal static PooledConcurrentDictionary<TKey, TValue> GetInstanceFromPool(
        ObjectPool<PooledConcurrentDictionary<TKey, TValue>> pool)
        => pool.Allocate().BeginRental();

    /// <summary>
    /// Publishes one rental and returns every unpublished contender to its pool.
    /// </summary>
    internal static PooledConcurrentDictionary<TKey, TValue> GetOrCreate(
        ref PooledConcurrentDictionary<TKey, TValue>? location,
        ObjectPool<PooledConcurrentDictionary<TKey, TValue>> pool)
    {
        var current = Volatile.Read(ref location);
        if (current != null)
        {
            return current;
        }

        var rented = GetInstanceFromPool(pool);
        current = Interlocked.CompareExchange(ref location, rented, null);
        if (current == null)
        {
            return rented;
        }

        rented.Free();
        return current;
    }

    private PooledConcurrentDictionary<TKey, TValue> BeginRental()
    {
        if (Interlocked.CompareExchange(ref _rented, 1, 0) != 0)
        {
            throw new InvalidOperationException("The dictionary already has an active owner.");
        }

        Debug.Assert(IsEmpty);
        return this;
    }

    public void Free() => Free(CancellationToken.None);

    public void Free(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _rented, 0) == 0)
        {
            throw new InvalidOperationException("The dictionary has already been released.");
        }

        if (cancellationToken.IsCancellationRequested)
        {
            // Cancellation may leave earlier callbacks in flight (Roslyn issue 46859).
            // Abandon this instance without clearing it or transferring it to a new owner.
            _pool?.ForgetTrackedObject(this);
            return;
        }

        // Clear replaces the buckets with the initial capacity. Fixed stripes prevent
        // a large workload from leaving a growing lock array behind in the pool.
        Clear();
        _pool?.Free(this);
    }

    public void Dispose() => Free();
}
