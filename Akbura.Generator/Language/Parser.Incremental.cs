using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Threading;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private readonly bool _isIncremental;
    private Blender _blender;
    private Blender[]? _blendersBeforeToken;

    public Parser(
        Lexer lexer,
        CancellationToken cancellationToken,
        AkburaDocumentSyntax? oldTree,
        IEnumerable<TextChangeRange>? changes)
        : this(lexer, cancellationToken)
    {
        if (oldTree == null)
        {
            return;
        }

        _isIncremental = true;
        _blender = new Blender(lexer, oldTree, changes);
        _blendersBeforeToken = new Blender[_lexedTokens.Length];
    }

    private void AddNewBlendedToken()
    {
        var beforeToken = _blender;
        var blended = _blender.ReadFreshToken(_mode);
        _blender = blended.Blender;

        AddLexedToken((GreenSyntaxToken)blended.Token.RequiredNode);
        _blendersBeforeToken![_tokenCount - 1] = beforeToken;
    }

    private GreenSyntaxToken FastPeekBlendedToken()
    {
        var blended = _blender.ReadFreshToken(_mode);
        return (GreenSyntaxToken)blended.Token.RequiredNode;
    }

    private bool TryEatReusableTopLevelMember(out GreenAkTopLevelMemberSyntax member)
    {
        member = null!;

        if (!_isIncremental ||
            _mode != Lexer.LexerMode.TopLevel ||
            _currentToken != null ||
            _tokenOffset < _tokenCount)
        {
            return false;
        }

        var blended = _blender.ReadNode(_mode);
        if (blended.Node is not AkTopLevelMemberSyntax node ||
            !CanReuseTopLevelMember(node.Green))
        {
            return false;
        }

        _blender = blended.Blender;
        _prevTokenTrailingTrivia = ((GreenSyntaxToken?)node.Green.GetLastTerminal())?.GetTrailingTrivia();
        member = node.Green;
        return true;
    }

    private static bool CanReuseTopLevelMember(GreenAkTopLevelMemberSyntax member)
    {
        return member.FullWidth > 0 &&
               !member.ContainsDiagnostics &&
               !member.ContainsSkippedText;
    }

    private void RestoreBlenderBeforeReturnedToken(int tokenIndex)
    {
        if (!_isIncremental || _blendersBeforeToken == null)
        {
            return;
        }

        _blender = _blendersBeforeToken[tokenIndex];
        _blendersBeforeToken[tokenIndex] = default;
    }

    private void ShiftBlendedTokenSlots(int shiftOffset, int shiftCount)
    {
        if (!_isIncremental || _blendersBeforeToken == null)
        {
            return;
        }

        if (shiftCount > 0)
        {
            Array.Copy(_blendersBeforeToken, shiftOffset, _blendersBeforeToken, 0, shiftCount);
        }

        Array.Clear(_blendersBeforeToken, shiftCount, _blendersBeforeToken.Length - shiftCount);
    }

    private void ResizeBlendedTokenSlots(int length)
    {
        if (!_isIncremental || _blendersBeforeToken == null)
        {
            return;
        }

        Array.Resize(ref _blendersBeforeToken, length);
    }
}
