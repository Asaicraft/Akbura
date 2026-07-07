// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable enable

using System.Collections;
using System.Collections.Generic;

namespace Akbura.Collections;

internal readonly partial struct ImmutableSegmentedHashSet<T>
{
    public struct Enumerator : IEnumerator<T>
    {
        private readonly SegmentedHashSet<T> _set;
        private IEnumerator<T> _enumerator;

        internal Enumerator(SegmentedHashSet<T> set)
        {
            _set = set;
            _enumerator = set.GetEnumerator();
        }

        public readonly T Current => _enumerator.Current;

        readonly object? IEnumerator.Current => Current;

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
            _enumerator = _set.GetEnumerator();
        }
    }
}
