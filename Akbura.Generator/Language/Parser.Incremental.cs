using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language;

internal sealed partial class Parser
{
    private static readonly ObjectPool<Blender[]> s_blendersBeforeTokenPool = new(() => new Blender[CachedTokenArraySize]);

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
        _blendersBeforeToken = s_blendersBeforeTokenPool.Allocate();
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

    private bool TryParseIncrementalCommandDeclaration(out GreenCommandDeclarationSyntax command)
    {
        command = null!;

        if (!CanReadIncrementalNodeOrToken() ||
            !TryReadIncrementalToken(SyntaxKind.CommandKeyword, out var commandKeyword))
        {
            return false;
        }

        var returnType = ParseIncrementalCSharpType();
        var name = ParseIncrementalIdentifierName();
        var parameters = ParseIncrementalCSharpParameterList();
        var semicolon = ReadRequiredIncrementalToken(SyntaxKind.SemicolonToken);

        command = GreenSyntaxFactory.CommandDeclarationSyntax(
            commandKeyword,
            returnType,
            name,
            parameters,
            semicolon);
        return true;
    }

    private bool TryParseIncrementalInjectDeclaration(out GreenInjectDeclarationSyntax inject)
    {
        inject = null!;

        if (!CanReadIncrementalNodeOrToken() ||
            !TryReadIncrementalToken(SyntaxKind.InjectKeyword, out var injectKeyword))
        {
            return false;
        }

        var type = ParseIncrementalCSharpType();
        var name = ParseIncrementalIdentifierName();
        var semicolon = ReadRequiredIncrementalToken(SyntaxKind.SemicolonToken);

        inject = GreenSyntaxFactory.InjectDeclarationSyntax(
            injectKeyword,
            type,
            name,
            semicolon);
        return true;
    }

    private bool TryParseIncrementalInlineAkcssBlockSyntax(out GreenInlineAkcssBlockSyntax block)
    {
        block = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        if (TryReadReusableIncrementalNode<GreenInlineAkcssBlockSyntax>(out block))
        {
            return true;
        }

        if (PeekIncrementalTokenKind() != SyntaxKind.AtToken ||
            PeekIncrementalTokenKind(1) != SyntaxKind.AkcssKeyword)
        {
            return false;
        }

        var mode = _mode;
        _mode = Lexer.LexerMode.InAkcss;

        try
        {
            var atToken = ReadRequiredIncrementalToken(SyntaxKind.AtToken);
            var akcssKeyword = ReadRequiredIncrementalToken(SyntaxKind.AkcssKeyword);
            var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
            var members = ParseIncrementalAkcssTopLevelMemberList();
            var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

            block = GreenSyntaxFactory.InlineAkcssBlockSyntax(
                atToken,
                akcssKeyword,
                openBrace,
                members,
                closeBrace);
            return true;
        }
        finally
        {
            _mode = mode;
        }
    }

    private GreenSyntaxList<GreenAkcssTopLevelMemberSyntax> ParseIncrementalAkcssTopLevelMemberList(
        bool stopAtCloseBrace = true)
    {
        var members = _pool.Allocate<GreenAkcssTopLevelMemberSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() != SyntaxKind.EndOfFileToken &&
                   (!stopAtCloseBrace || PeekIncrementalTokenKind() != SyntaxKind.CloseBraceToken))
            {
                members.Add(ParseIncrementalAkcssTopLevelMemberSyntaxCore());
            }

