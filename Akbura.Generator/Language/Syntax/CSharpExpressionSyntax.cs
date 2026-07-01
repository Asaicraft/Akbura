using Akbura.Language.Syntax.Green;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpExpressionSyntax
{
    public CSharp.ExpressionSyntax? GetRawCSharpExpression()
    {
        var tokens = Green.Tokens;

        if (tokens.Any() && tokens[0]!.Kind == SyntaxKind.CSharpRawToken)
        {
            var token = tokens[0];

            Debug.Assert(tokens[0] is GreenSyntaxToken.CSharpRawToken);

            var rawNode = Unsafe.As<GreenSyntaxToken.CSharpRawToken>(token).RawNode;

            Debug.Assert(rawNode is CSharp.ExpressionSyntax);

            return Unsafe.As<CSharp.ExpressionSyntax>(rawNode);
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
