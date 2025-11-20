using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;

partial struct SyntaxList<TNode>
{
    [SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "<Pending>")]
    [SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression", Justification = "<Pending>")]
    public struct Enumerator
    {
        private readonly SyntaxList<TNode> _list;
        private int _index;

        public Enumerator(SyntaxList<TNode> list)
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

        public readonly TNode Current => (TNode)_list.ItemInternal(_index)!;

        public void Reset()
        {
            _index = -1;
        }

        public readonly override bool Equals(object? obj)
        {
            throw new NotSupportedException();
        }

        public readonly override int GetHashCode()
        {
            throw new NotSupportedException();
        }
    }

    private class EnumeratorImpl : IEnumerator<TNode>
    {
        private Enumerator _e;

        public EnumeratorImpl(in SyntaxList<TNode> list)
        {
            _e = new Enumerator(list);
        }

        public bool MoveNext()
        {
            return _e.MoveNext();
        }

        public TNode Current => _e.Current;

        void IDisposable.Dispose()
        {
        }

        object IEnumerator.Current => _e.Current;

        void IEnumerator.Reset()
        {
            _e.Reset();
        }
    }
}