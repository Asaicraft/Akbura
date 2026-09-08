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
        WriteDescriptorFactory(plan);
        _writer.WriteLine();
        WriteDirectPropertyGetter(plan);
        _writer.WriteLine();
        WriteDirectPropertySetter(plan);
        _writer.WriteLine();
        WriteSetter(plan);
        _writer.WriteLine();
        WriteProperty(plan);
    }

    public void WriteHotReloadAssignment(
        in ComponentInjectServicePlan plan,
        string previousManifestExpression)
    {
        Debug.Assert(!string.IsNullOrEmpty(previousManifestExpression));

        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteServiceFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "global::Akbura.HotReload.AkburaHotReloadRuntime.FindProperty<");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDirectPropertyType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(previousManifestExpression);
        _writer.WriteLine(",");
        _writer.WriteStringLiteral(plan.HotReloadKey);
        _writer.WriteLine("));");
        _writer.CurrentIndent -= _writer.TabSize * 4;
    }

    private void WriteBackingField(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private ");
        WriteNullableServiceType(plan);
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteServiceField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(";");
    }

    private void WriteDescriptor(in ComponentInjectServicePlan plan)
    {
        _writer.WriteLine("#if DEBUG");
        WriteDescriptorDeclaration(plan, isReadOnly: false);
        _writer.WriteLine("#else");
        WriteDescriptorDeclaration(plan, isReadOnly: true);
        _writer.WriteLine("#endif");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteServiceFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(null);");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDescriptorDeclaration(
        in ComponentInjectServicePlan plan,
        bool isReadOnly)
    {
        _writer.Write("public static ");

        if (isReadOnly)
        {
            _writer.Write("readonly ");
        }

        _writer.WriteLine("global::Akbura.ComponentTree.InjectService<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        WriteServiceType(plan);
        _writer.WriteLine(">");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDescriptorFactory(in ComponentInjectServicePlan plan)
    {
        _writer.WriteLine("private static global::Akbura.ComponentTree.InjectService<");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        WriteServiceType(plan);
        _writer.WriteLine(">");
        GeneratedMemberNameWriter.WriteServiceFactory(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        WriteDirectPropertyType(plan);
        _writer.WriteLine("? __previous)");
        _writer.CurrentIndent -= _writer.TabSize * 2;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("#if DEBUG");
        WriteDescriptorFactoryReturn(plan, recreate: true);
        _writer.WriteLine("#else");
        WriteDescriptorFactoryReturn(plan, recreate: false);
        _writer.WriteLine("#endif");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteDescriptorFactoryReturn(
        in ComponentInjectServicePlan plan,
        bool recreate)
    {
        _writer.Write("return global::Akbura.ComponentTree.InjectService.");
        _writer.Write(recreate ? "RecreateForHotReload" : "Create");
        _writer.Write("<");
        _writer.Write(_ownerTypeName);
        _writer.WriteLine(",");
        _writer.CurrentIndent += _writer.TabSize;
        WriteServiceType(plan);
        _writer.WriteLine(">(");

        if (recreate)
        {
            _writer.WriteLine("__previous,");
        }

        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");
        GeneratedMemberNameWriter.WriteServiceGetter(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(",");
        GeneratedMemberNameWriter.WriteServiceValueSetter(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(",");
        _writer.Write("isOptional: ");
        _writer.WriteBooleanLiteral(plan.IsOptional);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize;
    }

    private void WriteDirectPropertyGetter(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private static ");
        WriteNullableServiceType(plan);
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteServiceGetter(
            _writer,
            plan.GeneratedName);
        _writer.Write("(");
        _writer.Write(_ownerTypeName);
        _writer.Write(" __owner) => __owner.");
        GeneratedMemberNameWriter.WriteServiceField(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine(";");
    }

    private void WriteDirectPropertySetter(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private static void ");
        GeneratedMemberNameWriter.WriteServiceValueSetter(
            _writer,
            plan.GeneratedName);
        _writer.Write("(");
        _writer.Write(_ownerTypeName);
        _writer.Write(" __owner, ");
        WriteNullableServiceType(plan);
        _writer.Write(" __value) => __owner.");
        GeneratedMemberNameWriter.WriteServiceSetter(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(__value);");
    }

    private void WriteSetter(in ComponentInjectServicePlan plan)
    {
        _writer.Write("private void ");
        GeneratedMemberNameWriter.WriteServiceSetter(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        WriteNullableServiceType(plan);
        _writer.WriteLine(" value)");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("SetAndRaise(");
        WriteDescriptorName(plan.Name);
        _writer.Write(".AvaloniaProperty, ref ");
        GeneratedMemberNameWriter.WriteServiceField(
            _writer,
            plan.GeneratedName);
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
        GeneratedMemberNameWriter.WriteServiceField(
            _writer,
            plan.GeneratedName);

        if (!plan.IsOptional)
        {
            _writer.Write("!");
        }

        _writer.WriteLine(";");
        _writer.Write("set => ");
        GeneratedMemberNameWriter.WriteServiceSetter(
            _writer,
            plan.GeneratedName);
        _writer.WriteLine("(value);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteDirectPropertyType(in ComponentInjectServicePlan plan)
    {
        _writer.Write("global::Avalonia.DirectProperty<");
        _writer.Write(_ownerTypeName);
        _writer.Write(", ");
        WriteNullableServiceType(plan);
        _writer.Write(">");
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
