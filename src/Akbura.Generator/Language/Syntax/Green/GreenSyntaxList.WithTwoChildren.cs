using Akbura.Collections;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxList
{
    internal sealed class WithTwoChildrenGreen : GreenSyntaxList
    {
        private readonly GreenNode _child0;
        private readonly GreenNode _child1;

        public WithTwoChildrenGreen(GreenNode child0, GreenNode child1) : base(null, null)
        {
            SlotCount = 2;

            _child0 = child0;
            _child1 = child1;

            var fullWidth = 0;
            ushort flags = 0;

            AdjustWidthAndFlags(child0, ref fullWidth, ref flags);
            AdjustWidthAndFlags(child1, ref fullWidth, ref flags);

            FullWidth = fullWidth;
            Flags = flags;
        }

        public WithTwoChildrenGreen(ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations, GreenNode child0, GreenNode child1)
            : base(diagnostics, annotations)
        {
            SlotCount = 2;

            _child0 = child0;
            _child1 = child1;

            var fullWidth = 0;
            ushort flags = 0;

            AdjustWidthAndFlags(child0, ref fullWidth, ref flags);
            AdjustWidthAndFlags(child1, ref fullWidth, ref flags);

            FullWidth = fullWidth;
            Flags = flags;
        }

        public override GreenNode? GetSlot(int index)
        {
            return index switch
            {
                0 => _child0,
                1 => _child1,
                _ => null,
            };
        }

        public override void CopyTo(ArrayElement<GreenNode>[] array, int offset)
        {
            array[offset].Value = _child0;
            array[offset + 1].Value = _child1;
        }

        public override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
        {
            return new SyntaxList.WithTwoChildren(this, parent, position);
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? errors)
        {
            return new WithTwoChildrenGreen(errors, this.GetAnnotations(), _child0, _child1);
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new WithTwoChildrenGreen(GetDiagnostics(), annotations, _child0, _child1);
        }
    }
}
