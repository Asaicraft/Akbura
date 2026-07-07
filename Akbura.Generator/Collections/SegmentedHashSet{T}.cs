// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Akbura.Collections;

[DebuggerTypeProxy(typeof(ICollectionDebugView<>))]
[DebuggerDisplay("Count = {Count}")]
internal class SegmentedHashSet<T> : ICollection<T>, ISet<T>, IReadOnlyCollection<T>
{
    private HashSet<T> _set;

    public SegmentedHashSet()
        : this((IEqualityComparer<T>?)null)
    {
    }

    public SegmentedHashSet(IEqualityComparer<T>? comparer)
    {
        _set = new HashSet<T>(comparer);
    }

    public SegmentedHashSet(int capacity)
        : this(capacity, null)
    {
    }

    public SegmentedHashSet(int capacity, IEqualityComparer<T>? comparer)
        : this(comparer)
    {
        if (capacity < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
    }

    public SegmentedHashSet(IEnumerable<T> collection)
        : this(collection, null)
    {
    }

    public SegmentedHashSet(IEnumerable<T> collection, IEqualityComparer<T>? comparer)
    {
        if (collection is null)
        {
            throw new ArgumentNullException(nameof(collection));
        }

        _set = new HashSet<T>(collection, comparer);
    }

    public IEqualityComparer<T> Comparer => _set.Comparer;

    public int Count => _set.Count;

    bool ICollection<T>.IsReadOnly => false;

    public bool Add(T item)
    {
        return _set.Add(item);
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    public void Clear()
    {
        _set.Clear();
    }

    public bool Contains(T item)
    {
        return _set.Contains(item);
    }

    public void CopyTo(T[] array)
    {
        CopyTo(array, 0);
    }

    public void CopyTo(T[] array, int arrayIndex)
    {
        _set.CopyTo(array, arrayIndex);
    }

    public void CopyTo(T[] array, int arrayIndex, int count)
    {
        if (array is null)
        {
            throw new ArgumentNullException(nameof(array));
        }

        if (arrayIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(arrayIndex));
        }

        if (count < 0 || count > _set.Count || array.Length - arrayIndex < count)
        {
            throw new ArgumentOutOfRangeException(nameof(count));
        }

        var copied = 0;
        foreach (var item in _set)
        {
            if (copied == count)
            {
                break;
            }

            array[arrayIndex + copied] = item;
            copied++;
        }
    }

    public void ExceptWith(IEnumerable<T> other)
    {
        _set.ExceptWith(other);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(_set.GetEnumerator());
    }

    public void IntersectWith(IEnumerable<T> other)
    {
        _set.IntersectWith(other);
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        return _set.IsProperSubsetOf(other);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        return _set.IsProperSupersetOf(other);
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        return _set.IsSubsetOf(other);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        return _set.IsSupersetOf(other);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        return _set.Overlaps(other);
    }

    public bool Remove(T item)
    {
        return _set.Remove(item);
    }

    public int RemoveWhere(Predicate<T> match)
    {
        return _set.RemoveWhere(match);
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        return _set.SetEquals(other);
    }

    public void SymmetricExceptWith(IEnumerable<T> other)
    {
        _set.SymmetricExceptWith(other);
    }

    public T[] ToArray()
    {
        var array = new T[_set.Count];
        _set.CopyTo(array);
        return array;
    }

    public void TrimExcess()
    {
        _set = new HashSet<T>(_set, _set.Comparer);
    }

    public bool TryGetValue(T equalValue, [MaybeNullWhen(false)] out T actualValue)
    {
        foreach (var item in _set)
        {
            if (_set.Comparer.Equals(item, equalValue))
            {
                actualValue = item;
                return true;
            }
        }

        actualValue = default;
        return false;
    }

    public void UnionWith(IEnumerable<T> other)
    {
        _set.UnionWith(other);
    }

    public static IEqualityComparer<SegmentedHashSet<T>> CreateSetComparer()
    {
        return new SegmentedHashSetEqualityComparer();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    internal static bool EqualityComparersAreEqual(
        SegmentedHashSet<T> set1,
        SegmentedHashSet<T> set2)
    {
        return Equals(set1.Comparer, set2.Comparer);
    }

    public struct Enumerator : IEnumerator<T>
    {
        private HashSet<T>.Enumerator _enumerator;

        internal Enumerator(HashSet<T>.Enumerator enumerator)
        {
            _enumerator = enumerator;
        }

        public readonly T Current => _enumerator.Current;

        readonly object? IEnumerator.Current => ((IEnumerator)_enumerator).Current;

        public readonly void Dispose()
        {
            _enumerator.Dispose();
        }

        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Reset()
        {
            ((IEnumerator)_enumerator).Reset();
        }
    }

    private sealed class SegmentedHashSetEqualityComparer : IEqualityComparer<SegmentedHashSet<T>>
    {
        private readonly IEqualityComparer<HashSet<T>> _comparer = HashSet<T>.CreateSetComparer();

        public bool Equals(SegmentedHashSet<T>? x, SegmentedHashSet<T>? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return _comparer.Equals(x._set, y._set);
        }

        public int GetHashCode(SegmentedHashSet<T> obj)
        {
            return _comparer.GetHashCode(obj._set);
        }
    }
}
