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
        var closeBrace = ReadRequiredIncrementalToken(SyntaxKind.CloseBraceToken);

        return GreenSyntaxFactory.InlineExpressionSyntax(openBrace, csharpExpression, closeBrace);
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
        return ParseIncrementalCSharpType(token!.ToFullString());
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
            GreenSyntaxFactory.CSharpRawToken(CSharpFactory.ParseParameterList(token.ToFullString())));
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

    private bool CanReadIncrementalNodeOrToken()
    {
        return _isIncremental &&
               _mode == Lexer.LexerMode.TopLevel &&
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
        if (node is GreenInlineExpressionSyntax inlineExpression)
        {
            return ContainsInvalidInlineExpression(inlineExpression);
        }

        if (node.Kind is SyntaxKind.CSharpExpressionSyntax or
            SyntaxKind.CSharpTypeSyntax or
            SyntaxKind.CSharpParameterListSyntax or
            SyntaxKind.CSharpArgumentListSyntax)
        {
            return false;
        }

        if (node.ContainsDiagnosticsDirectly)
        {
            return true;
        }

        if (node.SlotCount == 0)
        {
            return node.ContainsSkippedText;
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
               !node.Expression.ToFullString().EndsWith("}");
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
