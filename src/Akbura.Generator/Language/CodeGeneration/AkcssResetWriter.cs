using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct AkcssResetWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly MarkupTargetPropertyWriter _targetPropertyWriter;

    public AkcssResetWriter(CodeWriter writer)
    {
        Debug.Assert(writer != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
        _targetPropertyWriter = new MarkupTargetPropertyWriter(_writer);
    }

    public void WriteMethod(ArrayBuilder<AkcssResetPropertyPlan> properties)
    {
        AkburaDebug.Assert(properties != null);

        if (properties.IsEmpty)
        {
            return;
        }

        WriteMethodStart();

        for (var i = properties.Count - 1; i >= 0; i--)
        {
            WriteProperty(properties[i]);
        }

        WriteMethodEnd();
    }

    public void WriteMethod(in AkcssResetPropertyPlan property)
    {
        if (!property.TargetProperty.IsValid)
        {
            return;
        }

        WriteMethodStart();
        WriteProperty(property);
        WriteMethodEnd();
    }

    private void WriteMethodStart()
    {
        _writer.WriteLine("public override void Reset(object __target)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(__target);");
        _writer.WriteLine("base.Reset(__target);");
    }

    private void WriteMethodEnd()
    {
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteProperty(in AkcssResetPropertyPlan property)
    {
        _writer.Write("if (__target is global::Avalonia.AvaloniaObject");

        if (property.ReceiverType is
            {
                TypeKind: not TypeKind.Error,
                SpecialType: not SpecialType.System_Object,
            } receiverType)
        {
            _writer.Write(" && __target is ");
            _valueWriter.WriteTypeName(receiverType);
        }

        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.Write("((global::Avalonia.AvaloniaObject)__target).ClearValue(");
        _targetPropertyWriter.Write(property.TargetProperty);
        _writer.WriteLine(");");

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }
}
