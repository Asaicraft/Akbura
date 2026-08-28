using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;
partial struct ChildSyntaxList
{
    /// <summary>Enumerates the elements of a <see cref="ChildSyntaxList" />.</summary>
    public struct Enumerator
    {
        private AkburaSyntax? _node;
        private int _count;
        private int _childIndex;
        private SlotData _slotData;

        public Enumerator(AkburaSyntax node, int count)
        {
            _node = node;
            _count = count;
            _childIndex = -1;
            _slotData = new SlotData(node);
        }

        // PERF: Initialize an Enumerator directly from a AkburaSyntax without going
        // via ChildNodesAndTokens. This saves constructing an intermediate ChildSyntaxList
        public void InitializeFrom(AkburaSyntax node)
        {
            _node = node;
            _count = CountNodes(node.Green);
            _childIndex = -1;
            _slotData = new SlotData(node);
        }

        /// <summary>Advances the enumerator to the next element of the <see cref="ChildSyntaxList" />.</summary>
        /// <returns>true if the enumerator was successfully advanced to the next element; false if the enumerator has passed the end of the collection.</returns>
        [MemberNotNullWhen(true, nameof(_node))]
        public bool MoveNext()
        {
            var newIndex = _childIndex + 1;
            if (newIndex < _count)
            {
                _childIndex = newIndex;
                Debug.Assert(_node != null);
#pragma warning disable CS8775 // Member must have a non-null value when exiting in some condition.
                return true;
#pragma warning restore CS8775 // Member must have a non-null value when exiting in some condition.
            }

            return false;
        }

        /// <summary>Gets the element at the current position of the enumerator.</summary>
        /// <returns>The element in the <see cref="ChildSyntaxList" /> at the current position of the enumerator.</returns>
        public SyntaxNodeOrToken Current
        {
            get
            {
                Debug.Assert(_node is not null);
                return ItemInternal(_node!, _childIndex, ref _slotData);
            }
        }

        /// <summary>Sets the enumerator to its initial position, which is before the first element in the collection.</summary>
        public void Reset()
        {
            _childIndex = -1;
        }

        public bool TryMoveNextAndGetCurrent(out SyntaxNodeOrToken current)
        {
            if (!MoveNext())
            {
                current = default;
                return false;
            }

            current = ItemInternal(_node, _childIndex, ref _slotData);
            return true;
        }

        public AkburaSyntax? TryMoveNextAndGetCurrentAsNode()
        {
            while (MoveNext())
            {
                var nodeValue = ItemInternalAsNode(_node, _childIndex, ref _slotData);
                if (nodeValue != null)
                {
                    return nodeValue;
                }
            }

            return null;
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
        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        /// <summary>
        /// Sets the enumerator to its initial position, which is before the first element in the collection.
        /// </summary>
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
}