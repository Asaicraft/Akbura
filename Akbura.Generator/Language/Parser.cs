using Akbura.Collections;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;
internal sealed class Parser : IDisposable
{
    // Array size held in token pool. This should be large enough to prevent most allocations, but
    //  not so large as to be wasteful when not in use.
    private const int CachedTokenArraySize = 4096;

    // Maximum index where a value has been written in _lexedTokens. This will allow Dispose
    //   to limit the range needed to clear when releasing the lexed token array back to the pool.
    private int _maxWrittenLexedTokenIndex = -1;

    private static readonly ObjectPool<ArrayElement<GreenSyntaxToken>[]> s_lexedTokensPool = new(() => new ArrayElement<GreenSyntaxToken>[CachedTokenArraySize]);

    private readonly GreenSyntaxListPool _pool = new();
    private readonly Lexer _lexer;
    private readonly CancellationToken _cancellationToken;

    private GreenSyntaxToken? _currentToken;
    private ArrayElement<GreenSyntaxToken>[] _lexedTokens;
    private Lexer.LexerMode _mode;
    private int _recursionDepth;
    private GreenNode? _prevTokenTrailingTrivia;
    private int _firstToken; // The position of _lexedTokens[0] (or _blendedTokens[0]).
    private int _tokenOffset; // The index of the current token within _lexedTokens or _blendedTokens.
    private int _tokenCount;
    private int _resetCount;
    private int _resetStart;

    public Parser(Lexer lexer, CancellationToken cancellationToken)
    {
        _lexer = lexer;
        _cancellationToken = cancellationToken;

        _lexedTokens = s_lexedTokensPool.Allocate();

    }

    private GreenSyntaxToken CurrentToken
    {
        get => _currentToken ??= FetchCurrentToken();
    }

    private GreenSyntaxToken FetchCurrentToken()
    {
        if (_tokenOffset >= _tokenCount)
        {
            AddNewToken();
        }

        return _lexedTokens[_tokenOffset];
    }

    private void AddNewToken()
    {
        AddLexedToken(_lexer.Lex(_mode));
    }

    private void AddLexedToken(GreenSyntaxToken token)
    {
        AkburaDebug.Assert(token != null);
        if (_tokenCount >= _lexedTokens.Length)
        {
            AddLexedTokenSlot();
        }

        if (_tokenCount > _maxWrittenLexedTokenIndex)
        {
            _maxWrittenLexedTokenIndex = _tokenCount;
        }

        _lexedTokens[_tokenCount].Value = token;
        _tokenCount++;
    }

    private void AddLexedTokenSlot()
    {
        // shift tokens to left if we are far to the right
        // don't shift if reset points have fixed locked the starting point at the token in the window
        if (_tokenOffset > (_lexedTokens.Length >> 1)
            && (_resetStart == -1 || _resetStart > _firstToken))
        {
            var shiftOffset = (_resetStart == -1) ? _tokenOffset : _resetStart - _firstToken;
            var shiftCount = _tokenCount - shiftOffset;
            Debug.Assert(shiftOffset > 0);
            if (shiftCount > 0)
            {
                Array.Copy(_lexedTokens, shiftOffset, _lexedTokens, 0, shiftCount);
            }

            _firstToken += shiftOffset;
            _tokenCount -= shiftOffset;
            _tokenOffset -= shiftOffset;
        }
        else
        {
            var lexedTokens = _lexedTokens;

            Array.Resize(ref _lexedTokens, _lexedTokens.Length * 2);

            ReturnLexedTokensToPool(lexedTokens);
        }
    }

    private GreenSyntaxToken PeekToken(int n)
    {
        Debug.Assert(n >= 0);
        while (_tokenOffset + n >= _tokenCount)
        {
            AddNewToken();
        }

        return _lexedTokens[_tokenOffset + n];
    }

    //this method is called very frequently
    //we should keep it simple so that it can be inlined.
    private GreenSyntaxToken EatToken()
    {
        var currentToken = CurrentToken;
        MoveToNextToken();
        return currentToken;
    }

    // <summary>
    /// Returns and consumes the current token if it has the requested <paramref name="kind"/>.
    /// Otherwise, returns <see langword="null"/>.
    /// </summary>
    private GreenSyntaxToken? TryEatToken(SyntaxKind kind)
        => CurrentToken.Kind == kind ? EatToken() : null;

