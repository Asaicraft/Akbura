using Akbura.Collections;
using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;

partial class SyntaxList
{
    public sealed class WithManyChildren(GreenSyntaxList green, AkburaSyntax? parent, int position) : SyntaxList(green, parent, position)
    {
        private readonly ArrayElement<AkburaSyntax?>[] _children = new ArrayElement<AkburaSyntax?>[green.SlotCount];

        public override AkburaSyntax? GetNodeSlot(int index)
        {
            return GetRedElement(ref _children[index].Value, index);
        }

        public override AkburaSyntax? GetCachedSlot(int index)
        {
            return _children[index];
        }
    }
}
