using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes a CLR property read directly to the generated source.
/// </summary>
internal readonly ref struct PropertyReadWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;

    public PropertyReadWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
    }

    public void Write(
        IPropertySymbol property,
        string targetExpression)
    {
        Debug.Assert(property != null);
        Debug.Assert(!string.IsNullOrEmpty(targetExpression));

        if (property == null ||
            property.IsStatic ||
            property.ContainingType == null ||
            string.IsNullOrEmpty(targetExpression))
        {
            Debug.Fail("An invalid CLR property read reached code generation.");
            _writer.Write("default");
            return;
        }

        _writer.Write("((");
        _valueWriter.WriteTypeName(property.ContainingType);
        _writer.Write(")").Write(targetExpression).Write(").");
        _valueWriter.WriteIdentifier(property.Name);
    }
}
