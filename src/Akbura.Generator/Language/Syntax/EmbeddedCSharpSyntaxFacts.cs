using Microsoft.CodeAnalysis.Text;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

internal static class EmbeddedCSharpSyntaxFacts
{
    public static bool TryGetExpression(
        CSharpExpressionSyntax syntax,
        out CSharp.ExpressionSyntax expression,
        out TextSpan hostSpan)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        expression = syntax.GetRawCSharpExpression()!;
        if (expression == null)
        {
            hostSpan = default;
            return false;
        }

        // Map the Roslyn node's exact span, excluding Akbura trailing trivia
        // while retaining skipped C# trivia used during incomplete input.
        var tokensFullSpan = syntax.Tokens.FullSpan;
        var expressionFullSpan = expression.FullSpan;
        if (expressionFullSpan.End > tokensFullSpan.Length)
        {
            hostSpan = default;
            return false;
        }

        hostSpan = new TextSpan(
            tokensFullSpan.Start + expressionFullSpan.Start,
            expressionFullSpan.Length);
        return true;
    }

    public static bool TryGetStatement(
        CSharpStatementSyntax syntax,
        out CSharp.StatementSyntax statement,
        out TextSpan hostSpan)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        hostSpan = GetStatementHostSpan(syntax);

        try
        {
            statement = CSharpSyntaxFactory.ParseStatement(
                syntax.Body == null
                    ? syntax.Tokens.ToFullString()
                    : syntax.ToFullString());
            return statement.FullSpan.Length == hostSpan.Length;
        }
        catch (ArgumentException)
        {
            statement = null!;
            return false;
        }
    }

    public static TextSpan GetStatementHostSpan(
        CSharpStatementSyntax syntax)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        return syntax.Body == null
            ? syntax.Tokens.FullSpan
            : syntax.FullSpan;
    }

    public static bool TryGetType(
        CSharpTypeSyntax syntax,
        out CSharp.TypeSyntax type,
        out TextSpan hostSpan)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        hostSpan = syntax.Tokens.Span;
        try
        {
            type = syntax.ToCSharp();
            return true;
        }
        catch (ArgumentException)
        {
            type = null!;
            return false;
        }
        catch (InvalidOperationException)
        {
            type = null!;
            return false;
        }
        catch (InvalidCastException)
        {
            type = null!;
            return false;
        }
    }

    public static bool TryGetParameterList(
        CSharpParameterListSyntax syntax,
        out CSharp.ParameterListSyntax parameters,
        out TextSpan hostSpan)
    {
        if (syntax == null)
        {
            throw new ArgumentNullException(nameof(syntax));
        }

        hostSpan = syntax.Parameters.FullSpan;
        try
        {
            parameters = syntax.GetRawCSharpParameterList()!;
            return parameters != null;
        }
        catch (ArgumentException)
        {
            parameters = null!;
            return false;
        }
        catch (InvalidCastException)
        {
            parameters = null!;
            return false;
        }
    }
}
