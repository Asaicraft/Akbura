using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Threading;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

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

        if (!CanReadIncrementalNodeOrToken())
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

    private bool TryParseIncrementalStateDeclaration(out GreenStateDeclarationSyntax state)
    {
        state = null!;

        if (!CanReadIncrementalNodeOrToken() ||
            !TryReadIncrementalToken(SyntaxKind.StateKeyword, out var stateKeyword))
        {
            return false;
        }

        var type = ParseIncrementalCSharpTypeOrNull();
        var name = ParseIncrementalIdentifierName();
        var equals = ReadRequiredIncrementalToken(SyntaxKind.EqualsToken);
        var initializer = ParseIncrementalStateInitializer();
        var semicolon = ReadRequiredIncrementalToken(SyntaxKind.SemicolonToken);

        state = GreenSyntaxFactory.StateDeclarationSyntax(
            stateKeyword,
            type,
            name,
            equals,
            initializer,
            semicolon);
        return true;
    }

    private GreenCSharpTypeSyntax? ParseIncrementalCSharpTypeOrNull()
    {
        if (TryReadReusableIncrementalNode<GreenCSharpTypeSyntax>(out var type))
        {
            return type;
        }

        var token = EatOrNullCSharpTypeSyntax();
        return token == null
            ? null
            : ParseIncrementalCSharpType(token.ToFullString());
    }

    private GreenCSharpTypeSyntax ParseIncrementalCSharpType(string text)
    {
        return GreenSyntaxFactory.CSharpTypeSyntax(
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseTypeName(text)));
    }

    private GreenIdentifierNameSyntax ParseIncrementalIdentifierName()
    {
        return TryReadReusableIncrementalNode<GreenIdentifierNameSyntax>(out var name)
            ? name
            : ParseIdentifierName();
    }

    private GreenStateInitializerSyntax ParseIncrementalStateInitializer()
    {
        if (TryReadReusableIncrementalNode<GreenStateInitializerSyntax>(out var initializer))
        {
            return initializer;
        }

        if (TryReadIncrementalToken(IsStateBindingKeyword, out var bindingKeyword))
        {
            var expression = ParseIncrementalCSharpExpressionUntilSemicolon();
            return GreenSyntaxFactory.BindableStateInitializerSyntax(bindingKeyword, expression);
        }

        return GreenSyntaxFactory.SimpleStateInitializerSyntax(
            ParseIncrementalCSharpExpressionUntilSemicolon());
    }

    private GreenCSharpExpressionSyntax ParseIncrementalCSharpExpressionUntilSemicolon()
    {
        if (TryReadReusableIncrementalNode<GreenCSharpExpressionSyntax>(out var expression))
        {
            return expression;
        }

        var mode = _mode;
        _mode = Lexer.LexerMode.InExpressionUntilSemicolon;

        var token = EatToken();

        _mode = mode;

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        return ParseIncrementalCSharpExpression(token.ToFullString());
    }

    private GreenCSharpExpressionSyntax ParseIncrementalCSharpExpression(string text)
    {
        return GreenSyntaxFactory.CSharpExpressionSyntax(
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseExpression(
                text,
                offset: 0,
                options: null,
                consumeFullText: true)));
    }

    private bool TryReadReusableIncrementalNode<TNode>(out TNode node)
        where TNode : GreenNode
    {
        node = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        var savedPosition = _lexer.TextWindow.Position;
        var blended = _blender.ReadNode(_mode);
        if (blended.Node?.Green is not TNode green ||
            !CanReuseIncrementalNode(green))
        {
            _lexer.TextWindow.Reset(savedPosition);
            return false;
        }

        _blender = blended.Blender;
        _prevTokenTrailingTrivia = ((GreenSyntaxToken?)green.GetLastTerminal())?.GetTrailingTrivia();
        node = green;
        return true;
    }

    private bool TryReadIncrementalToken(SyntaxKind kind, out GreenSyntaxToken token)
    {
        return TryReadIncrementalToken(current => current == kind, out token);
    }

    private bool TryReadIncrementalToken(Func<SyntaxKind, bool> predicate, out GreenSyntaxToken token)
    {
        token = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        var savedPosition = _lexer.TextWindow.Position;
        var blended = _blender.ReadToken(_mode);
        var green = (GreenSyntaxToken?)blended.Token.Node;

        if (green == null ||
            !predicate(green.Kind) ||
            !CanReuseIncrementalNode(green))
        {
            _lexer.TextWindow.Reset(savedPosition);
            return false;
        }

        _blender = blended.Blender;
        _prevTokenTrailingTrivia = green.GetTrailingTrivia();
        token = green;
        return true;
    }

    private GreenSyntaxToken ReadRequiredIncrementalToken(SyntaxKind kind)
    {
        if (TryReadIncrementalToken(kind, out var token))
        {
            return token;
        }

        var actual = PeekIncrementalTokenKind();
        return CreateMissingToken(kind, actual);
    }

    private SyntaxKind PeekIncrementalTokenKind()
    {
        if (!CanReadIncrementalNodeOrToken())
        {
            return CurrentToken.Kind;
        }

        var savedPosition = _lexer.TextWindow.Position;
        var blended = _blender.ReadToken(_mode);
        var kind = blended.Token.Node?.Kind ?? SyntaxKind.None;
        _lexer.TextWindow.Reset(savedPosition);
        return kind;
    }

    private bool CanReadIncrementalNodeOrToken()
    {
        return _isIncremental &&
               _mode == Lexer.LexerMode.TopLevel &&
               _currentToken == null &&
               _tokenOffset >= _tokenCount;
    }

    private static bool CanReuseTopLevelMember(GreenAkTopLevelMemberSyntax member)
    {
        return member.FullWidth > 0 &&
               !member.ContainsDiagnostics &&
               !member.ContainsSkippedText;
    }

    private static bool CanReuseIncrementalNode(GreenNode node)
    {
        return node.FullWidth > 0 &&
               !node.ContainsDiagnostics &&
               !node.ContainsSkippedText;
    }

    private static bool IsStateBindingKeyword(SyntaxKind kind)
    {
        return kind is SyntaxKind.InToken or SyntaxKind.OutToken or SyntaxKind.BindToken;
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
