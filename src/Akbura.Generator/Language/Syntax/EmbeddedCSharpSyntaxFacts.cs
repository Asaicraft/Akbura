using Microsoft.CodeAnalysis;
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

        if (syntax.Parent is MarkupExpressionBindingSyntax binding)
        {
            // The markup lexer owns separator trivia after `bind`; an empty
            // Roslyn node can also omit whitespace retained by the raw token.
            // Project that exact region without reparsing the expression or
            // including the directive/outer braces in its C# host span.
            var start = binding.BindKeyword.Span.End;
            var end = binding.CloseBrace.Span.Start;
            var rawStart = tokensFullSpan.Start + expressionFullSpan.Start;
            var rawEnd = rawStart + expressionFullSpan.Length;
            if (rawStart < start || rawEnd > end)
            {
                hostSpan = default;
                return false;
            }

            var text = binding.ToFullString();
            var prefix = text.Substring(start - binding.FullSpan.Start, rawStart - start);
            var suffix = text.Substring(rawEnd - binding.FullSpan.Start, end - rawEnd);
            expression = expressionFullSpan.Length == 0
                ? expression.WithLeadingTrivia(CSharpSyntaxFactory.ParseLeadingTrivia(prefix + suffix))
                : expression.WithLeadingTrivia(CSharpSyntaxFactory.ParseLeadingTrivia(prefix)
                    .AddRange(expression.GetLeadingTrivia()))
                    .WithTrailingTrivia(expression.GetTrailingTrivia()
                        .AddRange(CSharpSyntaxFactory.ParseTrailingTrivia(suffix)));
            hostSpan = TextSpan.FromBounds(start, end);
            return expression.FullSpan.Length == hostSpan.Length;
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
