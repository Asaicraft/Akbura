using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private int _markupBlockTagDepth = -1;

    private GreenSyntaxToken PeekMarkupConditionalToken(bool incremental, int offset = 0) =>
        incremental ? PeekIncrementalToken(offset) : PeekToken(offset);

    private GreenSyntaxToken ReadMarkupConditionalToken(SyntaxKind kind, bool incremental) =>
        incremental ? ReadRequiredIncrementalToken(kind) : EatToken(kind);

    private bool IsMarkupConditionalDirectiveStart(bool incremental, SyntaxKind? keywordKind = null)
    {
        var dollar = PeekMarkupConditionalToken(incremental);
        if (dollar.Kind != SyntaxKind.DollarToken)
        {
            return false;
        }

        var keyword = PeekMarkupConditionalToken(incremental, 1);
        return AreAdjacent(dollar, keyword) && (keywordKind.HasValue
            ? keyword.Kind == keywordKind.Value
            : keyword.Kind is SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword);
    }

    private GreenMarkupIfStatementSyntax ParseMarkupIfStatementSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental)
    {
        var clauses = _pool.Allocate<GreenMarkupElseIfClauseSyntax>();
        try
        {
            var orphanElse = IsMarkupConditionalDirectiveStart(incremental, SyntaxKind.ElseKeyword);
            var dollar = orphanElse ? GreenSyntaxFactory.MissingToken(SyntaxKind.DollarToken)
                : ReadMarkupConditionalToken(SyntaxKind.DollarToken, incremental);
            var keyword = orphanElse ? CreateMissingToken(SyntaxKind.IfKeyword, SyntaxKind.ElseKeyword)
                : ReadMarkupConditionalToken(SyntaxKind.IfKeyword, incremental);
            var openParen = orphanElse ? GreenSyntaxFactory.MissingToken(SyntaxKind.OpenParenToken)
                : ReadMarkupConditionalToken(SyntaxKind.OpenParenToken, incremental);
            var condition = orphanElse ? CreateMissingMarkupCondition()
                : ParseMarkupConditionSyntax(incremental);
            var closeParen = orphanElse ? GreenSyntaxFactory.MissingToken(SyntaxKind.CloseParenToken)
                : ReadMarkupConditionalToken(SyntaxKind.CloseParenToken, incremental);
            var body = orphanElse ? CreateMissingMarkupBlock()
                : ParseMarkupBlockSyntax(openTags, ref pendingEndTag, incremental);
            GreenMarkupElseClauseSyntax? elseClause = null;

            while (pendingEndTag == null &&
                IsMarkupConditionalDirectiveStart(incremental, SyntaxKind.ElseKeyword))
            {
                var elseDollar = ReadMarkupConditionalToken(SyntaxKind.DollarToken, incremental);
                var elseKeyword = ReadMarkupConditionalToken(SyntaxKind.ElseKeyword, incremental);
                if (PeekMarkupConditionalToken(incremental).Kind == SyntaxKind.IfKeyword)
                {
                    var ifKeyword = ReadMarkupConditionalToken(SyntaxKind.IfKeyword, incremental);
                    var clauseOpenParen = ReadMarkupConditionalToken(SyntaxKind.OpenParenToken, incremental);
                    var clauseCondition = ParseMarkupConditionSyntax(incremental);
                    var clauseCloseParen = ReadMarkupConditionalToken(SyntaxKind.CloseParenToken, incremental);
                    var clauseBody = ParseMarkupBlockSyntax(openTags, ref pendingEndTag, incremental);
                    clauses.Add(GreenSyntaxFactory.MarkupElseIfClauseSyntax(elseDollar, elseKeyword,
                        ifKeyword, clauseOpenParen, clauseCondition, clauseCloseParen, clauseBody));
                    continue;
                }

                elseClause = GreenSyntaxFactory.MarkupElseClauseSyntax(elseDollar, elseKeyword,
                    ParseMarkupBlockSyntax(openTags, ref pendingEndTag, incremental));
                break;
            }

            return GreenSyntaxFactory.MarkupIfStatementSyntax(dollar, keyword, openParen,
                condition, closeParen, body, clauses.ToList(), elseClause);
        }
        finally
        {
            _pool.Free(clauses);
        }
    }

    private GreenMarkupBlockSyntax ParseMarkupBlockSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental)
    {
        if (incremental && TryReadReusableIncrementalNode<GreenMarkupBlockSyntax>(out var reusable))
        {
            return reusable;
        }

        var openBrace = ReadMarkupConditionalToken(SyntaxKind.OpenBraceToken, incremental);
        var content = _pool.Allocate<GreenMarkupContentSyntax>();
        var previousDepth = _markupBlockTagDepth;
        _markupBlockTagDepth = openTags.Count;
        try
        {
            while (pendingEndTag == null)
            {
                var kind = PeekMarkupConditionalToken(incremental).Kind;
                if (kind is SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken or SyntaxKind.LessSlashToken ||
                    IsMarkupConditionalDirectiveStart(incremental, SyntaxKind.ElseKeyword))
                {
                    break;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                content.Add(incremental
                    ? ParseIncrementalMarkupContentSyntax(openTags, ref pendingEndTag)
                    : ParseMarkupContentSyntax(openTags, ref pendingEndTag));
            }

            var closeBrace = pendingEndTag == null
                ? ReadMarkupConditionalToken(SyntaxKind.CloseBraceToken, incremental)
                : CreateMissingToken(SyntaxKind.CloseBraceToken, SyntaxKind.LessSlashToken);
            return GreenSyntaxFactory.MarkupBlockSyntax(openBrace, content.ToList(), closeBrace);
        }
        finally
        {
            _markupBlockTagDepth = previousDepth;
            _pool.Free(content);
        }
    }

    private GreenCSharpExpressionSyntax ParseMarkupConditionSyntax(bool incremental)
    {
        ResetLookaheadForMarkupCondition();
        var previousMode = _mode;
        _mode = Lexer.LexerMode.InMarkupCondition;
        try
        {
            if (incremental && TryReadReusableIncrementalNode<GreenCSharpExpressionSyntax>(out var reusable))
            {
                return reusable;
            }

            var token = EatToken();
            var expression = GreenSyntaxFactory.CSharpExpressionSyntax(token);
            return token.Width == 0 || token is GreenSyntaxToken.CSharpRawToken
                { RawNode: CSharp.ExpressionSyntax { Span.Length: 0 } }
                ? AddErrorToFirstToken(expression, ErrorCodes.ERR_SyntaxError, "expression")
                : expression;
        }
        finally
        {
            _mode = previousMode;
        }
    }

    private void ResetLookaheadForMarkupCondition()
    {
        if (_tokenOffset >= _tokenCount)
        {
            return;
        }

        var position = _lexer.TextWindow.Position;
        for (var i = _tokenOffset; i < _tokenCount; i++)
        {
            position -= _lexedTokens[i].Value.FullWidth;
            _lexedTokens[i].Value = null!;
        }
        if (_isIncremental)
        {
            _blender = _blendersBeforeToken![_tokenOffset];
        }
        _lexer.TextWindow.Reset(position);
        _tokenCount = _tokenOffset;
        _currentToken = null;
    }

    private GreenCSharpExpressionSyntax CreateMissingMarkupCondition() =>
        GreenSyntaxFactory.CSharpExpressionSyntax(GreenSyntaxFactory.CSharpRawToken(
            string.Empty, CSharpFactory.ParseExpression(string.Empty)));

    private static GreenMarkupBlockSyntax CreateMissingMarkupBlock() =>
        GreenSyntaxFactory.MarkupBlockSyntax(GreenSyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken),
            default, GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));

    private bool CanReuseMarkupConditionalNode(GreenNode node)
    {
        if (node is not GreenMarkupIfStatementSyntax { ElseClause: null })
        {
            return true;
        }

        // A new else immediately after an old chain changes that chain's grammar,
        // even when the insertion does not intersect the old node's FullSpan.
        var position = _lexer.TextWindow.Position;
        var next = _lexer.Lex(_mode);
        var keyword = next.Kind == SyntaxKind.DollarToken ? _lexer.Lex(_mode) : null;
        _lexer.TextWindow.Reset(position);
        return keyword == null || keyword.Kind != SyntaxKind.ElseKeyword || !AreAdjacent(next, keyword);
    }
}
