using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpToken = Microsoft.CodeAnalysis.SyntaxToken;
using CSharpTriviaList = Microsoft.CodeAnalysis.SyntaxTriviaList;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private bool _inAkcssTemplate;

    private GreenSyntaxToken PeekAkcssToken(bool incremental, int offset = 0) =>
        incremental ? PeekIncrementalToken(offset) : PeekToken(offset);

    private GreenSyntaxToken ReadAkcssToken(bool incremental) =>
        incremental ? ReadIncrementalToken() : EatToken();

    private GreenSyntaxToken ReadAkcssToken(SyntaxKind kind, bool incremental) =>
        incremental ? ReadRequiredIncrementalToken(kind) : EatToken(kind);

    private SyntaxKind PeekAkcssDirectiveKeyword(bool incremental) =>
        PeekAkcssToken(incremental).Kind == SyntaxKind.AtToken
            ? PeekAkcssToken(incremental, 1).ContextualKind
            : SyntaxKind.None;

    private bool IsAkcssNamedDeclarationStart(bool incremental) =>
        PeekAkcssDirectiveKeyword(incremental) is SyntaxKind.ThemeKeyword or SyntaxKind.MixinKeyword;

    private bool IsAkcssTopLevelDirectiveStart(bool incremental) =>
        PeekAkcssDirectiveKeyword(incremental) is SyntaxKind.ThemeKeyword or SyntaxKind.MixinKeyword or
            SyntaxKind.UsingKeyword or SyntaxKind.UtilitiesKeyword;

    private GreenSyntaxToken ReadAkcssKeyword(SyntaxKind kind, bool incremental)
    {
        var token = PeekAkcssToken(incremental);
        return token.ContextualKind == kind
            ? ConvertToKeyword(ReadAkcssToken(incremental))
            : CreateMissingToken(kind, token.Kind);
    }

    private GreenIdentifierNameSyntax ReadAkcssName(bool incremental)
    {
        var token = PeekAkcssToken(incremental);
        if (!IsAkcssNameToken(token))
        {
            return GreenSyntaxFactory.IdentifierName(ReadAkcssToken(SyntaxKind.IdentifierToken, incremental));
        }

        token = ReadAkcssToken(incremental);
        return GreenSyntaxFactory.IdentifierName(token.Kind == SyntaxKind.IdentifierToken
            ? token
            : ConvertToIdentifier(token));
    }

    private GreenAkcssTopLevelMemberSyntax ParseAkcssNamedDeclaration(SyntaxKind keywordKind, bool incremental)
    {
        var at = ReadAkcssToken(SyntaxKind.AtToken, incremental);
        var keyword = ReadAkcssKeyword(keywordKind, incremental);
        var targetType = ReadAkcssCSharpType(incremental, allowTuple: true);
        var name = ReadAkcssName(incremental);
        var parameters = keywordKind == SyntaxKind.MixinKeyword ? ParseAkcssMixinParameters(incremental) : null;
        var openBrace = ReadAkcssToken(SyntaxKind.OpenBraceToken, incremental);
        var (members, closeBrace) = ParseAkcssBracedMembers(openBrace, incremental);

        return parameters is not null
            ? GreenSyntaxFactory.AkcssMixinDeclarationSyntax(at, keyword, targetType, name, parameters, openBrace, members, closeBrace)
            : GreenSyntaxFactory.AkcssThemeDeclarationSyntax(at, keyword, targetType, name, openBrace, members, closeBrace);
    }

    private (GreenSyntaxList<GreenAkcssBodyMemberSyntax> Members, GreenSyntaxToken CloseBrace)
        ParseAkcssBracedMembers(GreenSyntaxToken openBrace, bool incremental)
    {
        if (openBrace.IsMissing)
        {
            return (default, GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken));
        }

        var members = incremental ? ParseIncrementalAkcssBodyMemberList() : ParseAkcssBodyMemberList();
        return (members, ReadAkcssToken(SyntaxKind.CloseBraceToken, incremental));
    }

    private GreenAkcssBodyMemberSyntax? ParseNewAkcssBodyMember(bool incremental)
    {
        if (IsAkcssSelectorAnchor(PeekAkcssToken(incremental).Kind))
        {
            return ParseAkcssNestedStyleRule(incremental);
        }

        return PeekAkcssDirectiveKeyword(incremental) switch
        {
            SyntaxKind.TemplateKeyword when PeekAkcssToken(incremental, 2).Kind == SyntaxKind.MinusToken =>
                GreenSyntaxFactory.AkcssTemplateBindDirectiveSyntax(ParseAkcssTemplateBind(incremental)),
            SyntaxKind.TemplateKeyword => ParseAkcssTemplate(incremental),
            SyntaxKind.IncludeKeyword => GreenSyntaxFactory.AkcssIncludeDirectiveSyntax(ParseAkcssInclude(incremental)),
            SyntaxKind.BasedKeyword => ParseAkcssBasedOn(incremental),
            _ => null,
        };
    }

    private GreenAkcssTemplateDirectiveSyntax ParseAkcssTemplate(bool incremental)
    {
        var at = ReadAkcssToken(SyntaxKind.AtToken, incremental);
        var keyword = ReadAkcssKeyword(SyntaxKind.TemplateKeyword, incremental);
        if (PeekAkcssToken(incremental).Kind != SyntaxKind.OpenBraceToken)
        {
            var open = ReadAkcssToken(SyntaxKind.OpenBraceToken, incremental);
            var close = GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken);
            return GreenSyntaxFactory.AkcssTemplateDirectiveSyntax(at, keyword,
                GreenSyntaxFactory.MarkupBlockSyntax(open, default, close));
        }

        var openTags = ArrayBuilder<GreenMarkupComponentNameSyntax>.GetInstance();
        var previousMode = _mode;
        var previousTemplate = _inAkcssTemplate;
        var previousContentDepth = _markupCurrentContentTagDepth;
        ResetLookaheadForLexerModeChange();
        _mode = Lexer.LexerMode.TopLevel;
        _inAkcssTemplate = true;
        try
        {
            GreenMarkupEndTagSyntax? pendingEndTag = null;
            return GreenSyntaxFactory.AkcssTemplateDirectiveSyntax(at, keyword,
                ParseMarkupBlockSyntax(openTags, ref pendingEndTag, incremental, isAkcssTemplateBlock: true));
        }
        finally
        {
            ResetLookaheadForLexerModeChange();
            _markupCurrentContentTagDepth = previousContentDepth;
            _inAkcssTemplate = previousTemplate;
            _mode = previousMode;
            openTags.Free();
        }
    }

    private GreenMarkupElementSyntax DiagnoseUnclosedAkcssTemplateElement(GreenMarkupElementSyntax element)
    {
        return _inAkcssTemplate && element.StartTag is { CloseToken.Kind: not SyntaxKind.SlashGreaterToken } &&
            element.EndTag is null
            ? AddError(element, element.FullWidth, 0, ErrorCodes.ERR_SyntaxError, "closing tag")
            : element;
    }

    private bool CanReuseAkcssTemplateContext(AkburaSyntax node)
    {
        if (node is not (MarkupNodeSyntax or MarkupBlockSyntax))
        {
            return true;
        }

        for (var parent = node.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is AkcssTemplateDirectiveSyntax)
            {
                return _inAkcssTemplate &&
                    (node is not MarkupTextLiteralSyntax || IsAkcssTemplateTextBoundary());
            }
        }

        return !_inAkcssTemplate;
    }

    private SyntaxKind PeekAkcssTemplateMarkupDirective(bool incremental)
    {
        if (!_inAkcssTemplate)
        {
            return SyntaxKind.None;
        }

        var first = PeekAkcssToken(incremental);
        if (first.Kind != SyntaxKind.AtToken && first.Text is not ("@include" or "@template"))
        {
            return SyntaxKind.None;
        }

        // Reused directive tokens can already be split into '@' and a keyword.
        // Classify fresh tokens from the current markup mode.
        var offset = 0;
        try
        {
            return ClassifyAkcssTemplateMarkupDirective(PeekToken(0), () => PeekToken(++offset));
        }
        finally
        {
            if (incremental)
            {
                ResetLookaheadForLexerModeChange();
            }
        }
    }

    private static SyntaxKind ClassifyAkcssTemplateMarkupDirective(
        GreenSyntaxToken first, Func<GreenSyntaxToken> readNext)
    {
        if (first.Text is not ("@include" or "@template"))
        {
            return SyntaxKind.None;
        }

        var next = readNext();
        if (first.Text == "@include")
        {
            return next.Kind == SyntaxKind.MinusToken && AreAdjacent(first, next)
                ? SyntaxKind.None
                : SyntaxKind.IncludeKeyword;
        }

        if (next.Kind != SyntaxKind.MinusToken || !AreAdjacent(first, next))
        {
            return SyntaxKind.None;
        }

        var suffix = readNext();
        if (!AreAdjacent(next, suffix))
        {
            return SyntaxKind.TemplateKeyword;
        }

        if (suffix.Kind == SyntaxKind.BindToken)
        {
            var afterSuffix = readNext();
            return afterSuffix.Kind == SyntaxKind.MinusToken && AreAdjacent(suffix, afterSuffix)
                ? SyntaxKind.None
                : SyntaxKind.TemplateKeyword;
        }

        return suffix.Kind != SyntaxKind.IdentifierToken || IsPartialAkcssSuffix(suffix, SyntaxKind.BindToken)
            ? SyntaxKind.TemplateKeyword
            : SyntaxKind.None;
    }

    private bool IsAkcssTemplateTextBoundary()
    {
        // The blender is at the end of the old literal. Check the new boundary
        // before committing reuse, since a replaced directive can join text.
        var position = _lexer.TextWindow.Position;
        try
        {
            var token = _lexer.Lex(_mode);
            if (token.Kind is SyntaxKind.EndOfFileToken or SyntaxKind.LessThanToken or
                SyntaxKind.LessSlashToken or SyntaxKind.OpenBraceToken or SyntaxKind.CloseBraceToken)
            {
                return true;
            }

            if (token.Kind == SyntaxKind.DollarToken)
            {
                var keyword = _lexer.Lex(_mode);
                return AreAdjacent(token, keyword) &&
                    keyword.Kind is SyntaxKind.IfKeyword or SyntaxKind.ElseKeyword or SyntaxKind.ForeachKeyword;
            }

            return ClassifyAkcssTemplateMarkupDirective(token, () => _lexer.Lex(_mode)) != SyntaxKind.None;
        }
        finally
        {
            _lexer.TextWindow.Reset(position);
        }
    }

    private bool IsAkcssDeclarationAfterTemplateMarkup(bool incremental)
    {
        var first = PeekAkcssToken(incremental);
        if (!IsAfterAkcssLineBreak(first))
        {
            return false;
        }

        ResetLookaheadForLexerModeChange();
        var previousMode = _mode;
        _mode = Lexer.LexerMode.InAkcss;
        try
        {
            var token = CurrentToken;
            if (token.Kind != SyntaxKind.AtToken)
            {
                return HasAkcssRuleHeader(allowRelative: true, allowUtilityParameters: true);
            }

            return PeekToken(1).ContextualKind switch
            {
                SyntaxKind.ThemeKeyword or SyntaxKind.MixinKeyword => HasAkcssNamedDeclarationHeader(PeekToken(1).ContextualKind),
                SyntaxKind.UtilitiesKeyword => PeekToken(2).Kind == SyntaxKind.OpenBraceToken,
                _ => false,
            };
        }
        finally
        {
            ResetLookaheadForLexerModeChange();
            _mode = previousMode;
        }
    }

    private GreenMarkupStatementSyntax ParseAkcssTemplateMarkupDirective(SyntaxKind kind, bool incremental)
    {
        if (incremental)
        {
            if (kind == SyntaxKind.IncludeKeyword &&
                TryReadReusableIncrementalNode<GreenMarkupIncludeDirectiveSyntax>(out var include))
            {
                return include;
            }

            if (kind == SyntaxKind.TemplateKeyword &&
                TryReadReusableIncrementalNode<GreenMarkupTemplateBindDirectiveSyntax>(out var binding))
            {
                return binding;
            }
        }

        var previousMode = _mode;
        ResetLookaheadForLexerModeChange();
        _mode = Lexer.LexerMode.InAkcss;
        try
        {
            return kind == SyntaxKind.IncludeKeyword
                ? GreenSyntaxFactory.MarkupIncludeDirectiveSyntax(ParseAkcssInclude(incremental))
                : GreenSyntaxFactory.MarkupTemplateBindDirectiveSyntax(ParseAkcssTemplateBind(incremental));
        }
        finally
        {
            ResetLookaheadForLexerModeChange();
            _mode = previousMode;
        }
    }

    private static bool IsPartialAkcssSuffix(GreenSyntaxToken token, SyntaxKind expected) =>
        token.Kind == SyntaxKind.IdentifierToken && token.Text.Length > 0 &&
        SyntaxFacts.GetText(expected).StartsWith(token.Text, StringComparison.Ordinal);

    private GreenSyntaxToken ReadAkcssDirectiveSuffix(SyntaxKind kind, GreenSyntaxToken minus, bool incremental)
    {
        var token = PeekAkcssToken(incremental);
        if (token.ContextualKind == kind)
        {
            return ReadAkcssKeyword(kind, incremental);
        }

        var missing = CreateMissingToken(kind, token.Kind);
        return !minus.IsMissing && AreAdjacent(minus, token) && IsPartialAkcssSuffix(token, kind)
            ? AddTrailingSkippedSyntax(missing, ReadAkcssToken(incremental))
            : missing;
    }

    private GreenAkcssIncludeSyntax ParseAkcssInclude(bool incremental)
    {
        var at = ReadAkcssToken(SyntaxKind.AtToken, incremental);
        var keyword = ReadAkcssKeyword(SyntaxKind.IncludeKeyword, incremental);
        var name = ReadAkcssName(incremental);
        var arguments = ParseAkcssIncludeArguments(incremental);
        return GreenSyntaxFactory.AkcssIncludeSyntax(at, keyword, name, arguments,
            ReadAkcssToken(SyntaxKind.SemicolonToken, incremental));
    }

    private GreenAkcssTemplateBindSyntax ParseAkcssTemplateBind(bool incremental)
    {
        var at = ReadAkcssToken(SyntaxKind.AtToken, incremental);
        var template = ReadAkcssKeyword(SyntaxKind.TemplateKeyword, incremental);
        var minus = ReadAkcssToken(SyntaxKind.MinusToken, incremental);
        var bind = ReadAkcssDirectiveSuffix(SyntaxKind.BindToken, minus, incremental);
        var properties = _pool.AllocateSeparated<GreenCSharpTypeSyntax>();
        try
        {
            properties.Add(ReadAkcssCSharpType(incremental));
            while (PeekAkcssToken(incremental).Kind == SyntaxKind.CommaToken)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                properties.AddSeparator(ReadAkcssToken(incremental));
                properties.Add(ReadAkcssCSharpType(incremental));
            }

            return GreenSyntaxFactory.AkcssTemplateBindSyntax(at, template, minus, bind, properties.ToList(),
                ReadAkcssToken(SyntaxKind.SemicolonToken, incremental));
        }
        finally
        {
            _pool.Free(properties);
        }
    }

    private GreenAkcssBasedOnDirectiveSyntax ParseAkcssBasedOn(bool incremental)
    {
        var at = ReadAkcssToken(SyntaxKind.AtToken, incremental);
        var based = ReadAkcssKeyword(SyntaxKind.BasedKeyword, incremental);
        var minus = ReadAkcssToken(SyntaxKind.MinusToken, incremental);
        var on = ReadAkcssDirectiveSuffix(SyntaxKind.OnKeyword, minus, incremental);
        var fragment = ReadAkcssCSharpSyntax(Lexer.LexerMode.InAkcssBasedOnExpression);
        var expression = AddAkcssCSharpErrors(GreenSyntaxFactory.CSharpExpressionSyntax(fragment), fragment.RawNode!, "C# expression");
        return GreenSyntaxFactory.AkcssBasedOnDirectiveSyntax(at, based, minus, on, expression,
            ReadAkcssToken(SyntaxKind.SemicolonToken, incremental));
    }

    private GreenCSharpTypeSyntax ReadAkcssCSharpType(bool incremental, bool allowTuple = false)
    {
        var token = PeekAkcssToken(incremental);
        if (!IsAkcssNameToken(token) && token.Kind != SyntaxKind.CSharpRawToken &&
            !(allowTuple && token.Kind == SyntaxKind.OpenParenToken))
        {
            return CreateMissingAkcssCSharpType();
        }

        var fragment = ReadAkcssCSharpSyntax(Lexer.LexerMode.InTypeName);
        return AddAkcssCSharpErrors(GreenSyntaxFactory.CSharpTypeSyntax(fragment), fragment.RawNode!, "C# type");
    }

    private GreenCSharpTypeSyntax CreateMissingAkcssCSharpType() =>
        AddError(CreateAkcssCSharpTypeSyntax(string.Empty), 0, 0, ErrorCodes.ERR_SyntaxError, "C# type");

    private GreenSyntaxToken.CSharpRawToken ReadAkcssCSharpSyntax(Lexer.LexerMode mode)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var previousMode = _mode;
        ResetLookaheadForLexerModeChange();
        _mode = mode;
        try
        {
            // EatToken uses fresh lexing even when an incremental cursor is active.
            return (GreenSyntaxToken.CSharpRawToken)EatToken();
        }
        finally
        {
            ResetLookaheadForLexerModeChange();
            _mode = previousMode;
        }
    }

    private GreenAkcssMixinParameterListSyntax ParseAkcssMixinParameters(bool incremental)
    {
        if (PeekAkcssToken(incremental).Kind != SyntaxKind.OpenParenToken)
        {
            return GreenSyntaxFactory.AkcssMixinParameterListSyntax(
                ReadAkcssToken(SyntaxKind.OpenParenToken, incremental), default,
                GreenSyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));
        }

        var parsed = (CSharp.ParameterListSyntax)ReadAkcssCSharpSyntax(Lexer.LexerMode.InAkcssMixinParameters).RawNode!;
        var parameters = _pool.AllocateSeparated<GreenAkcssMixinParameterSyntax>();
        try
        {
            foreach (var entry in parsed.Parameters.GetWithSeparators())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsToken)
                {
                    parameters.AddSeparator(ProjectAkcssToken(entry.AsToken(), SyntaxKind.CommaToken));
                }
                else
                {
                    parameters.Add(ProjectAkcssParameter((CSharp.ParameterSyntax)entry.AsNode()!));
                }
            }

            var list = GreenSyntaxFactory.AkcssMixinParameterListSyntax(
                ProjectAkcssToken(parsed.OpenParenToken, SyntaxKind.OpenParenToken),
                parameters.ToList(),
                ProjectAkcssToken(parsed.CloseParenToken, SyntaxKind.CloseParenToken));
            return AddAkcssCSharpErrors(list, parsed, "mixin parameter list");
        }
        finally
        {
            _pool.Free(parameters);
        }
    }

    private GreenAkcssMixinParameterSyntax ProjectAkcssParameter(CSharp.ParameterSyntax parameter)
    {
        var prefix = parameter.AttributeLists.ToFullString() + parameter.Modifiers.ToFullString();
        var roslynType = parameter.Type ?? CSharpFactory.ParseTypeName(string.Empty);
        var type = GreenSyntaxFactory.CSharpTypeSyntax(
            GreenSyntaxFactory.CSharpRawToken(prefix + roslynType.ToFullString(), roslynType));
        var name = GreenSyntaxFactory.IdentifierName(ProjectAkcssToken(parameter.Identifier, SyntaxKind.IdentifierToken));
        var defaultClause = parameter.Default is { } clause
            ? GreenSyntaxFactory.AkcssMixinParameterDefaultSyntax(
                ProjectAkcssToken(clause.EqualsToken, SyntaxKind.EqualsToken),
                GreenSyntaxFactory.CSharpExpressionSyntax(GreenSyntaxFactory.CSharpRawToken(clause.Value)))
            : null;
        var result = GreenSyntaxFactory.AkcssMixinParameterSyntax(type, name, defaultClause);
        if (prefix.Length > 0)
        {
            result = AddError(result, 0, prefix.Length, ErrorCodes.ERR_SyntaxError, "parameter type and name");
        }

        return parameter.Type is null
            ? AddError(result, prefix.Length, 0, ErrorCodes.ERR_SyntaxError, "C# type")
            : result;
    }

    private GreenAkcssMixinArgumentListSyntax ParseAkcssIncludeArguments(bool incremental)
    {
        if (PeekAkcssToken(incremental).Kind != SyntaxKind.OpenParenToken)
        {
            return GreenSyntaxFactory.AkcssMixinArgumentListSyntax(
                ReadAkcssToken(SyntaxKind.OpenParenToken, incremental), default,
                GreenSyntaxFactory.MissingToken(SyntaxKind.CloseParenToken));
        }

        var parsed = (CSharp.ArgumentListSyntax)ReadAkcssCSharpSyntax(Lexer.LexerMode.InAkcssIncludeArguments).RawNode!;
        var arguments = _pool.AllocateSeparated<GreenCSharpExpressionSyntax>();
        try
        {
            foreach (var entry in parsed.Arguments.GetWithSeparators())
            {
                _cancellationToken.ThrowIfCancellationRequested();
                if (entry.IsToken)
                {
                    arguments.AddSeparator(ProjectAkcssToken(entry.AsToken(), SyntaxKind.CommaToken));
                    continue;
                }

                var argument = (CSharp.ArgumentSyntax)entry.AsNode()!;
                var expression = GreenSyntaxFactory.CSharpExpressionSyntax(
                    GreenSyntaxFactory.CSharpRawToken(argument.ToFullString(), argument.Expression));
                if (argument.NameColon is not null || argument.RefKindKeyword.RawKind != 0)
                {
                    expression = AddError(expression, 0, argument.Expression.FullSpan.Start - argument.FullSpan.Start,
                        ErrorCodes.ERR_SyntaxError, "positional expression");
                }
                arguments.Add(expression);
            }

            var list = GreenSyntaxFactory.AkcssMixinArgumentListSyntax(
                ProjectAkcssToken(parsed.OpenParenToken, SyntaxKind.OpenParenToken),
                arguments.ToList(),
                ProjectAkcssToken(parsed.CloseParenToken, SyntaxKind.CloseParenToken));
            return AddAkcssCSharpErrors(list, parsed, "mixin argument list");
        }
        finally
        {
            _pool.Free(arguments);
        }
    }

    private TNode AddAkcssCSharpErrors<TNode>(TNode node, SyntaxNode parsed, string expected)
        where TNode : GreenNode
    {
        if (parsed.ContainsDiagnostics)
        {
            foreach (var diagnostic in parsed.GetDiagnostics())
            {
                if (diagnostic.Severity == DiagnosticSeverity.Error)
                {
                    var span = diagnostic.Location.SourceSpan;
                    node = AddError(node, span.Start - parsed.FullSpan.Start, span.Length,
                        ErrorCodes.ERR_SyntaxError, expected);
                }
            }
        }

        return node;
    }

    private GreenSyntaxToken ProjectAkcssToken(CSharpToken token, SyntaxKind kind)
    {
        var leading = ProjectAkcssTrivia(token.LeadingTrivia);
        var trailing = ProjectAkcssTrivia(token.TrailingTrivia);
        if (token.IsMissing || token.RawKind == 0)
        {
            return GreenSyntaxFactory.MissingToken(leading, kind, trailing!);
        }

        return kind == SyntaxKind.IdentifierToken
            ? GreenSyntaxFactory.Identifier(kind, leading, token.Text, token.ValueText, trailing)
            : GreenSyntaxFactory.Token(leading, kind, trailing);
    }

    private GreenNode? ProjectAkcssTrivia(CSharpTriviaList trivia)
    {
        if (trivia.Count == 0)
        {
            return null;
        }

        var builder = _pool.Allocate();
        try
        {
            foreach (var item in trivia)
            {
                var text = item.ToFullString();
                builder.Add((CSharpKind)item.RawKind switch
                {
                    CSharpKind.WhitespaceTrivia => GreenSyntaxFactory.Whitespace(text),
                    CSharpKind.EndOfLineTrivia => GreenSyntaxFactory.EndOfLine(text),
                    CSharpKind.SingleLineCommentTrivia or CSharpKind.SingleLineDocumentationCommentTrivia =>
                        GreenSyntaxFactory.Comment(text),
                    CSharpKind.MultiLineCommentTrivia or CSharpKind.MultiLineDocumentationCommentTrivia =>
                        GreenSyntaxFactory.MultiLineComment(text),
                    _ => GreenSyntaxFactory.SkippedTokensTrivia(GreenSyntaxFactory.BadToken(null, text, null!)),
                });
            }

            return builder.ToListNode();
        }
        finally
        {
            _pool.Free(builder);
        }
    }
}
