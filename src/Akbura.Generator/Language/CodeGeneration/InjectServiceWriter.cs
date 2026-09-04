using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct InjectServiceWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly string _ownerTypeName;

    public InjectServiceWriter(
        CodeWriter writer,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _ownerTypeName = ownerTypeName;
    }

    public void Write(in ComponentInjectServicePlan plan)
    {
        WriteBackingField(plan);
        _writer.WriteLine();
        WriteDescriptor(plan);
        _writer.WriteLine();
        WriteSetter(plan);
        _writer.WriteLine();
        WriteProperty(plan);
    }

    private void WriteBackingField(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private ");
        WriteNullableServiceType(plan);
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteServiceField(_writer, plan.Id);
        _writer.WriteLine(";");
    }

    private void WriteDescriptor(in ComponentInjectServicePlan plan)
    {
        _writer.WriteLine("public static readonly");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Akbura.ComponentTree.InjectService<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        WriteServiceType(plan);
        _writer.WriteLine(">");
        _writer.CurrentIndent -= _writer.TabSize;
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Akbura.ComponentTree.InjectService.Create<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        WriteServiceType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");
        _writer.Write("static __owner => __owner.");
        GeneratedMemberNameWriter.WriteServiceField(_writer, plan.Id);
        _writer.WriteLine(",");
        _writer.WriteLine("static (__owner, __value) =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("__owner.");
        GeneratedMemberNameWriter.WriteServiceSetter(_writer, plan.Id);
        _writer.WriteLine("(__value),");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.Write("isOptional: ");
        _writer.WriteBooleanLiteral(plan.IsOptional);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 4;
    }

    private void WriteSetter(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private void ");
        GeneratedMemberNameWriter.WriteServiceSetter(_writer, plan.Id);
        _writer.Write("(");
        _writer.WriteLine();
        _writer.CurrentIndent += _writer.TabSize;
        WriteNullableServiceType(plan);
        _writer.WriteLine(" value)");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("SetAndRaise(");
        WriteDescriptorName(plan.Name);
        _writer.Write(".AvaloniaProperty, ref ");
        GeneratedMemberNameWriter.WriteServiceField(_writer, plan.Id);
        _writer.WriteLine(", value);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteProperty(in ComponentInjectServicePlan plan)
    {
        _writer.Write("public ");

        if (plan.IsOptional)
        {
            WriteNullableServiceType(plan);
        }
        else
        {
            WriteServiceType(plan);
        }

        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("get => ");
        GeneratedMemberNameWriter.WriteServiceField(_writer, plan.Id);

        if (!plan.IsOptional)
        {
            _writer.Write("!");
        }

        _writer.WriteLine(";");
        _writer.Write("set => ");
        GeneratedMemberNameWriter.WriteServiceSetter(_writer, plan.Id);
        _writer.WriteLine("(value);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteServiceType(in ComponentInjectServicePlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.ServiceType);
    }

    private void WriteNullableServiceType(in ComponentInjectServicePlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(
            plan.ServiceType.WithNullableAnnotation(
                Microsoft.CodeAnalysis.NullableAnnotation.Annotated));
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }
}
