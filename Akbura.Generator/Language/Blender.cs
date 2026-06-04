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

    private BlendedNode ReadNodeOrToken(Lexer.LexerMode mode, bool asToken)
    {
        var reader = new Reader(this);
        return reader.ReadNodeOrToken(mode, asToken);
    }

    private static TextChangeRange ExtendToAffectedRange(
        AkburaSyntax oldTree,
        TextChangeRange changeRange)
    {
        if (oldTree == null || oldTree.FullWidth == 0)
        {
            return changeRange;
        }

        const int maxLookahead = 1;
        var start = changeRange.Span.Start;

        for (var i = 0; start > 0 && i < maxLookahead; i++)
        {
            start--;
        }

        var finalSpan = TextSpan.FromBounds(start, changeRange.Span.End);
        var finalLength = changeRange.NewLength + (changeRange.Span.Start - start);

        return new TextChangeRange(finalSpan, finalLength);
    }
}
