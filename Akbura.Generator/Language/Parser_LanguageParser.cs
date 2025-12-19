using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Text;
using static Akbura.Language.Syntax.Green.GreenSyntaxToken;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

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

        var equalsToken = EatOrReturn(SyntaxKind.EqualsToken);

        var initializer = ParseStateInitializer();

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.StateDeclarationSyntax(stateKeyword, type, name, equalsToken, initializer, semicolonToken);

        CSharpRawToken? EatOrNullCSharpTypeSyntax()
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

            if(fastToken.Kind == SyntaxKind.EqualsToken)
            {
                // if the next token is '=', then it's a identifier used as a name, not a type
                ReturnToken();
                return null;
            }

            if(fastToken.Kind == SyntaxKind.IdentifierToken)
            {
                // if the next token is an identifier, then it's definitely a type
                return (CSharpRawToken)token;
            }

            ReturnToken();
            return null;
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
