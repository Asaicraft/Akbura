using Akbura.Language.Symbols;
using System.Diagnostics;

namespace Akbura.Language.CodeGeneration;

internal readonly ref struct ParameterWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly CSharpSyntaxWriter _syntaxWriter;
    private readonly SourceMappingWriter _mappings;
    private readonly string _ownerTypeName;

    public ParameterWriter(
        CodeWriter writer,
        ComponentGenerationSourceMap sourceMap,
        string ownerTypeName)
    {
        Debug.Assert(writer != null);
        Debug.Assert(sourceMap != null);
        Debug.Assert(!string.IsNullOrEmpty(ownerTypeName));

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(writer!);
        _syntaxWriter = new CSharpSyntaxWriter(writer!);
        _mappings = new SourceMappingWriter(writer!, sourceMap!);
        _ownerTypeName = ownerTypeName;
    }

    public void Write(in ComponentParameterPlan plan)
    {
        if (plan.Kind == ComponentParameterKind.Collection)
        {
            WriteCollection(plan);
            return;
        }

        WriteValue(plan);
    }

    private void WriteValue(in ComponentParameterPlan plan)
    {
        WriteValueDescriptor(plan);
        _writer.WriteLine();

        if (plan.IsContent)
        {
            _writer.WriteLine("[global::Avalonia.Metadata.Content]");
        }

        WriteValueProperty(plan);

        if (plan.IsContent)
        {
            _writer.WriteLine();
            WriteSingleContentChangedHandler();
        }
    }

    private void WriteValueDescriptor(in ComponentParameterPlan plan)
    {
        _writer.Write("public static readonly global::Akbura.ComponentTree.Parameter<");
        _writer.Write(_ownerTypeName);
        _writer.Write(", ");
        WriteParameterType(plan);
        _writer.Write("> ");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("global::Akbura.ComponentTree.Parameter.Create<");
        _writer.Write(_ownerTypeName);
        _writer.Write(", ");
        WriteParameterType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");
        WriteOptionalValue(plan);
        WriteBinding(plan.BindingKind);

        if (plan.IsContent)
        {
            _writer.WriteLine(",");
            _writer.WriteLine("changed: static (__owner, __change) =>");
            _writer.CurrentIndent += _writer.TabSize;
            _writer.WriteLine("__owner.__OnContentChanged(__change));");
            _writer.CurrentIndent -= _writer.TabSize;
        }
        else
        {
            _writer.WriteLine(");");
        }

        _writer.CurrentIndent -= _writer.TabSize * 2;
    }

    private void WriteOptionalValue(in ComponentParameterPlan plan)
    {
        if (!plan.HasDefaultValue)
        {
            _writer.WriteLine("default,");
            return;
        }

        _writer.Write("new global::Avalonia.Data.Optional<");
        WriteParameterType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;

        var sourceSyntax = plan.Syntax.DefaultValue;
        Debug.Assert(sourceSyntax != null);

        if (plan.DefaultValue != null && sourceSyntax != null)
        {
            using var mapping = _mappings.WriteStart(
                sourceSyntax,
                plan.DefaultValue.Span);

            _syntaxWriter.WriteExpression(plan.DefaultValue);
        }
        else
        {
            // Error-tolerant fallback. The semantic diagnostic
            // is already attached to the .akbura source.
            _writer.Write("default!");
        }

        _writer.WriteLine();
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("),");
    }

    private void WriteBinding(ParamBindingKind bindingKind)
    {
        _writer.Write("global::Akbura.ComponentTree.ParameterBinding.");
        _writer.Write(bindingKind switch
        {
            ParamBindingKind.Bind => "Bind",
            ParamBindingKind.Out => "Out",
            _ => "In",
        });
    }

    private void WriteValueProperty(in ComponentParameterPlan plan)
    {
        _writer.Write("public ");
        WriteParameterType(plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("get => GetValue(");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(".AvaloniaProperty);");
        _writer.Write("set => SetValue(");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(".AvaloniaProperty, value);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteSingleContentChangedHandler()
    {
        _writer.WriteLine(
            "private void __OnContentChanged(" +
            "global::Avalonia.AvaloniaPropertyChangedEventArgs __change)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "if (__change.OldValue is " +
            "global::Avalonia.Controls.Control __oldContent)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("LogicalChildren.Remove(__oldContent);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.WriteLine(
            "if (__change.NewValue is " +
            "global::Avalonia.Controls.Control __newContent)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("LogicalChildren.Add(__newContent);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteCollection(in ComponentParameterPlan plan)
    {
        WriteCollectionFields(plan);
        _writer.WriteLine();
        WriteCollectionDescriptor(plan);
        _writer.WriteLine();

        if (plan.IsContent)
        {
            _writer.WriteLine("[global::Avalonia.Metadata.Content]");
        }

        WriteCollectionProperty(plan);
        _writer.WriteLine();
        WriteCollectionAddMethod(plan);

        if (!plan.IsContent)
        {
            return;
        }

        if (plan.Collection.ObservesChanges)
        {
            _writer.WriteLine();
            WriteCollectionChangedHandler(plan);
        }

        _writer.WriteLine();
        WriteLogicalChildrenSynchronizer(plan);
    }

    private void WriteCollectionFields(in ComponentParameterPlan plan)
    {
        _writer.Write("private readonly ");
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Collection.BackingType);
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.Id);
        _writer.WriteLine(" = [];");

        if (plan.IsContent && plan.Collection.ObservesChanges)
        {
            _writer.Write("private bool ");
            GeneratedMemberNameWriter.WriteCollectionSubscribedField(_writer, plan.Id);
            _writer.WriteLine(";");
        }

        if (plan.IsContent)
        {
            _writer.Write(
                "private readonly global::System.Collections.Generic.List<" +
                "global::Avalonia.Controls.Control> ");
            GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(_writer, plan.Id);
            _writer.WriteLine(" = [];");
        }
    }

    private void WriteCollectionDescriptor(in ComponentParameterPlan plan)
    {
        _writer.Write("public static readonly global::Akbura.ComponentTree.ReadOnlyParameter<");
        _writer.Write(_ownerTypeName);
        _writer.Write(", ");
        WriteCollectionPropertyType(plan);
        _writer.Write("> ");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(" =");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("global::Akbura.ComponentTree.Parameter.CreateReadOnly<");
        _writer.Write(_ownerTypeName);
        _writer.Write(", ");
        WriteCollectionPropertyType(plan);
        _writer.WriteLine(">(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteStringLiteral(plan.Name);
        _writer.WriteLine(",");
        _writer.Write("static __owner => __owner.");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine(");");
        _writer.CurrentIndent -= _writer.TabSize * 2;
    }

    private void WriteCollectionProperty(in ComponentParameterPlan plan)
    {
        _writer.Write("public ");
        WriteCollectionPropertyType(plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);

        if (!plan.IsContent || !plan.Collection.ObservesChanges)
        {
            _writer.Write(" => ");
            GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.Id);
            _writer.WriteLine(";");
            return;
        }

        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("get");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("if (!");
        GeneratedMemberNameWriter.WriteCollectionSubscribedField(_writer, plan.Id);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.Id);
        _writer.Write(".CollectionChanged += ");
        GeneratedMemberNameWriter.WriteCollectionChangedMethod(_writer, plan.Id);
        _writer.WriteLine(";");
        GeneratedMemberNameWriter.WriteCollectionSubscribedField(_writer, plan.Id);
        _writer.WriteLine(" = true;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine();
        _writer.Write("return ");
        GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.Id);
        _writer.WriteLine(";");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteCollectionAddMethod(in ComponentParameterPlan plan)
    {
        _writer.WriteLine(
            "[global::System.ComponentModel.EditorBrowsable(" +
            "global::System.ComponentModel.EditorBrowsableState.Never)]");
        _writer.WriteLine("[global::System.ComponentModel.Browsable(false)]");
        _writer.Write("public void ");

        GeneratedMemberNameWriter.WriteCollectionAddMethod(_writer, plan.Name);

        _writer.Write("(");
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Collection.ElementType);
        _writer.WriteLine(" __value)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine(".Add(__value);");

        if (plan.IsContent && !plan.Collection.ObservesChanges)
        {
            GeneratedMemberNameWriter.WriteCollectionSynchronizeMethod(_writer, plan.Id);
            _writer.WriteLine("();");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteCollectionChangedHandler(in ComponentParameterPlan plan)
    {
        _writer.Write("private void ");
        GeneratedMemberNameWriter.WriteCollectionChangedMethod(_writer, plan.Id);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("object? __sender,");
        _writer.WriteLine(
            "global::System.Collections.Specialized.NotifyCollectionChangedEventArgs __event)");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("switch (__event.Action)");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("case global::System.Collections.Specialized.NotifyCollectionChangedAction.Add:");
        _writer.WriteLine("case global::System.Collections.Specialized.NotifyCollectionChangedAction.Move:");
        _writer.WriteLine("case global::System.Collections.Specialized.NotifyCollectionChangedAction.Remove:");
        _writer.WriteLine("case global::System.Collections.Specialized.NotifyCollectionChangedAction.Replace:");
        _writer.CurrentIndent += _writer.TabSize;
        GeneratedMemberNameWriter.WriteCollectionSynchronizeMethod(_writer, plan.Id);
        _writer.WriteLine("();");
        _writer.WriteLine("break;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("case global::System.Collections.Specialized.NotifyCollectionChangedAction.Reset:");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "throw new global::System.NotSupportedException(" +
            "\"Resetting component content is not supported.\");");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("default:");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "throw new global::System.ArgumentOutOfRangeException(nameof(__event));");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteLogicalChildrenSynchronizer(in ComponentParameterPlan plan)
    {
        _writer.Write("private void ");
        GeneratedMemberNameWriter.WriteCollectionSynchronizeMethod(_writer, plan.Id);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("foreach (var __oldContent in ");
        GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(_writer, plan.Id);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("LogicalChildren.Remove(__oldContent);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(_writer, plan.Id);
        _writer.WriteLine(".Clear();");
        _writer.Write("foreach (var __item in ");
        GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.Id);
        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(
            "if (__item is global::Avalonia.Controls.Control __contentControl &&");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("!");
        GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(_writer, plan.Id);
        _writer.WriteLine(".Contains(__contentControl))");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("LogicalChildren.Add(__contentControl);");
        GeneratedMemberNameWriter.WriteCollectionLogicalChildrenField(_writer, plan.Id);
        _writer.WriteLine(".Add(__contentControl);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteParameterType(in ComponentParameterPlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Type);
    }

    private void WriteCollectionPropertyType(in ComponentParameterPlan plan)
    {
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Collection.PropertyType);
    }

    private void WriteDescriptorName(string name)
    {
        _valueWriter.WriteIdentifier(name);
        _writer.Write("Property");
    }
}
