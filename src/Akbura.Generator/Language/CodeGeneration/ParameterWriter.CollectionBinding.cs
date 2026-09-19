namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ParameterWriter
{
    private void WriteCollectionBindingName(in ComponentParameterPlan plan, bool getter)
    {
        _writer.Write(getter ? "__GetCollectionBinding_" : "__collectionBinding_");
        _writer.Write(plan.GeneratedName);
    }

    private void WriteCollectionBindingType(in ComponentParameterPlan plan)
    {
        _writer.Write("global::Akbura.Collections.CollectionParameterBinding<");
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Collection.ElementType);
        _writer.Write(">");
    }

    private void WriteCollectionBindingMembers(in ComponentParameterPlan plan)
    {
        _writer.Write("private ");
        WriteCollectionBindingType(plan);
        _writer.Write("? ");
        WriteCollectionBindingName(plan, getter: false);
        _writer.WriteLine(";");
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private ");
        WriteCollectionBindingType(plan);
        _writer.Write(" ");
        WriteCollectionBindingName(plan, getter: true);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.Write("if (");
        WriteCollectionBindingName(plan, getter: false);
        _writer.WriteLine(" is { } __existing) return __existing;");
        _writer.Write("var __binding = CreateCollectionParameterBinding(");
        GeneratedMemberNameWriter.WriteCollectionBackingGetter(_writer, plan.GeneratedName);
        _writer.Write("(), ");
        WriteDescriptorName(plan.Name);
        _writer.WriteLine(".AvaloniaProperty);");
        WriteCollectionBindingName(plan, getter: false);
        _writer.WriteLine(" = __binding;");
        if (plan.HasDefaultValue && plan.DefaultValue != null && plan.Syntax.DefaultValue != null)
        {
            _writer.Write("__binding.SetSource((");
            WriteCollectionPropertyType(plan);
            _writer.WriteLine(")(");
            _writer.CurrentIndent += _writer.TabSize;
            using (var mapping = _mappings.WriteStart(plan.Syntax.DefaultValue, plan.DefaultValue.Span))
            {
                _syntaxWriter.WriteExpression(plan.DefaultValue);
                _writer.WriteLine();
            }
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("));");
        }
        _writer.WriteLine("return __binding;");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteObservableCollectionProperty(in ComponentParameterPlan plan)
    {
        _writer.Write("public ");
        WriteCollectionPropertyType(plan);
        _writer.Write(" ");
        _valueWriter.WriteIdentifier(plan.Name);
        _writer.WriteLine();
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("get");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        if (plan.IsContent)
        {
            _writer.Write("if (!");
            GeneratedMemberNameWriter.WriteCollectionSubscribedField(_writer, plan.GeneratedName);
            _writer.WriteLine(")");
            _writer.WriteLine("{");
            _writer.CurrentIndent += _writer.TabSize;
            GeneratedMemberNameWriter.WriteCollectionBackingGetter(_writer, plan.GeneratedName);
            _writer.Write("().CollectionChanged += ");
            GeneratedMemberNameWriter.WriteCollectionChangedMethod(_writer, plan.GeneratedName);
            _writer.WriteLine(";");
            GeneratedMemberNameWriter.WriteCollectionSubscribedField(_writer, plan.GeneratedName);
            _writer.WriteLine(" = true;");
            _writer.CurrentIndent -= _writer.TabSize;
            _writer.WriteLine("}");
        }
        // Initialize synchronization/defaults, but return the concrete backing
        // rather than the holder's ObservableCollection<T>-typed Items property.
        _writer.Write("_ = ");
        WriteCollectionBindingName(plan, getter: true);
        _writer.WriteLine("();");
        _writer.Write("return ");
        GeneratedMemberNameWriter.WriteCollectionBackingGetter(_writer, plan.GeneratedName);
        _writer.WriteLine("();");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.WriteLine("set");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;
        if (plan.IsContent)
        {
            _writer.Write("_ = ");
            _valueWriter.WriteIdentifier(plan.Name);
            _writer.WriteLine(";");
        }
        WriteCollectionBindingName(plan, getter: true);
        _writer.WriteLine("().SetSource(value);");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }
}
