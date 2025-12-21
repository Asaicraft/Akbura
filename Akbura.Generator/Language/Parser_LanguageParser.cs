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
            SyntaxKind.ParamKeyword => ParseParamDeclarationSyntax(),
            SyntaxKind.InjectKeyword => ParseInjectDeclarationSyntax(),
            SyntaxKind.CommandKeyword => ParseCommandDeclarationSyntax(),
            _ => default!
        };
    }

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

    #endregion

    #region ParamDeclarationSyntax

    internal GreenParamDeclarationSyntax ParseParamDeclarationSyntax()
    {
        var token = EatToken(SyntaxKind.ParamKeyword);

        var bindingToken = FastPeekToken();

        if(bindingToken.Kind != SyntaxKind.BindToken && bindingToken.Kind != SyntaxKind.OutToken)
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
        if(equalsToken.IsMissing)
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

    #region IdentifierNameSyntax

    private GreenIdentifierNameSyntax ParseIdentifierName()
    {
        if(CurrentToken.Kind == SyntaxKind.IdentifierToken)
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
        if(_currentToken != null)
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

    private GreenParameterSyntax ParseParameter()
    {
        var type = ParseCShaprType();
        var name = ParseIdentifierName();

        return GreenSyntaxFactory.ParameterSyntax(type, name);
    }

    #endregion

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
}
