using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

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
            if (!IsWithinCurrentChangeInNewText(_newPosition))
            {
                SkipOldTreePastNewPosition();
            }

            if (TryReadOldNodeOrToken(mode, asToken, out var blended))
            {
                return blended;
            }

            if (!asToken)
            {
                return default;
            }

            _oldTreeCursor = MoveToFirstToken(_oldTreeCursor);
            return ReadNewToken(mode);
        }

        public BlendedNode ReadFreshToken(Lexer.LexerMode mode)
        {
            SkipPastChanges();
            if (!IsWithinCurrentChangeInNewText(_newPosition))
            {
                SkipOldTreePastNewPosition();
            }

            return ReadNewToken(mode);
        }

        private bool TryReadOldNodeOrToken(
            Lexer.LexerMode mode,
            bool asToken,
            out BlendedNode blended)
        {
            if (IsWithinCurrentChangeInNewText(_newPosition))
            {
                blended = default;
                return false;
            }

            var cursor = asToken
                ? MoveToFirstToken(_oldTreeCursor)
                : MoveToReusableNode(_oldTreeCursor);

            if (cursor.IsFinished)
            {
                blended = default;
                return false;
            }

            var nodeOrToken = cursor.Current;
            var oldSpan = nodeOrToken.FullSpan;
            var expectedNewPosition = oldSpan.Start + _changeDelta;

            if (expectedNewPosition != _newPosition ||
                IntersectsNextChange(nodeOrToken) ||
                !CanReuse(
                    nodeOrToken,
                    asToken,
                    mode))
            {
                blended = default;
                return false;
            }

            _newPosition += nodeOrToken.FullSpan.Length;
            _oldTreeCursor = cursor;
            _oldTreeCursor = MoveOldTreePast(_newPosition);
            _lexer.TextWindow.Reset(_newPosition);

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
            var nextCursor = IsWithinCurrentChangeInNewText(position)
                ? _oldTreeCursor
                : MoveOldTreePast(position + token.FullWidth);

            _oldTreeCursor = nextCursor;
            _newPosition += token.FullWidth;

            var blended = new BlendedNode(
                null,
                new SyntaxToken(parent: null, token: token, position: position, index: 0),
                CreateBlender());
            return blended;
        }

        private bool IsWithinCurrentChangeInNewText(int position)
        {
            if (_changes.IsEmpty)
            {
                return false;
            }

            var change = _changes.Peek();
            var newStart = change.Span.Start + _changeDelta;
            var newEnd = change.Span.Start + _changeDelta + change.NewLength;
            return position >= newStart && position < newEnd;
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
                var newEnd = change.Span.Start + _changeDelta + change.NewLength;
                if (_newPosition < newEnd)
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

        private bool IntersectsNextChange(SyntaxNodeOrToken nodeOrToken)
        {
            if (_changes.IsEmpty)
            {
                return false;
            }

            var change = _changes.Peek();
            var oldSpan = nodeOrToken.FullSpan;
            var changeSpan = change.Span;

            if (oldSpan.Start < changeSpan.End &&
                changeSpan.Start < oldSpan.End)
            {
                return true;
            }

            if (!changeSpan.IsEmpty ||
                change.NewLength == 0 ||
                changeSpan.Start != oldSpan.End)
            {
                return false;
            }

            var underlyingNode =
                nodeOrToken.RequiredUnderlyingNode;

            var markupElement =
                underlyingNode switch
                {
                    GreenMarkupRootSyntax root =>
                        root.Element,
                    GreenMarkupElementSyntax element =>
                        element,
                    _ => null,
                };

            if (markupElement?.StartTag is
                    { CloseToken.Kind: SyntaxKind.GreaterThanToken } &&
                markupElement.EndTag == null)
            {
                return true;
            }

            var lastTerminal =
                underlyingNode as GreenSyntaxToken ??
                underlyingNode.GetLastTerminal();

            if (lastTerminal == null)
            {
                return false;
            }

            if (lastTerminal.Kind ==
                SyntaxKind.CSharpRawToken)
            {
                return true;
            }

            var insertedPosition =
                changeSpan.Start + _changeDelta;
            var text = _lexer.TextWindow.Text;

            if ((uint)insertedPosition >=
                (uint)text.Length)
            {
                return false;
            }

            var insertedCharacter =
                text[insertedPosition];

            if (lastTerminal.Kind ==
                SyntaxKind.LessThanToken)
            {
                return SyntaxFacts.IsIdentifierStartCharacter(
                    insertedCharacter);
            }

            if (lastTerminal.Kind !=
                    SyntaxKind.IdentifierToken &&
                !SyntaxFacts.IsReservedKeyword(
                    lastTerminal.Kind) &&
                !SyntaxFacts.IsContextualKeyword(
                    lastTerminal.Kind))
            {
                return false;
            }

            return SyntaxFacts.IsIdentifierPartCharacter(
                insertedCharacter);
        }

        private static bool CanReuse(
            SyntaxNodeOrToken nodeOrToken,
            bool asToken,
            Lexer.LexerMode mode)
        {
            var node =
                nodeOrToken.RequiredUnderlyingNode;

            if (ContainsDiagnosticsOrSkippedText(node))
            {
                return false;
            }

            if (!asToken)
            {
                return nodeOrToken.IsNode;
            }

            if (!nodeOrToken.IsToken)
            {
                return false;
            }

            var isCSharpRawToken =
                node.Kind == SyntaxKind.CSharpRawToken;

            if (ExpectsCSharpRawToken(mode))
            {
                return isCSharpRawToken;
            }

            // AKCSS also contains parser-created raw C# type and expression tokens.
            return mode == Lexer.LexerMode.InAkcss ||
                !isCSharpRawToken;
        }

        private static bool ExpectsCSharpRawToken(
            Lexer.LexerMode mode)
        {
            return mode is
                Lexer.LexerMode.InInlineExpression or
                Lexer.LexerMode.InExpressionUntilSemicolon or
                Lexer.LexerMode.InExpressionUntilComma or
                Lexer.LexerMode.InArgumentExpression or
                Lexer.LexerMode.InTypeName or
                Lexer.LexerMode.InCSharpParameterList or
                Lexer.LexerMode.InCSharpArgumentList;
        }

        private static bool ContainsDiagnosticsOrSkippedText(GreenNode node)
        {
            if (node.Kind is SyntaxKind.CSharpExpressionSyntax or
                SyntaxKind.CSharpTypeSyntax or
                SyntaxKind.CSharpParameterListSyntax or
                SyntaxKind.CSharpArgumentListSyntax)
            {
                return false;
            }

            if (node.ContainsSkippedText)
            {
                return true;
            }

            if (!node.ContainsDiagnostics)
            {
                return false;
            }

            return ContainsDiagnosticsOrSkippedTextSlow(node);
        }

        private static bool ContainsDiagnosticsOrSkippedTextSlow(GreenNode node)
        {
            if (node.Kind is SyntaxKind.CSharpExpressionSyntax or
                SyntaxKind.CSharpTypeSyntax or
                SyntaxKind.CSharpParameterListSyntax or
                SyntaxKind.CSharpArgumentListSyntax)
            {
                return false;
            }

            if (node.ContainsDiagnosticsDirectly ||
                node.ContainsSkippedText)
            {
                return true;
            }

            for (var i = 0; i < node.SlotCount; i++)
            {
                var child = node.GetSlot(i);
                if (child != null && ContainsDiagnosticsOrSkippedText(child))
                {
                    return true;
                }
            }

            return false;
        }


        private static Cursor MoveToFirstToken(Cursor cursor)
        {
            while (!cursor.IsFinished && cursor.Current.IsNode)
            {
                cursor = cursor.MoveToFirstChild();
            }

            return cursor;
        }

        private static Cursor MoveToReusableNode(Cursor cursor)
        {
            if (cursor.IsFinished)
            {
                return cursor;
            }

            while (!cursor.IsFinished &&
                   cursor.Current.IsNode &&
                   cursor.Current.RequiredUnderlyingNode.IsList)
            {
                cursor = cursor.MoveToFirstChild();
            }

            if (cursor.Current.IsToken)
            {
                cursor = cursor.MoveToParent();
            }

            while (!cursor.IsFinished)
            {
                var parent = cursor.MoveToParent();
                if (parent.IsFinished ||
                    parent.Current.FullSpan.Start != cursor.Current.FullSpan.Start ||
                    parent.Current.RequiredUnderlyingNode.IsList ||
                    parent.Current.Kind is SyntaxKind.AkburaDocumentSyntax or
                        SyntaxKind.AkcssDocumentSyntax)
                {
                    break;
                }

                cursor = parent;
            }

            return cursor;
        }
    }
}
