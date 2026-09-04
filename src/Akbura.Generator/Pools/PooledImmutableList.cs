using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#if AKBURA_RUNTIME
namespace Akbura.RuntimePools;
#else
namespace Akbura.Pools;
#endif

internal readonly struct PooledImmutableList<T> : IReadOnlyList<T>
{
    private const int SmallCapacity = 16;
    private const int SmallCapacityPoolSize = 32;

    private const int LargeCapacity = 64;
    private const int LargeCapacityPoolSize = 16;

    private static readonly ObjectPool<T[]> s_smallCapacityPool = new(() => new T[SmallCapacity], SmallCapacityPoolSize);

    private static readonly ObjectPool<T[]> s_largeCapacityPool = new(() => new T[LargeCapacity], LargeCapacityPoolSize);

    private readonly int _size;
    private readonly T[]? _array;

    private PooledImmutableList(T[] array, int size)
    {
        AkburaDebug.Assert(array != null);
        Debug.Assert(size >= 0);
        Debug.Assert(size <= array.Length);

        _array = array;
        _size = size;
    }

    public int Count => _size;

    public int Length => _size;

    public int Capacity => _array?.Length ?? 0;

    public bool IsEmpty => _size == 0;

    public bool IsDefaultOrEmpty => _size == 0;

    public T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if ((uint)index >= (uint)_size)
            {
                ThrowIndexOutOfRange();
            }

            return _array![index];
        }
    }

    public static PooledImmutableList<T> Create(T[] buffer)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        return Create(buffer.AsSpan());
    }

    public static PooledImmutableList<T> Create(T[] buffer, int start, int length)
    {
        if (buffer == null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if ((uint)start > (uint)buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(start));
        }

        if ((uint)length > (uint)(buffer.Length - start))
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        return Create(buffer.AsSpan(start, length));
    }

    public static PooledImmutableList<T> Create(scoped ReadOnlySpan<T> buffer)
    {
        if (buffer.IsEmpty)
        {
            return default;
        }

        var array = Rent(buffer.Length);

        buffer.CopyTo(array);

        return new PooledImmutableList<T>(array, buffer.Length);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_array, _size);
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> AsSpan()
    {
        return _array == null
            ? default
            : new ReadOnlySpan<T>(_array, 0, _size);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ReadOnlySpan<T> AsSpan(int start, int length)
    {
        return AsSpan().Slice(start, length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref readonly T ItemRef(int index)
    {
        if ((uint)index >= (uint)_size)
        {
            ThrowIndexOutOfRange();
        }

        return ref _array![index];
    }

    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[]? _items;
        private readonly int _count;
        private int _index;

        public Enumerator(T[]? items, int count)
        {
            _items = items;
            _count = count;
            _index = -1;
        }

        public T Current
        {
            get
            {
                if (_index < 0 || _index >= _count)
                {
                    throw new InvalidOperationException();
                }

                return _items![_index];
            }
        }

        object IEnumerator.Current => Current!;

        public bool MoveNext()
        {
            var index = _index + 1;

            if ((uint)index < (uint)_count)
            {
                _index = index;
                return true;
            }

            _index = _count;
            return false;
        }

        public void Reset()
        {
            _index = -1;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>
    /// Returns the backing storage to its pool.
    /// The owner must call this exactly once for every non-empty list.
    /// </summary>
    internal void ReturnToPool()
    {
        var array = _array;

        if (array == null)
        {
            return;
        }

        var containsReferences = AkburaRuntimeHelpers.IsReferenceOrContainsReferences<T>();

        if (array.Length == SmallCapacity)
        {
            if (containsReferences)
            {
                Array.Clear(array, 0, _size);
            }

            s_smallCapacityPool.Free(array);
            return;
        }

        if (array.Length == LargeCapacity)
        {
            if (containsReferences)
            {
                Array.Clear(array, 0, _size);
            }

            s_largeCapacityPool.Free(array);
            return;
        }

        ArrayPool<T>.Shared.Return(array, clearArray: containsReferences);
    }

    private static T[] Rent(int size)
    {
        Debug.Assert(size > 0);

        if (size <= SmallCapacity)
        {
            return s_smallCapacityPool.Allocate();
        }

        if (size <= LargeCapacity)
        {
            return s_largeCapacityPool.Allocate();
        }

        return ArrayPool<T>.Shared.Rent(size);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowIndexOutOfRange()
    {
        throw new IndexOutOfRangeException();
    }
}
