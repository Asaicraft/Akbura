using Akbura.Collections;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxList
{
    internal sealed class WithLotsOfChildrenGreen : WithManyChildrenBaseGreen
    {
        private readonly int[] _childOffsets;

        public WithLotsOfChildrenGreen(ArrayElement<GreenNode>[] children) : base(children)
        {
            _childOffsets = CalculateOffsets(children);
        }

        public WithLotsOfChildrenGreen(ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations, ArrayElement<GreenNode>[] children)
            : base(diagnostics, annotations, children)
        {
            _childOffsets = CalculateOffsets(children);
        }

        public override int GetSlotOffset(int index)
        {
            return _childOffsets[index];
        }

        /// <summary>
        /// Find the slot that contains the given offset.
        /// </summary>
        /// <param name="offset">The target offset. Must be between 0 and <see cref="GreenNode.Width"/>.</param>
        /// <returns>The slot index of the slot containing the given offset.</returns>
        /// <remarks>
        /// This implementation uses a binary search to find the first slot that contains
        /// the given offset.
        /// </remarks>
        public override int FindSlotIndexContainingOffset(int offset)
        {
            Debug.Assert(offset >= 0 && offset < Width);
            return _childOffsets.BinarySearchUpperBound(offset) - 1;
        }

        private static int[] CalculateOffsets(ArrayElement<GreenNode>[] children)
        {
            var n = children.Length;
            var childOffsets = new int[n];
            var offset = 0;
            for (var i = 0; i < n; i++)
            {
                childOffsets[i] = offset;
                offset += children[i].Value.Width;
            }
            return childOffsets;
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new WithLotsOfChildrenGreen(GetDiagnostics(), annotations, _children);
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new WithLotsOfChildrenGreen(diagnostics, GetAnnotations(), _children);
        }
    }
}
