using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            SyntaxKind.UseEffectKeyword => ParseUseEffectDeclarationSyntax(),
            SyntaxKind.LessThanToken => ParseMarkupRootSyntax(),
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

    #region UseEffectDeclarationSyntax

    internal GreenUseEffectDeclarationSyntax ParseUseEffectDeclarationSyntax()
    {
        var useEffectKeyword = EatToken(SyntaxKind.UseEffectKeyword);

        var arguments = ParseCSharpArgumentList();

        var block = ParseCSharpBlock();

        var tails = _pool.Allocate<GreenUseEffectTailBlockSyntax>();
        try
        {
            // first tail
            if (CurrentToken.Kind is SyntaxKind.CancelKeyword or SyntaxKind.FinallyKeyword)
            {
                var tail = ParseUseEffectTailBlockSyntax();
                tails.Add(tail);
            }

            // second tail
            if (CurrentToken.Kind is SyntaxKind.CancelKeyword or SyntaxKind.FinallyKeyword)
            {
                var tail = ParseUseEffectTailBlockSyntax();
                tails.Add(tail);
            }

            return GreenSyntaxFactory.UseEffectDeclarationSyntax(
                useEffectKeyword,
                arguments,
                block,
                tails.ToList());
        }
        finally
        {
            _pool.Free(tails);
        }
    }

    private GreenUseEffectTailBlockSyntax ParseUseEffectTailBlockSyntax()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.CancelKeyword => ParseEffectCancelBlockSyntax(),
            SyntaxKind.FinallyKeyword => ParseEffectFinallyBlockSyntax(),
            _ => throw new UnreachableException("Unexpected token kind in use effect tail block."),
        };
    }

    private GreenEffectCancelBlockSyntax ParseEffectCancelBlockSyntax()
    {
        var cancelKeyword = EatToken(SyntaxKind.CancelKeyword);
        var body = ParseCSharpBlock();
        return GreenSyntaxFactory.EffectCancelBlockSyntax(cancelKeyword, body);
    }

    private GreenEffectFinallyBlockSyntax ParseEffectFinallyBlockSyntax()
    {
        var finallyKeyword = EatToken(SyntaxKind.FinallyKeyword);
        var body = ParseCSharpBlock();
        return GreenSyntaxFactory.EffectFinallyBlockSyntax(finallyKeyword, body);
    }

    #endregion

    #region MarkupRootSyntax

    private GreenMarkupRootSyntax ParseMarkupRootSyntax()
    {
        throw new NotImplementedException();
    }

    #endregion

    #region MarkupComponentNameSyntax

    internal GreenMarkupComponentNameSyntax ParseMarkupComponentNameSyntax()
    {
        // alias:: ... ?
        GreenMarkupAliasQualifierSyntax? aliasQualifier = null;

        if (CurrentToken.Kind == SyntaxKind.IdentifierToken &&
            PeekToken(1).Kind == SyntaxKind.DoubleColonToken)
        {
            var alias = ParseIdentifierName();
            var doubleColon = EatToken(SyntaxKind.DoubleColonToken);
            aliasQualifier = GreenSyntaxFactory.MarkupAliasQualifierSyntax(alias, doubleColon);
        }

        var firstName = ParseIdentifierName();
        GreenMarkupGenericArgumentListSyntax? firstGenericArgs = null;

        if (CurrentToken.Kind == SyntaxKind.OpenBraceToken)
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

                var segName = ParseIdentifierName();
                GreenMarkupGenericArgumentListSyntax? segGenerics = null;

                if (CurrentToken.Kind == SyntaxKind.OpenBraceToken)
                {
                    segGenerics = ParseMarkupGenericArgumentListSyntax();
                }

                segments.Add(
                    segGenerics is null
                        ? GreenSyntaxFactory.MarkupIdentifierNameSegmentSyntax(segName)
                        : GreenSyntaxFactory.MarkupGenericNameSegmentSyntax(segName, segGenerics)
                );
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

    private GreenMarkupGenericArgumentListSyntax ParseMarkupGenericArgumentListSyntax()
    {
        var open = EatToken(SyntaxKind.OpenBraceToken);

        var list = _pool.AllocateSeparated<GreenCSharpTypeSyntax>();
        try
        {
            if (CurrentToken.Kind != SyntaxKind.CloseBraceToken &&
                CurrentToken.Kind != SyntaxKind.EndOfFileToken)
            {
                list.Add(ParseCShaprType());

                while (CurrentToken.Kind == SyntaxKind.CommaToken)
                {
                    list.AddSeparator(EatToken(SyntaxKind.CommaToken));

                    // recovery: allow trailing comma before }
                    if (CurrentToken.Kind == SyntaxKind.CloseBraceToken ||
                        CurrentToken.Kind == SyntaxKind.EndOfFileToken)
                    {
                        break;
                    }

                    list.Add(ParseCShaprType());
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
        var mode = _mode;

        _mode = Lexer.LexerMode.InExpressionUntilSemicolon;

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
}
