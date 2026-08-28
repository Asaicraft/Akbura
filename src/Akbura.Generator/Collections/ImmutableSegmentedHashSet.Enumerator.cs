// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System.Collections;
using System.Collections.Generic;

namespace Akbura.Collections;

internal readonly partial struct ImmutableSegmentedHashSet<T>
{
    /// <inheritdoc cref="ImmutableHashSet{T}.Enumerator"/>
    public struct Enumerator : IEnumerator<T>
    {
        private readonly SegmentedHashSet<T> _set;
        private SegmentedHashSet<T>.Enumerator _enumerator;

        internal Enumerator(SegmentedHashSet<T> set)
        {
            _set = set;
            _enumerator = set.GetEnumerator();
        }

        /// <inheritdoc cref="ImmutableHashSet{T}.Enumerator.Current"/>
        public readonly T Current => _enumerator.Current;

        readonly object? IEnumerator.Current => ((IEnumerator)_enumerator).Current;

        /// <inheritdoc cref="ImmutableHashSet{T}.Enumerator.Dispose()"/>
        public readonly void Dispose()
            => _enumerator.Dispose();

        /// <inheritdoc cref="ImmutableHashSet{T}.Enumerator.MoveNext()"/>
        public bool MoveNext()
            => _enumerator.MoveNext();

        /// <inheritdoc cref="ImmutableHashSet{T}.Enumerator.Reset()"/>
        public void Reset()
        {
            // Create a new enumerator, since _enumerator.Reset() will fail for cases where the set was mutated
            // after enumeration started, and ImmutableSegmentHashSet<T>.Builder allows for this case without error.
            _enumerator = _set.GetEnumerator();
        }
    }
}