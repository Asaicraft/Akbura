// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System.Collections;
using System.Collections.Generic;

namespace Akbura.Collections;

internal readonly partial struct ImmutableSegmentedHashSet<T>
{
    public sealed class Builder : ISet<T>, IReadOnlyCollection<T>
    {
        private SegmentedHashSet<T> _set;

        internal Builder(ImmutableSegmentedHashSet<T> set)
        {
            _set = new SegmentedHashSet<T>(set.Set, set.KeyComparer);
        }

        public IEqualityComparer<T> KeyComparer
        {
            get
            {
                return _set.Comparer;
            }

            set
            {
                var comparer = value ?? EqualityComparer<T>.Default;
                if (Equals(_set.Comparer, comparer))
                {
                    return;
                }

                _set = new SegmentedHashSet<T>(_set, comparer);
            }
        }

        public int Count => _set.Count;

        bool ICollection<T>.IsReadOnly => false;

        public bool Add(T item)
        {
            return _set.Add(item);
        }

        public void Clear()
        {
            _set.Clear();
        }

        public bool Contains(T item)
        {
            return _set.Contains(item);
        }

        public void CopyTo(T[] array, int arrayIndex)
        {
            _set.CopyTo(array, arrayIndex);
        }

        public void ExceptWith(IEnumerable<T> other)
        {
            _set.ExceptWith(other);
        }

        public Enumerator GetEnumerator()
        {
            return new Enumerator(new SegmentedHashSet<T>(_set, _set.Comparer));
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

        public bool SetEquals(IEnumerable<T> other)
        {
            return _set.SetEquals(other);
        }

        public void SymmetricExceptWith(IEnumerable<T> other)
        {
            _set.SymmetricExceptWith(other);
        }

        public bool TryGetValue(T equalValue, out T actualValue)
        {
            foreach (var item in _set)
            {
                if (_set.Comparer.Equals(item, equalValue))
                {
                    actualValue = item;
                    return true;
                }
            }

            actualValue = equalValue;
            return false;
        }

        public void UnionWith(IEnumerable<T> other)
        {
            _set.UnionWith(other);
        }

        public ImmutableSegmentedHashSet<T> ToImmutable()
        {
            return new ImmutableSegmentedHashSet<T>(new SegmentedHashSet<T>(_set, _set.Comparer));
        }

        void ICollection<T>.Add(T item)
        {
            Add(item);
        }

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            return GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
