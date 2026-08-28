using System;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpStatementSyntax
{
    public CSharp.StatementSyntax? GetRawCSharpStatement()
    {
        var text = Tokens.ToFullString();

        try
        {
            if (Body == null)
            {
                return CSharpSyntaxFactory.ParseStatement(text);
            }

            var statement = CSharpSyntaxFactory.ParseStatement(text + "{}");
            return statement is CSharp.LocalFunctionStatementSyntax
                ? CSharpSyntaxFactory.ParseStatement(ToFullString())
                : statement;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
