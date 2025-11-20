using Akbura.Language.Syntax.Green;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Akbura.Language.Syntax;
partial struct SyntaxTriviaList
{
    [StructLayout(LayoutKind.Auto)]
    public struct Enumerator
    {
        private SyntaxToken _token;
        private GreenNode? _singleNodeOrList;
        private int _baseIndex;
        private int _count;

        private int _index;
        private GreenNode? _current;
        private int _position;

        public Enumerator(ref readonly SyntaxTriviaList list)
        {
            _token = list.Token;
            _singleNodeOrList = list.Node;
            _baseIndex = list.Index;
            _count = list.Count;

            _index = -1;
            _current = null;
            _position = list.Position;
        }

        // PERF: Passing SyntaxToken by ref since it's a non-trivial struct
        private void InitializeFrom(ref readonly SyntaxToken token, GreenNode greenNode, int index, int position)
        {
            _token = token;
            _singleNodeOrList = greenNode;
            _baseIndex = index;
            _count = greenNode.IsList ? greenNode.SlotCount : 1;

            _index = -1;
            _current = null;
            _position = position;
        }

        // PERF: Used to initialize an enumerator for leading trivia directly from a token.
        // This saves constructing an intermediate SyntaxTriviaList. Also, passing token
        // by ref since it's a non-trivial struct
        public void InitializeFromLeadingTrivia(ref readonly SyntaxToken token)
        {
            Debug.Assert(token.Node is not null);
            var node = token.Node!.GetLeadingTrivia();
            Debug.Assert(node is not null);
            InitializeFrom(in token, node!, 0, token.Position);
        }

        // PERF: Used to initialize an enumerator for trailing trivia directly from a token.
        // This saves constructing an intermediate SyntaxTriviaList. Also, passing token
        // by ref since it's a non-trivial struct
        public void InitializeFromTrailingTrivia(ref readonly SyntaxToken token)
        {
            Debug.Assert(token.Node is not null);
            var leading = token.Node!.GetLeadingTrivia();
            var index = 0;
            if (leading != null)
            {
                index = leading.IsList ? leading.SlotCount : 1;
            }

            var trailingGreen = token.Node.GetTrailingTrivia();
            var trailingPosition = token.Position + token.FullWidth;
            if (trailingGreen != null)
            {
                trailingPosition -= trailingGreen.FullWidth;
            }

            Debug.Assert(trailingGreen is not null);
            InitializeFrom(in token, trailingGreen!, index, trailingPosition);
        }

        public bool MoveNext()
        {
            var newIndex = _index + 1;
            if (newIndex >= _count)
            {
                // invalidate iterator
                _current = null;
                return false;
            }

            _index = newIndex;

            if (_current != null)
            {
                _position += _current.FullWidth;
            }

            Debug.Assert(_singleNodeOrList is not null);
            _current = GetGreenNodeAt(_singleNodeOrList!, newIndex);
            return true;
        }

        public readonly SyntaxTrivia Current
        {
            get
            {
                if (_current == null)
                {
                    throw new InvalidOperationException();
                }

                return new SyntaxTrivia(_token, _current, _position, _baseIndex + _index);
            }
        }

        public bool TryMoveNextAndGetCurrent(out SyntaxTrivia current)
        {
            if (!MoveNext())
            {
                current = default;
                return false;
            }

            current = new(_token, _current, _position, _baseIndex + _index);
            return true;
        }
    }

    private class EnumeratorImpl : IEnumerator<SyntaxTrivia>
    {
        private Enumerator _enumerator;

        // SyntaxTriviaList is a relatively big struct so is passed as ref
        public EnumeratorImpl(ref readonly SyntaxTriviaList list)
        {
            _enumerator = new Enumerator(in list);
        }

        public SyntaxTrivia Current => _enumerator.Current;

        object IEnumerator.Current => _enumerator.Current;

        public bool MoveNext()
        {
            return _enumerator.MoveNext();
        }

        public void Reset()
        {
            throw new NotSupportedException();
        }

        public void Dispose()
        {
        }
    }
}