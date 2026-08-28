using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language.Syntax.Green;
partial class GreenNode
{
    public readonly ref struct NodeEnumerable(GreenNode node)
    {
        private readonly GreenNode _node = node;

        public readonly Enumerator GetEnumerator() => new(_node);

        public ref struct Enumerator
        {
            private readonly ArrayBuilder<GreenChildSyntaxList.Enumerator> _stack;

            private bool _started;
            private GreenNode _current;

            public Enumerator(GreenNode node)
            {
                _current = node;
                _stack = ArrayBuilder<GreenChildSyntaxList.Enumerator>.GetInstance();
                _stack.Push(node.ChildNodesAndTokens().GetEnumerator());
            }

            public readonly void Dispose()
                => _stack.Free();

            public readonly GreenNode Current
            {
                get
                {
                    AkburaDebug.Assert(_started);
                    return _current;
                }
            }

            public bool MoveNext()
            {
                if (!_started)
                {
                    // First call that starts the whole process.  We don't actually want to start processing the stack
                    // yet.  We just want to return the original node (which we already stored into _current).
                    _started = true;
                    return true;
                }
                else
                {
                    while (_stack.TryPop(out var currentEnumerator))
                    {
                        if (currentEnumerator.MoveNext())
                        {
                            _current = currentEnumerator.Current;

                            // push back this enumerator back onto the stack as it may still have more elements to give.
                            _stack.Push(currentEnumerator);

                            // also push the children of this current node so we'll walk into those.
                            if (!_current.IsToken)
                            {
                                _stack.Push(_current.ChildNodesAndTokens().GetEnumerator());
                            }

                            return true;
                        }
                    }
                }

                return false;
            }
        }
    }
}
