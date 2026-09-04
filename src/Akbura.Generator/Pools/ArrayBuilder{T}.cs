using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Pools;

[DebuggerDisplay("Count = {Count,nq}")]
[DebuggerTypeProxy(typeof(ArrayBuilder<>.DebuggerProxy))]
internal sealed partial class ArrayBuilder<T>(int size) : IReadOnlyCollection<T>, IReadOnlyList<T>, ICollection<T>
{
    /// <summary>
    /// See <see cref="Free()"/> for an explanation of this constant value.
    /// </summary>
    public const int PooledArrayLengthLimitExclusive = 128;

    #region DebuggerProxy

    private sealed class DebuggerProxy(ArrayBuilder<T> builder)
    {
        private readonly ArrayBuilder<T> _builder = builder;

        [DebuggerBrowsable(DebuggerBrowsableState.RootHidden)]
        public T[] A
        {
            get
            {
                var result = new T[_builder.Count];
                for (var i = 0; i < result.Length; i++)
                {
                    result[i] = _builder[i];
                }

                return result;
            }
        }
    }

    #endregion

    private readonly List<T> _items = new(size);

    private readonly ObjectPool<ArrayBuilder<T>>? _pool;

    public ArrayBuilder()
        : this(8)
    { }

    private ArrayBuilder(ObjectPool<ArrayBuilder<T>> pool)
        : this()
    {
        _pool = pool;
    }

    /// <summary>
    /// Realizes the array.
    /// </summary>
    public ImmutableArray<T> ToImmutable()
    {
        return [.. _items];
    }

    /// <summary>
    /// Creates a pooled immutable snapshot containing the current items.
    /// </summary>
    public PooledImmutableList<T> ToPooledImmutableList()
    {
        return PooledImmutableList<T>.CreateFromList(_items);
    }

    /// <summary>
    /// Realizes the array and clears the collection.
    /// </summary>
    public ImmutableArray<T> ToImmutableAndClear()
    {
        ImmutableArray<T> result;
        if (Count == 0)
        {
            result = [];
        }
        else
        {
            result = ToImmutable();
            Clear();
        }

        return result;
    }

    public int Count
    {
        get => _items.Count;
        set => SetCount(value);
    }

    public int Capacity
    {
        get => _items.Capacity;
        set => _items.Capacity = value;
    }

    public T this[int index]
    {
        get => _items[index];
        set => _items[index] = value;
    }

    public bool IsReadOnly => false;

    public bool IsEmpty => Count == 0;

    /// <summary>
    /// Write <paramref name="value"/> to slot <paramref name="index"/>. 
    /// Fills in unallocated slots preceding the <paramref name="index"/>, if any.
    /// </summary>
    public void SetItem(int index, T value)
    {
        while (index > _items.Count)
        {
            _items.Add(default!);
        }

        if (index == _items.Count)
        {
            _items.Add(value);
        }
        else
        {
            _items[index] = value;
        }
    }

    public void Add(T item) => _items.Add(item);

    public void Insert(int index, T item) => _items.Insert(index, item);

    public void EnsureCapacity(int capacity)
    {
        if (_items.Capacity < capacity)
        {
            _items.Capacity = capacity;
        }
    }

    private void SetCount(int count)
    {
        ThrowHelper.ThrowIfNegative(count);
        if (count < _items.Count)
        {
            _items.RemoveRange(count, _items.Count - count);
            return;
        }

        EnsureCapacity(count);
        while (_items.Count < count)
        {
            _items.Add(default!);
        }
    }

    public void Clear() => _items.Clear();

    public bool Contains(T item) => _items.Contains(item);

    public int IndexOf(T item) => _items.IndexOf(item);

    public int IndexOf(T item, IEqualityComparer<T> equalityComparer)
        => IndexOf(item, 0, _items.Count, equalityComparer);

    public int IndexOf(T item, int startIndex, int count)
        => _items.IndexOf(item, startIndex, count);

