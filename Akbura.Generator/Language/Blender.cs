using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;

#nullable disable

namespace Akbura.Language;

internal readonly partial struct Blender
{
    private readonly Lexer _lexer;
    private readonly Cursor _oldTreeCursor;
    private readonly ImmutableStack<TextChangeRange> _changes;
    private readonly int _newPosition;
    private readonly int _changeDelta;

    public Blender(
        Lexer lexer,
        AkburaSyntax oldTree,
        IEnumerable<TextChangeRange> changes)
    {
        Debug.Assert(lexer != null);

        _lexer = lexer;
        _changes = ImmutableStack<TextChangeRange>.Empty;

        if (changes != null)
        {
            var collapsed = TextChangeRange.Collapse(changes);
            _changes = _changes.Push(ExtendToAffectedRange(oldTree, collapsed));
        }

        if (oldTree == null)
        {
            _oldTreeCursor = default;
            _newPosition = lexer.TextWindow.Position;
        }
        else
        {
            _oldTreeCursor = new Cursor(oldTree).MoveToFirstChild();
            _newPosition = 0;
        }

        _changeDelta = 0;
    }

    private Blender(
        Lexer lexer,
        Cursor oldTreeCursor,
        ImmutableStack<TextChangeRange> changes,
        int newPosition,
        int changeDelta)
    {
        Debug.Assert(lexer != null);
        Debug.Assert(newPosition >= 0);

        _lexer = lexer;
        _oldTreeCursor = oldTreeCursor;
        _changes = changes;
        _newPosition = newPosition;
        _changeDelta = changeDelta;
    }

    public BlendedNode ReadNode(Lexer.LexerMode mode)
    {
        return ReadNodeOrToken(mode, asToken: false);
    }

    public BlendedNode ReadToken(Lexer.LexerMode mode)
    {
        return ReadNodeOrToken(mode, asToken: true);
    }

    public BlendedNode ReadFreshToken(Lexer.LexerMode mode)
    {
        var reader = new Reader(this);
        return reader.ReadFreshToken(mode);
    }

    private BlendedNode ReadNodeOrToken(Lexer.LexerMode mode, bool asToken)
    {
        var reader = new Reader(this);
        return reader.ReadNodeOrToken(mode, asToken);
    }

    private static TextChangeRange ExtendToAffectedRange(
        AkburaSyntax oldTree,
        TextChangeRange changeRange)
    {
        if (oldTree == null ||
            oldTree.FullWidth == 0 ||
            changeRange.Span.Start == 0 ||
            changeRange.NewLength >= changeRange.Span.Length)
        {
            return changeRange;
        }

        var originalStart = changeRange.Span.Start;
        // Only partial edits of "/>" need a left anchor; broad expansion breaks node reuse.
        var changedToken =
            oldTree.FindToken(changeRange.Span.Start);

        if (changedToken.Kind != SyntaxKind.SlashGreaterToken ||
            changeRange.Span.Start < changedToken.Span.Start ||
            changeRange.Span.End > changedToken.Span.End ||
            changeRange.Span.Length >= changedToken.Span.Length)
        {
            return changeRange;
        }

        var affectedStart = originalStart - 1;
        var affectedSpan = TextSpan.FromBounds(
            affectedStart,
            changeRange.Span.End);
        var affectedNewLength =
            changeRange.NewLength +
            originalStart -
            affectedStart;

        return new TextChangeRange(
            affectedSpan,
            affectedNewLength);
    }
}
