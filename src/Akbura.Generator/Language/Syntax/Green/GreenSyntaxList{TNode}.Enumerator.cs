using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial struct GreenSyntaxList<TNode> where TNode : GreenNode
{
    public ref struct Enumerator
    {
        private readonly GreenSyntaxList<TNode> _list;
        private int _index;

        public Enumerator(GreenSyntaxList<TNode> list)
        {
            _list = list;
            _index = -1;
        }

        public bool MoveNext()
        {
            var newIndex = _index + 1;
            if (newIndex < _list.Count)
            {
                _index = newIndex;
                return true;
            }

            return false;
        }

        public readonly TNode Current => _list[_index]!;
    }
}