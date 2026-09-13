using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.CSharp;
using Akbura.Language.Symbols;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct UseHookInvocationWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly CSharpSyntaxWriter _syntaxWriter;

    public UseHookInvocationWriter(CodeWriter writer)
    {
        AkburaDebug.Assert(writer != null);

        _writer = writer;
        _valueWriter = new CSharpValueWriter(writer);
        _syntaxWriter = new CSharpSyntaxWriter(writer);
    }

    public void Write(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        ImmutableArray<UseHookStateArgument> stateArguments = default)
    {
        AkburaDebug.Assert(method != null);
        AkburaDebug.Assert(invocation != null);
        Debug.Assert(method.IsStatic);

        _valueWriter.WriteTypeName(method.ContainingType);
        _writer.Write(".");
        _valueWriter.WriteIdentifier(method.Name);

        if (method.IsGenericMethod)
        {
            _writer.Write("<");

            for (var i = 0; i < method.TypeArguments.Length; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                _valueWriter.WriteTypeNameWithNullableAnnotation(method.TypeArguments[i]);
            }

            _writer.Write(">");
        }

        var arguments = invocation.ArgumentList;
        if (!stateArguments.IsDefaultOrEmpty)
        {
            foreach (var substitution in stateArguments)
            {
                var argument = arguments.Arguments[substitution.ArgumentIndex];
                var state = substitution.State;
                var isHook = state.UseHook != null;
                var identity = ComponentHotReloadIdentity.CreateStateKey(
                    state.Name,
                    (ITypeSymbol)state.Type.Symbol!,
                    isHook ? ComponentStateFactoryKind.State : ComponentStateFactoryKind.Value,
                    isHook && !ComponentStatePlan.IsInitializerHook(state.UseHook!.Method));
                var name = ComponentHotReloadIdentity.CreateGeneratedName(state.Name, identity);
                var expression = SyntaxFactory.IdentifierName("__State_" + name)
                    .WithTriviaFrom(argument.Expression);
                arguments = arguments.ReplaceNode(argument, argument.WithExpression(expression));
            }
        }

        _syntaxWriter.WriteArgumentList(arguments);
    }
}
