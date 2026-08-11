using Akbura.Language.Binder;
using Akbura.Language.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        InlineExpressionSyntax inlineExpression,
        int relativePosition)
    {
        if (inlineExpression == null)
        {
            throw new ArgumentNullException(nameof(inlineExpression));
        }

        ValidateSyntaxTreeOwnership(inlineExpression);

        var expression = inlineExpression.GetRawCSharpExpression() ??
            throw new InvalidOperationException(
                "The inline expression could not be parsed as C#.");
        var isMarkup = IsInsideMarkup(inlineExpression);
        var scope = isMarkup
            ? GetMarkupBindingScope(inlineExpression)
            : inlineExpression;
        var binder = BindingSession.GetCSharpProbeBinder(
            scope,
            isMarkup
                ? BinderUsage.Markup
                : BinderUsage.Expression);

        return new CSharpProbeBuilder(binder)
            .CreateExpressionProjection(
                scope,
                expression,
                relativePosition);
    }

    private static bool IsInsideMarkup(AkburaSyntax syntax)
    {
        for (var current = syntax.Parent;
             current != null;
             current = current.Parent)
        {
            if (current is MarkupElementSyntax or MarkupRootSyntax)
            {
                return true;
            }
        }

        return false;
    }
}
