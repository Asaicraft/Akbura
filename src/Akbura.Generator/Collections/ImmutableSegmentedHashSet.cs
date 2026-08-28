// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Collections;

internal readonly partial struct ImmutableSegmentedHashSet<T> :
    IImmutableSet<T>,
    ISet<T>,
    ICollection,
    IEquatable<ImmutableSegmentedHashSet<T>>
{
    public static readonly ImmutableSegmentedHashSet<T> Empty = new([]);

    private readonly SegmentedHashSet<T>? _set;

    private ImmutableSegmentedHashSet(SegmentedHashSet<T> set)
    {
        _set = set;
    }

    private SegmentedHashSet<T> Set => _set ?? new SegmentedHashSet<T>();

    public IEqualityComparer<T> KeyComparer => Set.Comparer;

    public int Count => Set.Count;

    public bool IsDefault => _set is null;

    public bool IsEmpty => Set.Count == 0;

    bool ICollection<T>.IsReadOnly => true;

    bool ICollection.IsSynchronized => true;

    object ICollection.SyncRoot => Set;

    public static bool operator ==(ImmutableSegmentedHashSet<T> left, ImmutableSegmentedHashSet<T> right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ImmutableSegmentedHashSet<T> left, ImmutableSegmentedHashSet<T> right)
    {
        return !left.Equals(right);
    }

    public static bool operator ==(ImmutableSegmentedHashSet<T>? left, ImmutableSegmentedHashSet<T>? right)
    {
        return left.GetValueOrDefault().Equals(right.GetValueOrDefault());
    }

    public static bool operator !=(ImmutableSegmentedHashSet<T>? left, ImmutableSegmentedHashSet<T>? right)
    {
        return !left.GetValueOrDefault().Equals(right.GetValueOrDefault());
    }

    public ImmutableSegmentedHashSet<T> Add(T value)
    {
        var current = Set;
        if (current.Contains(value))
        {
            return this;
        }

        var set = new SegmentedHashSet<T>(current, current.Comparer);
        set.Add(value);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public ImmutableSegmentedHashSet<T> Clear()
    {
        return IsEmpty
            ? this
            : Empty.WithComparer(KeyComparer);
    }

    public bool Contains(T value)
    {
        return Set.Contains(value);
    }

    public ImmutableSegmentedHashSet<T> Except(IEnumerable<T> other)
    {
        var set = new SegmentedHashSet<T>(Set, KeyComparer);
        set.ExceptWith(other);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(Set);
    }

    public ImmutableSegmentedHashSet<T> Intersect(IEnumerable<T> other)
    {
        var set = new SegmentedHashSet<T>(Set, KeyComparer);
        set.IntersectWith(other);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public bool IsProperSubsetOf(IEnumerable<T> other)
    {
        return Set.IsProperSubsetOf(other);
    }

    public bool IsProperSupersetOf(IEnumerable<T> other)
    {
        return Set.IsProperSupersetOf(other);
    }

    public bool IsSubsetOf(IEnumerable<T> other)
    {
        return Set.IsSubsetOf(other);
    }

    public bool IsSupersetOf(IEnumerable<T> other)
    {
        return Set.IsSupersetOf(other);
    }

    public bool Overlaps(IEnumerable<T> other)
    {
        return Set.Overlaps(other);
    }

    public ImmutableSegmentedHashSet<T> Remove(T value)
    {
        var current = Set;
        if (!current.Contains(value))
        {
            return this;
        }

        var set = new SegmentedHashSet<T>(current, current.Comparer);
        set.Remove(value);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public bool SetEquals(IEnumerable<T> other)
    {
        return Set.SetEquals(other);
    }

    public ImmutableSegmentedHashSet<T> SymmetricExcept(IEnumerable<T> other)
    {
        var set = new SegmentedHashSet<T>(Set, KeyComparer);
        set.SymmetricExceptWith(other);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public bool TryGetValue(T equalValue, out T actualValue)
    {
        return Set.TryGetValue(equalValue, out actualValue!);
    }

    public ImmutableSegmentedHashSet<T> Union(IEnumerable<T> other)
    {
        var set = new SegmentedHashSet<T>(Set, KeyComparer);
        set.UnionWith(other);
        return new ImmutableSegmentedHashSet<T>(set);
    }

    public Builder ToBuilder()
    {
        return new Builder(this);
    }

    public ImmutableSegmentedHashSet<T> WithComparer(IEqualityComparer<T>? equalityComparer)
    {
        var comparer = equalityComparer ?? EqualityComparer<T>.Default;
        return Equals(KeyComparer, comparer)
            ? this
            : new ImmutableSegmentedHashSet<T>(new SegmentedHashSet<T>(Set, comparer));
    }

    public override int GetHashCode()
    {
        return Set.GetHashCode();
    }

    public override bool Equals(object? obj)
    {
        return obj is ImmutableSegmentedHashSet<T> other && Equals(other);
    }

    public bool Equals(ImmutableSegmentedHashSet<T> other)
    {
        return ReferenceEquals(_set, other._set);
    }

    IImmutableSet<T> IImmutableSet<T>.Clear()
    {
        return Clear();
    }

    IImmutableSet<T> IImmutableSet<T>.Add(T value)
    {
        return Add(value);
    }

    IImmutableSet<T> IImmutableSet<T>.Remove(T value)
    {
        return Remove(value);
    }

    IImmutableSet<T> IImmutableSet<T>.Intersect(IEnumerable<T> other)
    {
        return Intersect(other);
    }

    IImmutableSet<T> IImmutableSet<T>.Except(IEnumerable<T> other)
    {
        return Except(other);
    }

    IImmutableSet<T> IImmutableSet<T>.SymmetricExcept(IEnumerable<T> other)
    {
        return SymmetricExcept(other);
    }

    IImmutableSet<T> IImmutableSet<T>.Union(IEnumerable<T> other)
    {
        return Union(other);
    }

    void ICollection<T>.CopyTo(T[] array, int arrayIndex)
    {
        ((ICollection<T>)Set).CopyTo(array, arrayIndex);
    }

    void ICollection.CopyTo(Array array, int index)
    {
        foreach (var item in this)
        {
            array.SetValue(item, index++);
        }
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    bool ISet<T>.Add(T item)
    {
        throw new NotSupportedException();
    }

    void ISet<T>.UnionWith(IEnumerable<T> other)
    {
        throw new NotSupportedException();
    }

    void ISet<T>.IntersectWith(IEnumerable<T> other)
    {
        throw new NotSupportedException();
    }

    void ISet<T>.ExceptWith(IEnumerable<T> other)
    {
        throw new NotSupportedException();
    }

    void ISet<T>.SymmetricExceptWith(IEnumerable<T> other)
    {
        throw new NotSupportedException();
    }

    void ICollection<T>.Add(T item)
    {
        throw new NotSupportedException();
    }

    void ICollection<T>.Clear()
    {
        throw new NotSupportedException();
    }

    bool ICollection<T>.Remove(T item)
    {
        throw new NotSupportedException();
    }
}