    private void MoveToNextToken()
    {
        _prevTokenTrailingTrivia = _currentToken?.GetTrailingTrivia();

        _currentToken = null;

        _tokenOffset++;
    }

    private void ForceEndOfFile()
    {
        _currentToken = GreenSyntaxFactory.Token(SyntaxKind.EndOfFileToken);
    }

    private GreenSyntaxToken EatToken(SyntaxKind kind)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));

        var currentToken = CurrentToken;
        if (currentToken.Kind == kind)
        {
            MoveToNextToken();
            return currentToken;
        }

        //slow part of EatToken(SyntaxKind kind)
        return CreateMissingToken(kind, this.CurrentToken.Kind);
    }

    // Consume a token if it is the right kind. Otherwise skip a token and replace it with one of the correct kind.
    private GreenSyntaxToken EatTokenAsKind(SyntaxKind expected)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(expected));

        var currentToken = CurrentToken;
        if (currentToken.Kind == expected)
        {
            MoveToNextToken();
            return currentToken;
        }

        var replacement = CreateMissingToken(expected, this.CurrentToken.Kind);
        return AddTrailingSkippedSyntax(replacement, this.EatToken());
    }

    private void ReturnLexedTokensToPool(ArrayElement<GreenSyntaxToken>[] lexedTokens)
    {
        // Put lexedTokens back into the pool if it's correctly sized.
        if (lexedTokens.Length == CachedTokenArraySize)
        {
            // Clear all written indexes in lexedTokens before releasing back to the pool
            Array.Clear(lexedTokens, 0, _maxWrittenLexedTokenIndex + 1);

            s_lexedTokensPool.Free(lexedTokens);
        }
    }

    private GreenSyntaxToken CreateMissingToken(SyntaxKind expected, SyntaxKind actual)
    {
        var token = GreenSyntaxFactory.MissingToken(expected);
        return WithAdditionalDiagnostics(token, this.GetExpectedMissingNodeOrTokenError(token, expected, actual));
    }

    private GreenSyntaxToken CreateMissingToken(SyntaxKind expected, string errorCode, bool reportError)
    {
        // should we eat the current ParseToken's leading trivia?
        var token = GreenSyntaxFactory.MissingToken(expected);
        if (reportError)
        {
            token = AddError(token, errorCode);
        }

        return token;
    }

    private GreenSyntaxToken EatToken(SyntaxKind kind, bool reportError)
    {
        if (reportError)
        {
            return EatToken(kind);
        }

        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        if (CurrentToken.Kind != kind)
        {
            // should we eat the current ParseToken's leading trivia?
            return GreenSyntaxFactory.MissingToken(kind);
        }
        else
        {
            return EatToken();
        }
    }

    private GreenSyntaxToken EatToken(SyntaxKind kind, string errorCode, bool reportError = true)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        if (CurrentToken.Kind != kind)
        {
            return CreateMissingToken(kind, errorCode, reportError);
        }
        else
        {
            return EatToken();
        }
    }

    /// <summary>
    /// Called when we need to eat a token even if its kind is different from what we're looking for.  This will
    /// place a diagnostic on the resultant token if the kind is not correct.  Note: the token's kind will
    /// <em>not</em> be the same as <paramref name="kind"/>.  As such, callers should take great care here to ensure
    /// they process the result properly in their context.  For example, adding the token as skipped syntax, or
    /// forcibly changing its kind by some other means.
    /// </summary>
    private GreenSyntaxToken EatTokenEvenWithIncorrectKind(SyntaxKind kind)
    {
        var token = this.CurrentToken;
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));
        if (token.Kind != kind)
        {
            var (offset, width) = getDiagnosticSpan();
            token = WithAdditionalDiagnostics(token, this.GetExpectedTokenError(kind, token.Kind, offset, width));
        }

        MoveToNextToken();
        return token;

        (int offset, int width) getDiagnosticSpan()
        {
            // We got the wrong kind while forcefully eating this token.  If it's on the same line as the last
            // token, just squiggle it as being the wrong kind. If it's on the next line, move the squiggle back to
            // the end of the previous token and make it zero width, indicating the expected token was missed at
            // that location (even though we're still unilaterally consuming this token).

            var trivia = _prevTokenTrailingTrivia;
            var triviaList = new GreenSyntaxList<GreenNode>(trivia);
            return triviaList.Any((int)SyntaxKind.EndOfLineTrivia)
                ? (offset: -(trivia.FullWidth + token.GetLeadingTriviaWidth()), width: 0)
                : ((int offset, int width))(offset: 0, token.Width);
        }
    }


    private GreenSyntaxToken EatTokenWithPrejudice(string errorCode, params object[] args)
    {
        var token = EatToken();
        token = WithAdditionalDiagnostics(token, MakeError(offset: 0, token.Width, errorCode, args));
        return token;
    }

    private GreenSyntaxToken EatContextualToken(SyntaxKind kind, string errorCode)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));

        if (CurrentToken.ContextualKind != kind)
        {
            return CreateMissingToken(kind, errorCode, reportError: true);
        }
        else
        {
            return ConvertToKeyword(EatToken());
        }
    }

    private GreenSyntaxToken EatContextualToken(SyntaxKind kind)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));

        var contextualKind = CurrentToken.ContextualKind;
        if (contextualKind != kind)
        {
            return CreateMissingToken(kind, contextualKind);
        }
        else
        {
            return ConvertToKeyword(EatToken());
        }
    }

    private SyntaxDiagnosticInfo GetExpectedTokenError(SyntaxKind expected, SyntaxKind actual, int offset, int width)
    {
        var code = GetExpectedTokenErrorCode(expected, actual);
        if (code == ErrorCodes.ERR_SyntaxError)
        {
            return new SyntaxDiagnosticInfo(offset, width, code, [SyntaxFacts.GetText(expected)]);
        }
        else if (code == ErrorCodes.ERR_IdentifierExpectedKW)
        {
            return new SyntaxDiagnosticInfo(offset, width, code, [SyntaxFacts.GetText(actual)]);
        }
        else
        {
            return new SyntaxDiagnosticInfo(offset, width, code);
        }
    }

    private static string GetExpectedTokenErrorCode(SyntaxKind expected, SyntaxKind actual)
    {
        switch (expected)
        {
            case SyntaxKind.IdentifierToken:
                if (SyntaxFacts.IsReservedKeyword(actual))
                {
                    return ErrorCodes.ERR_IdentifierExpectedKW;   // A keyword -- use special message.
                }
                else
                {
                    return ErrorCodes.ERR_IdentifierExpected;
                }

            case SyntaxKind.SemicolonToken:
                return ErrorCodes.ERR_SemicolonExpected;

            // case TokenKind::Colon:         iError = ERR_ColonExpected;          break;
            // case TokenKind::OpenParen:     iError = ERR_LparenExpected;         break;
            case SyntaxKind.CloseParenToken:
                return ErrorCodes.ERR_CloseParenExpected;
            case SyntaxKind.OpenBraceToken:
                return ErrorCodes.ERR_LbraceExpected;
            case SyntaxKind.CloseBraceToken:
                return ErrorCodes.ERR_RbraceExpected;

            // case TokenKind::CloseSquare:   iError = ERR_CloseSquareExpected;    break;
            default:
                return ErrorCodes.ERR_SyntaxError;
        }
    }

    private TNode WithAdditionalDiagnostics<TNode>(TNode node, params ImmutableArray<AkburaDiagnostic> diagnostics) where TNode : GreenNode
    {
        return (TNode)node.AddDiagnostics(diagnostics);
    }

    private TNode AddError<TNode>(TNode node, string errorCode) where TNode : GreenNode
    {
        return AddError(node, errorCode, []);
    }

    private TNode AddErrorAsWarning<TNode>(TNode node, string errorCode, params object[] args) where TNode : GreenNode
    {
        Debug.Assert(!node.IsMissing);
        return AddError(node, ErrorCodes.WRN_ErrorOverride, MakeError(node, errorCode, args), errorCode);
    }

    private TNode AddError<TNode>(TNode nodeOrToken, string errorCode, params object[] args) where TNode : GreenNode
    {
        if (!nodeOrToken.IsMissing)
        {
            // We have a normal node or token that has actual SyntaxToken.Text within it (or the EOF token). Place
            // the diagnostic at the start (not full start) of that real node/token, with a width that encompasses
            // the entire normal width of the node or token.
            Debug.Assert(nodeOrToken.Width > 0 || nodeOrToken.RawKind is (int)SyntaxKind.EndOfFileToken);
            return WithAdditionalDiagnostics(nodeOrToken, MakeError(nodeOrToken, errorCode, args));
        }
        else
        {
            var (offset, width) = this.GetDiagnosticSpanForMissingNodeOrToken(nodeOrToken);
            return WithAdditionalDiagnostics(nodeOrToken, MakeError(offset, width, errorCode, args));
        }
    }

    /// <summary>
    /// Computes the diagnostic span for a missing node or token. Since missing nodes
    /// have no real text, we attach the diagnostic either:
    ///   1. At the end of the previous line, if the previous token ends with an EOL.
    ///   2. On the next real token, otherwise.
    /// </summary>
    private (int offset, int width) GetDiagnosticSpanForMissingNodeOrToken(GreenNode missingNodeOrToken)
    {
        Debug.Assert(missingNodeOrToken.IsMissing);

        return getOffsetAndWidthBasedOnPriorAndNextTokens();

        (int offset, int width) getOffsetAndWidthBasedOnPriorAndNextTokens()
        {
            AkburaDebug.AssertNotNull(_prevTokenTrailingTrivia);

            var trailingTrivia = _prevTokenTrailingTrivia;
            var trailingList = new GreenSyntaxList<GreenNode>(trailingTrivia);

            // Case 1: previous token ended with newline => place diagnostic at end of the line.
            if (trailingList.Any((int)SyntaxKind.EndOfLineTrivia))
            {
                // Shift back by leading trivia of the missing node and full width of the trailing trivia.
                return (
                    offset: -missingNodeOrToken.GetLeadingTriviaWidth() - trailingTrivia.FullWidth,
                    width: 0
                );
            }

            // Case 2: same line => place diagnostic on next token
            var next = this.CurrentToken;

            return (
                offset:
                    missingNodeOrToken.Width +
                    missingNodeOrToken.GetTrailingTriviaWidth() +
                    next.GetLeadingTriviaWidth(),
                width: next.Width
            );
        }
    }

    /// <summary>
    /// Adds an error diagnostic to the node at the specified (offset,length).
    /// Works both for normal and missing nodes.
    /// </summary>
    private TNode AddError<TNode>(TNode node, int offset, int length, string errorCode, params object[] args)
        where TNode : GreenNode
    {
        return WithAdditionalDiagnostics(node, MakeError(offset, length, errorCode, args));
    }

    /// <summary>
    /// Adds an error diagnostic to the first token of the node.
    /// </summary>
    private TNode AddErrorToFirstToken<TNode>(TNode node, string errorCode)
        where TNode : GreenNode
    {
        var firstToken = (GreenSyntaxToken?)node.GetFirstTerminal();

        AkburaDebug.AssertNotNull(firstToken);

        return WithAdditionalDiagnostics(
            node,
            MakeError(offset: 0, firstToken.Width, errorCode)
        );
    }

    /// <summary>
    /// Adds an error diagnostic with arguments to the first token of the node.
    /// </summary>
    private TNode AddErrorToFirstToken<TNode>(TNode node, string errorCode, params ImmutableArray<object> args)
        where TNode : GreenNode
    {
        var firstToken = (GreenSyntaxToken?)node.GetFirstTerminal();

        AkburaDebug.AssertNotNull(firstToken);

        return WithAdditionalDiagnostics(
            node,
            MakeError(offset: 0, firstToken.Width, errorCode, args)
        );
    }

    /// <summary>
    /// Adds an error diagnostic to the last token of the node.
    /// </summary>
    private TNode AddErrorToLastToken<TNode>(TNode node, string errorCode)
        where TNode : GreenNode
    {
        GetOffsetAndWidthForLastToken(node, out var offset, out var width);
        return WithAdditionalDiagnostics(node, MakeError(offset, width, errorCode));
    }

    private static void GetOffsetAndWidthForLastToken<TNode>(TNode node, out int offset, out int width) where TNode : GreenNode
    {
        var lastToken = (GreenSyntaxToken?)node.GetLastNonmissingTerminal();

        AkburaDebug.AssertNotNull(lastToken);

        offset = node.Width + node.GetTrailingTriviaWidth(); //advance to end of entire node
        width = 0;
        if (lastToken != null) //will be null if all tokens are missing
        {
            offset -= lastToken.FullWidth; //rewind past last token
            offset += lastToken.GetLeadingTriviaWidth(); //advance past last token leading trivia - now at start of last token
            width = lastToken.Width;
        }
    }


    public void Dispose()
    {
        ReturnLexedTokensToPool(_lexedTokens);
    }



}
