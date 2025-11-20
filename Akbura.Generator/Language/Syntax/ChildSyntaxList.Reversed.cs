using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;
readonly partial struct ChildSyntaxList
{
    public readonly partial struct Reversed : IEnumerable<SyntaxNodeOrToken>, IEquatable<Reversed>
    {
        private readonly AkburaSyntax? _node;
        private readonly int _count;

        public Reversed(AkburaSyntax node, int count)
        {
            _node = node;
            _count = count;
        }

        public Enumerator GetEnumerator()
        {
            Debug.Assert(_node is not null);
            return new Enumerator(_node!, _count);
        }

        IEnumerator<SyntaxNodeOrToken> IEnumerable<SyntaxNodeOrToken>.GetEnumerator()
        {
            if (_node == null)
            {
                return EmptyEnumerator.For<SyntaxNodeOrToken>();
            }

            return new EnumeratorImpl(_node, _count);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            if (_node == null)
            {
                return EmptyEnumerator.For<SyntaxNodeOrToken>();
            }

            return new EnumeratorImpl(_node, _count);
        }

        public override int GetHashCode()
        {
            return _node != null ? HashCode.Combine(_node.GetHashCode(), _count) : 0;
        }

        public override bool Equals(object? obj)
        {
            return (obj is Reversed r) && Equals(r);
        }

        public bool Equals(Reversed other)
        {
            return _node == other._node
                && _count == other._count;
        }

        public struct Enumerator
        {
            private readonly AkburaSyntax? _node;
            private readonly int _count;
            private int _childIndex;

            public Enumerator(AkburaSyntax node, int count)
            {
                _node = node;
                _count = count;
                _childIndex = count;
            }

            [MemberNotNullWhen(true, nameof(_node))]
            public bool MoveNext()
            {
                return --_childIndex >= 0;
            }

            public readonly SyntaxNodeOrToken Current
            {
                get
                {
                    Debug.Assert(_node is not null);
                    return ItemInternal(_node!, _childIndex);
                }
            }

            public void Reset()
            {
                _childIndex = _count;
            }
        }

        private class EnumeratorImpl : IEnumerator<SyntaxNodeOrToken>
        {
            private Enumerator _enumerator;

            public EnumeratorImpl(AkburaSyntax node, int count)
            {
                _enumerator = new Enumerator(node, count);
            }

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            /// <returns>
            /// The element in the collection at the current position of the enumerator.
            ///   </returns>
            public SyntaxNodeOrToken Current => _enumerator.Current;

            /// <summary>
            /// Gets the element in the collection at the current position of the enumerator.
            /// </summary>
            /// <returns>
            /// The element in the collection at the current position of the enumerator.
            ///   </returns>
            object IEnumerator.Current => _enumerator.Current;

            /// <summary>
            /// Advances the enumerator to the next element of the collection.
            /// </summary>
            /// <returns>
            /// true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.
            /// </returns>
            /// <exception cref="InvalidOperationException">The collection was modified after the enumerator was created. </exception>
            public bool MoveNext()
            {
                return _enumerator.MoveNext();
            }

            /// <summary>
            /// Sets the enumerator to its initial position, which is before the first element in the collection.
            /// </summary>
            /// <exception cref="InvalidOperationException">The collection was modified after the enumerator was created. </exception>
            public void Reset()
            {
                _enumerator.Reset();
            }

            /// <summary>
            /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
            /// </summary>
            public void Dispose()
            { }
        }

        public static bool operator ==(Reversed left, Reversed right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Reversed left, Reversed right)
        {
            return !(left == right);
        }
    }
}