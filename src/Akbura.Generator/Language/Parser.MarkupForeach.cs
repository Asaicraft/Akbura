using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private int _markupCodeBlockTagDepth = -1;
    private int _markupCurrentContentTagDepth = -1;

    private bool IsMarkupForeachDirectiveStart(bool incremental) =>
        IsMarkupConditionalDirectiveStart(incremental, SyntaxKind.ForeachKeyword);

    private GreenMarkupForeachStatementSyntax ParseMarkupForeachStatementSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental)
    {
        var dollar = ReadMarkupConditionalToken(SyntaxKind.DollarToken, incremental);
        var keyword = ReadMarkupConditionalToken(SyntaxKind.ForeachKeyword, incremental);
        var openParen = ReadMarkupConditionalToken(SyntaxKind.OpenParenToken, incremental);
        var header = ParseMarkupForeachHeaderSyntax(incremental);
        GreenMarkupForeachKeyClauseSyntax? key = null;
        if (PeekMarkupConditionalToken(incremental).Kind == SyntaxKind.SemicolonToken)
        {
            var semicolon = ReadMarkupConditionalToken(SyntaxKind.SemicolonToken, incremental);
            var name = incremental ? ParseIncrementalIdentifierName() : ParseIdentifierName();
            var colon = ReadMarkupConditionalToken(SyntaxKind.ColonToken, incremental);
            var expression = ParseMarkupConditionSyntax(incremental, foreachKey: true);
            key = GreenSyntaxFactory.MarkupForeachKeyClauseSyntax(semicolon, name, colon, expression);
            if (name.ToFullString().Trim() != "key")
            {
                key = AddErrorToFirstToken(key, ErrorCodes.ERR_SyntaxError, "key");
            }
        }

        var closeParen = ReadMarkupConditionalToken(SyntaxKind.CloseParenToken, incremental);
        var body = ParseMarkupCodeBlockSyntax(openTags, ref pendingEndTag, incremental);
        return GreenSyntaxFactory.MarkupForeachStatementSyntax(dollar, keyword, openParen, header,
            key, closeParen, body);
    }

    private GreenMarkupForeachHeaderSyntax ParseMarkupForeachHeaderSyntax(bool incremental)
    {
        ResetLookaheadForMarkupCondition();
        var previousMode = _mode;
        _mode = Lexer.LexerMode.InMarkupForeachHeader;
        try
        {
            if (incremental && TryReadReusableIncrementalNode<GreenMarkupForeachHeaderSyntax>(out var reusable))
            {
                return reusable;
            }

            var token = EatToken();
            var header = GreenSyntaxFactory.MarkupForeachHeaderSyntax(token);
            if (token is not GreenSyntaxToken.CSharpRawToken
                { RawNode: CSharp.ForEachStatementSyntax { ContainsDiagnostics: false } loop } ||
                loop.AwaitKeyword.RawKind != 0 || loop.Type is CSharp.RefTypeSyntax)
            {
                header = AddErrorToFirstToken(header, ErrorCodes.ERR_SyntaxError, "synchronous foreach header");
            }

            return header;
        }
        finally
        {
            _mode = previousMode;
        }
    }

    private GreenMarkupCodeBlockSyntax ParseMarkupCodeBlockSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental,
        bool allowSingleStatement = false)
    {
        if (incremental && TryReadReusableIncrementalNode<GreenMarkupCodeBlockSyntax>(out var reusable))
        {
            return reusable;
        }

        var single = allowSingleStatement && PeekMarkupConditionalToken(incremental).Kind != SyntaxKind.OpenBraceToken;
        if (!allowSingleStatement && PeekMarkupConditionalToken(incremental).Kind != SyntaxKind.OpenBraceToken)
        {
            return GreenSyntaxFactory.MarkupCodeBlockSyntax(
                ReadMarkupConditionalToken(SyntaxKind.OpenBraceToken, incremental), default,
                GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));
        }

        var openBrace = single ? GreenSyntaxFactory.MissingToken(SyntaxKind.OpenBraceToken)
            : ReadMarkupConditionalToken(SyntaxKind.OpenBraceToken, incremental);
        var content = _pool.Allocate<GreenMarkupContentSyntax>();
        var previousDepth = _markupCodeBlockTagDepth;
        var previousBlockDepth = _markupBlockTagDepth;
        _markupCodeBlockTagDepth = _markupBlockTagDepth = openTags.Count;
        try
        {
            while (pendingEndTag == null)
            {
                var kind = PeekMarkupConditionalToken(incremental).Kind;
                if (kind is SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken or SyntaxKind.LessSlashToken or
                    SyntaxKind.ElseKeyword || IsMarkupConditionalDirectiveStart(incremental, SyntaxKind.ElseKeyword))
                {
                    break;
                }

                _cancellationToken.ThrowIfCancellationRequested();
                content.Add(ParseMarkupCodeContentSyntax(openTags, ref pendingEndTag, incremental));
                if (single) break;
            }

            var closeBrace = single ? GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken)
                : pendingEndTag == null ? ReadMarkupConditionalToken(SyntaxKind.CloseBraceToken, incremental)
                : CreateMissingToken(SyntaxKind.CloseBraceToken, SyntaxKind.LessSlashToken);
            return GreenSyntaxFactory.MarkupCodeBlockSyntax(openBrace, content.ToList(), closeBrace);
        }
        finally
        {
            _markupCodeBlockTagDepth = previousDepth;
            _markupBlockTagDepth = previousBlockDepth;
            _pool.Free(content);
        }
    }

    private GreenMarkupContentSyntax ParseMarkupCodeContentSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental)
    {
        _markupCurrentContentTagDepth = openTags.Count;
        var kind = PeekMarkupConditionalToken(incremental).Kind;
        if (kind == SyntaxKind.IfKeyword)
        {
            if (incremental && TryReadReusableIncrementalNode<GreenMarkupCodeIfStatementSyntax>(out var reusableIf))
            {
                return reusableIf;
            }

            return ParseMarkupCodeIfStatementSyntax(openTags, ref pendingEndTag, incremental);
        }

        if (kind is SyntaxKind.LessThanToken or SyntaxKind.OpenBraceToken or SyntaxKind.DollarToken)
        {
            return incremental ? ParseIncrementalMarkupContentSyntax(openTags, ref pendingEndTag)
                : ParseMarkupContentSyntax(openTags, ref pendingEndTag);
        }

        return ParseMarkupCodeStatementSyntax(incremental);
    }

    private GreenMarkupCodeIfStatementSyntax ParseMarkupCodeIfStatementSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag,
        bool incremental)
    {
        var keyword = ReadMarkupConditionalToken(SyntaxKind.IfKeyword, incremental);
        var openParen = ReadMarkupConditionalToken(SyntaxKind.OpenParenToken, incremental);
        var condition = ParseMarkupConditionSyntax(incremental);
        var closeParen = ReadMarkupConditionalToken(SyntaxKind.CloseParenToken, incremental);
        var body = ParseMarkupCodeBlockSyntax(openTags, ref pendingEndTag, incremental, allowSingleStatement: true);
        GreenSyntaxToken? elseKeyword = null;
        GreenMarkupCodeBlockSyntax? elseBody = null;
        if (pendingEndTag == null && PeekMarkupConditionalToken(incremental).Kind == SyntaxKind.ElseKeyword)
        {
            elseKeyword = ReadMarkupConditionalToken(SyntaxKind.ElseKeyword, incremental);
            elseBody = ParseMarkupCodeBlockSyntax(openTags, ref pendingEndTag, incremental, allowSingleStatement: true);
        }

        return GreenSyntaxFactory.MarkupCodeIfStatementSyntax(keyword, openParen, condition, closeParen,
            body, elseKeyword, elseBody);
    }

    private GreenMarkupCodeStatementSyntax ParseMarkupCodeStatementSyntax(bool incremental)
    {
        ResetLookaheadForMarkupCondition();
        var previousMode = _mode;
        _mode = Lexer.LexerMode.InMarkupCodeStatement;
        try
        {
            if (incremental && TryReadReusableIncrementalNode<GreenMarkupCodeStatementSyntax>(out var reusable))
            {
                return reusable;
            }

            var token = EatToken();
            var statement = GreenSyntaxFactory.MarkupCodeStatementSyntax(token);
            if (token is not GreenSyntaxToken.CSharpRawToken { RawNode: CSharp.StatementSyntax raw } ||
                raw.ContainsDiagnostics || raw is not (CSharp.LocalDeclarationStatementSyntax or
                    CSharp.ExpressionStatementSyntax or CSharp.BreakStatementSyntax or CSharp.ContinueStatementSyntax or
                    CSharp.EmptyStatementSyntax))
            {
                statement = AddErrorToFirstToken(statement, ErrorCodes.ERR_SyntaxError, "supported loop statement");
            }

            return statement;
        }
        finally
        {
            _mode = previousMode;
        }
    }
}
