using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Akbura.Collections;

// Very simple fixed-size cache.
// Expiration policy is "new entry wins over old entry if hashed into the same bucket".
internal sealed class ConcurrentCache<TKey, TValue> : CachingBase<ConcurrentCache<TKey, TValue>.Entry>
    where TKey : notnull
{
    private readonly IEqualityComparer<TKey> _keyComparer;
    private int _occupiedCount;

    internal sealed class Entry
    {
        internal readonly int hash;
        internal readonly TKey key;
        internal readonly TValue value;

        public Entry(int hash, TKey key, TValue value)
        {
            this.hash = hash;
            this.key = key;
            this.value = value;
        }
    }

    public ConcurrentCache(int size, IEqualityComparer<TKey> keyComparer)
        : base(size, createBackingArray: false)
    {
        _keyComparer = keyComparer;
    }

    public ConcurrentCache(int size)
        : this(size, EqualityComparer<TKey>.Default)
    {
    }

    public int Count => _occupiedCount;

    public bool TryAdd(TKey key, TValue value)
    {
        var hash = _keyComparer.GetHashCode(key);
        var index = hash & mask;

        var entry = Entries[index];
        if (entry != null &&
            entry.hash == hash &&
            _keyComparer.Equals(entry.key, key))
        {
            return false;
        }

        Entries[index] = new Entry(hash, key, value);
        if (entry == null)
        {
            Interlocked.Increment(ref _occupiedCount);
        }

        return true;
    }

    public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        var hash = _keyComparer.GetHashCode(key);
        var index = hash & mask;

        var entry = Entries[index];
        if (entry != null &&
            entry.hash == hash &&
            _keyComparer.Equals(entry.key, key))
        {
            value = entry.value;
            return true;
        }

        value = default;
        return false;
    }
}
