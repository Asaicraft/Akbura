using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Akbura.Pools;
using System;
using System.Collections.Generic;
using System.Text;

namespace Akbura.Language;

partial class Parser
{
    public GreenAkburaDocumentSyntax ParseCompilationUnit()
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

    private GreenAkTopLevelMemberSyntax ParseTopLevelMember()
    {
        return CurrentToken.Kind switch
        {
            SyntaxKind.StateKeyword => ParseStateDeclaration(),
            _ => ParseCshaprStatement(),
        };
    }

    private GreenStateDeclarationSyntax ParseStateDeclaration()
    {
        var stateKeyword = EatToken(SyntaxKind.StateKeyword);

        var typeSyntax = EatOrNullCSharpTypeSyntax();

        GreenCSharpTypeSyntax? type = null;

        if (typeSyntax != null)
        {
            type = GreenSyntaxFactory.CSharpTypeSyntax(typeSyntax);
        }

        var name = EatToken(SyntaxKind.IdentifierToken);

        var equalsToken = EatToken(SyntaxKind.EqualsToken);

        var initializer = ParseStateInitializer();

        var semicolonToken = EatToken(SyntaxKind.SemicolonToken);

        return GreenSyntaxFactory.StateDeclarationSyntax(stateKeyword, type, name, equalsToken, initializer, semicolonToken);
    }
}
