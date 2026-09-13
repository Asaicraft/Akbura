using Akbura.Language.Binder;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Globalization;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentCommandBindingWriter
{
    private readonly CodeWriter _writer;

    public ComponentCommandBindingWriter(CodeWriter writer)
    {
        _writer = writer;
    }

    public void WriteValue(in ComponentCommandBindingPlan plan)
    {
        var syntaxWriter = new CSharpSyntaxWriter(_writer);
        if (plan.IsCommandReference)
        {
            syntaxWriter.WriteExpression(plan.HandlerExpression);
            return;
        }

        var hasResult = plan.ResultMode == MarkupCommandResultMode.ReturnsResult;
        _writer.Write("global::Akbura.Commands.AkburaCommandFactory.");
        _writer.Write(GetFactoryMethod(plan, hasResult));
        var values = new CSharpValueWriter(_writer);
        var parameterCount = plan.ParameterTypes.IsDefault ? 0 : plan.ParameterTypes.Length;
        _writer.Write("<");
        for (var i = 0; i < parameterCount; i++)
        {
            if (i != 0)
            {
                _writer.Write(", ");
            }

            values.WriteTypeNameWithNullableAnnotation(plan.ParameterTypes[i]);
        }

        if (parameterCount != 0)
        {
            _writer.Write(", ");
        }

        if (plan.ResultType == null || plan.ResultType.SpecialType == Microsoft.CodeAnalysis.SpecialType.System_Void)
        {
            _writer.Write("object");
        }
        else
        {
            values.WriteTypeNameWithNullableAnnotation(plan.ResultType);
        }

        _writer.Write(">");
        _writer.Write("(");
        syntaxWriter.WriteExpression(AdaptHandler(plan, parameterCount));
        _writer.Write(")");
    }

    private static ExpressionSyntax AdaptHandler(in ComponentCommandBindingPlan plan, int parameterCount)
    {
        var expression = plan.HandlerExpression;
        if (plan.HandlerKind == MarkupCommandHandlerKind.Expression)
        {
            expression = SyntaxFactory.ParenthesizedLambdaExpression()
                .WithParameterList(CreateIgnoredParameters(parameterCount))
                .WithExpressionBody(expression);
        }
        else if (plan.ArgumentMode == MarkupCommandArgumentMode.IgnoresCommandArgument && parameterCount != 0)
        {
            expression = expression switch
            {
                ParenthesizedLambdaExpressionSyntax lambda => lambda.WithParameterList(CreateIgnoredParameters(parameterCount)),
                AnonymousMethodExpressionSyntax method => method.WithParameterList(CreateIgnoredParameters(parameterCount)),
                _ => SyntaxFactory.ParenthesizedLambdaExpression()
                    .WithParameterList(CreateIgnoredParameters(parameterCount))
                    .WithExpressionBody(SyntaxFactory.InvocationExpression(expression)),
            };
        }

        if (plan.IsAsync && plan.ContainsAwait)
        {
            expression = expression switch
            {
                LambdaExpressionSyntax lambda => lambda.WithAsyncKeyword(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space)),
                AnonymousMethodExpressionSyntax method => method.WithAsyncKeyword(SyntaxFactory.Token(SyntaxKind.AsyncKeyword)
                    .WithTrailingTrivia(SyntaxFactory.Space)),
                _ => expression,
            };
        }

        return expression;
    }

    private static string GetFactoryMethod(in ComponentCommandBindingPlan plan, bool hasResult)
    {
        if (plan.AwaitableKind == ComponentCommandAwaitableKind.Task)
        {
            return hasResult ? "CreateTaskFunction" : "CreateTaskAction";
        }

        if (plan.IsAsync || plan.AwaitableKind == ComponentCommandAwaitableKind.ValueTask)
        {
            return hasResult ? "CreateAsyncFunction" : "CreateAsyncAction";
        }

        return hasResult ? "CreateFunction" : "CreateAction";
    }

    private static ParameterListSyntax CreateIgnoredParameters(int count)
    {
        var parameters = new ParameterSyntax[count];
        for (var i = 0; i < count; i++)
        {
            parameters[i] = SyntaxFactory.Parameter(SyntaxFactory.Identifier(
                "__commandIgnored" + i.ToString(CultureInfo.InvariantCulture)));
        }

        return SyntaxFactory.ParameterList(SyntaxFactory.SeparatedList(parameters));
    }
}
