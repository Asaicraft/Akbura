using Akbura.Language.Binder;
using Akbura.Language.Syntax;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        CSharpExpressionSyntax expressionSyntax,
        int relativePosition)
    {
        if (expressionSyntax == null)
        {
            throw new ArgumentNullException(nameof(expressionSyntax));
        }

        ValidateSyntaxTreeOwnership(expressionSyntax);

        if (!EmbeddedCSharpSyntaxFacts.TryGetExpression(
                expressionSyntax,
                out var expression,
                out _))
        {
            throw new InvalidOperationException(
                "The expression could not be parsed as C#.");
        }

        var isMarkup = IsInsideMarkup(expressionSyntax);
        var scope = isMarkup
            ? GetMarkupBindingScope(expressionSyntax)
            : expressionSyntax;
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

    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        CSharpStatementSyntax statementSyntax,
        int relativePosition)
    {
        if (statementSyntax == null)
        {
            throw new ArgumentNullException(nameof(statementSyntax));
        }

        ValidateSyntaxTreeOwnership(statementSyntax);
        if (!EmbeddedCSharpSyntaxFacts.TryGetStatement(
                statementSyntax,
                out var statement,
                out _))
        {
            throw new InvalidOperationException(
                "The statement could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            statementSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateStatementProjection(
                statementSyntax,
                statement,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        CSharpTypeSyntax typeSyntax,
        int relativePosition)
    {
        if (typeSyntax == null)
        {
            throw new ArgumentNullException(nameof(typeSyntax));
        }

        ValidateSyntaxTreeOwnership(typeSyntax);
        if (!EmbeddedCSharpSyntaxFacts.TryGetType(
                typeSyntax,
                out var type,
                out _))
        {
            throw new InvalidOperationException(
                "The type could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            typeSyntax,
            BinderUsage.Expression);
        var builder = new CSharpProbeBuilder(binder);
        return typeSyntax.Parent is CommandDeclarationSyntax
            ? builder.CreateReturnTypeProjection(
                type,
                relativePosition)
            : builder.CreateTypeProjection(
                type,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        UsingDirectiveSyntax usingSyntax,
        int relativePosition)
    {
        if (usingSyntax == null)
        {
            throw new ArgumentNullException(nameof(usingSyntax));
        }

        ValidateSyntaxTreeOwnership(usingSyntax);
        var binder = BindingSession.GetCSharpProbeBinder(
            usingSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateUsingDirectiveProjection(
                usingSyntax,
                relativePosition);
    }

    internal CSharpProbeProjection CreateCSharpCompletionProjection(
        CSharpParameterListSyntax parameterListSyntax,
        int relativePosition)
    {
        if (parameterListSyntax == null)
        {
            throw new ArgumentNullException(
                nameof(parameterListSyntax));
        }

        ValidateSyntaxTreeOwnership(parameterListSyntax);
        if (parameterListSyntax.Parent is not
                CommandDeclarationSyntax command ||
            !EmbeddedCSharpSyntaxFacts.TryGetParameterList(
                parameterListSyntax,
                out var parameters,
                out _))
        {
            throw new InvalidOperationException(
                "The command parameter list could not be parsed as C#.");
        }

        var binder = BindingSession.GetCSharpProbeBinder(
            parameterListSyntax,
            BinderUsage.Expression);
        return new CSharpProbeBuilder(binder)
            .CreateCommandParameterProjection(
                command,
                parameters,
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
