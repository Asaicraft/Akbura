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
        WriteDescriptorFactory(plan);
        _writer.WriteLine();
        WriteProperty(memberPlan, plan);
    }

    public void WriteHotReloadAssignment(
        in ComponentCommandPlan plan,
        string previousManifestExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(previousManifestExpression));

        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteCommandFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.FindProperty<");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorType();
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(previousManifestExpression);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(plan.HotReloadKey);
        _writer.WriteLine("));");
        _writer.CurrentIndent -= _writer.TabSize * 4;
    }

    private void WriteDescriptor(in ComponentCommandPlan plan)
    {
        _writer.WriteLine("#if DEBUG");
        WriteDescriptorDeclaration(plan, isReadOnly: false);
        _writer.WriteLine("#else");
        WriteDescriptorDeclaration(plan, isReadOnly: true);
        _writer.WriteLine("#endif");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteCommandFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(null);");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDescriptorDeclaration(
        in ComponentCommandPlan plan,
        bool isReadOnly)
    {
        _writer.Write("public static ");

        if (isReadOnly)
        {
            _writer.Write("readonly ");
        }

        WriteDescriptorType();
        _writer.Write(" ");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
    }

    private void WriteDescriptorFactory(in ComponentCommandPlan plan)
    {
        _writer.Write("private static ");
        WriteDescriptorType();
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteCommandFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDescriptorType();
        _writer.WriteLine("? __previous)");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("#if DEBUG");
        _writer.WriteLine("if (__previous != null)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("return __previous;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine("#endif");
        _writer.WriteLine();
        _writer.WriteLine("return global::Avalonia.AvaloniaProperty.Register<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        _writer.WriteLine("global::Akbura.IAkburaCommand>(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
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

    private void WriteDescriptorType()
    {
        _writer.Write(
            "global::Avalonia.StyledProperty<" +
            "global::Akbura.IAkburaCommand>");
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }
}
