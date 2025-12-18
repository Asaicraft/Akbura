using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using System.Text;

namespace Akbura.Language;

partial class Parser
{
    internal GreenAkburaDocumentSyntax ParseCompilationUnit()
    {
        var members = _pool.Allocate<GreenAkTopLevelMemberSyntax>();

        try
        {
            while (CurrentToken.Kind != SyntaxKind.EndOfFileToken)
            {
                var member = ParseTopLevelMember();
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

    internal GreenAkTopLevelMemberSyntax ParseTopLevelMember()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.StateKeyword => ParseStateDeclaration(),
            _ => default!
        };
    }

    internal GreenStateDeclarationSyntax ParseStateDeclaration()
    {
        var stateKeyword = EatToken(SyntaxKind.StateKeyword);

        var typeSyntax = EatOrNullCSharpTypeSyntax();

        GreenCSharpTypeSyntax? type = null;

        if (typeSyntax != null)
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(typeSyntax);
        }

        var name = GreenSyntaxFactory.IdentifierName(EatToken(SyntaxKind.IdentifierToken));

        var equalsToken = EatToken(SyntaxKind.EqualsToken);

        var initializer = ParseStateInitializer();

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.StateDeclarationSyntax(stateKeyword, type, name, equalsToken, initializer, semicolonToken);

        GreenSyntaxToken.CSharpRawToken? EatOrNullCSharpTypeSyntax()
        {
            // Fast path: "state <name> = ..." (no explicit type).
            if (PeekToken(0).Kind == SyntaxKind.IdentifierToken &&
                PeekToken(1).Kind == SyntaxKind.EqualsToken)
            {
                return null;
            }

            const int MaxLookahead = 128;
            var nameIndex = -1;

            // Find "<identifier> =" => that's the variable name token.
            for (var i = 1; i < MaxLookahead; i++)
            {
                var t = PeekToken(i);

                if (t.Kind == SyntaxKind.EndOfFileToken || t.Kind == SyntaxKind.SemicolonToken)
                    break;

                if (t.Kind == SyntaxKind.IdentifierToken && PeekToken(i + 1).Kind == SyntaxKind.EqualsToken)
                {
                    nameIndex = i;
                    break;
                }
            }

            if (nameIndex <= 0)
                return null;

            var firstTypeToken = PeekToken(0);
            var lastTypeToken = PeekToken(nameIndex - 1);

            // Preserve trivia that would otherwise be lost after collapsing tokens into a single CSharpRawToken.
            var leadingTrivia = firstTypeToken.GetLeadingTrivia();
            var trailingTrivia = lastTypeToken.GetTrailingTrivia(); // <-- includes the space between type and name

            // Build the type text from tokens [0..nameIndex-1],
            // but DO NOT include trailing trivia of the last type token (it must remain between type and name).
            var sb = PooledStringBuilder.GetInstance();

            for (var i = 0; i < nameIndex; i++)
            {
                var token = PeekToken(i);

                sb.Builder.Append(GetTokenCoreText(token));

                if (i != nameIndex - 1)
                {
                    // Keep internal spacing/comments between type tokens.
                    sb.Builder.Append(token.GetTrailingTrivia()?.ToFullString() ?? null);
                }
            }

            var typeText = sb.ToStringAndFree();
            if (typeText.Length == 0)
                return null;

            Microsoft.CodeAnalysis.CSharp.Syntax.TypeSyntax parsedType;
            try
            {
                parsedType = CSharpFactory.ParseTypeName(typeText, offset: 0, options: null, consumeFullText: true);
            }
            catch
            {
                return null;
            }

            // Consume the original type tokens.
            for (var i = 0; i < nameIndex; i++)
            {
                EatToken();
            }

            // Create raw token and re-attach trivia from the original boundary tokens.
            var raw = GreenSyntaxFactory.CSharpRawToken(parsedType);

            raw = (GreenSyntaxToken.CSharpRawToken)raw
                .TokenWithLeadingTrivia(leadingTrivia)
                .TokenWithTrailingTrivia(trailingTrivia);

            return raw;

            static string GetTokenCoreText(GreenSyntaxToken token)
                => !string.IsNullOrEmpty(token.Text)
                    ? token.Text
                    : (SyntaxFacts.GetText(token.Kind) ?? string.Empty);
        }
    }

    private GreenStateInitializerSyntax ParseStateInitializer()
    {
        var token = FastPeekToken();

        // in out bind tokens
        if((int)token.Kind >= (int)SyntaxKind.BindToken && (int)token.Kind <= (int)SyntaxKind.OutToken)
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

    #region CSharpExpressionSyntax

    private GreenCSharpExpressionSyntax ParseCShaprExpressionUntilSemicolon()
    {
        var mode = _mode;

        _mode = Lexer.LexerMode.InExpressionUntilSemicolon;

        var token = EatToken();

        AkburaDebug.Assert(token.Kind == SyntaxKind.CSharpRawToken, "Expected CSharpRawToken");
        AkburaDebug.Assert(((GreenSyntaxToken.CSharpRawToken)token).RawNode is CSharp.ExpressionSyntax, "Exprected Expression");

        _mode = mode;

        return GreenSyntaxFactory.CSharpExpressionSyntax(token);
    }

    #endregion
}
