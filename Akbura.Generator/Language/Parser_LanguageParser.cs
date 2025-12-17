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
            // Called right after consuming 'state'.
            // Fast path: "state <name> = ..." (no explicit type).
            if (PeekToken(0).Kind == SyntaxKind.IdentifierToken &&
                PeekToken(1).Kind == SyntaxKind.EqualsToken)
            {
                return null;
            }

            // Find the first occurrence of: "<identifier> ="
            // That identifier is considered the state variable name.
            const int MaxLookahead = 128;
            var nameIndex = -1;

            for (var i = 1; i < MaxLookahead; i++)
            {
                var t = PeekToken(i);

                if (t.Kind == SyntaxKind.EndOfFileToken || t.Kind == SyntaxKind.SemicolonToken)
                {
                    break;
                }

                if (t.Kind == SyntaxKind.IdentifierToken && PeekToken(i + 1).Kind == SyntaxKind.EqualsToken)
                {
                    nameIndex = i;
                    break;
                }
            }

            // If we didn't find "<identifier> =", we can't reliably detect a type.
            // If nameIndex == 0 => it would mean empty type (not allowed), so treat as no-type as well.
            if (nameIndex <= 0)
            {
                return null;
            }

            // Build C# type text from tokens [0..nameIndex-1].
            var sb = PooledStringBuilder.GetInstance();
            for (var i = 0; i < nameIndex; i++)
            {
                var token = PeekToken(i);
                sb.Builder.Append(GetTokenTextForCSharp(token));
            }

            var typeText = sb.ToStringAndFree();
            if (typeText.Length == 0)
            {
                return null;
            }

            // Parse the type using Roslyn.
            // consumeFullText: true => we require the whole string to be a type.
            CSharp.TypeSyntax typeSyntax;
            try
            {
                typeSyntax = CSharpFactory.ParseTypeName(typeText, offset: 0, options: null, consumeFullText: true);
            }
            catch
            {
                return null;
            }

            // Consume the type tokens so the caller can read the variable name next.
            for (var i = 0; i < nameIndex; i++)
            {
                EatToken();
            }

            return GreenSyntaxFactory.CSharpRawToken(typeSyntax);

            static string GetTokenTextForCSharp(GreenSyntaxToken token)
            {
                // Identifiers/keywords/literals usually carry Text.
                if (!string.IsNullOrEmpty(token.Text))
                {
                    return token.Text;
                }

                // Punctuation usually doesn't carry Text, so take canonical spelling from SyntaxFacts.
                return SyntaxFacts.GetText(token.Kind) ?? string.Empty;
            }
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
