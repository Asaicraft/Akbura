using Akbura.Language.Syntax.Green;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax;
partial class SyntaxList
{
    public sealed class WithThreeChildren : SyntaxList
    {
        private AkburaSyntax? _child0;
        private AkburaSyntax? _child1;
        private AkburaSyntax? _child2;

        public WithThreeChildren(GreenSyntaxList green, AkburaSyntax? parent, int position)
            : base(green, parent, position)
        {
        }

        public override AkburaSyntax? GetNodeSlot(int index)
        {
            return index switch
            {
                0 => GetRedElement(ref _child0, 0),
                1 => GetRedElementIfNotToken(ref _child1),
                2 => GetRedElement(ref _child2, 2),
                _ => null,
            };
        }

        public override AkburaSyntax? GetCachedSlot(int index)
        {
            return index switch
            {
                0 => _child0,
                1 => _child1,
                2 => _child2,
                _ => null,
            };
        }
    }
}
