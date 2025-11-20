using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Akbura.Collections;

namespace Akbura.Language.Syntax.Green;
partial class GreenSyntaxList
{
    public abstract class WithManyChildrenBaseGreen : GreenSyntaxList
    {
        public readonly ArrayElement<GreenNode>[] _children;

        public WithManyChildrenBaseGreen(ArrayElement<GreenNode>[] children)
        {
            _children = children;
            SlotCount = children.Length;

            var fullWidth = 0;
            ushort nodeAndFlags = 0;
            AdjustWidthAndFlags(children, ref fullWidth, ref nodeAndFlags);

            FullWidth = fullWidth;
            Flags = nodeAndFlags;
        }

        public WithManyChildrenBaseGreen(ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations, ArrayElement<GreenNode>[] children)
                : base(diagnostics, annotations)
        {
            _children = children;
            SlotCount = children.Length;

            var fullWidth = 0;
            ushort nodeFlags = 0;
            AdjustWidthAndFlags(children, ref fullWidth, ref nodeFlags);

            FullWidth = fullWidth;
            Flags = nodeFlags;
        }

        public sealed override void CopyTo(ArrayElement<GreenNode>[] array, int offset)
        {
            Array.Copy(_children, 0, array, offset, _children.Length);
        }

        public sealed override AkburaSyntax CreateRed(AkburaSyntax? parent, int position)
        {
            var separated = SlotCount > 1 && HasNodeTokenPattern();

            return separated
                    ? new SyntaxList.SeparatedWithManyChildren(this, parent, position)
                    : new SyntaxList.WithManyChildren(this, parent, position);
        }

        private bool HasNodeTokenPattern()
        {
            for (var i = 0; i < SlotCount; i++)
            {
                // even slots must not be tokens, odds slots must be tokens
                if (GetSlot(i)!.IsToken == ((i & 1) == 0))
                {
                    return false;
                }
            }

            return true;
        }

        public override GreenNode? GetSlot(int index) => _children[index].Value;

        protected override int GetSlotCount()
        {
            return _children.Length;
        }
    }

    public sealed class WithManyChildrenGreen : WithManyChildrenBaseGreen
    {
        public WithManyChildrenGreen(ArrayElement<GreenNode>[] children) : base(children)
        {
        }

        public WithManyChildrenGreen(ImmutableArray<AkburaDiagnostic>? diagnostics, ImmutableArray<AkburaSyntaxAnnotation>? annotations, ArrayElement<GreenNode>[] children)
                : base(diagnostics, annotations, children)
        {
        }

        public override GreenNode WithAnnotations(ImmutableArray<AkburaSyntaxAnnotation>? annotations)
        {
            return new WithManyChildrenGreen(GetDiagnostics(), annotations, _children);
        }

        public override GreenNode WithDiagnostics(ImmutableArray<AkburaDiagnostic>? diagnostics)
        {
            return new WithManyChildrenGreen(diagnostics, GetAnnotations(), _children);
        }
    }
}
