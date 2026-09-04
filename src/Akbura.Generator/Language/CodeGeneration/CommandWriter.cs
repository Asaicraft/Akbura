using Microsoft.CodeAnalysis;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct CommandWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly string _ownerTypeName;

    public CommandWriter(
        CodeWriter writer,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _ownerTypeName = ownerTypeName;
    }

    public void Write(
        in ComponentMemberPlan memberPlan,
        in ComponentCommandPlan plan)
    {
        Debug.Assert(plan.Parameters.Start >= 0);
        Debug.Assert(plan.Parameters.Length >= 0);
        Debug.Assert(
            plan.Parameters.Start <=
            memberPlan.CommandParameters.Length - plan.Parameters.Length);

        WriteDescriptor(plan);
        _writer.WriteLine();
        WriteProperty(memberPlan, plan);
    }

    private void WriteDescriptor(in ComponentCommandPlan plan)
    {
        _writer.WriteLine("public static readonly");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Avalonia.StyledProperty<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Akbura.IAkburaCommand>");
        _writer.CurrentIndent -= _writer.TabSize;
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("global::Avalonia.AvaloniaProperty.Register<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        _writer.WriteLine("global::Akbura.IAkburaCommand>(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 4;
    }

    private void WriteProperty(
        in ComponentMemberPlan memberPlan,
        in ComponentCommandPlan plan)
    {
        _writer.Write("public ");
        WriteCommandType(memberPlan, plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("get =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("(");
        WriteCommandType(memberPlan, plan);
        _writer.WriteLine(")");
        _writer.Write("GetValue(");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(")!;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine();
        _writer.WriteLine("set =>");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("SetValue(");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(",");
        _writer.WriteLine("value);");
        _writer.CurrentIndent -= _writer.TabSize * 3;
        _writer.WriteLine("}");
    }

    private void WriteCommandType(
        in ComponentMemberPlan memberPlan,
        in ComponentCommandPlan plan)
    {
        _writer.Write("global::Akbura.IAkburaCommand");

        if (plan.Parameters.IsEmpty)
        {
            return;
        }

        _writer.Write("<");

        for (var i = 0; i < plan.Parameters.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            ref readonly var parameter = ref memberPlan.CommandParameters.ItemRef(
                plan.Parameters.Start + i);
            _valueWriter.WriteTypeNameWithNullableAnnotation(parameter.Type);
        }

        _writer.Write(", ");

        if (plan.ResultType.SpecialType == SpecialType.System_Void)
        {
            _writer.Write("object");
        }
        else
        {
            _valueWriter.WriteTypeNameWithNullableAnnotation(plan.ResultType);
        }

        _writer.Write(">");
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }
}
