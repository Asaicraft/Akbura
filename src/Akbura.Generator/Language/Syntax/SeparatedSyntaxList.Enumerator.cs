using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Akbura.Language.Syntax;
partial struct SeparatedSyntaxList<TNode>
{
    // Public struct enumerator
    // Only implements enumerator pattern as used by foreach
    // Does not implement IEnumerator. Doing so would require the struct to implement IDisposable too.
    [SuppressMessage("Usage", "CA2231:Overload operator equals on overriding value type Equals", Justification = "<Pending>")]
    [SuppressMessage("CodeQuality", "IDE0079:Remove unnecessary suppression", Justification = "<Pending>")]
    public struct Enumerator
    {
        private readonly SeparatedSyntaxList<TNode> _list;
        private int _index;

        public Enumerator(in SeparatedSyntaxList<TNode> list)
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

        public TNode Current
        {
            get
            {
                return _list[_index];
            }
        }

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

    // IEnumerator wrapper for Enumerator.
    private class EnumeratorImpl : IEnumerator<TNode>
    {
        private Enumerator _e;

        public EnumeratorImpl(in SeparatedSyntaxList<TNode> list)
        {
            _e = new Enumerator(in list);
        }

        public TNode Current => _e.Current;

        object IEnumerator.Current => _e.Current;

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            return _e.MoveNext();
        }

        public void Reset()
        {
            _e.Reset();
        }
    }
}