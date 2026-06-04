using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Language;

internal readonly partial struct Blender
{
    private struct Reader
    {
        private Lexer _lexer;
        private Cursor _oldTreeCursor;
        private ImmutableStack<TextChangeRange> _changes;
        private int _newPosition;
        private int _changeDelta;

        public Reader(Blender blender)
        {
            _lexer = blender._lexer;
            _oldTreeCursor = blender._oldTreeCursor;
            _changes = blender._changes;
            _newPosition = blender._newPosition;
            _changeDelta = blender._changeDelta;
        }

        public BlendedNode ReadNodeOrToken(Lexer.LexerMode mode, bool asToken)
        {
            SkipPastChanges();
            SkipOldTreePastNewPosition();

            if (TryReadOldNodeOrToken(mode, asToken, out var blended))
            {
                return blended;
            }

            return ReadNewToken(mode);
        }

        private bool TryReadOldNodeOrToken(
            Lexer.LexerMode mode,
            bool asToken,
            out BlendedNode blended)
        {
            var cursor = asToken
                ? MoveToFirstToken(_oldTreeCursor)
                : _oldTreeCursor;

            if (cursor.IsFinished)
            {
                blended = default;
                return false;
            }

            var nodeOrToken = cursor.Current;
            var oldSpan = nodeOrToken.FullSpan;
            var expectedNewPosition = oldSpan.Start + _changeDelta;

            if (expectedNewPosition != _newPosition ||
                IntersectsNextChange(oldSpan) ||
                !CanReuse(nodeOrToken, asToken))
            {
                blended = default;
                return false;
            }

            var nextCursor = cursor.MoveToNextSibling();
            _oldTreeCursor = nextCursor;
            _newPosition += nodeOrToken.FullSpan.Length;

            blended = asToken
                ? new BlendedNode(null, nodeOrToken.AsToken(), CreateBlender())
                : new BlendedNode(nodeOrToken.AsNode(), default, CreateBlender());
            return true;
        }

        private BlendedNode ReadNewToken(Lexer.LexerMode mode)
        {
            _lexer.TextWindow.Reset(_newPosition);

            var position = _lexer.TextWindow.Position;
            var token = _lexer.Lex(mode);
            var nextCursor = MoveOldTreePast(position + token.FullWidth);

            _oldTreeCursor = nextCursor;
            _newPosition += token.FullWidth;

            var blended = new BlendedNode(
                null,
                new SyntaxToken(parent: null, token: token, position: position, index: 0),
                CreateBlender());
            return blended;
        }

        private Blender CreateBlender()
        {
            return new Blender(
                _lexer,
                _oldTreeCursor,
                _changes,
                _newPosition,
                _changeDelta);
        }

        private void SkipPastChanges()
        {
            while (!_changes.IsEmpty)
            {
                var change = _changes.Peek();
                var oldEndInNewText = change.Span.End + _changeDelta;
                if (_newPosition < oldEndInNewText)
                {
                    break;
                }

                _changes = _changes.Pop();
                _changeDelta += change.NewLength - change.Span.Length;
            }
        }

        private void SkipOldTreePastNewPosition()
        {
            _oldTreeCursor = MoveOldTreePast(_newPosition);
        }

        private Cursor MoveOldTreePast(int newPosition)
        {
            var cursor = _oldTreeCursor;

            while (!cursor.IsFinished)
            {
                var oldNode = cursor.Current;
                var mappedEnd = oldNode.FullSpan.End + _changeDelta;
                if (mappedEnd > newPosition)
                {
                    break;
                }

                cursor = cursor.MoveToNextSibling();
            }

            return cursor;
        }

        private bool IntersectsNextChange(TextSpan oldSpan)
        {
            if (_changes.IsEmpty)
            {
                return false;
            }

            return oldSpan.IntersectsWith(_changes.Peek().Span);
        }

        private static bool CanReuse(SyntaxNodeOrToken nodeOrToken, bool asToken)
        {
            if (nodeOrToken.RequiredUnderlyingNode.ContainsSkippedText ||
                nodeOrToken.RequiredUnderlyingNode.ContainsDiagnostics)
            {
                return false;
            }

            return asToken
                ? nodeOrToken.IsToken
                : nodeOrToken.IsNode;
        }

        private static Cursor MoveToFirstToken(Cursor cursor)
        {
            while (!cursor.IsFinished && cursor.Current.IsNode)
            {
                cursor = cursor.MoveToFirstChild();
            }

            return cursor;
        }
    }
}
