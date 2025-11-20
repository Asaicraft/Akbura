using Akbura.Collections;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;
partial class SyntaxList
{
    public sealed class SeparatedWithManyChildren : SyntaxList
    {
        private readonly ArrayElement<AkburaSyntax?>[] _children;

        public SeparatedWithManyChildren(GreenSyntaxList green, AkburaSyntax? parent, int position)
            : base(green, parent, position)
        {
            _children = new ArrayElement<AkburaSyntax?>[(green.SlotCount + 1) >> 1];
        }

        public override AkburaSyntax? GetNodeSlot(int i)
        {
            if ((i & 1) != 0)
            {
                //separator
                return null;
            }

            return GetRedElement(ref _children[i >> 1].Value, i);
        }

        public override AkburaSyntax? GetCachedSlot(int i)
        {
            if ((i & 1) != 0)
            {
                //separator
                return null;
            }

            return _children[i >> 1].Value;
        }

        public override int GetChildPosition(int index)
        {
            // If the previous sibling (ignoring separator) is not cached, but the next sibling
            // (ignoring separator) is cached, use the next sibling to determine position.
            var valueIndex = (index & 1) != 0 ? index - 1 : index;
            // The check for valueIndex >= Green.SlotCount - 2 ignores the last item because the last item
            // is a separator and separators are not cached. In those cases, when the index represents
            // the last or next to last item, we still want to calculate the position from the end of
            // the list rather than the start.
            if (valueIndex > 1
                && GetCachedSlot(valueIndex - 2) is null
                && (valueIndex >= Green.SlotCount - 2 || GetCachedSlot(valueIndex + 2) is { }))
            {
                return GetChildPositionFromEnd(index);
            }

            return base.GetChildPosition(index);
        }
    }
}
