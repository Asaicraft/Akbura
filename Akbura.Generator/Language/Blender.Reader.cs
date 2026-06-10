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
				IntersectsNextChange(oldSpan) ||
				!CanReuse(nodeOrToken, asToken))
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

		private bool IntersectsNextChange(TextSpan oldSpan)
		{
			if (_changes.IsEmpty)
			{
				return false;
			}

			var changeSpan = _changes.Peek().Span;
			return oldSpan.Start < changeSpan.End && changeSpan.Start < oldSpan.End;
		}

		private static bool CanReuse(SyntaxNodeOrToken nodeOrToken, bool asToken)
		{
			if (ContainsDiagnosticsOrSkippedText(nodeOrToken.RequiredUnderlyingNode))
			{
				return false;
			}

			return asToken
				? nodeOrToken.IsToken
				: nodeOrToken.IsNode;
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
					parent.Current.Kind == SyntaxKind.AkburaDocumentSyntax)
				{
					break;
				}

				cursor = parent;
			}

			return cursor;
		}
	}
}
