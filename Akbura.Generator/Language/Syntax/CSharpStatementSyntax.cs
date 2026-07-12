using System;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpStatementSyntax
{
    public CSharp.StatementSyntax? GetRawCSharpStatement()
    {
        var text = Tokens.ToFullString();
        if (Body != null)
        {
            text += "{}";
        }

        try
        {
            return CSharpSyntaxFactory.ParseStatement(text);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
