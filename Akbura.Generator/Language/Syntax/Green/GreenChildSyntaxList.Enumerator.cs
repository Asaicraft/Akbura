using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;

readonly partial struct GreenChildSyntaxList
{
    public struct Enumerator(GreenNode? node)
    {
        private readonly GreenNode? _node = node;
        private int _childIndex = -1;
        private GreenNode? _list = null;
        private int _listIndex = -1;
        private GreenNode? _currentChild = null;

        public bool MoveNext()
        {
            if (_node != null)
            {
                if (_list != null)
                {
                    _listIndex++;

                    if (_listIndex < _list.SlotCount)
                    {
                        _currentChild = _list.GetSlot(_listIndex);
                        return true;
                    }

                    _list = null;
                    _listIndex = -1;
                }

                while (true)
                {
                    _childIndex++;

                    if (_childIndex == _node.SlotCount)
                    {
                        break;
                    }

                    var child = _node.GetSlot(_childIndex);
                    if (child == null)
                    {
                        continue;
                    }

                    if (child.RawKind == GreenNode.ListKind)
                    {
                        _list = child;
                        _listIndex++;

                        if (_listIndex < _list.SlotCount)
                        {
                            _currentChild = _list.GetSlot(_listIndex);
                            return true;
                        }
                        else
                        {
                            _list = null;
                            _listIndex = -1;
                            continue;
                        }
                    }
                    else
                    {
                        _currentChild = child;
                    }

                    return true;
                }
            }

            _currentChild = null;
            return false;
        }

        public readonly GreenNode Current => _currentChild!;
    }
}