    private int IndexOf(
        T item,
        int startIndex,
        int count,
        IEqualityComparer<T> equalityComparer)
    {
        var endIndex = startIndex + count;
        for (var index = startIndex; index < endIndex; index++)
        {
            if (equalityComparer.Equals(_items[index], item))
            {
                return index;
            }
        }

        return -1;
    }

    public int FindIndex(Predicate<T> match) => FindIndex(0, Count, match);

    public int FindIndex(int startIndex, Predicate<T> match) => FindIndex(startIndex, Count - startIndex, match);

    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        var endIndex = startIndex + count;
        for (var i = startIndex; i < endIndex; i++)
        {
            if (match(_items[i]))
            {
                return i;
            }
        }

        return -1;
    }

    public int FindIndex<TArg>(Func<T, TArg, bool> match, TArg arg) => FindIndex(0, Count, match, arg);

    public int FindIndex<TArg>(int startIndex, Func<T, TArg, bool> match, TArg arg) => FindIndex(startIndex, Count - startIndex, match, arg);

    public int FindIndex<TArg>(int startIndex, int count, Func<T, TArg, bool> match, TArg arg)
    {
        var endIndex = startIndex + count;
        for (var i = startIndex; i < endIndex; i++)
        {
            if (match(_items[i], arg))
            {
                return i;
            }
        }

        return -1;
    }

    public bool Remove(T element) => _items.Remove(element);

    public void RemoveAt(int index) => _items.RemoveAt(index);

    public void RemoveRange(int index, int length) => _items.RemoveRange(index, length);

    public void RemoveLast() => _items.RemoveAt(_items.Count - 1);

    public void RemoveAll(Predicate<T> match) => _items.RemoveAll(match);

    public void RemoveAll<TArg>(Func<T, TArg, bool> match, TArg arg)
    {
        var i = 0;
        for (var j = 0; j < _items.Count; j++)
        {
            if (!match(_items[j], arg))
            {
                if (i != j)
                {
                    _items[i] = _items[j];
                }

                i++;
            }
        }

        Clip(i);
    }

    public void ReverseContents() => _items.Reverse();

    public void Sort() => _items.Sort();

    public void Sort(IComparer<T> comparer) => _items.Sort(comparer);

    public void Sort(Comparison<T> compare)
    {
        if (Count <= 1)
        {
            return;
        }

        Sort(Comparer<T>.Create(compare));
    }

    public void Sort(int startIndex, IComparer<T> comparer)
        => _items.Sort(startIndex, _items.Count - startIndex, comparer);

    public T[] ToArray() => _items.ToArray();

    public void CopyTo(T[] array, int start) => _items.CopyTo(array, start);

    public T Last() => _items[_items.Count - 1];

    public T? LastOrDefault() => Count == 0 ? default : Last();

    public T First() => _items[0];

    public bool Any() => _items.Count > 0;

    /// <summary>
    /// Realizes the array.
    /// </summary>
    public ImmutableArray<T> ToImmutableOrNull()
    {
        if (Count == 0)
        {
            return default;
        }

        return ToImmutable();
    }

    /// <summary>
    /// Realizes the array, downcasting each element to a derived type.
    /// </summary>
    public ImmutableArray<U> ToDowncastedImmutable<U>()
        where U : T
    {
        if (Count == 0)
        {
            return [];
        }

        var tmp = ArrayBuilder<U>.GetInstance(Count);

        foreach (var i in this)
        {
            tmp.Add((U)i!);
        }

        return tmp.ToImmutableAndFree();
    }

    public ImmutableArray<U> ToDowncastedImmutableAndFree<U>() where U : T
    {
        var result = ToDowncastedImmutable<U>();
        Free();
        return result;
    }

    /// <summary>
    /// Realizes the array and disposes the builder in one operation.
    /// </summary>
    public ImmutableArray<T> ToImmutableAndFree()
    {
        // Materialize the immutable result before returning this reusable builder to its pool.
        ImmutableArray<T> result;
        if (Count == 0)
        {
            result = [];
        }
        else
        {
            result = ToImmutable();
        }

        Free();
        return result;
    }

    public T[] ToArrayAndFree()
    {
        var result = ToArray();
        Free();
        return result;
    }

    #region Poolable

    // To implement Poolable, you need two things:
    // 1) Expose Freeing primitive. 
    public void Free()
    {
        var pool = _pool;
        if (pool != null)
        {
            // According to the statistics of a C# compiler self-build, the most commonly used builder size is 0.  (808003 uses).
            // The distant second is the Count == 1 (455619), then 2 (106362) ...
            // After about 50 (just 67) we have a long tail of infrequently used builder sizes.
            // However we have builders with size up to 50K   (just one such thing)
            //
            // We do not want to retain (potentially indefinitely) very large builders 
            // while the chance that we will need their size is diminishingly small.
            // It makes sense to constrain the size to some "not too small" number. 
            // Overall perf does not seem to be very sensitive to this number, so I picked 128 as a limit.
            if (_items.Capacity < PooledArrayLengthLimitExclusive)
            {
                if (Count != 0)
                {
                    Clear();
                }

                pool.Free(this);
                return;
            }
            else
            {
                pool.ForgetTrackedObject(this);
            }
        }
    }

    // 2) Expose the pool or the way to create a pool or the way to get an instance.
    //    for now we will expose both and figure which way works better
    private static readonly ObjectPool<ArrayBuilder<T>> s_poolInstance = CreatePool();
    public static ArrayBuilder<T> GetInstance()
    {
        var builder = s_poolInstance.Allocate();
        Debug.Assert(builder.Count == 0);
        return builder;
    }

    public static ArrayBuilder<T> GetInstance(int capacity)
    {
        var builder = GetInstance();
        builder.EnsureCapacity(capacity);
        return builder;
    }

    public static ArrayBuilder<T> GetInstance(int capacity, T fillWithValue)
    {
        var builder = GetInstance();
        builder.EnsureCapacity(capacity);

        for (var i = 0; i < capacity; i++)
        {
            builder.Add(fillWithValue);
        }

        return builder;
    }

    public static ObjectPool<ArrayBuilder<T>> CreatePool()
    {
        return CreatePool(128); // we rarely need more than 10
    }

    public static ObjectPool<ArrayBuilder<T>> CreatePool(int size)
    {
        ObjectPool<ArrayBuilder<T>>? pool = null;
        pool = new ObjectPool<ArrayBuilder<T>>(() => new ArrayBuilder<T>(pool!), size);
        return pool;
    }

    #endregion

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
    {
        return _items.GetEnumerator();
    }

    public Dictionary<K, ImmutableArray<T>> ToDictionary<K>(Func<T, K> keySelector, IEqualityComparer<K>? comparer = null)
        where K : notnull
    {
        if (Count == 1)
        {
            var dictionary1 = new Dictionary<K, ImmutableArray<T>>(1, comparer);
            var value = this[0];
            dictionary1.Add(keySelector(value), [value]);
            return dictionary1;
        }

        if (Count == 0)
        {
            return new Dictionary<K, ImmutableArray<T>>(comparer);
        }

        // bucketize
        // prevent reallocation. it may not have 'count' entries, but it won't have more. 
        var accumulator = new Dictionary<K, ArrayBuilder<T>>(Count, comparer);
        for (var i = 0; i < Count; i++)
        {
            var item = this[i];
            var key = keySelector(item);
            if (!accumulator.TryGetValue(key, out var bucket))
            {
                bucket = ArrayBuilder<T>.GetInstance();
                accumulator.Add(key, bucket);
            }

            bucket.Add(item);
        }

        var dictionary = new Dictionary<K, ImmutableArray<T>>(accumulator.Count, comparer);

        // freeze
        foreach (var pair in accumulator)
        {
            dictionary.Add(pair.Key, pair.Value.ToImmutableAndFree());
        }

        return dictionary;
    }

    public void AddRange(ArrayBuilder<T> items)
    {
        _items.AddRange(items._items);
    }

    public void AddRange<U>(ArrayBuilder<U> items, Func<U, T> selector)
    {
        foreach (var item in items)
        {
            _items.Add(selector(item));
        }
    }

    public void AddRange<U>(ArrayBuilder<U> items) where U : T
    {
        foreach (var item in items)
        {
            _items.Add(item);
        }
    }

    public void AddRange<U>(ArrayBuilder<U> items, int start, int length) where U : T
    {
        Debug.Assert(start >= 0 && length >= 0);
        Debug.Assert(start + length <= items.Count);
        for (int i = start, end = start + length; i < end; i++)
        {
            Add(items[i]);
        }
    }

    public void AddRange(ImmutableArray<T> items)
    {
        foreach (var item in items)
        {
            _items.Add(item);
        }
    }

    public void AddRange(ImmutableArray<T> items, int length)
    {
        AddRange(items, 0, length);
    }

    public void AddRange(ImmutableArray<T> items, int start, int length)
    {
        Debug.Assert(start >= 0 && length >= 0);
        Debug.Assert(start + length <= items.Length);
        for (int i = start, end = start + length; i < end; i++)
        {
            Add(items[i]);
        }
    }

    public void AddRange<S>(ImmutableArray<S> items) where S : class, T
    {
        AddRange(ImmutableArray<T>.CastUp(items));
    }

    public void AddRange(T[] items, int start, int length)
    {
        Debug.Assert(start >= 0 && length >= 0);
        Debug.Assert(start + length <= items.Length);
        for (int i = start, end = start + length; i < end; i++)
        {
            Add(items[i]);
        }
    }

    public void AddRange(IEnumerable<T> items)
    {
        _items.AddRange(items);
    }

    public void AddRange(params T[] items)
    {
        _items.AddRange(items);
    }

    public void AddRange(T[] items, int length)
    {
        AddRange(items, 0, length);
    }