            return members.ToList();
        }
        finally
        {
            _pool.Free(members);
        }
    }

    private GreenAkcssTopLevelMemberSyntax ParseIncrementalAkcssTopLevelMemberSyntaxCore()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssTopLevelMemberSyntax>(out var member))
        {
            return member;
        }

        return PeekIncrementalTokenKind() == SyntaxKind.AtToken &&
            PeekIncrementalTokenKind(1) == SyntaxKind.UtilitiesKeyword
                ? ParseIncrementalAkcssUtilitiesSectionSyntaxCore()
                : ParseIncrementalAkcssStyleRuleSyntaxCore();
    }

    private GreenAkcssStyleRuleSyntax ParseIncrementalAkcssStyleRuleSyntaxCore()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssStyleRuleSyntax>(out var rule))
        {
            return rule;
        }

        var selector = ParseIncrementalAkcssStyleSelectorSyntax();
        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var members = ParseIncrementalAkcssBodyMemberList();
        var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssStyleRuleSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssStyleSelectorSyntax ParseIncrementalAkcssStyleSelectorSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssStyleSelectorSyntax>(out var selector))
        {
            return selector;
        }

        var (targetType, dotToken, name) = ParseIncrementalAkcssDottedSelectorParts();
        return GreenSyntaxFactory.AkcssStyleSelectorSyntax(targetType, dotToken, name);
    }

    private GreenAkcssUtilitiesSectionSyntax ParseIncrementalAkcssUtilitiesSectionSyntaxCore()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssUtilitiesSectionSyntax>(out var section))
        {
            return section;
        }

        var atToken = ReadRequiredIncrementalToken(SyntaxKind.AtToken);
        var utilitiesToken = ReadRequiredIncrementalToken(SyntaxKind.UtilitiesKeyword);
        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var utilities = _pool.Allocate<GreenAkcssUtilityDeclarationSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
            {
                utilities.Add(ParseIncrementalAkcssUtilityDeclarationSyntax());
            }

            var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

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

    private GreenAkcssUtilityDeclarationSyntax ParseIncrementalAkcssUtilityDeclarationSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssUtilityDeclarationSyntax>(out var utility))
        {
            return utility;
        }

        var selector = ParseIncrementalAkcssUtilitySelectorSyntax();
        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var members = ParseIncrementalAkcssBodyMemberList();
        var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssUtilityDeclarationSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssUtilitySelectorSyntax ParseIncrementalAkcssUtilitySelectorSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssUtilitySelectorSyntax>(out var selector))
        {
            return selector;
        }

        var (targetType, dotToken, name) = ParseIncrementalAkcssDottedSelectorParts();
        var parameters = _pool.Allocate<GreenAkcssUtilityParameterSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() == SyntaxKind.MinusToken &&
                   PeekIncrementalTokenKind(1) == SyntaxKind.OpenParenToken)
            {
                parameters.Add(ParseIncrementalAkcssUtilityParameterSyntax());
            }

            return GreenSyntaxFactory.AkcssUtilitySelectorSyntax(
                targetType,
                dotToken,
                name,
                parameters.ToList());
        }
        finally
        {
            _pool.Free(parameters);
        }
    }

    private GreenAkcssUtilityParameterSyntax ParseIncrementalAkcssUtilityParameterSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssUtilityParameterSyntax>(out var parameter))
        {
            return parameter;
        }

        var minus = ReadRequiredIncrementalToken(SyntaxKind.MinusToken);
        var openParen = ReadRequiredIncrementalToken(SyntaxKind.OpenParenToken);
        var type = ParseIncrementalCSharpType();
        var paramName = ParseIncrementalAkcssSimpleName();
        var closeParen = ReadRequiredIncrementalToken(SyntaxKind.CloseParenToken);

        return GreenSyntaxFactory.AkcssUtilityParameterSyntax(
            minus,
            openParen,
            type,
            paramName,
            closeParen);
    }

    private GreenSyntaxList<GreenAkcssBodyMemberSyntax> ParseIncrementalAkcssBodyMemberList()
    {
        var members = _pool.Allocate<GreenAkcssBodyMemberSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() is not (SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken))
            {
                members.Add(ParseIncrementalAkcssBodyMemberSyntax());
            }

            return members.ToList();
        }
        finally
        {
            _pool.Free(members);
        }
    }

    private GreenAkcssBodyMemberSyntax ParseIncrementalAkcssBodyMemberSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssBodyMemberSyntax>(out var member))
        {
            return member;
        }

        if (PeekIncrementalTokenKind() == SyntaxKind.AtToken &&
            PeekIncrementalTokenKind(1) == SyntaxKind.IfKeyword)
        {
            return ParseIncrementalAkcssIfDirectiveSyntax();
        }

        if (PeekIncrementalTokenKind() == SyntaxKind.AtToken)
        {
            return ParseIncrementalAkcssPseudoBlockSyntax();
        }

        return ParseIncrementalAkcssAssignmentSyntax();
    }

    private GreenAkcssAssignmentSyntax ParseIncrementalAkcssAssignmentSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssAssignmentSyntax>(out var assignment))
        {
            return assignment;
        }

        var propertyName = ParseIncrementalAkcssSimpleName();
        var colon = ReadRequiredIncrementalToken(SyntaxKind.ColonToken);
        var expression = ParseIncrementalAkcssExpressionUntilSemicolonOrCloseBrace();
        var semicolon = PeekIncrementalTokenKind() == SyntaxKind.SemicolonToken
            ? ReadRequiredIncrementalToken(SyntaxKind.SemicolonToken)
            : null;

        return GreenSyntaxFactory.AkcssAssignmentSyntax(propertyName, colon, expression, semicolon);
    }

    private GreenAkcssIfDirectiveSyntax ParseIncrementalAkcssIfDirectiveSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssIfDirectiveSyntax>(out var directive))
        {
            return directive;
        }

        var atToken = ReadRequiredIncrementalToken(SyntaxKind.AtToken);
        var ifKeyword = ReadRequiredIncrementalToken(SyntaxKind.IfKeyword);
        var openParen = ReadRequiredIncrementalToken(SyntaxKind.OpenParenToken);
        var condition = ParseIncrementalAkcssExpressionUntil(SyntaxKind.CloseParenToken);
        var closeParen = ReadRequiredIncrementalToken(SyntaxKind.CloseParenToken);
        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var members = ParseIncrementalAkcssBodyMemberList();
        var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

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

    private GreenAkcssPseudoBlockSyntax ParseIncrementalAkcssPseudoBlockSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssPseudoBlockSyntax>(out var block))
        {
            return block;
        }

        var selector = ParseIncrementalAkcssPseudoSelectorSyntax();
        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var members = ParseIncrementalAkcssBodyMemberList();
        var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.AkcssPseudoBlockSyntax(selector, openBrace, members, closeBrace);
    }

    private GreenAkcssPseudoSelectorSyntax ParseIncrementalAkcssPseudoSelectorSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssPseudoSelectorSyntax>(out var selector))
        {
            return selector;
        }

        var atToken = ReadRequiredIncrementalToken(SyntaxKind.AtToken);
        var firstState = ParseIncrementalAkcssSimpleName();
        var additional = _pool.Allocate<GreenAkcssAdditionalPseudoStateSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() == SyntaxKind.AtToken &&
                   IsIncrementalAkcssNameToken(PeekIncrementalTokenKind(1)))
            {
                additional.Add(ParseIncrementalAkcssAdditionalPseudoStateSyntax());
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

    private GreenAkcssAdditionalPseudoStateSyntax ParseIncrementalAkcssAdditionalPseudoStateSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenAkcssAdditionalPseudoStateSyntax>(out var additional))
        {
            return additional;
        }

        var atToken = ReadRequiredIncrementalToken(SyntaxKind.AtToken);
        var state = ParseIncrementalAkcssSimpleName();

        return GreenSyntaxFactory.AkcssAdditionalPseudoStateSyntax(atToken, state);
    }

    private (GreenSimpleNameSyntax? TargetType, GreenSyntaxToken DotToken, GreenSimpleNameSyntax Name)
        ParseIncrementalAkcssDottedSelectorParts()
    {
        GreenSimpleNameSyntax? targetType = null;

        if (IsIncrementalAkcssNameToken(PeekIncrementalTokenKind()) &&
            PeekIncrementalTokenKind(1) == SyntaxKind.DotToken)
        {
            targetType = ParseIncrementalAkcssSimpleName();
        }

        var dotToken = ReadRequiredIncrementalToken(SyntaxKind.DotToken);
        var name = ParseIncrementalAkcssSimpleName();

        return (targetType, dotToken, name);
    }

    private GreenIdentifierNameSyntax ParseIncrementalAkcssSimpleName()
    {
        if (TryReadReusableIncrementalNode<GreenIdentifierNameSyntax>(out var name))
        {
            return name;
        }

        if (TryReadIncrementalToken(IsIncrementalAkcssNameToken, out var identifier))
        {
            return GreenSyntaxFactory.IdentifierName(
                identifier.Kind == SyntaxKind.IdentifierToken
                    ? identifier
                    : ConvertToIdentifier(identifier));
        }

        return ParseAkcssSimpleName();
    }

    private GreenCSharpExpressionSyntax ParseIncrementalAkcssExpressionUntilSemicolonOrCloseBrace()
    {
        return ParseIncrementalAkcssExpressionUntil(
            SyntaxKind.SemicolonToken,
            SyntaxKind.CloseBraceToken);
    }

    private GreenCSharpExpressionSyntax ParseIncrementalAkcssExpressionUntil(
        SyntaxKind firstTerminator,
        SyntaxKind? secondTerminator = null)
    {
        return TryReadReusableIncrementalNode<GreenCSharpExpressionSyntax>(out var expression)
            ? expression
            : ParseAkcssExpressionUntil(firstTerminator, secondTerminator);
    }

    private bool TryParseIncrementalMarkupRootSyntax(out GreenMarkupRootSyntax markup)
    {
        markup = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        if (TryReadReusableIncrementalNode<GreenMarkupRootSyntax>(out markup))
        {
            return true;
        }

        if (PeekIncrementalTokenKind() != SyntaxKind.LessThanToken)
        {
            return false;
        }

        markup = GreenSyntaxFactory.MarkupRootSyntax(ParseIncrementalMarkupElementSyntax());
        return true;
    }

    private GreenMarkupElementSyntax ParseIncrementalMarkupElementSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupElementSyntax>(out var element))
        {
            return element;
        }

        var startTag = ParseIncrementalMarkupStartTagSyntax();
        var body = _pool.Allocate<GreenMarkupContentSyntax>();

        try
        {
            GreenMarkupEndTagSyntax? endTag = null;

            if (startTag.CloseToken.Kind != SyntaxKind.SlashGreaterToken)
            {
                while (PeekIncrementalTokenKind() is not (SyntaxKind.EndOfFileToken or SyntaxKind.LessSlashToken))
                {
                    body.Add(ParseIncrementalMarkupContentSyntax());
                }

                if (PeekIncrementalTokenKind() == SyntaxKind.LessSlashToken)
                {
                    endTag = ParseIncrementalMarkupEndTagSyntax();
                }
            }

            return GreenSyntaxFactory.MarkupElementSyntax(startTag, body.ToList(), endTag);
        }
        finally
        {
            _pool.Free(body);
        }
    }

    private GreenMarkupStartTagSyntax ParseIncrementalMarkupStartTagSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupStartTagSyntax>(out var startTag))
        {
            return startTag;
        }

        var less = ReadRequiredIncrementalToken(SyntaxKind.LessThanToken);
        var name = ParseIncrementalMarkupComponentNameSyntax();
        var attributes = _pool.Allocate<GreenMarkupAttributeSyntax>();

        try
        {
            while (PeekIncrementalTokenKind() is not (SyntaxKind.EndOfFileToken or
                   SyntaxKind.GreaterThanToken or
                   SyntaxKind.SlashGreaterToken) &&
                   IsIncrementalMarkupAttributeStart())
            {
                attributes.Add(ParseIncrementalMarkupAttributeSyntax());
            }

            var close = PeekIncrementalTokenKind() == SyntaxKind.SlashGreaterToken
                ? ReadRequiredIncrementalToken(SyntaxKind.SlashGreaterToken)
                : ReadRequiredIncrementalToken(SyntaxKind.GreaterThanToken);

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

    private GreenMarkupEndTagSyntax ParseIncrementalMarkupEndTagSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupEndTagSyntax>(out var endTag))
        {
            return endTag;
        }

        var lessSlash = ReadRequiredIncrementalToken(SyntaxKind.LessSlashToken);
        var name = ParseIncrementalMarkupComponentNameSyntax();
        var greater = ReadRequiredIncrementalToken(SyntaxKind.GreaterThanToken);

        return GreenSyntaxFactory.MarkupEndTagSyntax(lessSlash, name, greater);
    }

    private GreenMarkupContentSyntax ParseIncrementalMarkupContentSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupContentSyntax>(out var content))
        {
            return content;
        }

        return PeekIncrementalTokenKind() switch
        {
            SyntaxKind.LessThanToken => GreenSyntaxFactory.MarkupElementContentSyntax(
                ParseIncrementalMarkupElementSyntax()),
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.MarkupInlineExpressionSyntax(
                ParseIncrementalInlineExpressionSyntax()),
            _ => ParseIncrementalMarkupTextLiteralSyntax(),
        };
    }

    private GreenMarkupTextLiteralSyntax ParseIncrementalMarkupTextLiteralSyntax()
    {
        return TryReadReusableIncrementalNode<GreenMarkupTextLiteralSyntax>(out var text)
            ? text
            : ParseMarkupTextLiteralSyntax();
    }

    private GreenMarkupAttributeSyntax ParseIncrementalMarkupAttributeSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupAttributeSyntax>(out var attribute))
        {
            return attribute;
        }

        if (IsIncrementalMarkupPrefixedAttributeStart())
        {
            return ParseIncrementalMarkupPrefixedAttributeSyntax();
        }

        if (IsIncrementalPlainMarkupAttributeStart())
        {
            return ParseIncrementalMarkupPlainAttributeSyntax();
        }

        return ParseIncrementalTailwindAttributeSyntax();
    }

    private GreenTailwindAttributeSyntax ParseIncrementalTailwindAttributeSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenTailwindAttributeSyntax>(out var attribute))
        {
            return attribute;
        }

        if (IsIncrementalTailwindPrefixSegmentStart())
        {
            return ParseTailwindAttributeSyntax();
        }

        var prefix = TryParseIncrementalTailwindPrefixSegmentSyntax();
        var name = ParseIncrementalTailwindSimpleName();

        if (PeekIncrementalTokenKind() != SyntaxKind.MinusToken)
        {
            return prefix is null
                ? GreenSyntaxFactory.TailwindFlagAttributeSyntax(name)
                : GreenSyntaxFactory.TailwindFullAttributeSyntax(
                    prefix,
                    name,
                    minus: null,
                    segments: default);
        }

        var minus = ReadRequiredIncrementalToken(SyntaxKind.MinusToken);
        var segments = _pool.AllocateSeparated<GreenTailwindSegmentSyntax>();

        try
        {
            segments.Add(ParseIncrementalTailwindSegmentSyntax());

            while (PeekIncrementalTokenKind() == SyntaxKind.MinusToken)
            {
                segments.AddSeparator(ReadRequiredIncrementalToken(SyntaxKind.MinusToken));
                segments.Add(ParseIncrementalTailwindSegmentSyntax());
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

    private GreenTailwindPrefixSegmentSyntax? TryParseIncrementalTailwindPrefixSegmentSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenTailwindPrefixSegmentSyntax>(out var prefix))
        {
            return prefix;
        }

        if (PeekIncrementalTokenKind() == SyntaxKind.OpenBraceToken)
        {
            var expression = ParseIncrementalInlineExpressionSyntax();
            var colon = ReadRequiredIncrementalToken(SyntaxKind.ColonToken);
            return GreenSyntaxFactory.ExpressionConditionalPrefixSyntax(expression, colon);
        }

        if (IsIncrementalTailwindNameToken(PeekIncrementalTokenKind()) &&
            PeekIncrementalTokenKind(1) == SyntaxKind.ColonToken &&
            !IsIncrementalMarkupPrefixedAttributeStart())
        {
            var name = ParseIncrementalTailwindSimpleName();
            var colon = ReadRequiredIncrementalToken(SyntaxKind.ColonToken);
            return GreenSyntaxFactory.SimpleConditionalPrefixSyntax(name, colon);
        }

        return null;
    }

    private bool IsIncrementalTailwindPrefixSegmentStart()
    {
        return PeekIncrementalTokenKind() == SyntaxKind.OpenBraceToken ||
            (IsIncrementalTailwindNameToken(PeekIncrementalTokenKind()) &&
             PeekIncrementalTokenKind(1) == SyntaxKind.ColonToken &&
             !IsIncrementalMarkupPrefixedAttributeStart());
    }

    private GreenTailwindSegmentSyntax ParseIncrementalTailwindSegmentSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenTailwindSegmentSyntax>(out var segment))
        {
            return segment;
        }

        return PeekIncrementalTokenKind() switch
        {
            SyntaxKind.NumericLiteralToken => GreenSyntaxFactory.TailwindNumericSegmentSyntax(
                ReadRequiredIncrementalToken(SyntaxKind.NumericLiteralToken)),
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.TailwindExpressionSegmentSyntax(
                ParseIncrementalInlineExpressionSyntax()),
            _ => GreenSyntaxFactory.TailwindIdentifierSegmentSyntax(
                ParseIncrementalTailwindSimpleName()),
        };
    }

    private GreenMarkupPlainAttributeSyntax ParseIncrementalMarkupPlainAttributeSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupPlainAttributeSyntax>(out var attribute))
        {
            return attribute;
        }

        var name = ParseIncrementalMarkupSimpleName();
        var equals = ReadRequiredIncrementalToken(SyntaxKind.EqualsToken);
        var value = ParseIncrementalMarkupAttributeValueSyntax();

        return GreenSyntaxFactory.MarkupPlainAttributeSyntax(name, equals, value);
    }

    private GreenMarkupPrefixedAttributeSyntax ParseIncrementalMarkupPrefixedAttributeSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupPrefixedAttributeSyntax>(out var attribute))
        {
            return attribute;
        }

        var prefix = ReadRequiredIncrementalToken(
            kind => kind is SyntaxKind.BindToken or SyntaxKind.OutToken,
            SyntaxKind.BindToken);
        var colon = ReadRequiredIncrementalToken(SyntaxKind.ColonToken);
        var name = ParseIncrementalMarkupSimpleName();
        var equals = ReadRequiredIncrementalToken(SyntaxKind.EqualsToken);
        var value = ParseIncrementalMarkupAttributeValueSyntax();

        return GreenSyntaxFactory.MarkupPrefixedAttributeSyntax(prefix, colon, name, equals, value);
    }

    private GreenMarkupAttributeValueSyntax? ParseIncrementalMarkupAttributeValueSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupAttributeValueSyntax>(out var value))
        {
            return value;
        }

        return PeekIncrementalTokenKind() switch
        {
            SyntaxKind.OpenBraceToken => GreenSyntaxFactory.MarkupDynamicAttributeValueSyntax(
                prefix: null,
                expression: ParseIncrementalInlineExpressionSyntax()),
            SyntaxKind.DoubleQuoteToken or SyntaxKind.SingleQuoteToken => GreenSyntaxFactory.MarkupLiteralAttributeValueSyntax(
                prefix: null,
                value: ParseIncrementalQuotedMarkupTextLiteralSyntax()),
            _ => null,
        };
    }

    private GreenMarkupTextLiteralSyntax ParseIncrementalQuotedMarkupTextLiteralSyntax()
    {
        return TryReadReusableIncrementalNode<GreenMarkupTextLiteralSyntax>(out var text)
            ? text
            : ParseQuotedMarkupTextLiteralSyntax();
    }

    private GreenInlineExpressionSyntax ParseIncrementalInlineExpressionSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenInlineExpressionSyntax>(out var expression))
        {
            return expression;
        }

        var openBrace = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var csharpExpression = ParseIncrementalCSharpExpressionInMode(Lexer.LexerMode.InInlineExpression);
        var closeBrace = PeekIncrementalTokenKind() == SyntaxKind.CloseBraceToken
            ? ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken)
            : InlineExpressionRawEndsWithCloseBrace(csharpExpression)
                ? GreenSyntaxFactory.MissingToken(SyntaxKind.CloseBraceToken)
                : ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.InlineExpressionSyntax(openBrace, csharpExpression, closeBrace);
    }

    private bool TryParseIncrementalCSharpStatementSyntax(
        bool allowFileScopedDirectives,
        out GreenCSharpStatementSyntax statement)
    {
        statement = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        if (TryReadReusableIncrementalNode<GreenCSharpStatementSyntax>(out statement))
        {
            return true;
        }

        if (!IsIncrementalCSharpStatementStart(allowFileScopedDirectives))
        {
            return false;
        }

        statement = ParseIncrementalCSharpStatementSyntaxCore();
        return true;
    }

    private GreenCSharpStatementSyntax ParseIncrementalCSharpStatementSyntaxCore()
    {
        var tokens = _pool.Allocate<GreenSyntaxToken>();
        var canHaveBlockBody = IsCSharpBlockStatementStarter(PeekIncrementalToken());
        var parenDepth = 0;
        var bracketDepth = 0;
        var braceDepth = 0;

        try
        {
            while (PeekIncrementalTokenKind() != SyntaxKind.EndOfFileToken)
            {
                var kind = PeekIncrementalTokenKind();

                if (kind == SyntaxKind.CloseBraceToken && braceDepth == 0)
                {
                    break;
                }

                if (kind == SyntaxKind.OpenBraceToken &&
                    parenDepth == 0 &&
                    bracketDepth == 0 &&
                    braceDepth == 0 &&
                    canHaveBlockBody)
                {
                    var body = ParseIncrementalCSharpBlockSyntaxCore();
                    return GreenSyntaxFactory.CSharpStatementSyntax(tokens.ToList(), body);
                }

                var token = ReadIncrementalToken();
                tokens.Add(token);

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
                    case SyntaxKind.SemicolonToken when parenDepth == 0 && bracketDepth == 0 && braceDepth == 0:
                        return GreenSyntaxFactory.CSharpStatementSyntax(tokens.ToList(), body: null);
                }
            }

            return GreenSyntaxFactory.CSharpStatementSyntax(tokens.ToList(), body: null);
        }
        finally
        {
            _pool.Free(tokens);
        }
    }

    private bool TryParseIncrementalCSharpBlockSyntax(out GreenCSharpBlockSyntax block)
    {
        block = null!;

        if (!CanReadIncrementalNodeOrToken())
        {
            return false;
        }

        if (TryReadReusableIncrementalNode<GreenCSharpBlockSyntax>(out block))
        {
            return true;
        }

        if (PeekIncrementalTokenKind() != SyntaxKind.OpenBraceToken)
        {
            return false;
        }

        block = ParseIncrementalCSharpBlockSyntaxCore();
        return true;
    }

    private GreenCSharpBlockSyntax ParseIncrementalCSharpBlockSyntaxCore()
    {
        var openBraceToken = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var members = _pool.Allocate<GreenAkTopLevelMemberSyntax>();

        try
        {
            while (true)
            {
                if (TryEatReusableTopLevelMember(out var reusableMember))
                {
                    members.Add(reusableMember);
                    continue;
                }

                if (PeekIncrementalTokenKind() is SyntaxKind.EndOfFileToken or SyntaxKind.CloseBraceToken)
                {
                    break;
                }

                members.Add(ParseTopLevelMemberAfterReusableProbe());
            }

            var closeBraceToken = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);
            return GreenSyntaxFactory.CSharpBlockSyntax(openBraceToken, members.ToList(), closeBraceToken);
        }
        finally
        {
            _pool.Free(members);
        }
    }

    private GreenAkTopLevelMemberSyntax ParseTopLevelMemberAfterReusableProbe()
    {
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
            SyntaxKind.UseEffectKeyword => ParseUseEffectDeclarationSyntax(),
            SyntaxKind.LessThanToken => ParseMarkupRootSyntax(),
            _ => ParseCSharpStatementSyntax()
        };
    }

    private bool IsIncrementalCSharpStatementStart(bool allowFileScopedDirectives)
    {
        var kind = PeekIncrementalTokenKind();

        if (kind is SyntaxKind.EndOfFileToken or
            SyntaxKind.CloseBraceToken or
            SyntaxKind.StateKeyword or
            SyntaxKind.ParamKeyword or
            SyntaxKind.InjectKeyword or
            SyntaxKind.CommandKeyword or
            SyntaxKind.UseEffectKeyword or
            SyntaxKind.LessThanToken)
        {
            return false;
        }

        if (kind == SyntaxKind.AtToken &&
            PeekIncrementalTokenKind(1) == SyntaxKind.AkcssKeyword)
        {
            return false;
        }

        if (!allowFileScopedDirectives &&
            (kind is SyntaxKind.UsingKeyword or SyntaxKind.NamespaceKeyword ||
             kind == SyntaxKind.GlobalKeyword && PeekIncrementalTokenKind(1) == SyntaxKind.UsingKeyword))
        {
            return false;
        }

        return true;
    }

    private GreenMarkupComponentNameSyntax ParseIncrementalMarkupComponentNameSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupComponentNameSyntax>(out var name))
        {
            return name;
        }

        GreenMarkupAliasQualifierSyntax? aliasQualifier = null;
        if (PeekIncrementalTokenKind() == SyntaxKind.IdentifierToken &&
            PeekIncrementalTokenKind(1) == SyntaxKind.DoubleColonToken)
        {
            aliasQualifier = ParseIncrementalMarkupAliasQualifierSyntax();
        }

        var firstName = ParseIncrementalIdentifierName();
        GreenMarkupGenericArgumentListSyntax? firstGenericArgs = null;

        if (PeekIncrementalTokenKind() == SyntaxKind.OpenBraceToken)
        {
            firstGenericArgs = ParseIncrementalMarkupGenericArgumentListSyntax();
        }

        if (aliasQualifier is null &&
            firstGenericArgs is null &&
            PeekIncrementalTokenKind() != SyntaxKind.DotToken)
        {
            return GreenSyntaxFactory.MarkupSimpleComponentNameSyntax(firstName);
        }

        var segments = _pool.AllocateSeparated<GreenMarkupNameSegmentSyntax>();

        try
        {
            segments.Add(
                firstGenericArgs is null
                    ? GreenSyntaxFactory.MarkupIdentifierNameSegmentSyntax(firstName)
                    : GreenSyntaxFactory.MarkupGenericNameSegmentSyntax(firstName, firstGenericArgs));

            while (PeekIncrementalTokenKind() == SyntaxKind.DotToken)
            {
                segments.AddSeparator(ReadRequiredIncrementalToken(SyntaxKind.DotToken));
                segments.Add(ParseIncrementalMarkupNameSegmentSyntax());
            }

            var qualifiedName = GreenSyntaxFactory.MarkupQualifiedNameSyntax(segments.ToList());

            return GreenSyntaxFactory.MarkupQualifiedComponentNameSyntax(
                aliasQualifier,
                qualifiedName);
        }
        finally
        {
            _pool.Free(segments);
        }
    }

    private GreenMarkupAliasQualifierSyntax ParseIncrementalMarkupAliasQualifierSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupAliasQualifierSyntax>(out var aliasQualifier))
        {
            return aliasQualifier;
        }

        var alias = ParseIncrementalIdentifierName();
        var doubleColon = ReadRequiredIncrementalToken(SyntaxKind.DoubleColonToken);

        return GreenSyntaxFactory.MarkupAliasQualifierSyntax(alias, doubleColon);
    }

    private GreenMarkupNameSegmentSyntax ParseIncrementalMarkupNameSegmentSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupNameSegmentSyntax>(out var segment))
        {
            return segment;
        }

        var name = ParseIncrementalIdentifierName();

        if (PeekIncrementalTokenKind() != SyntaxKind.OpenBraceToken)
        {
            return GreenSyntaxFactory.MarkupIdentifierNameSegmentSyntax(name);
        }

        var genericArgs = ParseIncrementalMarkupGenericArgumentListSyntax();
        return GreenSyntaxFactory.MarkupGenericNameSegmentSyntax(name, genericArgs);
    }

    private GreenMarkupGenericArgumentListSyntax ParseIncrementalMarkupGenericArgumentListSyntax()
    {
        if (TryReadReusableIncrementalNode<GreenMarkupGenericArgumentListSyntax>(out var genericArgs))
        {
            return genericArgs;
        }

        var open = ReadRequiredIncrementalToken(SyntaxKind.OpenBraceToken);
        var arguments = _pool.AllocateSeparated<GreenCSharpTypeSyntax>();

        try
        {
            if (PeekIncrementalTokenKind() is not (SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken))
            {
                arguments.Add(ParseIncrementalCSharpType());

                while (PeekIncrementalTokenKind() == SyntaxKind.CommaToken)
                {
                    arguments.AddSeparator(ReadRequiredIncrementalToken(SyntaxKind.CommaToken));

                    if (PeekIncrementalTokenKind() is SyntaxKind.CloseBraceToken or SyntaxKind.EndOfFileToken)
                    {
                        break;
                    }

                    arguments.Add(ParseIncrementalCSharpType());
                }
            }

            var close = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);
            return GreenSyntaxFactory.MarkupGenericArgumentListSyntax(open, arguments.ToList(), close);
        }
        finally
        {
            _pool.Free(arguments);
        }
    }

    private GreenIdentifierNameSyntax ParseIncrementalMarkupSimpleName()
    {
        if (TryReadReusableIncrementalNode<GreenIdentifierNameSyntax>(out var name))
        {
            return name;
        }

        if (TryReadIncrementalToken(IsIncrementalMarkupNameToken, out var identifier))
        {
            return GreenSyntaxFactory.IdentifierName(
                identifier.Kind == SyntaxKind.IdentifierToken
                    ? identifier
                    : ConvertToIdentifier(identifier));
        }

        return ParseMarkupSimpleName();
    }

    private GreenIdentifierNameSyntax ParseIncrementalTailwindSimpleName()
    {
        if (TryReadReusableIncrementalNode<GreenIdentifierNameSyntax>(out var name))
        {
            return name;
        }

        if (TryReadIncrementalToken(IsIncrementalTailwindNameToken, out var identifier))
        {
            return GreenSyntaxFactory.IdentifierName(
                identifier.Kind == SyntaxKind.IdentifierToken
                    ? identifier
                    : ConvertToIdentifier(identifier));
        }

        return ParseTailwindSimpleName();
    }

    private GreenCSharpTypeSyntax ParseIncrementalCSharpType()
    {
        if (TryReadReusableIncrementalNode<GreenCSharpTypeSyntax>(out var type))
        {
            return type;
        }

        var token = EatCSharpTypeSyntax();
        AkburaDebug.Assert(token != null, "Expected required C# return type.");
        return GreenSyntaxFactory.CSharpTypeSyntax(
            EnsureCSharpRawToken(
                token!,
                text => CSharpFactory.ParseTypeName(text)));
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
            : GreenSyntaxFactory.CSharpTypeSyntax(
                EnsureCSharpRawToken(
                    token,
                    text => CSharpFactory.ParseTypeName(text)));
    }

    private GreenCSharpParameterListSyntax ParseIncrementalCSharpParameterList()
    {
        if (TryReadReusableIncrementalNode<GreenCSharpParameterListSyntax>(out var parameters))
        {
            return parameters;
        }

        var mode = _mode;
        _mode = Lexer.LexerMode.InCSharpParameterList;

        var token = EatToken();

        _mode = mode;

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        return GreenSyntaxFactory.CSharpParameterListSyntax(
            EnsureCSharpRawToken(
                (GreenSyntaxToken.CSharpRawToken)token,
                text => CSharpFactory.ParseParameterList(text)));
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
        return ParseIncrementalCSharpExpressionInMode(Lexer.LexerMode.InExpressionUntilSemicolon);
    }

    private GreenCSharpExpressionSyntax ParseIncrementalCSharpExpressionInMode(Lexer.LexerMode expressionMode)
    {
        if (TryReadReusableIncrementalNode<GreenCSharpExpressionSyntax>(out var expression))
        {
            return expression;
        }

        var mode = _mode;
        _mode = expressionMode;

        var token = EatToken();

        _mode = mode;

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        return GreenSyntaxFactory.CSharpExpressionSyntax(
            EnsureCSharpRawToken(
                (GreenSyntaxToken.CSharpRawToken)token,
                text => CSharpFactory.ParseExpression(
                    text,
                    offset: 0,
                    options: null,
                    consumeFullText: true)));
    }

    private static GreenSyntaxToken.CSharpRawToken EnsureCSharpRawToken<TNode>(
        GreenSyntaxToken.CSharpRawToken token,
        Func<string, TNode> parse)
        where TNode : Microsoft.CodeAnalysis.CSharp.CSharpSyntaxNode
    {
        if (token.RawNode is TNode)
        {
            return token;
        }

        return GreenSyntaxFactory.CSharpRawToken(parse(token.ToFullString()));
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
        if (!CanReadIncrementalNodeOrToken())
        {
            return EatToken(kind);
        }

        if (TryReadIncrementalToken(kind, out var token))
        {
            return token;
        }

        var actual = PeekIncrementalTokenKind();
        return CreateMissingToken(kind, actual);
    }

    private GreenSyntaxToken ReadRequiredIncrementalToken(
        Func<SyntaxKind, bool> predicate,
        SyntaxKind missingKind)
    {
        if (!CanReadIncrementalNodeOrToken())
        {
            if (predicate(CurrentToken.Kind))
            {
                return EatToken();
            }

            return CreateMissingToken(missingKind, CurrentToken.Kind);
        }

        if (TryReadIncrementalToken(predicate, out var token))
        {
            return token;
        }

        var actual = PeekIncrementalTokenKind();
        return CreateMissingToken(missingKind, actual);
    }

    private SyntaxKind PeekIncrementalTokenKind()
    {
        return PeekIncrementalTokenKind(offset: 0);
    }

    private GreenSyntaxToken PeekIncrementalToken()
    {
        return PeekIncrementalToken(offset: 0);
    }

    private GreenSyntaxToken PeekIncrementalToken(int offset)
    {
        if (!CanReadIncrementalNodeOrToken())
        {
            return PeekToken(offset);
        }

        var savedPosition = _lexer.TextWindow.Position;
        var blender = _blender;
        GreenSyntaxToken? token = null;

        for (var i = 0; i <= offset; i++)
        {
            var blended = blender.ReadToken(_mode);
            token = (GreenSyntaxToken?)blended.Token.Node;
            blender = blended.Blender;
        }

        _lexer.TextWindow.Reset(savedPosition);
        AkburaDebug.Assert(token != null, "Expected a token from the incremental lexer.");
        return token!;
    }

    private SyntaxKind PeekIncrementalTokenKind(int offset)
    {
        if (!CanReadIncrementalNodeOrToken())
        {
            return PeekToken(offset).Kind;
        }

        var savedPosition = _lexer.TextWindow.Position;
        var blender = _blender;
        var kind = SyntaxKind.None;

        for (var i = 0; i <= offset; i++)
        {
            var blended = blender.ReadToken(_mode);
            kind = blended.Token.Node?.Kind ?? SyntaxKind.None;
            blender = blended.Blender;
        }

        _lexer.TextWindow.Reset(savedPosition);
        return kind;
    }

    private GreenSyntaxToken ReadIncrementalToken()
    {
        if (!CanReadIncrementalNodeOrToken())
        {
            return EatToken();
        }

        var blended = _blender.ReadToken(_mode);
        var token = (GreenSyntaxToken?)blended.Token.Node;
        AkburaDebug.Assert(token != null, "Expected a token from the incremental lexer.");

        _blender = blended.Blender;
        _prevTokenTrailingTrivia = token!.GetTrailingTrivia();
        return token;
    }

    private bool IsIncrementalMarkupAttributeStart()
    {
        return IsIncrementalMarkupPrefixedAttributeStart() ||
            IsIncrementalPlainMarkupAttributeStart() ||
            IsIncrementalTailwindAttributeStart();
    }

    private bool IsIncrementalMarkupPrefixedAttributeStart()
    {
        return PeekIncrementalTokenKind() is SyntaxKind.BindToken or SyntaxKind.OutToken &&
            PeekIncrementalTokenKind(1) == SyntaxKind.ColonToken;
    }

    private bool IsIncrementalPlainMarkupAttributeStart()
    {
        return IsIncrementalMarkupNameToken(PeekIncrementalTokenKind()) &&
            (PeekIncrementalTokenKind(1) == SyntaxKind.EqualsToken ||
             IsIncrementalMarkupAttributeValueStart(PeekIncrementalTokenKind(1)));
    }

    private bool IsIncrementalTailwindAttributeStart()
    {
        var kind = PeekIncrementalTokenKind();
        return kind is SyntaxKind.OpenBraceToken or SyntaxKind.MinusToken ||
            IsIncrementalTailwindNameToken(kind);
    }

    private static bool IsIncrementalMarkupAttributeValueStart(SyntaxKind kind)
    {
        return kind is SyntaxKind.OpenBraceToken or
            SyntaxKind.DoubleQuoteToken or
            SyntaxKind.SingleQuoteToken;
    }

    private static bool IsIncrementalMarkupNameToken(SyntaxKind kind)
    {
        return kind == SyntaxKind.IdentifierToken ||
            SyntaxFacts.IsReservedKeyword(kind);
    }

    private static bool IsIncrementalTailwindNameToken(SyntaxKind kind)
    {
        return kind == SyntaxKind.IdentifierToken ||
            SyntaxFacts.IsReservedKeyword(kind);
    }

    private static bool IsIncrementalAkcssNameToken(SyntaxKind kind)
    {
        return kind == SyntaxKind.IdentifierToken ||
            kind == SyntaxKind.UtilitiesKeyword ||
            kind == SyntaxKind.AkcssKeyword ||
            SyntaxFacts.IsReservedKeyword(kind);
    }

    private bool CanReadIncrementalNodeOrToken()
    {
        return _isIncremental &&
               _mode is Lexer.LexerMode.TopLevel or Lexer.LexerMode.InAkcss &&
               _currentToken == null &&
               _tokenOffset >= _tokenCount;
    }

    private static bool CanReuseTopLevelMember(GreenAkTopLevelMemberSyntax member)
    {
        return CanReuseIncrementalNode(member);
    }

    private static bool CanReuseIncrementalNode(GreenNode node)
    {
        return node.FullWidth > 0 &&
               !ContainsDiagnosticsOrSkippedText(node);
    }

    private static bool ContainsDiagnosticsOrSkippedText(GreenNode node)
    {
        if (node.Kind == SyntaxKind.InlineExpressionSyntax)
        {
            return ContainsInvalidInlineExpression(Unsafe.As<GreenInlineExpressionSyntax>(node));
        }

        if (node.Kind is SyntaxKind.CSharpExpressionSyntax or
            SyntaxKind.CSharpTypeSyntax or
            SyntaxKind.CSharpParameterListSyntax or
            SyntaxKind.CSharpArgumentListSyntax)
        {
            return false;
        }

        if (node.ContainsSkippedText)
        {
            return true;
        }

        if (!node.ContainsDiagnostics)
        {
            return false;
        }

        return ContainsDiagnosticsOrSkippedTextSlow(node);
    }

    private static bool ContainsDiagnosticsOrSkippedTextSlow(GreenNode node)
    {
        if (node.Kind == SyntaxKind.InlineExpressionSyntax)
        {
            return ContainsInvalidInlineExpression(Unsafe.As<GreenInlineExpressionSyntax>(node));
        }

        if (node.Kind is SyntaxKind.CSharpExpressionSyntax or
            SyntaxKind.CSharpTypeSyntax or
            SyntaxKind.CSharpParameterListSyntax or
            SyntaxKind.CSharpArgumentListSyntax)
        {
            return false;
        }

        if (node.ContainsDiagnosticsDirectly ||
            node.ContainsSkippedText)
        {
            return true;
        }

        for (var i = 0; i < node.SlotCount; i++)
        {
            var child = node.GetSlot(i);
            if (child != null && ContainsDiagnosticsOrSkippedText(child))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsInvalidInlineExpression(GreenInlineExpressionSyntax node)
    {
        if (ContainsDiagnosticsOrSkippedText(node.OpenBrace) ||
            ContainsDiagnosticsOrSkippedText(node.Expression))
        {
            return true;
        }

        if (!node.CloseBrace.ContainsDiagnosticsDirectly &&
            !node.CloseBrace.ContainsSkippedText)
        {
            return false;
        }

        // The current raw C# inline-expression lexer consumes the closing
        // '}' into the CSharpRawToken, so the parser creates a zero-width
        // missing CloseBraceToken. That recovery shape is stable and safe
        // to reuse outside changed text. A truly unterminated expression
        // does not have the terminator in the raw expression text and stays
        // non-reusable.
        return !node.CloseBrace.IsMissing ||
               node.CloseBrace.FullWidth != 0 ||
               !InlineExpressionRawEndsWithCloseBrace(node.Expression);
    }

    private static bool InlineExpressionRawEndsWithCloseBrace(GreenCSharpExpressionSyntax expression)
    {
        var tokens = expression.Tokens;
        if (tokens.Count == 0)
        {
            return false;
        }

        var token = tokens[0];
        if (token == null || token.Kind != SyntaxKind.CSharpRawToken)
        {
            return false;
        }

        var rawToken = Unsafe.As<GreenSyntaxToken.CSharpRawToken>(token);
        var rawNode = rawToken.RawNode;
        if (rawNode != null)
        {
            var lastToken = rawNode.GetLastToken(includeZeroWidth: true, includeSkipped: true);
            return lastToken.RawKind == (int)CSharpSyntaxKind.CloseBraceToken;
        }

        return rawToken.Text.EndsWith("}", StringComparison.Ordinal);
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

    private void ReturnBlendersBeforeTokenToPool()
    {
        var blendersBeforeToken = _blendersBeforeToken;
        if (blendersBeforeToken == null)
        {
            return;
        }

        _blendersBeforeToken = null;

        var clearCount = Math.Min(_maxWrittenLexedTokenIndex + 1, blendersBeforeToken.Length);
        if (clearCount > 0)
        {
            Array.Clear(blendersBeforeToken, 0, clearCount);
        }

        if (blendersBeforeToken.Length == CachedTokenArraySize)
        {
            s_blendersBeforeTokenPool.Free(blendersBeforeToken);
        }
    }
}
