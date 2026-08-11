using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using static Akbura.Language.Syntax.Green.GreenSyntaxToken;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

partial class Parser
{
    internal GreenAkburaDocumentSyntax ParseCompilationUnit()
    {
        var members = _pool.Allocate<GreenAkTopLevelMemberSyntax>();

        try
        {
            while (true)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                if (TryEatReusableTopLevelMember(out var reusableMember))
                {
                    members.Add(reusableMember);
                    continue;
                }

                if (TryParseIncrementalCSharpStatementSyntax(
                    allowFileScopedDirectives: false,
                    out var incrementalStatement))
                {
                    members.Add(incrementalStatement);
                    continue;
                }

                if (TryParseIncrementalStateDeclaration(out var incrementalState))
                {
                    members.Add(incrementalState);
                    continue;
                }

                if (TryParseIncrementalCommandDeclaration(out var incrementalCommand))
                {
                    members.Add(incrementalCommand);
                    continue;
                }

                if (TryParseIncrementalInjectDeclaration(out var incrementalInject))
                {
                    members.Add(incrementalInject);
                    continue;
                }

                if (TryParseIncrementalInlineAkcssBlockSyntax(out var incrementalAkcss))
                {
                    members.Add(incrementalAkcss);
                    continue;
                }

                if (TryParseIncrementalMarkupRootSyntax(out var incrementalMarkup))
                {
                    members.Add(incrementalMarkup);
                    continue;
                }

                if (CurrentToken.Kind == SyntaxKind.CloseBraceToken)
                {
                    members.Add(ParseUnexpectedTopLevelToken());

                    continue;
                }

                if (CurrentToken.Kind == SyntaxKind.EndOfFileToken)
                {
                    break;
                }

                var member = ParseCompilationUnitMember();
                members.Add(member);
            }

            var eof = EatToken(SyntaxKind.EndOfFileToken);
            return GreenSyntaxFactory.AkburaDocumentSyntax(members.ToList(), eof);
        }
        finally
        {
            _pool.Free(members);
        }
    }

    /// <summary>
    /// Consumes one unexpected compilation-unit token and preserves it in the
    /// round-trippable syntax tree as an erroneous C# statement.
    /// </summary>
    private GreenCSharpStatementSyntax ParseUnexpectedTopLevelToken()
    {
        var tokens = _pool.Allocate<GreenSyntaxToken>();

        try
        {
            tokens.Add(EatTokenWithPrejudice(
                ErrorCodes.ERR_SyntaxError,
                "declaration or markup"));

            return GreenSyntaxFactory
                .CSharpStatementSyntax(
                    tokens.ToList(),
                    body: null);
        }
        finally
        {
            _pool.Free(tokens);
        }
    }


    internal GreenAkTopLevelMemberSyntax ParseCompilationUnitMember()
    {
        if (TryEatReusableTopLevelMember(out var reusableMember))
        {
            return reusableMember;
        }

        if (TryParseIncrementalCSharpStatementSyntax(
            allowFileScopedDirectives: false,
            out var incrementalStatement))
        {
            return incrementalStatement;
        }

        if (TryParseIncrementalStateDeclaration(out var incrementalState))
        {
            return incrementalState;
        }

        if (TryParseIncrementalCommandDeclaration(out var incrementalCommand))
        {
            return incrementalCommand;
        }

        if (TryParseIncrementalInjectDeclaration(out var incrementalInject))
        {
            return incrementalInject;
        }

        if (TryParseIncrementalInlineAkcssBlockSyntax(out var incrementalAkcss))
        {
            return incrementalAkcss;
        }

        if (TryParseIncrementalMarkupRootSyntax(out var incrementalMarkup))
        {
            return incrementalMarkup;
        }

        return CurrentToken.Kind switch
        {
            SyntaxKind.AtToken when PeekToken(1).Kind == SyntaxKind.AkcssKeyword => ParseInlineAkcssBlockSyntax(),
            SyntaxKind.UsingKeyword => ParseUsingDirectiveSyntax(),
            SyntaxKind.GlobalKeyword when PeekToken(1).Kind == SyntaxKind.UsingKeyword => ParseUsingDirectiveSyntax(),
            SyntaxKind.NamespaceKeyword => ParseNamespaceDeclarationSyntax(),
            _ => ParseTopLevelMember()
        };
    }

    internal GreenAkTopLevelMemberSyntax ParseTopLevelMember()
    {
        if (TryEatReusableTopLevelMember(out var reusableMember))
        {
            return reusableMember;
        }

        if (TryParseIncrementalCSharpStatementSyntax(
            allowFileScopedDirectives: true,
            out var incrementalStatement))
        {
            return incrementalStatement;
        }

        if (TryParseIncrementalStateDeclaration(out var incrementalState))
        {
            return incrementalState;
        }

        if (TryParseIncrementalCommandDeclaration(out var incrementalCommand))
        {
            return incrementalCommand;
        }

        if (TryParseIncrementalInjectDeclaration(out var incrementalInject))
        {
            return incrementalInject;
        }

        if (TryParseIncrementalInlineAkcssBlockSyntax(out var incrementalAkcss))
        {
            return incrementalAkcss;
        }

        if (TryParseIncrementalMarkupRootSyntax(out var incrementalMarkup))
        {
            return incrementalMarkup;
        }

        return CurrentToken.Kind switch
        {
            SyntaxKind.StateKeyword => ParseStateDeclaration(),
            SyntaxKind.ParamKeyword => ParseParamDeclarationSyntax(),
            SyntaxKind.InjectKeyword => ParseInjectDeclarationSyntax(),
            SyntaxKind.CommandKeyword => ParseCommandDeclarationSyntax(),
            SyntaxKind.LessThanToken => ParseMarkupRootSyntax(),
            _ => ParseCSharpStatementSyntax()
        };
    }

    #region InlineAkcssBlockSyntax

    internal GreenInlineAkcssBlockSyntax ParseInlineAkcssBlockSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            var atToken = EatToken(SyntaxKind.AtToken);
            var akcssKeyword = EatToken(SyntaxKind.AkcssKeyword);
            var openBrace = EatToken(SyntaxKind.OpenBraceToken);
            var members = ParseAkcssTopLevelMemberList();
            var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

            return GreenSyntaxFactory.InlineAkcssBlockSyntax(
                atToken,
                akcssKeyword,
                openBrace,
                members,
                closeBrace);
        }
        finally
        {
            _mode = mode;
        }
    }

    #endregion

    #region AkcssSyntax

    internal GreenAkcssDocumentSyntax ParseAkcssDocumentSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            var members = _isIncremental
                ? ParseIncrementalAkcssTopLevelMemberList(stopAtCloseBrace: false)
                : ParseAkcssTopLevelMemberList(stopAtCloseBrace: false);
            var eof = _isIncremental
                ? ReadRequiredIncrementalToken(SyntaxKind.EndOfFileToken)
                : EatToken(SyntaxKind.EndOfFileToken);

            return GreenSyntaxFactory.AkcssDocumentSyntax(members, eof);
        }
        finally
        {
            _mode = mode;
        }
    }

    internal GreenAkcssTopLevelMemberSyntax ParseAkcssTopLevelMemberSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            return ParseAkcssTopLevelMemberSyntaxCore();
        }
        finally
        {
            _mode = mode;
        }
    }

    private GreenAkcssTopLevelMemberSyntax ParseAkcssTopLevelMemberSyntaxCore()
    {
        if (CurrentToken.Kind == SyntaxKind.AtToken &&
            PeekToken(1).Kind == SyntaxKind.UsingKeyword)
        {
            return ParseAkcssUsingDirectiveSyntaxCore();
        }

        if (CurrentToken.Kind == SyntaxKind.AtToken &&
            PeekToken(1).Kind == SyntaxKind.UtilitiesKeyword)
        {
            return ParseAkcssUtilitiesSectionSyntaxCore();
        }

        return ParseAkcssStyleRuleSyntaxCore();
    }

    private GreenAkcssUsingDirectiveSyntax ParseAkcssUsingDirectiveSyntaxCore()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var usingKeyword = EatToken(SyntaxKind.UsingKeyword);
        var name = ParseAkcssCSharpTypeUntil(SyntaxKind.SemicolonToken);
        var semicolon = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.AkcssUsingDirectiveSyntax(
            atToken,
            usingKeyword,
            name,
            semicolon);
    }

    private GreenSyntaxList<GreenAkcssTopLevelMemberSyntax> ParseAkcssTopLevelMemberList(bool stopAtCloseBrace = true)
    {
        var members = _pool.Allocate<GreenAkcssTopLevelMemberSyntax>();

        try
        {
            while (CurrentToken.Kind != SyntaxKind.EndOfFileToken &&
                   (!stopAtCloseBrace || CurrentToken.Kind != SyntaxKind.CloseBraceToken))
            {
                members.Add(ParseAkcssTopLevelMemberSyntaxCore());
            }

            return members.ToList();
        }
        finally
        {
            _pool.Free(members);
        }
    }

    internal GreenAkcssStyleRuleSyntax ParseAkcssStyleRuleSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            return ParseAkcssStyleRuleSyntaxCore();
        }
        finally
        {
            _mode = mode;
        }
    }

    private GreenAkcssStyleRuleSyntax ParseAkcssStyleRuleSyntaxCore()
    {
        var selector = ParseAkcssStyleSelectorSyntax();
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var members = ParseAkcssBodyMemberList();
        var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssStyleRuleSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssStyleSelectorSyntax ParseAkcssStyleSelectorSyntax()
    {
        var (openParen, targetType, closeParen, dotToken, name) = ParseAkcssStyleSelectorParts();
        return GreenSyntaxFactory.AkcssStyleSelectorSyntax(openParen, targetType, closeParen, dotToken, name);
    }

    internal GreenAkcssUtilitiesSectionSyntax ParseAkcssUtilitiesSectionSyntax()
    {
        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            return ParseAkcssUtilitiesSectionSyntaxCore();
        }
        finally
        {
            _mode = mode;
        }
    }

    private GreenAkcssUtilitiesSectionSyntax ParseAkcssUtilitiesSectionSyntaxCore()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var utilitiesToken = EatToken(SyntaxKind.UtilitiesKeyword);
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var utilities = _pool.Allocate<GreenAkcssUtilityDeclarationSyntax>();

        try
        {
            while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
            {
                utilities.Add(ParseAkcssUtilityDeclarationSyntax());
            }

            var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

            return GreenSyntaxFactory.AkcssUtilitiesSectionSyntax(
                atToken,
                utilitiesToken,
                openBrace,
                utilities.ToList(),
                closeBrace);
        }
        finally
        {
            _pool.Free(utilities);
        }
    }

    private GreenAkcssUtilityDeclarationSyntax ParseAkcssUtilityDeclarationSyntax()
    {
        var selector = ParseAkcssUtilitySelectorSyntax();
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var members = ParseAkcssBodyMemberList();
        var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssUtilityDeclarationSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssUtilitySelectorSyntax ParseAkcssUtilitySelectorSyntax()
    {
        var (openParen, targetType, closeParen, dotToken, name) = ParseAkcssUtilitySelectorParts();
        var parameters = _pool.Allocate<GreenAkcssUtilityParameterSyntax>();

        try
        {
            while (CurrentToken.Kind == SyntaxKind.MinusToken &&
                   PeekToken(1).Kind == SyntaxKind.OpenParenToken)
            {
                parameters.Add(ParseAkcssUtilityParameterSyntax());
            }

            return GreenSyntaxFactory.AkcssUtilitySelectorSyntax(
                openParen,
                targetType,
                closeParen,
                dotToken,
                name,
                parameters.ToList());
        }
        finally
        {
            _pool.Free(parameters);
        }
    }

    private GreenAkcssUtilityParameterSyntax ParseAkcssUtilityParameterSyntax()
    {
        var minus = EatToken(SyntaxKind.MinusToken);
        var openParen = EatToken(SyntaxKind.OpenParenToken);
        var type = ParseCShaprType();
        var paramName = ParseAkcssSimpleName();
        var closeParen = EatToken(SyntaxKind.CloseParenToken);

        return GreenSyntaxFactory.AkcssUtilityParameterSyntax(
            minus,
            openParen,
            type,
            paramName,
            closeParen);
    }

    private GreenSyntaxList<GreenAkcssBodyMemberSyntax> ParseAkcssBodyMemberList()
    {
        var members = _pool.Allocate<GreenAkcssBodyMemberSyntax>();

        try
        {
            while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
            {
                members.Add(ParseAkcssBodyMemberSyntax());
            }

            return members.ToList();
        }
        finally
        {
            _pool.Free(members);
        }
    }

    private GreenAkcssBodyMemberSyntax ParseAkcssBodyMemberSyntax()
    {
        if (CurrentToken.Kind == SyntaxKind.AtToken &&
            PeekToken(1).Kind == SyntaxKind.IfKeyword)
        {
            return ParseAkcssIfDirectiveSyntax();
        }

        if (CurrentToken.Kind == SyntaxKind.AtToken &&
            PeekToken(1).Kind == SyntaxKind.ApplyKeyword)
        {
            return ParseAkcssApplyDirectiveSyntax();
        }

        if (CurrentToken.Kind == SyntaxKind.AtToken &&
            PeekToken(1).Kind == SyntaxKind.InterceptKeyword)
        {
            return ParseAkcssInterceptDirectiveSyntax();
        }

        if (CurrentToken.Kind == SyntaxKind.AtToken)
        {
            return ParseAkcssPseudoBlockSyntax();
        }

        return ParseAkcssAssignmentSyntax();
    }

    private GreenAkcssAssignmentSyntax ParseAkcssAssignmentSyntax()
    {
        var propertyName = ParseAkcssCSharpTypeUntil(SyntaxKind.ColonToken);
        var colon = EatToken(SyntaxKind.ColonToken);
        var expression = ParseAkcssExpressionUntilSemicolonOrCloseBrace();
        var semicolon = TryEatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.AkcssAssignmentSyntax(propertyName, colon, expression, semicolon);
    }

    private GreenAkcssApplyDirectiveSyntax ParseAkcssApplyDirectiveSyntax()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var applyKeyword = EatToken(SyntaxKind.ApplyKeyword);
        var items = _pool.Allocate<GreenSyntaxToken>();

        try
        {
            while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.SemicolonToken))
            {
                items.Add(EatToken());
            }

            var semicolon = EatToken(SyntaxKind.SemicolonToken);
            return GreenSyntaxFactory.AkcssApplyDirectiveSyntax(
                atToken,
                applyKeyword,
                items.ToList(),
                semicolon);
        }
        finally
        {
            _pool.Free(items);
        }
    }

    private GreenAkcssInterceptDirectiveSyntax ParseAkcssInterceptDirectiveSyntax()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var interceptKeyword = EatToken(SyntaxKind.InterceptKeyword);
        var type = ParseAkcssCSharpTypeUntil(SyntaxKind.SemicolonToken);
        var semicolon = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.AkcssInterceptDirectiveSyntax(
            atToken,
            interceptKeyword,
            type,
            semicolon);
    }

    private GreenAkcssIfDirectiveSyntax ParseAkcssIfDirectiveSyntax()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var ifKeyword = EatToken(SyntaxKind.IfKeyword);
        var openParen = EatToken(SyntaxKind.OpenParenToken);
        var condition = ParseAkcssExpressionUntil(SyntaxKind.CloseParenToken);
        var closeParen = EatToken(SyntaxKind.CloseParenToken);
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var members = ParseAkcssBodyMemberList();
        var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssIfDirectiveSyntax(
            atToken,
            ifKeyword,
            openParen,
            condition,
            closeParen,
            openBrace,
            members,
            closeBrace);
    }

    private GreenAkcssPseudoBlockSyntax ParseAkcssPseudoBlockSyntax()
    {
        var selector = ParseAkcssPseudoSelectorSyntax();
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var members = ParseAkcssBodyMemberList();
        var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssPseudoBlockSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssPseudoSelectorSyntax ParseAkcssPseudoSelectorSyntax()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var firstState = ParseAkcssSimpleName();
        var additional = _pool.Allocate<GreenAkcssAdditionalPseudoStateSyntax>();

        try
        {
            while (CurrentToken.Kind == SyntaxKind.AtToken &&
                   IsAkcssNameToken(PeekToken(1)))
            {
                additional.Add(ParseAkcssAdditionalPseudoStateSyntax());
            }

            return GreenSyntaxFactory.AkcssPseudoSelectorSyntax(
                atToken,
                firstState,
                additional.ToList());
        }
        finally
        {
            _pool.Free(additional);
        }
    }

    private GreenAkcssAdditionalPseudoStateSyntax ParseAkcssAdditionalPseudoStateSyntax()
    {
        var atToken = EatToken(SyntaxKind.AtToken);
        var state = ParseAkcssSimpleName();

        return GreenSyntaxFactory.AkcssAdditionalPseudoStateSyntax(atToken, state);
    }

    private (
        GreenSyntaxToken? OpenParen,
        GreenCSharpTypeSyntax? TargetType,
        GreenSyntaxToken? CloseParen,
        GreenSyntaxToken? DotToken,
        GreenSimpleNameSyntax? Name) ParseAkcssStyleSelectorParts()
    {
        var (openParen, targetType, closeParen) = ParseAkcssOptionalSelectorTarget();

        if (CurrentToken.Kind == SyntaxKind.DotToken)
        {
            var dotToken = EatToken(SyntaxKind.DotToken);
            var name = ParseAkcssUtilityName();
            return (openParen, targetType, closeParen, dotToken, name);
        }

        return (openParen, targetType, closeParen, null, null);
    }

    private (
        GreenSyntaxToken? OpenParen,
        GreenCSharpTypeSyntax? TargetType,
        GreenSyntaxToken? CloseParen,
        GreenSyntaxToken DotToken,
        GreenSimpleNameSyntax Name) ParseAkcssUtilitySelectorParts()
    {
        var (openParen, targetType, closeParen) = ParseAkcssOptionalSelectorTarget();
        var dotToken = EatToken(SyntaxKind.DotToken);
        var name = ParseAkcssUtilityName();
        return (openParen, targetType, closeParen, dotToken, name);
    }

    private (
        GreenSyntaxToken? OpenParen,
        GreenCSharpTypeSyntax? TargetType,
        GreenSyntaxToken? CloseParen) ParseAkcssOptionalSelectorTarget()
    {
        if (CurrentToken.Kind == SyntaxKind.OpenParenToken)
        {
            var openParen = EatToken(SyntaxKind.OpenParenToken);
            var targetType = ParseAkcssCSharpTypeUntil(SyntaxKind.CloseParenToken);
            var closeParen = EatToken(SyntaxKind.CloseParenToken);
            return (openParen, targetType, closeParen);
        }

        if (IsAkcssNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.DotToken)
        {
            var targetText = EatToken().ToFullString();
            return (null, CreateAkcssCSharpTypeSyntax(targetText), null);
        }

        return (null, null, null);
    }

    private GreenCSharpTypeSyntax ParseAkcssCSharpTypeUntil(SyntaxKind terminator)
    {
        var rawText = new StringBuilder();
        while (CurrentToken.Kind is not SyntaxKind.EndOfFileToken &&
               CurrentToken.Kind != terminator)
        {
            if (terminator == SyntaxKind.ColonToken &&
                CurrentToken.Kind is SyntaxKind.SemicolonToken or SyntaxKind.CloseBraceToken)
            {
                break;
            }

            rawText.Append(EatToken().ToFullString());
        }

        return CreateAkcssCSharpTypeSyntax(rawText.ToString());
    }

    private static GreenCSharpTypeSyntax CreateAkcssCSharpTypeSyntax(string rawText)
    {
        return GreenSyntaxFactory.CSharpTypeSyntax(
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseTypeName(rawText)));
    }

    private GreenIdentifierNameSyntax ParseAkcssSimpleName()
    {
        if (IsAkcssNameToken(CurrentToken))
        {
            return GreenSyntaxFactory.IdentifierName(EatMarkupNameTokenAsIdentifier());
        }

        return GreenSyntaxFactory.IdentifierName(ParseIdentifierToken());
    }

    private GreenIdentifierNameSyntax ParseAkcssUtilityName()
    {
        var first = IsAkcssNameToken(CurrentToken)
            ? EatMarkupNameTokenAsIdentifier()
            : ParseIdentifierToken();
        if (first.ContainsDiagnostics)
        {
            return GreenSyntaxFactory.IdentifierName(first);
        }

        StringBuilder? text = null;
        var last = first;
        while (CurrentToken.Kind == SyntaxKind.MinusToken &&
            PeekToken(1).Kind != SyntaxKind.OpenParenToken &&
            IsAkcssUtilityNamePartToken(PeekToken(1)) &&
            !CurrentToken.ContainsDiagnostics &&
            !PeekToken(1).ContainsDiagnostics &&
            AreAdjacent(last, CurrentToken) &&
            AreAdjacent(CurrentToken, PeekToken(1)))
        {
            text ??= new StringBuilder(first.Text);
            text.Append(EatToken(SyntaxKind.MinusToken).Text);

            last = EatToken();
            text.Append(last.Text);
            while (IsAkcssUtilityNamePartToken(CurrentToken) &&
                !CurrentToken.ContainsDiagnostics &&
                AreAdjacent(last, CurrentToken))
            {
                last = EatToken();
                text.Append(last.Text);
            }
        }

        if (text == null)
        {
            return GreenSyntaxFactory.IdentifierName(first);
        }

        var value = text.ToString();
        return GreenSyntaxFactory.IdentifierName(GreenSyntaxToken.Identifier(
            SyntaxKind.IdentifierToken,
            first.LeadingTrivia.Node,
            value,
            value,
            last.TrailingTrivia.Node));
    }

    private static bool IsAkcssUtilityNamePartToken(GreenSyntaxToken token)
    {
        return IsAkcssNameToken(token) ||
            token.Kind == SyntaxKind.NumericLiteralToken;
    }

    private static bool AreAdjacent(GreenSyntaxToken left, GreenSyntaxToken right)
    {
        return left.GetTrailingTriviaWidth() == 0 &&
            right.GetLeadingTriviaWidth() == 0;
    }

    private static bool IsAkcssNameToken(GreenSyntaxToken token)
    {
        return token.Kind == SyntaxKind.IdentifierToken ||
            token.Kind == SyntaxKind.UtilitiesKeyword ||
            token.Kind == SyntaxKind.AkcssKeyword ||
            (SyntaxFacts.IsReservedKeyword(token.Kind) && token.ValueText is not null);
    }

    private GreenCSharpExpressionSyntax ParseAkcssExpressionUntilSemicolonOrCloseBrace()
        => ParseAkcssExpressionUntil(SyntaxKind.SemicolonToken, SyntaxKind.CloseBraceToken);

    private GreenCSharpExpressionSyntax ParseAkcssExpressionUntil(
        SyntaxKind firstTerminator,
        SyntaxKind? secondTerminator = null)
    {
        var rawText = new StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        while (CurrentToken.Kind != SyntaxKind.EndOfFileToken)
        {
            var kind = CurrentToken.Kind;

            if (parenDepth == 0 &&
                bracketDepth == 0 &&
                braceDepth == 0 &&
                (kind == firstTerminator ||
                 (secondTerminator.HasValue && kind == secondTerminator.Value)))
            {
                break;
            }

            var token = EatToken();
            rawText.Append(token.ToFullString());

            switch (kind)
            {
                case SyntaxKind.OpenParenToken:
                    parenDepth++;
                    break;
                case SyntaxKind.CloseParenToken when parenDepth > 0:
                    parenDepth--;
                    break;
                case SyntaxKind.OpenBracketToken:
                    bracketDepth++;
                    break;
                case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    bracketDepth--;
                    break;
                case SyntaxKind.OpenBraceToken:
                    braceDepth++;
                    break;
                case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    braceDepth--;
                    break;
            }
        }

        var expression = CSharpFactory.ParseExpression(
            rawText.ToString(),
            offset: 0,
            options: null,
            consumeFullText: true);

        return GreenSyntaxFactory.CSharpExpressionSyntax(
            GreenSyntaxFactory.CSharpRawToken(expression));
    }

    #endregion

    #region UsingAndNamespaceSyntax

    internal GreenUsingDirectiveSyntax ParseUsingDirectiveSyntax()
    {
        GreenSyntaxToken? globalKeyword = null;
        if (CurrentToken.Kind == SyntaxKind.GlobalKeyword &&
            PeekToken(1).Kind == SyntaxKind.UsingKeyword)
        {
            globalKeyword = EatToken(SyntaxKind.GlobalKeyword);
        }

        var usingKeyword = EatToken(SyntaxKind.UsingKeyword);
        var staticKeyword = TryEatToken(SyntaxKind.StaticKeyword);
        var unsafeKeyword = TryEatToken(SyntaxKind.UnsafeKeyword);
        var alias = TryParseUsingAliasSyntax();
        var name = ParseRequiredCSharpTypeSyntax();
        var semicolon = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.UsingDirectiveSyntax(
            globalKeyword,
            usingKeyword,
            staticKeyword,
            unsafeKeyword,
            alias,
            name,
            semicolon);
    }

    private GreenUsingAliasSyntax? TryParseUsingAliasSyntax()
    {
        if (CurrentToken.Kind != SyntaxKind.IdentifierToken ||
            PeekToken(1).Kind != SyntaxKind.EqualsToken)
        {
            return null;
        }

        var name = ParseIdentifierName();
        var equals = EatToken(SyntaxKind.EqualsToken);

        return GreenSyntaxFactory.UsingAliasSyntax(name, equals);
    }

    internal GreenNamespaceDeclarationSyntax ParseNamespaceDeclarationSyntax()
    {
        var namespaceKeyword = EatToken(SyntaxKind.NamespaceKeyword);
        var name = ParseRequiredCSharpTypeSyntax();
        var semicolon = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.NamespaceDeclarationSyntax(namespaceKeyword, name, semicolon);
    }

    private GreenCSharpTypeSyntax ParseRequiredCSharpTypeSyntax()
    {
        var rawText = new StringBuilder();

        while (CurrentToken.Kind is not (SyntaxKind.SemicolonToken or SyntaxKind.EndOfFileToken))
        {
            rawText.Append(EatToken().ToFullString());
        }

        return GreenSyntaxFactory.CSharpTypeSyntax(
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseTypeName(rawText.ToString())));
    }

    #endregion

    #region CSharpStatementSyntax

    internal GreenCSharpStatementSyntax
    ParseCSharpStatementSyntax()
    {
        _cancellationToken.ThrowIfCancellationRequested();

        var mode = _mode;

        _mode =
            Lexer.LexerMode.InCSharpStatement;

        var tokens =
            _pool.Allocate<GreenSyntaxToken>();

        var canHaveBlockBody =
            IsCSharpBlockStatementStarter(
                CurrentToken);

        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        var iterationsUntilCancellationCheck = CancellationCheckInterval;

        try
        {
            while (CurrentToken.Kind !=
                   SyntaxKind.EndOfFileToken)
            {
                /*
                 * A statement can contain a very large raw expression.
                 * Poll periodically instead of checking on every token API.
                 */
                CheckCancellation(
                    ref iterationsUntilCancellationCheck);

                var kind =
                    CurrentToken.Kind;

                if (kind ==
                        SyntaxKind.CloseBraceToken &&
                    braceDepth == 0)
                {
                    break;
                }

                if (kind == SyntaxKind.OpenBraceToken &&
                    parenDepth == 0 &&
                    bracketDepth == 0 &&
                    braceDepth == 0 &&
                    (canHaveBlockBody ||
                     IsCSharpLocalFunctionHeader(
                         tokens)))
                {
                    _mode = mode;

                    var body =
                        ParseCSharpBlock();

                    return GreenSyntaxFactory
                        .CSharpStatementSyntax(
                            tokens.ToList(),
                            body);
                }

                var token = EatToken();

                tokens.Add(token);

                switch (kind)
                {
                    case SyntaxKind.OpenParenToken:
                        parenDepth++;
                        break;

                    case SyntaxKind.CloseParenToken
                        when parenDepth > 0:

                        parenDepth--;
                        break;

                    case SyntaxKind.OpenBracketToken:
                        bracketDepth++;
                        break;

                    case SyntaxKind.CloseBracketToken
                        when bracketDepth > 0:

                        bracketDepth--;
                        break;

                    case SyntaxKind.OpenBraceToken:
                        braceDepth++;
                        break;

                    case SyntaxKind.CloseBraceToken
                        when braceDepth > 0:

                        braceDepth--;
                        break;

                    case SyntaxKind.SemicolonToken
                        when parenDepth == 0 &&
                             bracketDepth == 0 &&
                             braceDepth == 0:

                        return GreenSyntaxFactory
                            .CSharpStatementSyntax(
                                tokens.ToList(),
                                body: null);
                }
            }

            return GreenSyntaxFactory
                .CSharpStatementSyntax(
                    tokens.ToList(),
                    body: null);
        }
        finally
        {
            _mode = mode;
            _pool.Free(tokens);
        }
    }


    private static bool IsCSharpLocalFunctionHeader(
        GreenSyntaxListBuilder<GreenSyntaxToken> tokens)
    {
        if (tokens.Count == 0)
        {
            return false;
        }

        var text = new StringBuilder();
        for (var index = 0; index < tokens.Count; index++)
        {
            text.Append(tokens[index].ToFullString());
        }

        text.Append("{}");
        return CSharpFactory.ParseStatement(text.ToString()) is
            CSharp.LocalFunctionStatementSyntax;
    }

    private static bool IsCSharpBlockStatementStarter(GreenSyntaxToken token)
    {
        return token.Kind is SyntaxKind.IfKeyword or
            SyntaxKind.ForKeyword or
            SyntaxKind.ElseKeyword or
            SyntaxKind.UsingKeyword or
            SyntaxKind.UnsafeKeyword or
            SyntaxKind.FinallyKeyword ||
            token.ValueText is "while" or
                "foreach" or
                "switch" or
                "lock" or
                "try" or
                "catch" or
                "finally" or
                "using" or
                "fixed" or
                "checked" or
                "unchecked" or
                "unsafe" or
                "do";
    }

    #endregion

    #region StateDeclarationSyntax

    internal GreenStateDeclarationSyntax ParseStateDeclaration()
    {
        var stateKeyword = EatToken(SyntaxKind.StateKeyword);

        var typeSyntax = EatOrNullCSharpTypeSyntax();

        GreenCSharpTypeSyntax? type = null;

        if (typeSyntax != null)
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(typeSyntax);
        }

        var name = ParseIdentifierName();

        var equalsToken = EatOrReturn(SyntaxKind.EqualsToken);

        var initializer = ParseStateInitializer();

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.StateDeclarationSyntax(stateKeyword, type, name, equalsToken, initializer, semicolonToken);
    }

    private GreenStateInitializerSyntax ParseStateInitializer()
    {
        var token = FastPeekToken();

        // in out bind tokens
        if ((int)token.Kind >= (int)SyntaxKind.BindToken && (int)token.Kind <= (int)SyntaxKind.OutToken)
        {
            return ParseBindingStateInitializer();
        }

        var expression = ParseCShaprExpressionUntilSemicolon();

        return GreenSyntaxFactory.SimpleStateInitializerSyntax(expression);
    }

    private GreenBindableStateInitializerSyntax ParseBindingStateInitializer()
    {
        var bindToken = EatToken();

        AkburaDebug.Assert(bindToken.Kind == SyntaxKind.BindToken
            || bindToken.Kind == SyntaxKind.OutToken
            || bindToken.Kind == SyntaxKind.InToken, "Expected bind token");

        var sourceExpression = ParseCShaprExpressionUntilSemicolon();

        return GreenSyntaxFactory.BindableStateInitializerSyntax(bindToken, sourceExpression);
    }

    #endregion

    #region ParamDeclarationSyntax

    internal GreenParamDeclarationSyntax ParseParamDeclarationSyntax()
    {
        var token = EatToken(SyntaxKind.ParamKeyword);

        var bindingToken = FastPeekToken();

        if (bindingToken.Kind != SyntaxKind.BindToken && bindingToken.Kind != SyntaxKind.OutToken)
        {
            bindingToken = null;
        }

        GreenSyntaxToken? bindingKeyword = null;
        if (bindingToken != null)
        {
            bindingKeyword = EatToken();
        }

        var typeSyntax = EatOrNullCSharpTypeSyntax();

        GreenCSharpTypeSyntax? type = null;
        if (typeSyntax != null)
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(typeSyntax);
        }

        var name = ParseIdentifierName();

        var equalsToken = EatOrReturn(SyntaxKind.EqualsToken);
        if (equalsToken.IsMissing)
        {
            equalsToken = null;
        }

        GreenCSharpExpressionSyntax? defaultValue = null;
        if (equalsToken != null)
        {
            defaultValue = ParseCShaprExpressionUntilSemicolon();
        }

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.ParamDeclarationSyntax(token, bindingKeyword, type, name, equalsToken, defaultValue, semicolonToken);
    }

    #endregion

    #region InjectDeclarationSyntax

    internal GreenInjectDeclarationSyntax ParseInjectDeclarationSyntax()
    {
        var token = EatToken(SyntaxKind.InjectKeyword);
        var typeSyntax = EatOrNullCSharpTypeSyntax();
        GreenCSharpTypeSyntax? type = null;

        if (typeSyntax != null)
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(typeSyntax);
        }
        else
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(EatToken(SyntaxKind.CSharpRawToken));
        }

        var name = ParseIdentifierName();

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.InjectDeclarationSyntax(token, type, name, semicolonToken);
    }

    #endregion

    #region CommandDeclarationSyntax

    internal GreenCommandDeclarationSyntax ParseCommandDeclarationSyntax()
    {
        var commandKeyword = EatToken(SyntaxKind.CommandKeyword);

        var returnTypeSyntax = ParseCShaprType();

        var name = ParseIdentifierName();

        var parameters = ParseCSharpParameterList();

        var semicolon = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.CommandDeclarationSyntax(
            commandKeyword,
            returnTypeSyntax,
            name,
            parameters,
            semicolon);
    }

    #endregion


    #region MarkupRootSyntax

    internal GreenMarkupRootSyntax ParseMarkupRootSyntax()
    {
        var openTags = ArrayBuilder<GreenMarkupComponentNameSyntax>.GetInstance();

        try
        {
            GreenMarkupEndTagSyntax? pendingEndTag = null;
            return GreenSyntaxFactory.MarkupRootSyntax(
                ParseMarkupElementSyntax(openTags, ref pendingEndTag));
        }
        finally
        {
            openTags.Free();
        }
    }

    internal GreenMarkupElementSyntax ParseMarkupElementSyntax()
    {
        var openTags = ArrayBuilder<GreenMarkupComponentNameSyntax>.GetInstance();

        try
        {
            GreenMarkupEndTagSyntax? pendingEndTag = null;
            return ParseMarkupElementSyntax(openTags, ref pendingEndTag);
        }
        finally
        {
            openTags.Free();
        }
    }

    private GreenMarkupElementSyntax ParseMarkupElementSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag)
    {
        var startTag = ParseMarkupStartTagSyntax();
        var body = _pool.Allocate<GreenMarkupContentSyntax>();

        try
        {
            GreenMarkupEndTagSyntax? endTag = null;

            if (startTag.CloseToken.Kind != SyntaxKind.SlashGreaterToken)
            {
                openTags.Push(startTag.Name);

                while (CurrentToken.Kind != SyntaxKind.EndOfFileToken || pendingEndTag != null)
                {
                    GreenMarkupEndTagSyntax? candidateEndTag;

                    if (pendingEndTag != null)
                    {
                        candidateEndTag = pendingEndTag;
                        pendingEndTag = null;
                    }
                    else if (CurrentToken.Kind == SyntaxKind.LessSlashToken)
                    {
                        candidateEndTag = ParseMarkupEndTagSyntax();
                    }
                    else
                    {
                        body.Add(ParseMarkupContentSyntax(openTags, ref pendingEndTag));
                        continue;
                    }

                    if (MarkupComponentNamesMatch(startTag.Name, candidateEndTag.Name))
                    {
                        endTag = candidateEndTag;
                        break;
                    }

                    if (MatchesOpenAncestor(openTags, candidateEndTag.Name))
                    {
                        pendingEndTag = candidateEndTag;
                        break;
                    }

                    var incompleteTag = GreenSyntaxFactory.IncompleteTagSyntax(
                        candidateEndTag);
                    body.Add(AddError(
                        incompleteTag,
                        ErrorCodes.ERR_SyntaxError,
                        "start tag"));
                }

                openTags.Pop();
            }

            return GreenSyntaxFactory.MarkupElementSyntax(startTag, body.ToList(), endTag);
        }
        finally
        {
            _pool.Free(body);
        }
    }

    private GreenMarkupStartTagSyntax ParseMarkupStartTagSyntax()
    {
        var less = EatToken(SyntaxKind.LessThanToken);
        var name = ParseMarkupComponentNameSyntax();
        var attributes = _pool.Allocate<GreenMarkupAttributeSyntax>();

        try
        {
            GreenNode? skippedSyntax = null;

            while (CurrentToken.Kind is not (
               SyntaxKind.EndOfFileToken or
               SyntaxKind.GreaterThanToken or
               SyntaxKind.SlashGreaterToken or
               SyntaxKind.LessThanToken or
               SyntaxKind.LessSlashToken))
            {
                if (!IsMarkupAttributeStart())
                {
                    skippedSyntax = ParseSkippedMarkupAttributeTokens(incremental: false);
                    continue;
                }

                var attribute = ParseMarkupAttributeSyntax();
                if (skippedSyntax != null)
                {
                    attribute = AddLeadingSkippedSyntax(attribute, skippedSyntax);
                    skippedSyntax = null;
                }

                attributes.Add(attribute);
            }

            var close = CurrentToken.Kind == SyntaxKind.SlashGreaterToken
                ? EatToken(SyntaxKind.SlashGreaterToken)
                : EatToken(SyntaxKind.GreaterThanToken);

            if (skippedSyntax != null)
            {
                close = AddLeadingSkippedSyntax(close, skippedSyntax);
            }

            return GreenSyntaxFactory.MarkupStartTagSyntax(
                less,
                name,
                attributes.ToList(),
                close);
        }
        finally
        {
            _pool.Free(attributes);
        }
    }

    private GreenMarkupEndTagSyntax ParseMarkupEndTagSyntax()
    {
        var lessSlash = EatToken(SyntaxKind.LessSlashToken);
        var name = ParseMarkupComponentNameSyntax();
        var greater = EatToken(SyntaxKind.GreaterThanToken);

        return GreenSyntaxFactory.MarkupEndTagSyntax(lessSlash, name, greater);
    }

    private GreenMarkupContentSyntax ParseMarkupContentSyntax(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        ref GreenMarkupEndTagSyntax? pendingEndTag)
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.LessThanToken => GreenSyntaxFactory.MarkupElementContentSyntax(
                ParseMarkupElementSyntax(openTags, ref pendingEndTag)),
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.MarkupInlineExpressionSyntax(
                ParseInlineExpressionSyntax()),
            _ => ParseMarkupTextLiteralSyntax(),
        };
    }

    private static bool MatchesOpenAncestor(
        ArrayBuilder<GreenMarkupComponentNameSyntax> openTags,
        GreenMarkupComponentNameSyntax endTagName)
    {
        for (var i = openTags.Count - 2; i >= 0; i--)
        {
            if (MarkupComponentNamesMatch(openTags[i], endTagName))
            {
                return true;
            }
        }

        return false;
    }

    private static bool MarkupComponentNamesMatch(
        GreenMarkupComponentNameSyntax left,
        GreenMarkupComponentNameSyntax right)
    {
        var leftEnumerator = left.EnumerateNodes().GetEnumerator();
        var rightEnumerator = right.EnumerateNodes().GetEnumerator();

        try
        {
            while (true)
            {
                var hasLeft = MoveToNextToken(ref leftEnumerator, out var leftToken);
                var hasRight = MoveToNextToken(ref rightEnumerator, out var rightToken);

                if (hasLeft != hasRight)
                {
                    return false;
                }

                if (!hasLeft)
                {
                    return true;
                }

                if (leftToken.Kind != rightToken.Kind ||
                    !string.Equals(leftToken.Text, rightToken.Text, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }
        finally
        {
            leftEnumerator.Dispose();
            rightEnumerator.Dispose();
        }
    }

    private static bool MoveToNextToken(
        ref GreenNode.NodeEnumerable.Enumerator enumerator,
        out GreenSyntaxToken token)
    {
        while (enumerator.MoveNext())
        {
            if (enumerator.Current is GreenSyntaxToken currentToken)
            {
                token = currentToken;
                return true;
            }
        }

        token = null!;
        return false;
    }

    private GreenMarkupTextLiteralSyntax ParseMarkupTextLiteralSyntax()
    {
        var rawText = new StringBuilder();
        var hasUnsupportedControlFlowDirective = false;

        while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or
               SyntaxKind.LessThanToken or
               SyntaxKind.LessSlashToken or
               SyntaxKind.OpenBraceToken))
        {
            _cancellationToken.ThrowIfCancellationRequested();

            if (IsUnsupportedMarkupControlFlowDirectiveStart())
            {
                hasUnsupportedControlFlowDirective = true;
                AppendUnsupportedMarkupControlFlowDirectiveText(rawText);
                continue;
            }

            rawText.Append(EatToken().ToFullString());
        }

        if (rawText.Length == 0 && CurrentToken.Kind != SyntaxKind.EndOfFileToken)
        {
            rawText.Append(EatToken().ToFullString());
        }

        var textToken = (GreenSyntaxToken)GreenSyntaxFactory.AkTextLiteralToken(
            rawText.ToString(),
            rawText.ToString())!;

        var tokens = _pool.Allocate<GreenSyntaxToken>();
        try
        {
            tokens.Add(textToken);
            var text = GreenSyntaxFactory.MarkupTextLiteralSyntax(tokens.ToList());

            if (hasUnsupportedControlFlowDirective)
            {
                text = AddErrorToFirstToken(
                    text,
                    ErrorCodes.ERR_SyntaxError,
                    "supported markup content");
            }

            return text;
        }
        finally
        {
            _pool.Free(tokens);
        }
    }

    private void AppendUnsupportedMarkupControlFlowDirectiveText(StringBuilder rawText)
    {
        var braceDepth = 0;
        var seenBlock = false;

        while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.LessSlashToken))
        {
            var token = EatToken();
            rawText.Append(token.ToFullString());

            if (token.Kind == SyntaxKind.OpenBraceToken)
            {
                braceDepth++;
                seenBlock = true;
                continue;
            }

            if (token.Kind == SyntaxKind.CloseBraceToken && seenBlock)
            {
                braceDepth--;

                if (braceDepth <= 0)
                {
                    break;
                }
            }
        }
    }

    private bool IsUnsupportedMarkupControlFlowDirectiveStart()
    {
        if (CurrentToken.Kind == SyntaxKind.IdentifierToken &&
            CurrentToken.Text.Length > 1 &&
            CurrentToken.Text[0] == '@')
        {
            var name = CurrentToken.Text.TrimStart('@');

            return name is "if" or
                "else" or
                "for" or
                "foreach" or
                "while";
        }

        if (CurrentToken.Kind != SyntaxKind.AtToken)
        {
            return false;
        }

        var keyword = PeekToken(1);

        return keyword.Kind is SyntaxKind.IfKeyword or
            SyntaxKind.ElseKeyword or
            SyntaxKind.ForKeyword ||
            keyword.ValueText is "foreach" or "while";
    }

    #endregion

    #region MarkupAttributeSyntax

    internal GreenMarkupAttributeSyntax ParseMarkupAttributeSyntax()
    {
        if (IsMarkupPrefixedAttributeStart())
        {
            return ParseMarkupPrefixedAttributeSyntax();
        }

        if (IsAttachedPropertyMarkupAttributeStart())
        {
            return ParseMarkupAttachedPropertyAttributeSyntax();
        }

        if (IsPlainMarkupAttributeStart())
        {
            return ParseMarkupPlainAttributeSyntax();
        }

        return ParseTailwindAttributeSyntax();
    }

    private GreenNode ParseSkippedMarkupAttributeTokens(bool incremental)
    {
        var skippedTokens = _pool.Allocate<GreenNode>();

        try
        {
            var lastToken = incremental
                ? ReadIncrementalToken()
                : EatToken();
            skippedTokens.Add(AddError(
                lastToken,
                ErrorCodes.ERR_SyntaxError,
                "attribute"));

            while (!IsMarkupStartTagBoundary(
                       incremental
                           ? PeekIncrementalTokenKind()
                           : CurrentToken.Kind))
            {
                var currentToken = incremental
                    ? PeekIncrementalToken()
                    : CurrentToken;
                var isAttributeStart = incremental
                    ? IsIncrementalMarkupAttributeStart()
                    : IsMarkupAttributeStart();

                if (isAttributeStart && !AreAdjacent(lastToken, currentToken))
                {
                    break;
                }

                lastToken = incremental
                    ? ReadIncrementalToken()
                    : EatToken();
                skippedTokens.Add(lastToken);
            }

            return skippedTokens.ToListNode()!;
        }
        finally
        {
            _pool.Free(skippedTokens);
        }
    }

    private static bool IsMarkupStartTagBoundary(SyntaxKind kind)
        => kind is SyntaxKind.EndOfFileToken or
            SyntaxKind.GreaterThanToken or
            SyntaxKind.SlashGreaterToken or
            SyntaxKind.LessThanToken or
            SyntaxKind.LessSlashToken;

    private bool IsMarkupAttributeStart()
    {
        return IsMarkupPrefixedAttributeStart() ||
            IsAttachedPropertyMarkupAttributeStart() ||
            IsPlainMarkupAttributeStart() ||
            IsTailwindAttributeStart();
    }

    private bool IsMarkupPrefixedAttributeStart()
    {
        return CurrentToken.Kind is SyntaxKind.BindToken or SyntaxKind.OutToken &&
            PeekToken(1).Kind == SyntaxKind.ColonToken;
    }

    private bool IsPlainMarkupAttributeStart()
    {
        var current = CurrentToken;

        if (!IsMarkupNameToken(current))
        {
            return false;
        }

        var next = PeekToken(1);

        return next.Kind == SyntaxKind.EqualsToken ||
               (IsMarkupAttributeValueStart(next) &&
                AreAdjacent(current, next));
    }

    private bool IsAttachedPropertyMarkupAttributeStart()
    {
        if (!IsMarkupNameToken(CurrentToken) ||
            PeekToken(1).Kind is not (SyntaxKind.DotToken or SyntaxKind.DoubleColonToken))
        {
            return false;
        }

        var offset = 0;
        var braceDepth = 0;
        var topLevelDotCount = 0;
        var lastTopLevelDotOffset = -1;

        while (true)
        {
            var kind = PeekToken(offset).Kind;
            if (kind is SyntaxKind.EndOfFileToken or
                SyntaxKind.GreaterThanToken or
                SyntaxKind.SlashGreaterToken)
            {
                return false;
            }

            if (braceDepth == 0 && kind == SyntaxKind.EqualsToken)
            {
                return topLevelDotCount > 0 &&
                    lastTopLevelDotOffset > 0 &&
                    IsMarkupNameToken(PeekToken(lastTopLevelDotOffset + 1)) &&
                    lastTopLevelDotOffset + 2 == offset;
            }

            switch (kind)
            {
                case SyntaxKind.OpenBraceToken:
                    braceDepth++;
                    break;
                case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    braceDepth--;
                    break;
                case SyntaxKind.DotToken when braceDepth == 0:
                    topLevelDotCount++;
                    lastTopLevelDotOffset = offset;
                    break;
            }

            offset++;
        }
    }

    private bool IsTailwindAttributeStart()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.DollarToken =>
                PeekToken(1).Kind == SyntaxKind.OpenBraceToken,

            SyntaxKind.OpenBraceToken or
            SyntaxKind.MinusToken =>
                true,

            _ => IsTailwindNameToken(CurrentToken),
        };
    }


    private GreenMarkupAttributeSyntax ParseMarkupPlainAttributeSyntax()
    {
        var name = ParseMarkupSimpleName();
        var equals = EatToken(SyntaxKind.EqualsToken);
        var value = ParseMarkupAttributeValueSyntax();

        if (!equals.IsMissing && value == null)
        {
            var incomplete = GreenSyntaxFactory.IncompleteAttributeSyntax(
                name,
                equals);
            return AddError(
                incomplete,
                incomplete.Width,
                length: 0,
                ErrorCodes.ERR_SyntaxError,
                "attribute value");
        }

        return GreenSyntaxFactory.MarkupPlainAttributeSyntax(name, equals, value);
    }

    private GreenMarkupAttachedPropertyAttributeSyntax ParseMarkupAttachedPropertyAttributeSyntax()
    {
        GreenMarkupAliasQualifierSyntax? aliasQualifier = null;

        if (IsMarkupNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.DoubleColonToken)
        {
            var alias = ParseMarkupSimpleName();
            var doubleColon = EatToken(SyntaxKind.DoubleColonToken);
            aliasQualifier = GreenSyntaxFactory.MarkupAliasQualifierSyntax(alias, doubleColon);
        }

        var ownerSegments = _pool.AllocateSeparated<GreenMarkupNameSegmentSyntax>();

        try
        {
            ownerSegments.Add(ParseMarkupNameSegmentSyntax());

            while (CurrentToken.Kind == SyntaxKind.DotToken)
            {
                var dot = EatToken(SyntaxKind.DotToken);
                if (IsMarkupNameToken(CurrentToken) &&
                    PeekToken(1).Kind == SyntaxKind.EqualsToken)
                {
                    var propertyName = ParseMarkupSimpleName();
                    var equals = EatToken(SyntaxKind.EqualsToken);
                    var value = ParseMarkupAttributeValueSyntax();

                    return GreenSyntaxFactory.MarkupAttachedPropertyAttributeSyntax(
                        CreateMarkupComponentName(aliasQualifier, ownerSegments),
                        dot,
                        propertyName,
                        equals,
                        value);
                }

                ownerSegments.AddSeparator(dot);
                ownerSegments.Add(ParseMarkupNameSegmentSyntax());
            }

            return GreenSyntaxFactory.MarkupAttachedPropertyAttributeSyntax(
                CreateMarkupComponentName(aliasQualifier, ownerSegments),
                EatToken(SyntaxKind.DotToken),
                ParseMarkupSimpleName(),
                EatToken(SyntaxKind.EqualsToken),
                ParseMarkupAttributeValueSyntax());
        }
        finally
        {
            _pool.Free(ownerSegments);
        }
    }

    private GreenMarkupPrefixedAttributeSyntax ParseMarkupPrefixedAttributeSyntax()
    {
        var prefix = EatToken();
        AkburaDebug.Assert(prefix.Kind is SyntaxKind.BindToken or SyntaxKind.OutToken, "Expected bind or out prefix.");

        var colon = EatToken(SyntaxKind.ColonToken);
        var name = ParseMarkupSimpleName();
        var equals = EatToken(SyntaxKind.EqualsToken);
        var value = ParseMarkupAttributeValueSyntax();

        return GreenSyntaxFactory.MarkupPrefixedAttributeSyntax(prefix, colon, name, equals, value);
    }

    private GreenMarkupAttributeValueSyntax? ParseMarkupAttributeValueSyntax()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.DollarToken => GreenSyntaxFactory.MarkupExtensionAttributeValueSyntax(
                ParseMarkupExtensionSyntax()),
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: ParseInlineExpressionSyntax()),
            SyntaxKind.DoubleQuoteToken or SyntaxKind.SingleQuoteToken => GreenSyntaxFactory.MarkupLiteralAttributeValueSyntax(
                prefix: null,
                value: ParseQuotedMarkupTextLiteralSyntax()),
            _ => null,
        };
    }

    private static bool IsMarkupAttributeValueStart(GreenSyntaxToken token)
    {
        return token.Kind is SyntaxKind.DollarToken or
            SyntaxKind.OpenBraceToken or
            SyntaxKind.DoubleQuoteToken or
            SyntaxKind.SingleQuoteToken;
    }

    private GreenMarkupTextLiteralSyntax ParseQuotedMarkupTextLiteralSyntax()
    {
        var quoteKind = CurrentToken.Kind;
        AkburaDebug.Assert(quoteKind is SyntaxKind.DoubleQuoteToken or SyntaxKind.SingleQuoteToken, "Expected quote token.");

        var rawText = new StringBuilder();
        var valueText = new StringBuilder();

        var openQuote = EatToken();
        rawText.Append(openQuote.ToFullString());

        while (CurrentToken.Kind != quoteKind &&
               CurrentToken.Kind != SyntaxKind.EndOfFileToken)
        {
            var token = EatToken();
            var tokenText = token.ToFullString();
            rawText.Append(tokenText);
            valueText.Append(tokenText);
        }

        var closeQuote = EatToken(quoteKind);
        rawText.Append(closeQuote.ToFullString());

        return CreateMarkupTextLiteralSyntax(rawText.ToString(), valueText.ToString());
    }

    private GreenMarkupExtensionSyntax ParseMarkupExtensionSyntax()
    {
        var dollarToken = EatToken(SyntaxKind.DollarToken);
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var type = ParseMarkupExtensionTypeSyntax();
        var arguments = _pool.AllocateSeparated<GreenMarkupExtensionArgumentSyntax>();

        try
        {
            if (CurrentToken.Kind is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                arguments.Add(ParseMarkupExtensionArgumentSyntax());

                while (CurrentToken.Kind == SyntaxKind.CommaToken)
                {
                    arguments.AddSeparator(EatToken(SyntaxKind.CommaToken));

                    if (CurrentToken.Kind is SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken)
                    {
                        break;
                    }

                    arguments.Add(ParseMarkupExtensionArgumentSyntax());
                }
            }

            var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

            return GreenSyntaxFactory.MarkupExtensionSyntax(
                dollarToken,
                openBrace,
                type,
                arguments.ToList(),
                closeBrace);
        }
        finally
        {
            _pool.Free(arguments);
        }
    }

    private GreenMarkupExtensionTypeSyntax ParseMarkupExtensionTypeSyntax()
    {
        GreenMarkupAliasQualifierSyntax? aliasQualifier = null;
        if (IsMarkupNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.DoubleColonToken)
        {
            aliasQualifier = GreenSyntaxFactory.MarkupAliasQualifierSyntax(
                ParseMarkupSimpleName(),
                EatToken(SyntaxKind.DoubleColonToken));
        }

        var segments = _pool.AllocateSeparated<GreenMarkupNameSegmentSyntax>();

        try
        {
            segments.Add(ParseMarkupNameSegmentSyntax());

            while (CurrentToken.Kind == SyntaxKind.DotToken)
            {
                segments.AddSeparator(EatToken(SyntaxKind.DotToken));
                segments.Add(ParseMarkupNameSegmentSyntax());
            }

            return GreenSyntaxFactory.MarkupExtensionTypeSyntax(
                aliasQualifier,
                GreenSyntaxFactory.MarkupQualifiedNameSyntax(segments.ToList()));
        }
        finally
        {
            _pool.Free(segments);
        }
    }

    private GreenMarkupExtensionArgumentSyntax ParseMarkupExtensionArgumentSyntax()
    {
        if (IsMarkupNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.EqualsToken)
        {
            var name = ParseMarkupSimpleName();
            var equals = EatToken(SyntaxKind.EqualsToken);
            var value = ParseMarkupExtensionValueSyntax();
            return GreenSyntaxFactory.MarkupExtensionPropertyArgumentSyntax(name, equals, value);
        }

        return GreenSyntaxFactory.MarkupExtensionPositionalArgumentSyntax(
            ParseMarkupExtensionValueSyntax());
    }

    private GreenMarkupExtensionValueSyntax ParseMarkupExtensionValueSyntax()
    {
        if (CurrentToken.Kind == SyntaxKind.DollarToken &&
            PeekToken(1).Kind == SyntaxKind.OpenBraceToken)
        {
            return GreenSyntaxFactory.MarkupExtensionNestedValueSyntax(
                ParseMarkupExtensionSyntax());
        }

        if (CurrentToken.Kind == SyntaxKind.OpenBraceToken)
        {
            return GreenSyntaxFactory.MarkupExtensionExpressionValueSyntax(
                ParseInlineExpressionSyntax());
        }

        return GreenSyntaxFactory.MarkupExtensionLiteralValueSyntax(
            ParseMarkupExtensionLiteralValueSyntax());
    }

    private GreenMarkupTextLiteralSyntax ParseMarkupExtensionLiteralValueSyntax()
    {
        var rawText = new StringBuilder();
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        while (CurrentToken.Kind != SyntaxKind.EndOfFileToken)
        {
            var kind = CurrentToken.Kind;
            if (parenDepth == 0 &&
                bracketDepth == 0 &&
                braceDepth == 0 &&
                kind is SyntaxKind.CommaToken or SyntaxKind.CloseBraceToken)
            {
                break;
            }

            var token = EatToken();
            rawText.Append(token.ToFullString());

            switch (kind)
            {
                case SyntaxKind.OpenParenToken:
                    parenDepth++;
                    break;
                case SyntaxKind.CloseParenToken when parenDepth > 0:
                    parenDepth--;
                    break;
                case SyntaxKind.OpenBracketToken:
                    bracketDepth++;
                    break;
                case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    bracketDepth--;
                    break;
                case SyntaxKind.OpenBraceToken:
                    braceDepth++;
                    break;
                case SyntaxKind.CloseBraceToken when braceDepth > 0:
                    braceDepth--;
                    break;
            }
        }

        var raw = rawText.ToString();
        return CreateMarkupTextLiteralSyntax(raw, raw.Trim());
    }

    private GreenMarkupTextLiteralSyntax CreateMarkupTextLiteralSyntax(string rawText, string valueText)
    {
        var textToken = (GreenSyntaxToken)GreenSyntaxFactory.AkTextLiteralToken(rawText, valueText)!;

        var tokens = _pool.Allocate<GreenSyntaxToken>();
        try
        {
            tokens.Add(textToken);
            return GreenSyntaxFactory.MarkupTextLiteralSyntax(tokens.ToList());
        }
        finally
        {
            _pool.Free(tokens);
        }
    }

    #endregion

    #region TailwindAttributeSyntax

    internal GreenMarkupAttributeSyntax ParseTailwindAttributeSyntax()
    {
        var prefix = TryParseTailwindPrefixSegmentSyntax();

        if (prefix != null && IsMarkupStartTagBoundary(CurrentToken.Kind))
        {
            var incomplete = GreenSyntaxFactory.IncompletePrefixedAttributeSyntax(prefix);
            return AddError(
                incomplete,
                incomplete.Width,
                length: 0,
                ErrorCodes.ERR_IdentifierExpected);
        }

        var name = ParseTailwindSimpleName();

        if (CurrentToken.Kind != SyntaxKind.MinusToken)
        {
            if (prefix is null)
            {
                return GreenSyntaxFactory.TailwindFlagAttributeSyntax(name);
            }

            return GreenSyntaxFactory.TailwindFullAttributeSyntax(
                prefix,
                name,
                minus: null,
                segments: default);
        }

        var minus = EatToken(SyntaxKind.MinusToken);
        var segments = _pool.AllocateSeparated<GreenTailwindSegmentSyntax>();

        try
        {
            segments.Add(ParseTailwindSegmentSyntax());

            while (CurrentToken.Kind == SyntaxKind.MinusToken)
            {
                segments.AddSeparator(EatToken(SyntaxKind.MinusToken));
                segments.Add(ParseTailwindSegmentSyntax());
            }

            return GreenSyntaxFactory.TailwindFullAttributeSyntax(
                prefix,
                name,
                minus,
                segments.ToList());
        }
        finally
        {
            _pool.Free(segments);
        }
    }

    private GreenTailwindPrefixSegmentSyntax? TryParseTailwindPrefixSegmentSyntax()
    {
        if (CurrentToken.Kind == SyntaxKind.DollarToken &&
            PeekToken(1).Kind == SyntaxKind.OpenBraceToken)
        {
            var extension = ParseMarkupExtensionSyntax();
            var colon = EatToken(SyntaxKind.ColonToken);
            return GreenSyntaxFactory.MarkupExtensionConditionalPrefixSyntax(
                extension,
                colon);
        }

        if (CurrentToken.Kind == SyntaxKind.OpenBraceToken)
        {
            var expression = ParseInlineExpressionSyntax();
            var colon = EatToken(SyntaxKind.ColonToken);
            return GreenSyntaxFactory.ExpressionConditionalPrefixSyntax(expression, colon);
        }

        if (IsTailwindNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.ColonToken &&
            !IsMarkupPrefixedAttributeStart())
        {
            var name = ParseTailwindSimpleName();
            var colon = EatToken(SyntaxKind.ColonToken);
            return GreenSyntaxFactory.SimpleConditionalPrefixSyntax(name, colon);
        }

        return null;
    }

    private GreenTailwindSegmentSyntax ParseTailwindSegmentSyntax()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.NumericLiteralToken => ParseTailwindNumericOrIdentifierSegmentSyntax(),
            SyntaxKind.DollarToken => GreenSyntaxFactory.TailwindMarkupExtensionSegmentSyntax(
                ParseMarkupExtensionSyntax()),
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.TailwindExpressionSegmentSyntax(
                ParseInlineExpressionSyntax()),
            _ => GreenSyntaxFactory.TailwindIdentifierSegmentSyntax(
                ParseTailwindSimpleName()),
        };
    }

    private GreenTailwindSegmentSyntax ParseTailwindNumericOrIdentifierSegmentSyntax()
    {
        var first = EatToken(SyntaxKind.NumericLiteralToken);
        if (!IsTailwindNameToken(CurrentToken) ||
            first.ContainsDiagnostics ||
            CurrentToken.ContainsDiagnostics ||
            !AreAdjacent(first, CurrentToken))
        {
            return GreenSyntaxFactory.TailwindNumericSegmentSyntax(first);
        }

        var text = new StringBuilder(first.Text);
        var last = first;
        do
        {
            last = EatToken();
            text.Append(last.Text);
        }
        while (IsTailwindNameToken(CurrentToken) &&
            !CurrentToken.ContainsDiagnostics &&
            AreAdjacent(last, CurrentToken));

        var value = text.ToString();
        var identifier = GreenSyntaxToken.Identifier(
            SyntaxKind.IdentifierToken,
            first.LeadingTrivia.Node,
            value,
            value,
            last.TrailingTrivia.Node);
        return GreenSyntaxFactory.TailwindIdentifierSegmentSyntax(
            GreenSyntaxFactory.IdentifierName(identifier));
    }

    #endregion

    #region InlineExpressionSyntax

    internal GreenInlineExpressionSyntax ParseInlineExpressionSyntax()
    {
        var openBrace = EatToken(SyntaxKind.OpenBraceToken);
        var expression = ParseCSharpExpressionInMode(Lexer.LexerMode.InInlineExpression);
        var semicolon = CurrentToken.Kind == SyntaxKind.SemicolonToken
            ? EatToken()
            : null;
        var closeBrace = EatToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.InlineExpressionSyntax(
            openBrace,
            expression,
            semicolon,
            closeBrace);
    }

    #endregion

    #region MarkupComponentNameSyntax

    internal GreenMarkupComponentNameSyntax ParseMarkupComponentNameSyntax()
    {
        // alias:: ... ?
        GreenMarkupAliasQualifierSyntax? aliasQualifier = null;

        if (IsMarkupNameToken(CurrentToken) &&
            PeekToken(1).Kind == SyntaxKind.DoubleColonToken)
        {
            var alias = ParseMarkupSimpleName();
            var doubleColon = EatToken(SyntaxKind.DoubleColonToken);
            aliasQualifier = GreenSyntaxFactory.MarkupAliasQualifierSyntax(alias, doubleColon);
        }

        var firstName = ParseMarkupSimpleName();
        GreenMarkupGenericArgumentListSyntax? firstGenericArgs = null;

        if (CurrentToken.Kind == SyntaxKind.OpenBraceToken &&
            AreAdjacent(firstName.Identifier, CurrentToken))
        {
            firstGenericArgs = ParseMarkupGenericArgumentListSyntax();
        }

        // If no alias, no dots, no generics => Simple name <Button />
        if (aliasQualifier is null &&
            firstGenericArgs is null &&
            CurrentToken.Kind != SyntaxKind.DotToken)
        {
            return GreenSyntaxFactory.MarkupSimpleComponentNameSyntax(firstName);
        }

        // Otherwise => Qualified component name (may still have single segment if it has generics)
        var segments = _pool.AllocateSeparated<GreenMarkupNameSegmentSyntax>();

        try
        {
            segments.Add(
                firstGenericArgs is null
                    ? GreenSyntaxFactory.MarkupIdentifierNameSegmentSyntax(firstName)
                    : GreenSyntaxFactory.MarkupGenericNameSegmentSyntax(firstName, firstGenericArgs)
            );

            while (CurrentToken.Kind == SyntaxKind.DotToken)
            {
                var dot = EatToken(SyntaxKind.DotToken);
                segments.AddSeparator(dot);
                segments.Add(ParseMarkupNameSegmentSyntax());
            }

            var qualifiedName = GreenSyntaxFactory.MarkupQualifiedNameSyntax(segments.ToList());

            return GreenSyntaxFactory.MarkupQualifiedComponentNameSyntax(
                aliasQualifier,
                qualifiedName
            );
        }
        finally
        {
            _pool.Free(segments);
        }
    }

    private GreenMarkupNameSegmentSyntax ParseMarkupNameSegmentSyntax()
    {
        var name = ParseMarkupSimpleName();

        if (CurrentToken.Kind != SyntaxKind.OpenBraceToken ||
            !AreAdjacent(name.Identifier, CurrentToken))
        {
            return GreenSyntaxFactory.MarkupIdentifierNameSegmentSyntax(name);
        }

        var genericArgs = ParseMarkupGenericArgumentListSyntax();
        return GreenSyntaxFactory.MarkupGenericNameSegmentSyntax(name, genericArgs);
    }

    private static GreenMarkupComponentNameSyntax CreateMarkupComponentName(
        GreenMarkupAliasQualifierSyntax? aliasQualifier,
        SeparatedGreenSyntaxListBuilder<GreenMarkupNameSegmentSyntax> segments)
    {
        if (aliasQualifier is null &&
            segments.Count == 1 &&
            segments[0] is GreenMarkupIdentifierNameSegmentSyntax identifierSegment)
        {
            return GreenSyntaxFactory.MarkupSimpleComponentNameSyntax(identifierSegment.Name);
        }

        var qualifiedName = GreenSyntaxFactory.MarkupQualifiedNameSyntax(segments.ToList());
        return GreenSyntaxFactory.MarkupQualifiedComponentNameSyntax(aliasQualifier, qualifiedName);
    }

    private GreenMarkupGenericArgumentListSyntax ParseMarkupGenericArgumentListSyntax()
    {
        var open = EatToken(SyntaxKind.OpenBraceToken);

        var list = _pool.AllocateSeparated<GreenCSharpTypeSyntax>();
        try
        {
            if (CurrentToken.Kind != SyntaxKind.CloseBraceToken &&
                CurrentToken.Kind != SyntaxKind.EndOfFileToken)
            {
                list.Add(ParseMarkupGenericArgumentType());

                while (CurrentToken.Kind == SyntaxKind.CommaToken)
                {
                    list.AddSeparator(EatToken(SyntaxKind.CommaToken));

                    // recovery: allow trailing comma before }
                    if (CurrentToken.Kind == SyntaxKind.CloseBraceToken ||
                        CurrentToken.Kind == SyntaxKind.EndOfFileToken)
                    {
                        break;
                    }

                    list.Add(ParseMarkupGenericArgumentType());
                }
            }

            var close = EatToken(SyntaxKind.CloseBraceToken);

            return GreenSyntaxFactory.MarkupGenericArgumentListSyntax(open, list.ToList(), close);
        }
        finally
        {
            _pool.Free(list);
        }
    }

    #endregion

    #region IdentifierNameSyntax

    private GreenCSharpTypeSyntax ParseMarkupGenericArgumentType()
    {
        var rawText = new StringBuilder();
        var angleDepth = 0;
        var parenDepth = 0;
        var bracketDepth = 0;

        while (CurrentToken.Kind != SyntaxKind.EndOfFileToken)
        {
            var kind = CurrentToken.Kind;
            if (angleDepth == 0 &&
                parenDepth == 0 &&
                bracketDepth == 0 &&
                (kind == SyntaxKind.CommaToken || kind == SyntaxKind.CloseBraceToken))
            {
                break;
            }

            var token = EatToken();
            rawText.Append(token.ToFullString());

            switch (kind)
            {
                case SyntaxKind.LessThanToken:
                    angleDepth++;
                    break;
                case SyntaxKind.GreaterThanToken when angleDepth > 0:
                    angleDepth--;
                    break;
                case SyntaxKind.OpenParenToken:
                    parenDepth++;
                    break;
                case SyntaxKind.CloseParenToken when parenDepth > 0:
                    parenDepth--;
                    break;
                case SyntaxKind.OpenBracketToken:
                    bracketDepth++;
                    break;
                case SyntaxKind.CloseBracketToken when bracketDepth > 0:
                    bracketDepth--;
                    break;
            }
        }

        return GreenSyntaxFactory.CSharpTypeSyntax(
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseTypeName(rawText.ToString())));
    }

    private GreenIdentifierNameSyntax ParseMarkupSimpleName()
    {
        if (IsMarkupNameToken(CurrentToken))
        {
            return GreenSyntaxFactory.IdentifierName(EatMarkupNameTokenAsIdentifier());
        }

        return GreenSyntaxFactory.IdentifierName(ParseIdentifierToken());
    }

    private GreenIdentifierNameSyntax ParseTailwindSimpleName()
    {
        if (IsTailwindNameToken(CurrentToken))
        {
            return GreenSyntaxFactory.IdentifierName(EatMarkupNameTokenAsIdentifier());
        }

        return GreenSyntaxFactory.IdentifierName(ParseIdentifierToken());
    }

    private GreenSyntaxToken EatMarkupNameTokenAsIdentifier()
    {
        var token = EatToken();

        return token.Kind == SyntaxKind.IdentifierToken
            ? token
            : ConvertToIdentifier(token);
    }

    private static bool IsMarkupNameToken(GreenSyntaxToken token)
    {
        return token.Kind == SyntaxKind.IdentifierToken ||
            (SyntaxFacts.IsReservedKeyword(token.Kind) && token.ValueText is not null);
    }

    private static bool IsTailwindNameToken(GreenSyntaxToken token)
    {
        return token.Kind == SyntaxKind.IdentifierToken ||
            (SyntaxFacts.IsReservedKeyword(token.Kind) && token.ValueText is not null);
    }

    private GreenIdentifierNameSyntax ParseIdentifierName()
    {
        if (CurrentToken.Kind == SyntaxKind.IdentifierToken)
        {
            var identifierToken = EatToken(SyntaxKind.IdentifierToken);
            return GreenSyntaxFactory.IdentifierName(identifierToken);
        }

        return GreenSyntaxFactory.IdentifierName(ParseIdentifierToken());
    }

    private GreenSyntaxToken ParseIdentifierToken()
    {
        return AddError(CreateMissingIdentifierToken(), ErrorCodes.ERR_IdentifierExpected);
    }

    private static GreenSyntaxToken CreateMissingIdentifierToken()
    {
        return GreenSyntaxFactory.MissingToken(SyntaxKind.IdentifierToken);
    }

    #endregion

    #region ParameterSyntax

    private GreenCSharpParameterListSyntax ParseCSharpParameterList()
    {
        if (_currentToken != null)
        {
            ReturnToken();
        }

        var mode = _mode;

        _mode = Lexer.LexerMode.InCSharpParameterList;

        var parameters = EatToken();

        _mode = mode;

        AkburaDebug.Assert(parameters.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        AkburaDebug.Assert(((GreenSyntaxToken.CSharpRawToken)parameters).RawNode is CSharp.ParameterListSyntax, "Expected ParameterListSyntax");

        return GreenSyntaxFactory.CSharpParameterListSyntax(parameters);
    }

    #endregion

    #region CSharpArgumentListSyntax

    private GreenCSharpArgumentListSyntax ParseCSharpArgumentList()
    {
        if (_currentToken != null)
        {
            ReturnToken();
        }
        var mode = _mode;
        _mode = Lexer.LexerMode.InCSharpArgumentList;
        var arguments = EatToken();
        _mode = mode;
        AkburaDebug.Assert(arguments.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        AkburaDebug.Assert(((GreenSyntaxToken.CSharpRawToken)arguments).RawNode is CSharp.ArgumentListSyntax, "Expected ArgumentListSyntax");
        return GreenSyntaxFactory.CSharpArgumentListSyntax(arguments);
    }

    #endregion

    #region CSharpExpressionSyntax

    private GreenCSharpExpressionSyntax ParseCShaprExpressionUntilSemicolon()
    {
        return ParseCSharpExpressionInMode(Lexer.LexerMode.InExpressionUntilSemicolon);
    }

    private GreenCSharpExpressionSyntax ParseCSharpExpressionInMode(Lexer.LexerMode expressionMode)
    {
        var mode = _mode;

        _mode = expressionMode;

        var token = EatToken();

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        AkburaDebug.Assert(((GreenSyntaxToken.CSharpRawToken)token).RawNode is CSharp.ExpressionSyntax, "Exprected Expression");

        _mode = mode;

        return GreenSyntaxFactory.CSharpExpressionSyntax(token);
    }

    #endregion

    #region CSharpBlock

    private GreenCSharpBlockSyntax ParseCSharpBlock()
    {
        if (TryParseIncrementalCSharpBlockSyntax(out var incrementalBlock))
        {
            return incrementalBlock;
        }

        var openBraceToken = EatToken(SyntaxKind.OpenBraceToken);

        var members = _pool.Allocate<GreenAkTopLevelMemberSyntax>();

        try
        {
            while (CurrentToken.Kind is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
            {
                var member = ParseTopLevelMember();
                members.Add(member);
            }

            var closeBraceToken = EatToken(SyntaxKind.CloseBraceToken);
            return GreenSyntaxFactory.CSharpBlockSyntax(openBraceToken, members.ToList(), closeBraceToken);
        }
        finally
        {
            _pool.Free(members);
        }
    }

    #endregion

    #region CSharpTypeSyntax

    private GreenCSharpTypeSyntax ParseCShaprType()
    {
        var token = EatCSharpTypeSyntax();

        return GreenSyntaxFactory.CSharpTypeSyntax(token);
    }

    private GreenCSharpTypeSyntax? ParseCSharpTypeOrNull()
    {
        var token = EatOrNullCSharpTypeSyntax();

        if (token == null)
        {
            return null;
        }

        return GreenSyntaxFactory.CSharpTypeSyntax(token);
    }

    #endregion

    private const int CancellationCheckInterval = 256;

    /// <summary>
    /// Checks cancellation periodically without adding a cancellation-token
    /// read to every CurrentToken, PeekToken, or EatToken operation.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckCancellation(ref int iterationsUntilCheck)
    {
        if (--iterationsUntilCheck > 0)
        {
            return;
        }

        _cancellationToken.ThrowIfCancellationRequested();

        iterationsUntilCheck = CancellationCheckInterval;
    }
}