#if COMPILERCORE
        public void AddRange(OneOrMany<T> items)
        {
            items.AddRangeTo(this);
        }
#endif

    public void Clip(int limit)
    {
        Debug.Assert(limit <= Count);
        if (limit < _items.Count)
        {
            _items.RemoveRange(limit, _items.Count - limit);
        }
    }

    public void ZeroInit(int count)
    {
        _items.Clear();
        SetCount(count);
    }

    public void AddMany(T item, int count)
    {
        EnsureCapacity(Count + count);

        for (var i = 0; i < count; i++)
        {
            Add(item);
        }
    }

    public void RemoveDuplicates()
    {
        var set = PooledHashSet<T>.GetInstance();

        var j = 0;
        for (var i = 0; i < Count; i++)
        {
            if (set.Add(this[i]))
            {
                this[j] = this[i];
                j++;
            }
        }

        Clip(j);
        set.Free();
    }

    public void SortAndRemoveDuplicates(IComparer<T> comparer)
    {
        if (Count <= 1)
        {
            return;
        }

        Sort(comparer);

        var j = 0;
        for (var i = 1; i < Count; i++)
        {
            if (comparer.Compare(this[j], this[i]) < 0)
            {
                j++;
                this[j] = this[i];
            }
        }

        Clip(j + 1);
    }

    public ImmutableArray<S> SelectDistinct<S>(Func<T, S> selector)
    {
        var result = ArrayBuilder<S>.GetInstance(Count);
        var set = PooledHashSet<S>.GetInstance();

        foreach (var item in this)
        {
            var selected = selector(item);
            if (set.Add(selected))
            {
                result.Add(selected);
            }
        }

        set.Free();
        return result.ToImmutableAndFree();
    }
}
