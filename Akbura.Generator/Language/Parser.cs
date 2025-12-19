// this file is ported and adapted from roslyn

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
using static Akbura.Language.Syntax.Green.GreenSyntaxToken;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;
internal sealed partial class Parser : IDisposable
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

    [DebuggerBrowsable(DebuggerBrowsableState.Never)] // watch only _currentToken to avoid fetching tokens in debugger
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

    private GreenSyntaxToken FastPeekToken()
    {
        var token = _lexer.Lex(_mode);

        _lexer.TextWindow.Reset(_lexer.TextWindow.Position - token.FullWidth);

        return token;
    }

    /// <summary>
    /// Rolls back the parser by one token (undoes a single EatToken / MoveToNextToken).
    /// The token must already exist in the lexed token buffer.
    /// </summary>
    private void ReturnToken()
    {
        // Nothing to rewind.
        if (_tokenOffset == 0)
        {
            return;
        }

        var lastToken = _lexedTokens[_tokenCount - 1];
        _lexedTokens[_tokenCount - 1] = default;

        // Undo MoveToNextToken increment.
        _tokenOffset--;
        _tokenCount--;

        // Clear cached current token so CurrentToken will be fetched from the buffer.
        _currentToken = null;

        // Restore trailing trivia of the token *before* the new current token.
        // This keeps GetDiagnosticSpanForMissingNodeOrToken behavior stable.
        if (_tokenOffset > 0)
        {
            var prev = _lexedTokens[_tokenOffset - 1].Value;
            _prevTokenTrailingTrivia = prev?.GetTrailingTrivia();
        }
        else
        {
            _prevTokenTrailingTrivia = null;
        }

        if (lastToken.Value == null)
        {
            return;
        }
        var currentPosition = _lexer.TextWindow.Position;
        _lexer.TextWindow.Reset(currentPosition - lastToken.Value.FullWidth);
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

    private GreenSyntaxToken EatOrReturn(SyntaxKind kind)
    {
        Debug.Assert(SyntaxFacts.IsAnyToken(kind));

        var currentToken = CurrentToken;
        if (currentToken.Kind == kind)
        {
            MoveToNextToken();
            return currentToken;
        }

        //slow part of EatToken(SyntaxKind kind)
        var returToken = CreateMissingToken(kind, currentToken.Kind);
        ReturnToken();

        _tokenOffset++;

        return returToken;
    }

    private CSharpRawToken? EatOrNullCSharpTypeSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InTypeName;

        var token = EatToken();

        _mode = mode;

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        AkburaDebug.Assert(((CSharpRawToken)token).RawNode is CSharp.TypeSyntax, "Exprected TypeSyntax");

        var typeOrIdentifier = (CSharp.TypeSyntax)((CSharpRawToken)token).RawNode!;

        // if it's not an identifier name, it's definitely a type
        if (typeOrIdentifier.Kind() != CSharpSyntaxKind.IdentifierName)
        {
            return (CSharpRawToken)token;
        }

        var fastToken = FastPeekToken();

        if (fastToken.Kind == SyntaxKind.EqualsToken)
        {
            // if the next token is '=', then it's a identifier used as a name, not a type
            ReturnToken();
            return null;
        }

        if (fastToken.Kind == SyntaxKind.IdentifierToken)
        {
            // if the next token is an identifier, then it's definitely a type
            return (CSharpRawToken)token;
        }

        ReturnToken();
        return null;
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
        return WithAdditionalDiagnostics(token, GetExpectedMissingNodeOrTokenError(token, expected, actual));
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
            token = WithAdditionalDiagnostics(token, GetExpectedTokenError(kind, token.Kind, offset, width));
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

   private SyntaxDiagnosticInfo GetExpectedMissingNodeOrTokenError(
            GreenNode missingNodeOrToken, SyntaxKind expected, SyntaxKind actual)
    {
        Debug.Assert(missingNodeOrToken.IsMissing);

        var (offset, width) = GetDiagnosticSpanForMissingNodeOrToken(missingNodeOrToken);
        return GetExpectedTokenError(expected, actual, offset, width);
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
    /// Given a "missing" node or token (one where <see cref="GreenNode.IsMissing"/> must be true), determines the
    /// ideal location to place the diagnostic for it.  The intuition here is that we want to place the diagnostic
    /// on the token that "follows" this 'missing' entity if they're on the same line.  Or, place it at the end of
    /// the 'preceding' token if the following token is on the next line.
    /// </summary>
    private (int offset, int width) GetDiagnosticSpanForMissingNodeOrToken(GreenNode missingNodeOrToken)
    {
        Debug.Assert(missingNodeOrToken.IsMissing);

        // Note: missingNodeOrToken.IsMissing means this is either a MissingToken itself, or a node comprised
        // (transitively) only from MissingTokens.  Missing tokens are guaranteed to have no text.  But they are
        // allowed to have trivia.  This is a common pattern the parser will follow when it encounters unexpected
        // tokens.  It will make a missing token of the expected kind for the current location, then attach the
        // unexpected tokens as missed tokens to it.

        // At this point, we have a node or token without real text in it.  The intuition we have here is that we
        // want to place the diagnostic on the token that "follows" this 'missing' entity.  There is a subtlety
        // here.  If the node or token contains skipped tokens, then we consider that skipped token the "following"
        // token, and we will want to place the diagnostic on it.  Otherwise, we want to place it on the true 'next
        // token' the parser is currently pointing at.

        if (!missingNodeOrToken.ContainsSkippedText)
        {
            // Simple case this node/token does not contain any skipped text.  Place the diagnostic at the start of
            // the token that follows.
            return getOffsetAndWidthBasedOnPriorAndNextTokens();
        }
        else
        {
            // Complex case.  This node or token contains skipped text.  Place the diagnostic on the skipped text.
            return getOffsetAndWidthOfSkippedToken();
        }

        (int offset, int width) getOffsetAndWidthBasedOnPriorAndNextTokens()
        {
            // If the previous token has a trailing EndOfLineTrivia, the missing token diagnostic position is moved
            // to the end of line containing the previous token and its width is set to zero. Otherwise we squiggle
            // the token following the missing token (the token we're currently pointing at).
            var trivia = _prevTokenTrailingTrivia;
            var triviaList = new GreenSyntaxList<GreenNode>(trivia);
            if (triviaList.Any((int)SyntaxKind.EndOfLineTrivia))
            {
                // We have:
                //
                //   [previous token][previous token trailing trivia...][missing node leading trivia...][missing node or token]
                //                                                                                      ^
                //                                                                                      | here
                //
                // Update so we report diagnostic here:
                //
                //   [previous token][previous token trailing trivia...][missing node leading trivia...][missing node or token]
                //                   ^
                //                   | here
                return (offset: -missingNodeOrToken.GetLeadingTriviaWidth() - trivia.FullWidth, width: 0);
            }
            else
            {
                // We have:
                //
                //   [missing node leading trivia...][missing node or token][missing node or token trailing trivia..][current token leading trivia ...][current token]
                //                                   ^
                //                                   | here
                //
                // Update so we report diagnostic here:
                //
                //   [missing node leading trivia...][missing node or token][missing node or token trailing trivia..][current token leading trivia ...][current token]
                //                                                                                                                                     ^             ^
                //                                                                                                                                     | --- here -- |
                var token = this.CurrentToken;
                return (missingNodeOrToken.Width + missingNodeOrToken.GetTrailingTriviaWidth() + token.GetLeadingTriviaWidth(), token.Width);
            }
        }

        (int offset, int width) getOffsetAndWidthOfSkippedToken()
        {
            var offset = 0;

            // Walk all the children of this nodeOrToken (including itself).  Note: this does not walk into trivia.
            // We are looking for the first token that has skipped text.  When we find that token (which must exist,
            // based on the check above), we will place the diagnostic on the skipped token within that token.
            foreach (var child in missingNodeOrToken.EnumerateNodes())
            {
                Debug.Assert(child.IsMissing, "All children of a missing node or token should themselves be missing.");
                if (!child.IsToken)
                    continue;

                var childToken = (GreenSyntaxToken)child;
                Debug.Assert(childToken.Text == "", "All missing tokens should have no text");
                if (!child.ContainsSkippedText)
                {
                    offset += child.FullWidth;
                    continue;
                }

                // Now, walk the trivia of this token, looking for the skipped tokens trivia.
                var allTrivia = new GreenSyntaxList<GreenNode>(GreenSyntaxList.Concat(childToken.GetLeadingTrivia(), childToken.GetTrailingTrivia()));
                Debug.Assert(allTrivia.Count > 0, "How can a token with skipped text not have trivia at all?");

                foreach (var trivia in allTrivia)
                {
                    if (!trivia.IsSkippedTokensTrivia)
                    {
                        offset += trivia.FullWidth;
                        continue;
                    }

                    // Found the skipped tokens trivia.  Place the diagnostic on it.
                    return (offset, trivia.Width);
                }

                Debug.Fail("This should not be reachable.  We should have hit a skipped token in the trivia of this token.");
                return default;
            }

            Debug.Fail("This should not be reachable.  We should have hit a child token with skipped text within this node.");
            return default;
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

    private static SyntaxDiagnosticInfo MakeError(int offset, int length, string errorCode, params ImmutableArray<object?> args)
    {
        return new SyntaxDiagnosticInfo(offset, length, errorCode, args);
    }

    private static SyntaxDiagnosticInfo MakeError(GreenNode node, string errorCode, params ImmutableArray<object?> args)
    {
        return new SyntaxDiagnosticInfo(0, node.Width, errorCode, args);
    }

    private static AkburaDiagnostic MakeError(string errorCode, params ImmutableArray<object?> args)
    {
        return new AkburaDiagnostic(args, errorCode, AkburaDiagnosticSeverity.Error);
    }

    #region Skipped Syntax Handling

    private TNode AddLeadingSkippedSyntax<TNode>(TNode node, GreenNode? skippedSyntax) where TNode : GreenNode
    {
        if (skippedSyntax is null)
        {
            return node;
        }

        var oldToken = node as GreenSyntaxToken ?? (GreenSyntaxToken)node.GetFirstTerminal()!;
        var newToken = AddSkippedSyntax(oldToken, skippedSyntax, trailing: false);
        return GreenSyntaxFirstTokenReplacer.Replace(node, oldToken, newToken, skippedSyntax.FullWidth);
    }

    private void AddTrailingSkippedSyntax(GreenSyntaxListBuilder list, GreenNode skippedSyntax)
    {
        list[^1] = AddTrailingSkippedSyntax(list[^1]!, skippedSyntax);
    }

    private void AddTrailingSkippedSyntax<TNode>(GreenSyntaxListBuilder<TNode> list, GreenNode skippedSyntax) where TNode : GreenNode
    {
        list[^1] = AddTrailingSkippedSyntax(list[^1], skippedSyntax);
    }

    private TNode AddTrailingSkippedSyntax<TNode>(TNode node, GreenNode skippedSyntax) where TNode : GreenNode
    {
        if (node is GreenSyntaxToken token)
        {
            return (TNode)(object)AddSkippedSyntax(token, skippedSyntax, trailing: true);
        }
        else
        {
            var lastToken = (GreenSyntaxToken)node.GetLastTerminal()!;
            var newToken = AddSkippedSyntax(lastToken, skippedSyntax, trailing: true);
            return GreenSyntaxLastTokenReplacer.Replace(node, newToken);
        }
    }

    /// <summary>
    /// Converts skippedSyntax node into all its constituent tokens (and their constituent trivias) and adds these
    /// all as trivia on the target token.  For example, given <c>token1-token2</c>, then target will have
    /// <c>leading_trivia1-token1-trailing_trivia1-leading_trivia2-token2-trailing_trivia2-</c> added to it.
    /// <para/>
    /// 
    /// Also adds the first node-based error, or error on a missing-token, in depth-first preorder, found in the
    /// skipped syntax tree to the target token.  This ensures that we do not lose token/node errors found in
    /// skipped syntax.
    /// 
    /// Note: This behavior could technically lead to buggy behavior.  Specifically, because we only take the first
    /// diagnostic we find, we might miss a more relevant diagnostic later in the tree.  For example, we might
    /// preserve a 'warning' while missing an error.
    /// 
    /// We should either:
    /// 
    /// 1. ensure that we copy over an error if it exists, overwriting any warnings we found along the way.
    /// 
    /// 2. just copy over everything.  This seems saner, as it means not losing anything. But it might be the case
    /// that when we recover from a big error recovery scan, we might report a ton of errors.
    ///
    /// For now, we do neither, and just take the first error/warning we find.  This can/should be revisited later
    /// if we discover it means we're losing important diagnostics.
    /// </summary>
    internal GreenSyntaxToken AddSkippedSyntax(GreenSyntaxToken target, GreenNode skippedSyntax, bool trailing)
    {
        var builder = new GreenSyntaxListBuilder(4);

        int currentOffset;
        if (trailing)
        {
            // The normal offset for a node/token is its start (not full start).  So if we're placing the skipped
            // syntax at the end of the trivia, then the offset relative to the node/token start will be adjusted
            // forward by the width of the node/token plus the existing trailing trivia.
            currentOffset = target.Width + target.GetTrailingTriviaWidth();
            builder.Add(target.GetTrailingTrivia());
        }
        else
        {
            // The normal offset for a node/token is its start (not full start). So if we're placing the skipped
            // syntax at the start of the trivia, then the offset relative to the node/token start will be adjusted
            // backward by the width of the existing leading trivia plus the width of the skipped syntax we're
            // tacking on at the front.
            currentOffset = -target.GetLeadingTriviaWidth() - skippedSyntax.FullWidth;
        }

        // the error in we'll attach to the node
        SyntaxDiagnosticInfo diagnostic = null!;
        var finalDiagnosticOffset = 0;

        foreach (var node in skippedSyntax.EnumerateNodes())
        {
            if (node is GreenSyntaxToken token)
            {
                // Strip the leading trivia of the token, and add it to the target's final trivia list.
                builder.Add(token.GetLeadingTrivia());

                if (token.Width > 0)
                {
                    // Then add the token (stripped of its own trivia) to the target's final trivia list.

                    builder.Add(GreenSyntaxFactory.SkippedTokensTrivia(
                        token.TokenWithLeadingTrivia(null).TokenWithTrailingTrivia(null)));
                }
                else
                {
                    // Do not bother adding zero-width tokens to target's final trivia list.  Lots of code (like
                    // GetStructure) does not like it at all. But do keep around any diagnostics that might have
                    // been on this zero width token, and move it to the target.
                    var existing = (SyntaxDiagnosticInfo)token.GetDiagnostics().FirstOrDefault()!;
                    if (existing != null)
                    {
                        diagnostic = existing;
                        finalDiagnosticOffset = currentOffset + token.GetLeadingTriviaWidth() + existing.Position;
                    }
                }

                // Finally strip the trailing trivia of the token, and add it to the target's final list.
                builder.Add(token.GetTrailingTrivia());

                currentOffset += token.FullWidth;
            }
            else if (node.ContainsDiagnostics && diagnostic == null)
            {
                // Ensure we don't lose any diagnostics on non-token nodes that we're diving into.
                // Only propagate the first error to reduce noise:
                var existing = (SyntaxDiagnosticInfo)node.GetDiagnostics().FirstOrDefault()!;
                if (existing != null)
                {
                    diagnostic = existing;
                    finalDiagnosticOffset = currentOffset + node.GetLeadingTriviaWidth() + existing.Position;
                }
            }
        }

        // If we found a diagnostic on a node (or empty-width token) in the skipped syntax, ensure it is moved
        // over to the target.
        if (diagnostic != null)
        {
            target = WithAdditionalDiagnostics(target,
                new SyntaxDiagnosticInfo(finalDiagnosticOffset, diagnostic.Width, diagnostic.Code, diagnostic.Parameters));
        }

        // If we were adding the skipped token as trailing trivia, then at this point we're done.  Otherwise, we
        // were adding it as leading trivia, so we need to tack on the existing leading trivia of the target.
        return trailing
            ? target.TokenWithTrailingTrivia(builder.ToListNode())
            : target.TokenWithLeadingTrivia(builder.AddRange(target.GetLeadingTrivia()).ToListNode());
    }

    #endregion

    private static GreenSyntaxToken ConvertToKeyword(GreenSyntaxToken token)
    {
        if (token.Kind != token.ContextualKind)
        {
            var keyword = token.IsMissing
                    ? GreenSyntaxFactory.MissingToken(token.LeadingTrivia.Node!, token.ContextualKind, token.TrailingTrivia.Node!)
                    : GreenSyntaxFactory.Token(token.LeadingTrivia.Node, token.ContextualKind, token.TrailingTrivia.Node);
            var diagnostics = token.GetDiagnostics();
            if (diagnostics != null && diagnostics.Length > 0)
            {
                keyword = (GreenSyntaxToken)keyword.WithDiagnostics(diagnostics);
            }

            return keyword;
        }

        return token;
    }

    private static GreenSyntaxToken ConvertToIdentifier(GreenSyntaxToken token)
    {
        Debug.Assert(!token.IsMissing);

        var identifier = GreenSyntaxToken.Identifier(token.Kind, token.LeadingTrivia.Node, token.Text, token.ValueText!, token.TrailingTrivia.Node);
        if (token.ContainsDiagnostics)
        {
            identifier = (GreenSyntaxToken)identifier.WithDiagnostics(token.GetDiagnostics());
        }

        return identifier;
    }

    public void Dispose()
    {
        ReturnLexedTokensToPool(_lexedTokens);
    }
}
