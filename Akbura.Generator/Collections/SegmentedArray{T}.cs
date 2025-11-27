using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;

/// <summary>
/// Defines a fixed-size collection with the same API surface and behavior as an "SZArray", which is a
/// single-dimensional zero-based array commonly represented in C# as <c>T[]</c>. The implementation of this
/// collection uses segmented arrays to avoid placing objects on the Large Object Heap.
/// </summary>
/// <typeparam name="T">The type of elements stored in the array.</typeparam>
public readonly partial struct SegmentedArray<T> : ICloneable, IList, IStructuralComparable, IStructuralEquatable, IList<T>, IReadOnlyList<T>, IEquatable<SegmentedArray<T>>
{
    /// <summary>
    /// The number of elements in each page of the segmented array of type <typeparamref name="T"/>.
    /// </summary>
    /// <remarks>
    /// <para>The segment size is calculated according to <see cref="Unsafe.SizeOf{T}"/>, performs the IL operation
    /// defined by <see cref="OpCodes.Sizeof"/>. ECMA-335 defines this operation with the following note:</para>
    ///
    /// <para><c>sizeof</c> returns the total size that would be occupied by each element in an array of this type –
    /// including any padding the implementation chooses to add. Specifically, array elements lie <c>sizeof</c>
    /// bytes apart.</para>
    /// </remarks>
    private static int SegmentSize => SegmentedArrayHelper.GetSegmentSize<T>();

    /// <summary>
    /// The bit shift to apply to an array index to get the page index within <see cref="_items"/>.
    /// </summary>
    private static int SegmentShift => SegmentedArrayHelper.GetSegmentShift<T>();

    /// <summary>
    /// The bit mask to apply to an array index to get the index within a page of <see cref="_items"/>.
    /// </summary>
    private static int OffsetMask => SegmentedArrayHelper.GetOffsetMask<T>();

    private readonly int _length;
    private readonly T[][] _items;

    public SegmentedArray(int length)
    {
        ThrowHelper.ThrowIfNegative(length);

        if (length == 0)
        {
            _items = [];
            _length = 0;
        }
        else
        {
            _items = new T[(length + SegmentSize - 1) >> SegmentShift][];
            for (var i = 0; i < _items.Length - 1; i++)
            {
                _items[i] = new T[SegmentSize];
            }

            // Make sure the last page only contains the number of elements required for the desired length. This
            // collection is not resizeable so any additional padding would be a waste of space.
            //
            // Avoid using (length & s_offsetMask) because it doesn't handle a last page size of s_segmentSize.
            var lastPageSize = length - ((_items.Length - 1) << SegmentShift);

            _items[^1] = new T[lastPageSize];
            _length = length;
        }
    }

    private SegmentedArray(int length, T[][] items)
    {
        _length = length;
        _items = items;
    }

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public int Length => _length;

    public object SyncRoot => _items;

    public ref T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref _items[index >> SegmentShift][index & OffsetMask];
    }

    int ICollection.Count => Length;

    int ICollection<T>.Count => Length;

    int IReadOnlyCollection<T>.Count => Length;

    T IReadOnlyList<T>.this[int index] => this[index];

    T IList<T>.this[int index]
    {
        get => this[index];
        set => this[index] = value;
    }

    object? IList.this[int index]
    {
        get => this[index];
        set => this[index] = (T)value!;
    }

    public object Clone()
    {
        var items = (T[][])_items.Clone();
        for (var i = 0; i < items.Length; i++)
        {
            items[i] = (T[])items[i].Clone();
        }

        return new SegmentedArray<T>(Length, items);
    }

    public void CopyTo(Array array, int index)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            _items[i].CopyTo(array, index + (i * SegmentSize));
        }
    }

    void ICollection<T>.CopyTo(T[] array, int arrayIndex)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            var collection = _items[i];
            collection.CopyTo(array, arrayIndex + (i * SegmentSize));
        }
    }

    public Enumerator GetEnumerator()
        => new(this);

    public override bool Equals(object? obj)
    {
        return obj is SegmentedArray<T> other
            && Equals(other);
    }

    public override int GetHashCode()
    {
        return _items.GetHashCode();
    }

    public bool Equals(SegmentedArray<T> other)
    {
        return _items == other._items;
    }

    int IList.Add(object? value)
    {
        return ThrowHelper.FixedSizeCollection<int>();
    }

    void ICollection<T>.Add(T value)
    {
        ThrowHelper.FixedSizeCollection();
    }

    void IList.Clear()
    {
        // Matches System.Array
        // https://github.com/dotnet/runtime/blob/e0ec035994179e8ebd6ccf081711ee11d4c5491b/src/libraries/System.Private.CoreLib/src/System/Array.cs#L279-L282
        foreach (IList list in _items)
        {
            list.Clear();
        }
    }

    void ICollection<T>.Clear()
    {
        ThrowHelper.FixedSizeCollection();
    }

    bool IList.Contains(object? value)
    {
        foreach (IList list in _items)
        {
            if (list.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    bool ICollection<T>.Contains(T value)
    {
        foreach (ICollection<T> collection in _items)
        {
            if (collection.Contains(value))
            {
                return true;
            }
        }

        return false;
    }

    int IList.IndexOf(object? value)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            IList list = _items[i];
            var index = list.IndexOf(value);
            if (index >= 0)
            {
                return index + i * SegmentSize;
            }
        }

        return -1;
    }

    int IList<T>.IndexOf(T value)
    {
        for (var i = 0; i < _items.Length; i++)
        {
            var index = ((IList<T>)_items[i]).IndexOf(value);

            if (index >= 0)
            {
                return index + i * SegmentSize;
            }
        }

        return -1;
    }

    void IList.Insert(int index, object? value)
    {
        ThrowHelper.FixedSizeCollection();
    }

    void IList<T>.Insert(int index, T value)
    {
        ThrowHelper.FixedSizeCollection();
    }

    void IList.Remove(object? value)
    {
        ThrowHelper.FixedSizeCollection();
    }

    bool ICollection<T>.Remove(T value)
    {
        return ThrowHelper.FixedSizeCollection<bool>();
    }

    void IList.RemoveAt(int index)
    {
        ThrowHelper.FixedSizeCollection();
    }

    void IList<T>.RemoveAt(int index)
    {
        ThrowHelper.FixedSizeCollection();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
        => GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator()
        => GetEnumerator();

    int IStructuralComparable.CompareTo(object? other, IComparer comparer)
    {
        if (other is null)
        {
            return 1;
        }

        // Matches System.Array
        // https://github.com/dotnet/runtime/blob/e0ec035994179e8ebd6ccf081711ee11d4c5491b/src/libraries/System.Private.CoreLib/src/System/Array.cs#L320-L323
        if (other is not SegmentedArray<T> o
            || Length != o.Length)
        {
            ThrowHelper.OtherNotArrayOfCorrectLength(nameof(other));
            return 0;
        }

        for (var i = 0; i < Length; i++)
        {
            var result = comparer.Compare(this[i], o[i]);
            
            if (result != 0)
            {
                return result;
            }
        }

        return 0;
    }

    bool IStructuralEquatable.Equals(object? other, IEqualityComparer comparer)
    {
        if (other is null)
        {
            return false;
        }

        if (other is not SegmentedArray<T> o)
        {
            return false;
        }

        if (ReferenceEquals(_items, o._items))
        {
            return true;
        }

        if (Length != o.Length)
        {
            return false;
        }

        for (var i = 0; i < Length; i++)
        {
            if (!comparer.Equals(this[i], o[i]))
            {
                return false;
            }
        }

        return true;
    }

    int IStructuralEquatable.GetHashCode(IEqualityComparer comparer)
    {
        ThrowHelper.ThrowIfNull(comparer);

        // Matches System.Array
        // https://github.com/dotnet/runtime/blob/e0ec035994179e8ebd6ccf081711ee11d4c5491b/src/libraries/System.Private.CoreLib/src/System/Array.cs#L380-L383
        var ret = 0;
        for (var i = Length >= 8 ? Length - 8 : 0; i < Length; i++)
        {
            ret = HashCode.Combine(comparer.GetHashCode(this[i]!), ret);
        }

        return ret;
    }

    public struct Enumerator : IEnumerator<T>
    {
        private readonly T[][] _items;
        private int _nextItemSegment;
        private int _nextItemIndex;
        private T _current;

        public Enumerator(SegmentedArray<T> array)
        {
            _items = array._items;
            _nextItemSegment = 0;
            _nextItemIndex = 0;
            _current = default!;
        }

        public readonly T Current => _current;
        readonly object? IEnumerator.Current => Current;

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (_items.Length == 0)
            {
                return false;
            }

            if (_nextItemIndex == _items[_nextItemSegment].Length)
            {
                if (_nextItemSegment == _items.Length - 1)
                {
                    return false;
                }

                _nextItemSegment++;
                _nextItemIndex = 0;
            }

            _current = _items[_nextItemSegment][_nextItemIndex];
            _nextItemIndex++;
            return true;
        }

        public void Reset()
        {
            _nextItemSegment = 0;
            _nextItemIndex = 0;
            _current = default!;
        }
    }

    public static class TestAccessor
    {
        public static int SegmentSize => SegmentedArray<T>.SegmentSize;
    }

    public static bool operator ==(SegmentedArray<T> left, SegmentedArray<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(SegmentedArray<T> left, SegmentedArray<T> right)
    {
        return !(left == right);
    }
}