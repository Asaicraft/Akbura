using Akbura.Language.Binder;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Diagnostics;
using System.Globalization;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ComponentCommandBindingWriter
{
    private readonly CodeWriter _writer;

    public ComponentCommandBindingWriter(CodeWriter writer)
    {
        _writer = writer;
    }

    public void WriteValue(in ComponentCommandBindingPlan plan, string targetExpression)
    {
        var syntaxWriter = new CSharpSyntaxWriter(_writer);
        if (plan.IsCommandReference)
        {
            syntaxWriter.WriteExpression(plan.HandlerExpression);
            return;
        }

        var hasResult = plan.ResultMode == MarkupCommandResultMode.ReturnsResult;
        _writer.Write(plan.IsICommandAdapter
            ? "global::Akbura.Commands.AkburaICommandAdapterFactory."
            : "global::Akbura.Commands.AkburaCommandFactory.");
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
        if (plan.IsICommandAdapter)
        {
            _writer.WriteStringLiteral(ComponentHotReloadIdentity.CreateOperationSyntaxIdentity(plan.Syntax));
            _writer.Write(", ");
            WriteCurrentCommand(plan.Destination, targetExpression);
            _writer.Write(", ");
        }

        syntaxWriter.WriteExpression(AdaptHandler(plan, parameterCount));
        _writer.Write(")");
    }

    private void WriteCurrentCommand(in PropertyWritePlan destination, string targetExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        switch (destination.Kind)
        {
            case PropertyWriteKind.ClrProperty:
                if (destination.ClrProperty?.GetMethod == null)
                {
                    _writer.Write(targetExpression);
                    break;
                }

                _writer.Write("(global::System.Windows.Input.ICommand?)");
                _writer.Write("((");
                new CSharpValueWriter(_writer).WriteTypeName(destination.ReceiverType!);
                _writer.Write(")").Write(targetExpression).Write(").");
                new CSharpValueWriter(_writer).WriteIdentifier(destination.ClrProperty!.Name);
                break;

            case PropertyWriteKind.AvaloniaProperty:
                _writer.Write("(global::System.Windows.Input.ICommand?)");
                _writer.Write("((global::Avalonia.AvaloniaObject)").Write(targetExpression).Write(").GetValue(");
                new CSharpValueWriter(_writer).WriteStaticMemberReference(destination.AvaloniaProperty!);
                _writer.Write(")");
                break;

            case PropertyWriteKind.ComponentParameter:
            case PropertyWriteKind.DirectMember:
                _writer.Write("(global::System.Windows.Input.ICommand?)");
                _writer.Write(targetExpression).Write(".");
                new CSharpValueWriter(_writer).WriteIdentifier(destination.MemberName!);
                break;

            default:
                _writer.Write("(global::System.Windows.Input.ICommand?)null");
                break;
        }
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
        var prefix = plan.IsICommandAdapter ? "CreateOrUpdate" : "Create";
        if (plan.AwaitableKind == ComponentCommandAwaitableKind.Task)
        {
            return prefix + (hasResult ? "TaskFunction" : "TaskAction");
        }

        if (plan.IsAsync || plan.AwaitableKind == ComponentCommandAwaitableKind.ValueTask)
        {
            return prefix + (hasResult ? "AsyncFunction" : "AsyncAction");
        }

        return prefix + (hasResult ? "Function" : "Action");
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
