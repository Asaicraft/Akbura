using Akbura.Language.Syntax.Green;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpExpressionSyntax
{
    public CSharp.ExpressionSyntax? GetRawCSharpExpression()
    {
        var tokens = Green.Tokens;

        if (tokens.Any() &&
            tokens[0] is GreenSyntaxToken.CSharpRawToken
            {
                RawNode: CSharp.ExpressionSyntax expression
            })
        {
            return expression;
        }

        try
        {
            return CSharpSyntaxFactory.ParseExpression(tokens.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
