using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
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

    public void Write(IMethodSymbol method, InvocationExpressionSyntax invocation)
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

        _syntaxWriter.WriteArgumentList(invocation.ArgumentList);
    }
}
