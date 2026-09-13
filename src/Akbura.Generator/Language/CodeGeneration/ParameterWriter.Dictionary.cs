using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal readonly ref partial struct ParameterWriter
{
    private void WriteDictionary(in ComponentParameterPlan plan)
    {
        _writer.Write("private ");
        _valueWriter.WriteTypeNameWithNullableAnnotation(
            plan.Dictionary.BackingType.WithNullableAnnotation(NullableAnnotation.Annotated));
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.GeneratedName);
        _writer.WriteLine(";");
        _writer.WriteLine();
        WriteDictionaryBackingGetter(plan);
        _writer.WriteLine();
        WriteCollectionDescriptor(plan);
        _writer.WriteLine();
        WriteCollectionDescriptorFactory(plan);
        _writer.WriteLine();
        WriteCollectionGetter(plan);
        _writer.WriteLine();
        _writer.WriteLine("[global::Avalonia.Metadata.Content]");
        WriteCollectionProperty(plan);
    }

    private void WriteDictionaryBackingGetter(in ComponentParameterPlan plan)
    {
        _writer.WriteHiddenApiAttributes();
        _writer.Write("private ");
        _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Dictionary.BackingType);
        _writer.Write(" ");
        GeneratedMemberNameWriter.WriteCollectionBackingGetter(_writer, plan.GeneratedName);
        _writer.WriteLine("()");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        if (!plan.Dictionary.CanCreateBacking)
        {
            _writer.Write("throw new global::System.InvalidOperationException(");
            _writer.WriteStringLiteral(
                "Dictionary content parameter '" + plan.Name +
                "' requires a default value initializer for its declared dictionary type.");
            _writer.WriteLine(");");
        }
        else
        {
            _writer.Write("return ");
            GeneratedMemberNameWriter.WriteCollectionField(_writer, plan.GeneratedName);
            _writer.Write(" ??= ");

            if (plan.HasDefaultValue && plan.DefaultValue != null && plan.Syntax.DefaultValue != null)
            {
                using var mapping = _mappings.WriteStart(
                    plan.Syntax.DefaultValue,
                    plan.DefaultValue.Span);
                _syntaxWriter.WriteExpression(plan.DefaultValue);
            }
            else if (plan.Dictionary.UsesStandardDictionaryFactory)
            {
                _writer.Write("global::Akbura.HotReload.AkburaRenderDictionaryReconciler<");
                _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Dictionary.Shape.KeyType!);
                _writer.Write(", ");
                _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Dictionary.Shape.ValueType!);
                _writer.Write(">.CreateDictionary()");
            }
            else
            {
                _writer.Write("new ");
                _valueWriter.WriteTypeNameWithNullableAnnotation(plan.Dictionary.BackingType);
                _writer.Write("()");
            }

            _writer.WriteLine(";");
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }
}
