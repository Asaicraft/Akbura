using Akbura.Pools;
using System;
using System.Diagnostics;
using CSharpExpressionSyntax = Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssIdentifierValue
{
    public AkcssIdentifierValue(string name, CSharpExpressionSyntax expression)
    {
        Debug.Assert(!string.IsNullOrEmpty(name));
        AkburaDebug.Assert(expression != null);

        Name = name;
        Expression = expression;
    }

    public string Name { get; }

    public CSharpExpressionSyntax Expression { get; }
}

internal static class AkcssIdentifierValueLookup
{
    public static bool TryGet(
        ArrayBuilder<AkcssIdentifierValue>? values,
        int count,
        string name,
        out CSharpExpressionSyntax expression)
    {
        if (values != null)
        {
            Debug.Assert((uint)count <= (uint)values.Count);

            for (var i = count - 1; i >= 0; i--)
            {
                var value = values[i];

                if (StringComparer.Ordinal.Equals(value.Name, name))
                {
                    expression = value.Expression;
                    return true;
                }
            }
        }

        expression = null!;
        return false;
    }

    public static void Add(
        ArrayBuilder<AkcssIdentifierValue> values,
        string name,
        CSharpExpressionSyntax expression)
    {
        AkburaDebug.Assert(values != null);
        AkburaDebug.Assert(expression != null);

        if (!string.IsNullOrEmpty(name))
        {
            values.Add(new AkcssIdentifierValue(name, expression));
        }
    }
}
