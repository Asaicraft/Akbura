using Akbura.Collections;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace Akbura.Collections;

/// <summary>
/// Represents a strongly typed list of objects that can be accessed by index. Provides methods to search, sort, and
/// manipulate lists.
/// </summary>
/// <remarks>
/// <para>This collection has the same performance characteristics as <see cref="List{T}"/>, but uses segmented
/// arrays to avoid allocations in the Large Object Heap.</para>
/// </remarks>
/// <typeparam name="T">The type of elements in the list.</typeparam>
[DebuggerTypeProxy(typeof(ICollectionDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
public class SegmentedList<T> : IList<T>, IList, IReadOnlyList<T>
{
    private const int DefaultCapacity = 4;
    private const int MaxLength = 0x7FFFFFC7;

    public SegmentedArray<T> _items;
    public int _size;
    public int _version;

    private static readonly SegmentedArray<T> s_emptyArray = new(0);
    private static IEnumerator<T>? s_emptyEnumerator;

    // Constructs a SegmentedList. The list is initially empty and has a capacity
    // of zero. Upon adding the first element to the list the capacity is
    // increased to DefaultCapacity, and then increased in multiples of two
    // as required.
    public SegmentedList()
    {
        _items = s_emptyArray;
    }

    // Constructs a SegmentedList with a given initial capacity. The list is
    // initially empty, but will have room for the given number of elements
    // before any reallocations are required.
    //
    public SegmentedList(int capacity)
    {
        ThrowHelper.ThrowIfNegative(capacity);

        _items = capacity == 0 ? s_emptyArray : new SegmentedArray<T>(capacity);
    }

    // Constructs a SegmentedList, copying the contents of the given collection. The
    // size and capacity of the new list will both be equal to the size of the
    // given collection.
    //
    public SegmentedList(IEnumerable<T> collection)
    {
        ThrowHelper.ThrowIfNull(collection);

        if (collection is SegmentedList<T> segmentedList)
        {
            _items = (SegmentedArray<T>)segmentedList._items.Clone();
            _size = segmentedList._size;
            return;
        }

        if (collection is ICollection<T> c)
        {
            var count = c.Count;
            if (count == 0)
            {
                _items = s_emptyArray;
                return;
            }
            else
            {
                _items = new SegmentedArray<T>(count);
                if (SegmentedCollectionsMarshal.AsSegments(_items) is { Length: 1 } segments)
                {
                    c.CopyTo(segments[0], 0);
                    _size = count;
                    return;
                }
            }

            // Continue below to add the items
        }
        else
        {
            _items = s_emptyArray;

            // Continue below to add the items
        }

        using var en = collection.GetEnumerator();
        while (en.MoveNext())
        {
            Add(en.Current);
        }
    }

    // Gets and sets the capacity of this list.  The capacity is the size of
    // the public array used to hold items.  When set, the internal
    // array of the list is reallocated to the given capacity.
    //
    public int Capacity
    {
        get => _items.Length;
        set
        {
            ThrowHelper.ThrowIfLessThan(value, _size);

            if (value == _items.Length)
            {
                return;
            }

            if (value <= 0)
            {
                _items = s_emptyArray;
                return;
            }

            if (_items.Length == 0)
            {
                // No data from existing array to reuse, just create a new one.
                _items = new SegmentedArray<T>(value);
            }
            else
            {
                // Rather than creating a copy of _items, instead reuse as much of it's data as possible.
                _items = CreateNewSegmentedArrayReusingOldSegments(_items, value);
            }
        }
    }

    private static SegmentedArray<T> CreateNewSegmentedArrayReusingOldSegments(SegmentedArray<T> oldArray, int newSize)
    {
        var segments = SegmentedCollectionsMarshal.AsSegments(oldArray);

        var oldSegmentCount = segments.Length;
        var newSegmentCount = (newSize + SegmentedArrayHelper.GetSegmentSize<T>() - 1) >> SegmentedArrayHelper.GetSegmentShift<T>();

        // Grow the array of segments, if necessary
        Array.Resize(ref segments, newSegmentCount);

        // Resize all segments to full segment size from the last old segment to the next to last
        // new segment.
        for (var i = oldSegmentCount - 1; i < newSegmentCount - 1; i++)
        {
            Array.Resize(ref segments[i], SegmentedArrayHelper.GetSegmentSize<T>());
        }

        // Resize the last segment
        var lastSegmentSize = newSize - ((newSegmentCount - 1) << SegmentedArrayHelper.GetSegmentShift<T>());
        Array.Resize(ref segments[newSegmentCount - 1], lastSegmentSize);

        return SegmentedCollectionsMarshal.AsSegmentedArray(newSize, segments);
    }

    // Read-only property describing how many elements are in the SegmentedList.
    public int Count => _size;

    bool IList.IsFixedSize => false;

    // Is this SegmentedList read-only?
    bool ICollection<T>.IsReadOnly => false;

    bool IList.IsReadOnly => false;

    // Is this SegmentedList synchronized (thread-safe)?
    bool ICollection.IsSynchronized => false;

    // Synchronization root for this object.
    object ICollection.SyncRoot => this;

    // Sets or Gets the element at the given index.
    public T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)_size)
            {
                ThrowHelper.IndexMustBeLess();
            }
            return _items[index];
        }

        set
        {
            if ((uint)index >= (uint)_size)
            {
                ThrowHelper.IndexMustBeLess();
            }
            _items[index] = value;
            _version++;
        }
    }

    private static bool IsCompatibleObject(object? value)
    {
        // Non-null values are fine.  Only accept nulls if T is a class or Nullable<U>.
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        return (value is T) || (value == null && default(T) == null);
    }

    object? IList.this[int index]
    {
        get => this[index];
        set
        {
            ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(value, nameof(value));

            try
            {
                this[index] = (T)value!;
            }
            catch (InvalidCastException)
            {
                ThrowHelper.WrongValueType(value, typeof(T));
            }
        }
    }


    // Adds the given object to the end of this list. The size of the list is
    // increased by one. If required, the capacity of the list is doubled
    // before adding the new element.
    //
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(T item)
    {
        _version++;
        var array = _items;
        var size = _size;
        if ((uint)size < (uint)array.Length)
        {
            _size = size + 1;
            array[size] = item;
        }
        else
        {
            AddWithResize(item);
        }
    }

    // Non-inline from SegmentedList.Add to improve its code quality as uncommon path
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void AddWithResize(T item)
    {
        AkburaDebug.Assert(_size == _items.Length);
        var size = _size;
        Grow(size + 1);
        _size = size + 1;
        _items[size] = item;
    }

    int IList.Add(object? item)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(item, nameof(item));

        try
        {
            Add((T)item!);
        }
        catch (InvalidCastException)
        {
            ThrowHelper.WrongValueType(item, typeof(T));
        }

        return Count - 1;
    }


    // Adds the elements of the given collection to the end of this list. If
    // required, the capacity of the list is increased to twice the previous
    // capacity or the new size, whichever is larger.
    //
    public void AddRange(IEnumerable<T> collection)
    {
        ThrowHelper.ThrowIfNull(collection);

        if (collection is ICollection<T> c)
        {
            var count = c.Count;
            if (count > 0)
            {
                if (_items.Length - _size < count)
                {
                    Grow(checked(_size + count));
                }

                if (c is SegmentedList<T> list)
                {
                    SegmentedArray.Copy(list._items, 0, _items, _size, list.Count);
                }
                else if (c is SegmentedArray<T> array)
                {
                    SegmentedArray.Copy(array, 0, _items, _size, array.Length);
                }
                else
                {
                    var targetIndex = _size;
                    foreach (var item in c)
                    {
                        _items[targetIndex++] = item;
                    }
                }

                _size += count;
                _version++;
            }
        }
        else
        {
            using var en = collection.GetEnumerator();
            while (en.MoveNext())
            {
                Add(en.Current);
            }
        }
    }


    public ReadOnlyCollection<T> AsReadOnly()
        => new(this);

    // Searches a section of the list for a given element using a binary search
    // algorithm. Elements of the list are compared to the search value using
    // the given IComparer interface. If comparer is null, elements of
    // the list are compared to the search value using the IComparable
    // interface, which in that case must be implemented by all elements of the
    // list and the given search value. This method assumes that the given
    // section of the list is already sorted; if this is not the case, the
    // result will be incorrect.
    //
    // The method returns the index of the given value in the list. If the
    // list does not contain the given value, the method returns a negative
    // integer. The bitwise complement operator (~) can be applied to a
    // negative result to produce the index of the first element (if any) that
    // is larger than the given search value. This is also the index at which
    // the search value should be inserted into the list in order for the list
    // to remain sorted.
    //
    // The method uses the Array.BinarySearch method to perform the
    // search.
    //
    public int BinarySearch(int index, int count, T item, IComparer<T>? comparer)
    {
        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        return SegmentedArray.BinarySearch(_items, index, count, item, comparer);
    }


    public int BinarySearch(T item)
        => BinarySearch(0, Count, item, null);

    public int BinarySearch(T item, IComparer<T>? comparer)
        => BinarySearch(0, Count, item, comparer);

    // Clears the contents of SegmentedList.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        _version++;

        if (!AkburaRuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _size = 0;
            return;
        }

        var size = _size;
        _size = 0;
        if (size > 0)
        {
            SegmentedArray.Clear(_items, 0, size); // Clear the elements so that the gc can reclaim the references.
        }
    }

    // Contains returns true if the specified element is in the SegmentedList.
    // It does a linear, O(n) search.  Equality is determined by calling
    // EqualityComparer<T>.Default.Equals().
    //
    public bool Contains(T item)
    {
        // PERF: IndexOf calls Array.IndexOf, which internally
        // calls EqualityComparer<T>.Default.IndexOf, which
        // is specialized for different types. This
        // boosts performance since instead of making a
        // virtual method call each iteration of the loop,
        // via EqualityComparer<T>.Default.Equals, we
        // only make one virtual call to EqualityComparer.IndexOf.

        return _size != 0 && IndexOf(item) >= 0;
    }

    bool IList.Contains(object? item)
    {
        if (IsCompatibleObject(item))
        {
            return Contains((T)item!);
        }
        return false;
    }

    public SegmentedList<TOutput> ConvertAll<TOutput>(Converter<T, TOutput> converter)
    {
        ThrowHelper.ThrowIfNull(converter);

        var list = new SegmentedList<TOutput>(_size);
        for (var i = 0; i < _size; i++)
        {
            list._items[i] = converter(_items[i]);
        }
        list._size = _size;
        return list;
    }


    // Copies this SegmentedList into array, which must be of a
    // compatible array type.
    public void CopyTo(T[] array)
        => CopyTo(array, 0);

    // Copies this SegmentedList into array, which must be of a
    // compatible array type.
    void ICollection.CopyTo(Array array, int arrayIndex)
    {
        if (array is not null && array.Rank != 1)
        {
            ThrowHelper.RankMultiDimNotSupported();
        }

        try
        {
            // Array.Copy проверит на null.
            SegmentedArray.Copy(_items, 0, array!, arrayIndex, _size);
        }
        catch (ArrayTypeMismatchException)
        {
            ThrowHelper.IncompatibleArrayType();
        }
    }


    // Copies a section of this list to the given array at the given index.
    //
    // The method uses the Array.Copy method to copy the elements.
    //
    public void CopyTo(int index, T[] array, int arrayIndex, int count)
    {
        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        // Делегируем остальную проверку ошибок в Array.Copy.
        SegmentedArray.Copy(_items, index, array, arrayIndex, count);
    }


    public void CopyTo(T[] array, int arrayIndex)
    {
        // Delegate rest of error checking to Array.Copy.
        SegmentedArray.Copy(_items, 0, array, arrayIndex, _size);
    }

    /// <summary>
    /// Ensures that the capacity of this list is at least the specified <paramref name="capacity"/>.
    /// If the current capacity of the list is less than specified <paramref name="capacity"/>,
    /// the capacity is increased by continuously twice current capacity until it is at least the specified <paramref name="capacity"/>.
    /// </summary>
    /// <param name="capacity">The minimum capacity to ensure.</param>
    /// <returns>The new capacity of this list.</returns>
    public int EnsureCapacity(int capacity)
    {
        ThrowHelper.ThrowIfNegative(capacity);

        if (_items.Length < capacity)
        {
            Grow(capacity);
        }

        return _items.Length;
    }


    /// <summary>
    /// Increase the capacity of this list to at least the specified <paramref name="capacity"/>.
    /// </summary>
    /// <param name="capacity">The minimum capacity to ensure.</param>
    public void Grow(int capacity)
    {
        AkburaDebug.Assert(_items.Length < capacity);

        var newCapacity = 0;

        if (_items.Length < SegmentedArrayHelper.GetSegmentSize<T>() / 2)
        {
            // The array isn't near the maximum segment size. If the array is empty, the new capacity 
            // should be DefaultCapacity. Otherwise, the new capacity should be double the current array size.
            newCapacity = _items.Length == 0 ? DefaultCapacity : _items.Length * 2;
        }
        else if (_items.Length < SegmentedArrayHelper.GetSegmentSize<T>())
        {
            // There is only a single segment that is over half full. Increase it to a full segment.
            newCapacity = SegmentedArrayHelper.GetSegmentSize<T>();
        }
        else
        {
            // If the last segment is fully sized, increase the number of segments by the desired growth rate
            if (0 == (_items.Length & SegmentedArrayHelper.GetOffsetMask<T>()))
            {
                // This value determines the growth rate of the number of segments to use.
                // For a value of 3, this means the segment count will grow at a rate of
                // 1 + (1 >> 3) or 12.5%
                const int segmentGrowthShiftValue = 3;

                var oldSegmentCount = (_items.Length + SegmentedArrayHelper.GetSegmentSize<T>() - 1) >> SegmentedArrayHelper.GetSegmentShift<T>();
                var newSegmentCount = oldSegmentCount + Math.Max(1, oldSegmentCount >> segmentGrowthShiftValue);

                newCapacity = SegmentedArrayHelper.GetSegmentSize<T>() * newSegmentCount;
            }
        }

        // If the computed capacity is less than specified, set to the original argument.
        // Capacities exceeding Array.MaxLength will be surfaced as OutOfMemoryException by Array.Resize.
        if (newCapacity < capacity)
        {
            newCapacity = capacity;
        }

        if (newCapacity > SegmentedArrayHelper.GetSegmentSize<T>())
        {
            // If the last segment isn't fully sized, increase the new capacity such that it will be.
            var lastSegmentLength = newCapacity & SegmentedArrayHelper.GetOffsetMask<T>();
            if (lastSegmentLength > 0)
            {
                newCapacity = (newCapacity - lastSegmentLength) + SegmentedArrayHelper.GetSegmentSize<T>();
            }

            // Allow the list to grow to maximum possible capacity (~2G elements) before encountering overflow.
            // Note that this check works even when _items.Length overflowed thanks to the (uint) cast
            if ((uint)newCapacity > MaxLength)
            {
                newCapacity = MaxLength;
            }
        }

        Capacity = newCapacity;
    }

    public bool Exists(Predicate<T> match)
        => FindIndex(match) != -1;

    public T? Find(Predicate<T> match)
    {
        ThrowHelper.ThrowIfNull(match);

        for (var i = 0; i < _size; i++)
        {
            if (match(_items[i]))
            {
                return _items[i];
            }
        }
        return default;
    }

    public SegmentedList<T> FindAll(Predicate<T> match)
    {
        ThrowHelper.ThrowIfNull(match);

        var list = new SegmentedList<T>();
        for (var i = 0; i < _size; i++)
        {
            if (match(_items[i]))
            {
                list.Add(_items[i]);
            }
        }
        return list;
    }


    public int FindIndex(Predicate<T> match)
        => FindIndex(0, _size, match);

    public int FindIndex(int startIndex, Predicate<T> match)
        => FindIndex(startIndex, _size - startIndex, match);

    public int FindIndex(int startIndex, int count, Predicate<T> match)
    {
        if ((uint)startIndex > (uint)_size)
        {
            ThrowHelper.IndexMustBeLessOrEqual();
        }

        if (count < 0 || startIndex > _size - count)
        {
            ThrowHelper.CountOutOfRange();
        }

        ThrowHelper.ThrowIfNull(match);

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

    public T? FindLast(Predicate<T> match)
    {
        ThrowHelper.ThrowIfNull(match);

        for (var i = _size - 1; i >= 0; i--)
        {
            if (match(_items[i]))
            {
                return _items[i];
            }
        }
        return default;
    }


    public int FindLastIndex(Predicate<T> match)
        => FindLastIndex(_size - 1, _size, match);

    public int FindLastIndex(int startIndex, Predicate<T> match)
        => FindLastIndex(startIndex, startIndex + 1, match);

    public int FindLastIndex(int startIndex, int count, Predicate<T> match)
    {
        ThrowHelper.ThrowIfNull(match);

        if (_size == 0)
        {
            // Special case for empty SegmentedList
            if (startIndex != -1)
            {
                ThrowHelper.IndexMustBeLess();
            }
        }
        else
        {
            // Ensure startIndex is within range
            if ((uint)startIndex >= (uint)_size)
            {
                ThrowHelper.IndexMustBeLess();
            }
        }

        // Ensure count is valid
        if (count < 0 || startIndex - count + 1 < 0)
        {
            ThrowHelper.CountOutOfRange();
        }

        var endIndex = startIndex - count;
        for (var i = startIndex; i > endIndex; i--)
        {
            if (match(_items[i]))
            {
                return i;
            }
        }
        return -1;
    }


    public void ForEach(Action<T> action)
    {
        ThrowHelper.ThrowIfNull(action);

        var version = _version;

        for (var i = 0; i < _size; i++)
        {
            if (version != _version)
            {
                break;
            }
            action(_items[i]);
        }

        if (version != _version)
        {
            ThrowHelper.EnumFailedVersion();
        }
    }


    // Returns an enumerator for this list with the given
    // permission for removal of elements. If modifications made to the list
    // while an enumeration is in progress, the MoveNext and
    // GetObject methods of the enumerator will throw an exception.
    //
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() =>
        Count == 0 ? GetEmptyEnumerator() :
        GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable<T>)this).GetEnumerator();

    private static IEnumerator<T> GetEmptyEnumerator()
    {
        return LazyInitializer.EnsureInitialized(ref s_emptyEnumerator, static () => new Enumerator([]))!;
    }

    public SegmentedList<T> GetRange(int index, int count)
    {
        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        var list = new SegmentedList<T>(count);
        SegmentedArray.Copy(_items, index, list._items, 0, count);
        list._size = count;
        return list;
    }


    /// <summary>
    /// Creates a shallow copy of a range of elements in the source <see cref="SegmentedList{T}" />.
    /// </summary>
    /// <param name="start">The zero-based <see cref="SegmentedList{T}" /> index at which the range starts.</param>
    /// <param name="length">The length of the range.</param>
    /// <returns>A shallow copy of a range of elements in the source <see cref="SegmentedList{T}" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="start" /> is less than 0.
    /// -or-
    /// <paramref name="length" /> is less than 0.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="start" /> and <paramref name="length" /> do not denote a valid range of elements in the <see cref="SegmentedList{T}" />.</exception>
    public SegmentedList<T> Slice(int start, int length) => GetRange(start, length);

    // Returns the index of the first occurrence of a given value in a range of
    // this list. The list is searched forwards from beginning to end.
    // The elements of the list are compared to the given value using the
    // Object.Equals method.
    //
    // This method uses the Array.IndexOf method to perform the
    // search.
    //
    public int IndexOf(T item)
        => SegmentedArray.IndexOf(_items, item, 0, _size);

    int IList.IndexOf(object? item)
    {
        if (IsCompatibleObject(item))
        {
            return IndexOf((T)item!);
        }
        return -1;
    }

    // Returns the index of the first occurrence of a given value in a range of
    // this list. The list is searched forwards, starting at index
    // index and ending at count number of elements. The
    // elements of the list are compared to the given value using the
    // Object.Equals method.
    //
    // This method uses the Array.IndexOf method to perform the
    // search.
    //
    public int IndexOf(T item, int index)
    {
        if (index > _size)
        {
            ThrowHelper.IndexMustBeLessOrEqual();
        }

        return SegmentedArray.IndexOf(_items, item, index, _size - index);
    }


    // Returns the index of the first occurrence of a given value in a range of
    // this list. The list is searched forwards, starting at index
    // index and upto count number of elements. The
    // elements of the list are compared to the given value using the
    // Object.Equals method.
    //
    // This method uses the Array.IndexOf method to perform the
    // search.
    //
    public int IndexOf(T item, int index, int count)
    {
        if (index > _size)
        {
            ThrowHelper.IndexMustBeLessOrEqual();
        }

        if (count < 0 || index > _size - count)
        {
            ThrowHelper.CountOutOfRange();
        }

        return SegmentedArray.IndexOf(_items, item, index, count);
    }


    public int IndexOf(T item, int index, int count, IEqualityComparer<T>? comparer)
    {
        if (index > _size)
        {
            ThrowHelper.IndexMustBeLessOrEqual();
        }

        if (count < 0 || index > _size - count)
        {
            ThrowHelper.CountOutOfRange();
        }

        return SegmentedArray.IndexOf(_items, item, index, count, comparer);
    }


    // Inserts an element into this list at a given index. The size of the list
    // is increased by one. If required, the capacity of the list is doubled
    // before inserting the new element.
    //  
    public void Insert(int index, T item)
    {
        // Note that insertions at the end are legal.
        if ((uint)index > (uint)_size)
        {
            ThrowHelper.ListInsertOutOfRange(nameof(index));
        }

        if (_size == _items.Length)
        {
            Grow(_size + 1);
        }

        if (index < _size)
        {
            SegmentedArray.Copy(_items, index, _items, index + 1, _size - index);
        }

        _items[index] = item;
        _size++;
        _version++;
    }

    void IList.Insert(int index, object? item)
    {
        ThrowHelper.IfNullAndNullsAreIllegalThenThrow<T>(item, nameof(item));

        try
        {
            Insert(index, (T)item!);
        }
        catch (InvalidCastException)
        {
            ThrowHelper.WrongValueType(item, typeof(T));
        }
    }


    // Inserts the elements of the given collection at a given index. If
    // required, the capacity of the list is increased to twice the previous
    // capacity or the new size, whichever is larger.  Ranges may be added
    // to the end of the list by setting index to the SegmentedList's size.
    //
    public void InsertRange(int index, IEnumerable<T> collection)
    {
        ThrowHelper.ThrowIfNull(collection);

        if ((uint)index > (uint)_size)
        {
            ThrowHelper.IndexMustBeLessOrEqual();
        }

        if (collection is ICollection<T> c)
        {
            var count = c.Count;
            if (count > 0)
            {
                if (_items.Length - _size < count)
                {
                    Grow(checked(_size + count));
                }

                if (index < _size)
                {
                    SegmentedArray.Copy(_items, index, _items, index + count, _size - index);
                }

                // If inserting the same list into itself, handle separately.
                if (this == c)
                {
                    SegmentedArray.Copy(_items, 0, _items, index, index);
                    SegmentedArray.Copy(_items, index + count, _items, index * 2, _size - index);
                }
                else if (c is SegmentedList<T> list)
                {
                    SegmentedArray.Copy(list._items, 0, _items, index, list.Count);
                }
                else if (c is SegmentedArray<T> array)
                {
                    SegmentedArray.Copy(array, 0, _items, index, array.Length);
                }
                else
                {
                    var targetIndex = index;
                    foreach (var item in c)
                    {
                        _items[targetIndex++] = item;
                    }
                }

                _size += count;
                _version++;
            }
        }
        else
        {
            using var en = collection.GetEnumerator();
            while (en.MoveNext())
            {
                Insert(index++, en.Current);
            }
        }
    }


    // Returns the index of the last occurrence of a given value in a range of
    // this list. The list is searched backwards, starting at the end
    // and ending at the first element in the list. The elements of the list
    // are compared to the given value using the Object.Equals method.
    //
    // This method uses the Array.LastIndexOf method to perform the
    // search.
    //
    public int LastIndexOf(T item)
    {
        if (_size == 0)
        {  // Special case for empty list
            return -1;
        }
        else
        {
            return LastIndexOf(item, _size - 1, _size);
        }
    }

    // Returns the index of the last occurrence of a given value in a range of
    // this list. The list is searched backwards, starting at index
    // index and ending at the first element in the list. The
    // elements of the list are compared to the given value using the
    // Object.Equals method.
    //
    // This method uses the Array.LastIndexOf method to perform the
    // search.
    //
    public int LastIndexOf(T item, int index)
    {
        if (index >= _size)
        {
            ThrowHelper.IndexMustBeLess();
        }

        return LastIndexOf(item, index, index + 1);
    }


    // Returns the index of the last occurrence of a given value in a range of
    // this list. The list is searched backwards, starting at index
    // index and upto count elements. The elements of
    // the list are compared to the given value using the Object.Equals
    // method.
    //
    // This method uses the Array.LastIndexOf method to perform the
    // search.
    //
    public int LastIndexOf(T item, int index, int count)
    {
        if (_size == 0)
        {
            // Special case for empty list
            return -1;
        }

        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (index >= _size)
        {
            ThrowHelper.BiggerThanCollection(nameof(index));
        }

        if (count > index + 1)
        {
            ThrowHelper.BiggerThanCollection(nameof(count));
        }

        return SegmentedArray.LastIndexOf(_items, item, index, count);
    }


    public int LastIndexOf(T item, int index, int count, IEqualityComparer<T>? comparer)
    {
        if (_size == 0)
        {
            // Special case for empty list
            return -1;
        }

        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (index >= _size)
        {
            ThrowHelper.BiggerThanCollection(nameof(index));
        }

        if (count > index + 1)
        {
            ThrowHelper.BiggerThanCollection(nameof(count));
        }

        return SegmentedArray.LastIndexOf(_items, item, index, count, comparer);
    }


    // Removes the first occurrence of the given element, if found.
    // The size of the list is decreased by one if successful.
    public bool Remove(T item)
    {
        var index = IndexOf(item);
        if (index >= 0)
        {
            RemoveAt(index);
            return true;
        }

        return false;
    }

    void IList.Remove(object? item)
    {
        if (IsCompatibleObject(item))
        {
            Remove((T)item!);
        }
    }

    // This method removes all items which matches the predicate.
    // The complexity is O(n).
    public int RemoveAll(Predicate<T> match)
    {
        ThrowHelper.ThrowIfNull(match);

        var freeIndex = 0; // The first free slot in items array

        // Find the first item that needs to be removed.
        while (freeIndex < _size && !match(_items[freeIndex]))
        {
            freeIndex++;
        }

        if (freeIndex >= _size)
        {
            return 0;
        }

        var current = freeIndex + 1;
        while (current < _size)
        {
            // Find the first item that needs to be kept.
            while (current < _size && match(_items[current]))
            {
                current++;
            }

            if (current < _size)
            {
                // Copy item to the free slot.
                _items[freeIndex++] = _items[current++];
            }
        }

        if (AkburaRuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            // Clear the elements so that GC can reclaim the references.
            SegmentedArray.Clear(_items, freeIndex, _size - freeIndex);
        }

        var result = _size - freeIndex;
        _size = freeIndex;
        _version++;
        return result;
    }


    // Removes the element at the given index. The size of the list is
    // decreased by one.
    public void RemoveAt(int index)
    {
        if ((uint)index >= (uint)_size)
        {
            ThrowHelper.IndexMustBeLess(nameof(index));
        }

        _size--;

        if (index < _size)
        {
            SegmentedArray.Copy(_items, index + 1, _items, index, _size - index);
        }

        if (AkburaRuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            _items[_size] = default!;
        }

        _version++;
    }


    // Removes a range of elements from this list.
    public void RemoveRange(int index, int count)
    {
        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        if (count > 0)
        {
            _size -= count;

            if (index < _size)
            {
                SegmentedArray.Copy(_items, index + count, _items, index, _size - index);
            }

            _version++;

            if (AkburaRuntimeHelpers.IsReferenceOrContainsReferences<T>())
            {
                SegmentedArray.Clear(_items, _size, count);
            }
        }
    }


    // Reverses the elements in this list.
    public void Reverse()
        => Reverse(0, Count);

    // Reverses the elements in a range of this list. Following a call to this
    // method, an element in the range given by index and count
    // which was previously located at index i will now be located at
    // index index + (index + count - i - 1).
    //
    public void Reverse(int index, int count)
    {
        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        if (count > 1)
        {
            SegmentedArray.Reverse(_items, index, count);
        }

        _version++;
    }


    // Sorts the elements in this list.  Uses the default comparer and
    // Array.Sort.
    public void Sort()
        => Sort(0, Count, null);

    // Sorts the elements in this list.  Uses Array.Sort with the
    // provided comparer.
    public void Sort(IComparer<T>? comparer)
        => Sort(0, Count, comparer);

    // Sorts the elements in a section of this list. The sort compares the
    // elements to each other using the given IComparer interface. If
    // comparer is null, the elements are compared to each other using
    // the IComparable interface, which in that case must be implemented by all
    // elements of the list.
    //
    // This method uses the Array.Sort method to sort the elements.
    //
    public void Sort(int index, int count, IComparer<T>? comparer)
    {
        ThrowHelper.ThrowIfNegative(index);
        ThrowHelper.ThrowIfNegative(count);

        if (_size - index < count)
        {
            ThrowHelper.InvalidOffLen();
        }

        if (count > 1)
        {
            SegmentedArray.Sort(_items, index, count, comparer);
        }

        _version++;
    }


    public void Sort(Comparison<T> comparison)
    {
        ThrowHelper.ThrowIfNull(comparison);

        if (_size > 1)
        {
            var segment = new SegmentedArraySegment<T>(_items, 0, _size);
            SegmentedArraySortHelper<T>.Sort(segment, comparison);
        }
        _version++;
    }

    // ToArray returns an array containing the contents of the SegmentedList.
    // This requires copying the SegmentedList, which is an O(n) operation.
    public T[] ToArray()
    {
        if (_size == 0)
        {
            return [];
        }

        var array = new T[_size];
        SegmentedArray.Copy(_items, array, _size);
        return array;
    }

    // Sets the capacity of this list to the size of the list. This method can
    // be used to minimize a list's memory overhead once it is known that no
    // new elements will be added to the list. To completely clear a list and
    // release all memory referenced by the list, execute the following
    // statements:
    //
    // list.Clear();
    // list.TrimExcess();
    //
    public void TrimExcess()
    {
        var threshold = (int)(_items.Length * 0.9);
        if (_size < threshold)
        {
            Capacity = _size;
        }
    }

    public bool TrueForAll(Predicate<T> match)
    {
        if (match == null)
        {
            ThrowHelper.ThrowArgumentNullException(nameof(match));
        }

        for (var i = 0; i < _size; i++)
        {
            if (!match(_items[i]))
            {
                return false;
            }
        }
        return true;
    }

    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly SegmentedList<T> _list;
        private int _index;
        private readonly int _version;
        private T? _current;

        public Enumerator(SegmentedList<T> list)
        {
            _list = list;
            _index = 0;
            _version = list._version;
            _current = default;
        }

        public readonly void Dispose()
        {
        }

        public bool MoveNext()
        {
            var localList = _list;

            if (_version == localList._version && ((uint)_index < (uint)localList._size))
            {
                _current = localList._items[_index];
                _index++;
                return true;
            }
            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
            {
                ThrowHelper.EnumFailedVersion();
            }

            _index = _list._size + 1;
            _current = default;
            return false;
        }


        public readonly T Current => _current!;

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_index == 0 || _index == _list._size + 1)
                {
                    ThrowHelper.EnumOpCantHappen();
                }
                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                ThrowHelper.EnumFailedVersion();
            }

            _index = 0;
            _current = default;
        }

    }

    public TestAccessor GetTestAccessor()=> new(this);

    public readonly struct TestAccessor(SegmentedList<T> instance)
    {
        public ref SegmentedArray<T> Items => ref instance._items;
    }
}