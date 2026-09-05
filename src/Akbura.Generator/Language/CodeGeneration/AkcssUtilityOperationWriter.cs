using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Diagnostics;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes independently resolved property operations for one generated utility.
/// </summary>
internal ref struct AkcssUtilityOperationWriter
{
    private const string RuntimeOperationType = "global::Akbura.Akcss.AkcssUtilityOperation";
    private const string RuntimePriorityType = "global::Akbura.CompilerAnotations.AkcssOperationPriority";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly MarkupTargetPropertyWriter _targetPropertyWriter;
    private readonly AkcssSourceMappingWriter _sourceMappingWriter;
    private readonly AkcssResetWriter _resetWriter;
    private readonly PooledImmutableList<AkcssOperationMetadataPlan> _metadata;
    private readonly PooledImmutableList<AkcssUtilityOperationPlan> _operations;
    private readonly ArrayBuilder<int> _conditionOrders;

    private AkcssRuntimeOperationWriter _runtimeWriter;

    public AkcssUtilityOperationWriter(
        CodeWriter writer,
        AkcssGenerationSourceMap sourceMap,
        in AkcssUtilityPlan plan,
        ArrayBuilder<AkcssIdentifierValue> identifierValues,
        PooledHashSet<IAkcssSymbol> expansionPath,
        ArrayBuilder<int> conditionOrders)
    {
        Debug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);
        AkburaDebug.Assert(identifierValues != null);
        AkburaDebug.Assert(expansionPath != null);
        AkburaDebug.Assert(conditionOrders != null);

        _writer = writer!;
        _valueWriter = new CSharpValueWriter(_writer);
        _targetPropertyWriter = new MarkupTargetPropertyWriter(_writer);
        _sourceMappingWriter = new AkcssSourceMappingWriter(_writer, sourceMap);
        _resetWriter = new AkcssResetWriter(_writer);
        _metadata = plan.Metadata;
        _operations = plan.Operations;
        _conditionOrders = conditionOrders;
        _runtimeWriter = new AkcssRuntimeOperationWriter(
            _writer,
            sourceMap,
            identifierValues,
            expansionPath);
    }

    public void Write(int styleIndex)
    {
        for (var i = 0; i < _operations.Length; i++)
        {
            if (i > 0)
            {
                _writer.WriteLine();
            }

            WriteOperation(styleIndex, i, _operations[i]);
        }
    }

    private void WriteOperation(
        int styleIndex,
        int operationIndex,
        in AkcssUtilityOperationPlan operation)
    {
        Debug.Assert((uint)operation.MetadataIndex < (uint)_metadata.Length);

        ref readonly var metadata = ref _metadata.ItemRef(operation.MetadataIndex);
        var setter = metadata.Setter;

        Debug.Assert(metadata.Kind == GeneratedAkcssOperationKind.Set);
        Debug.Assert(setter != null);

        if (setter?.Property is not { CanWrite: true } property)
        {
            return;
        }

        var hasAvaloniaTarget = TryGetAvaloniaPropertyTarget(property, out var targetProperty);

        var value = AkcssExpressionGenerator.RewriteGeneratedExpression(
            metadata.Expression,
            "__target",
            observeDynamicResource: hasAvaloniaTarget);

        CollectConditionOrders(metadata);

        _writer.Write("private sealed class ");
        AkcssGeneratedNameWriter.WriteUtilityOperationTypeName(_writer, operationIndex);
        _writer.Write(" : ");
        _writer.WriteLine(RuntimeOperationType);

        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        WriteConstructor(styleIndex, operationIndex, operation);

        _writer.WriteLine();
        WriteIsActive(metadata, property, value);

        _writer.WriteLine();
        WriteUpdate(setter, value);

        if (hasAvaloniaTarget)
        {
            _writer.WriteLine();
            WriteApply(setter, property, targetProperty, value);

            _writer.WriteLine();

            var resetProperty = new AkcssResetPropertyPlan(
                targetProperty,
                AkcssExpressionGenerator.GetPropertyReceiverType(property));

            _resetWriter.WriteMethod(resetProperty);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteConstructor(
        int styleIndex,
        int operationIndex,
        in AkcssUtilityOperationPlan operation)
    {
        _writer.Write("public ");
        AkcssGeneratedNameWriter.WriteUtilityOperationTypeName(_writer, operationIndex);
        _writer.Write("(");
        AkcssGeneratedNameWriter.WriteStyleTypeName(_writer, styleIndex);
        _writer.WriteLine(" utility)");

        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine(": base(");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine("utility,");

        _writer.WriteStringLiteral(operation.ConflictKey);
        _writer.WriteLine(",");

        _writer.Write(RuntimePriorityType);
        _writer.Write(".");
        _writer.Write(GetPriorityName(_metadata[operation.MetadataIndex].Priority));
        _writer.WriteLine(",");

        _writer.WriteIntegerLiteral(operation.Order);
        _writer.WriteLine(")");

        _writer.CurrentIndent -= _writer.TabSize * 2;

        _writer.WriteLine("{");
        _writer.WriteLine("}");
    }

    private void WriteIsActive(
        in AkcssOperationMetadataPlan metadata,
        AkburaPropertySymbol property,
        in AkcssGeneratedValue value)
    {
        _writer.WriteLine("public override bool IsActive(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("object __target,");
        _writer.WriteLine("global::System.Collections.Generic.IReadOnlyList<object?> __arguments)");
        _writer.CurrentIndent -= _writer.TabSize;

        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.Write("return ");

        var hasTerm = false;

        WriteTypeTerm(metadata.TargetType, ref hasTerm);
        WriteTypeTerm(AkcssExpressionGenerator.GetPropertyReceiverType(property), ref hasTerm);

        if (value.RequiresResourceHost)
        {
            WriteResourceHostTerm(ref hasTerm);
        }

        for (var i = _conditionOrders.Count - 1; i >= 0; i--)
        {
            ref readonly var condition = ref _metadata.ItemRef(_conditionOrders[i]);

            if (condition.HasErrors || string.IsNullOrWhiteSpace(condition.Expression))
            {
                WriteBooleanTerm(value: false, ref hasTerm);
                continue;
            }

            WriteTypeTerm(condition.TargetType, ref hasTerm);

            var generatedCondition = AkcssExpressionGenerator.RewriteGeneratedExpression(
                condition.Expression,
                "__target",
                observeDynamicResource: false);

            if (generatedCondition.RequiresResourceHost)
            {
                WriteResourceHostTerm(ref hasTerm);
            }

            WriteExpressionTerm(generatedCondition.Expression, ref hasTerm);
        }

        if (!hasTerm)
        {
            _writer.Write("true");
        }

        _writer.WriteLine(";");

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteUpdate(
        IAkcssPropertySetterOperation setter,
        in AkcssGeneratedValue value)
    {
        _writer.WriteLine("public override void Update(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("object __target,");
        _writer.WriteLine("global::System.Collections.Generic.IReadOnlyList<object?> __arguments)");
        _writer.CurrentIndent -= _writer.TabSize;

        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(__target);");
        _runtimeWriter.WriteSetter(setter, "__target", value);

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteApply(
        IAkcssPropertySetterOperation setter,
        AkburaPropertySymbol property,
        in MarkupTargetPropertyPlan targetProperty,
        in AkcssGeneratedValue value)
    {
        _writer.WriteLine("public override global::System.IDisposable Apply(");
        _writer.CurrentIndent += _writer.TabSize;
        _writer.WriteLine("object __target,");
        _writer.WriteLine("global::System.Collections.Generic.IReadOnlyList<object?> __arguments,");
        _writer.WriteLine("global::Avalonia.Data.BindingPriority __bindingPriority)");
        _writer.CurrentIndent -= _writer.TabSize;

        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        _writer.WriteLine("global::System.ArgumentNullException.ThrowIfNull(__target);");

        if (value.DynamicResource is { } dynamicResource)
        {
            WriteDynamicResourceContribution(
                setter,
                targetProperty,
                dynamicResource,
                value.Expression);
        }
        else
        {
            WriteValueContribution(setter, targetProperty, value.Expression);
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void WriteValueContribution(
        IAkcssPropertySetterOperation setter,
        in MarkupTargetPropertyPlan targetProperty,
        string valueExpression)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteLine("return ((global::Avalonia.AvaloniaObject)__target).SetValue(");
        _writer.CurrentIndent = indent + _writer.TabSize;

        _targetPropertyWriter.Write(targetProperty);
        _writer.WriteLine(",");

        var mapping = _sourceMappingWriter.WriteStart(setter.Syntax?.Expression);

        try
        {
            _writer.Write(valueExpression);
            _writer.WriteLine(",");
        }
        finally
        {
            mapping.Dispose();
        }

        _writer.CurrentIndent = indent + _writer.TabSize;
        _writer.WriteLine("__bindingPriority) ??");

        _writer.CurrentIndent = indent + _writer.TabSize;
        _writer.WriteLine(
            "throw new global::System.InvalidOperationException(" +
            "\"Avalonia did not return a reversible AKCSS utility contribution.\");");

        _writer.CurrentIndent = indent;
    }

    private void WriteDynamicResourceContribution(
        IAkcssPropertySetterOperation setter,
        in MarkupTargetPropertyPlan targetProperty,
        in AkcssDynamicResourceBinding dynamicResource,
        string valueExpression)
    {
        var indent = _writer.CurrentIndent;

        _writer.WriteLine("return ((global::Avalonia.AvaloniaObject)__target).Bind(");
        _writer.CurrentIndent = indent + _writer.TabSize;

        _targetPropertyWriter.Write(targetProperty);
        _writer.WriteLine(",");

        _writer.WriteLine(
            "global::Avalonia.Controls.ResourceNodeExtensions.GetResourceObservable(");

        _writer.CurrentIndent = indent + _writer.TabSize * 2;
        _writer.WriteLine("(global::Avalonia.Controls.IResourceHost)__target,");

        _writer.Write(dynamicResource.KeyExpression);
        _writer.WriteLine(",");

        _writer.Write("converter: ");
        _writer.Write(dynamicResource.ValueParameterName);
        _writer.WriteLine(" =>");

        _writer.CurrentIndent = indent + _writer.TabSize * 3;

        _writer.Write("global::System.Object.ReferenceEquals(");
        _writer.Write(dynamicResource.ValueParameterName);
        _writer.WriteLine(", global::Avalonia.AvaloniaProperty.UnsetValue)");

        _writer.WriteLine("? global::Avalonia.AvaloniaProperty.UnsetValue");
        _writer.WriteLine(": (object?)(");

        _writer.CurrentIndent = indent + _writer.TabSize * 4;

        var mapping = _sourceMappingWriter.WriteStart(setter.Syntax?.Expression);

        try
        {
            _writer.Write(valueExpression);
            _writer.WriteLine(")),");
        }
        finally
        {
            mapping.Dispose();
        }

        _writer.CurrentIndent = indent + _writer.TabSize;
        _writer.WriteLine("__bindingPriority);");

        _writer.CurrentIndent = indent;
    }

    private void CollectConditionOrders(in AkcssOperationMetadataPlan operation)
    {
        _conditionOrders.Count = 0;

        var parentOrder = operation.ParentOrder;

        while ((uint)parentOrder < (uint)_metadata.Length)
        {
            ref readonly var parent = ref _metadata.ItemRef(parentOrder);

            Debug.Assert(parent.Order == parentOrder);

            if (parent.Kind == GeneratedAkcssOperationKind.If)
            {
                _conditionOrders.Add(parentOrder);
            }

            if (parent.ParentOrder == parentOrder)
            {
                Debug.Fail("An AKCSS metadata operation cannot contain itself.");
                break;
            }

            parentOrder = parent.ParentOrder;
        }
    }

    private void WriteTypeTerm(ITypeSymbol? type, ref bool hasTerm)
    {
        if (type is null or
            {
                TypeKind: TypeKind.Error,
            } or
            {
                SpecialType: SpecialType.System_Object,
            })
        {
            return;
        }

        WriteAnd(ref hasTerm);

        _writer.Write("__target is ");
        _valueWriter.WriteTypeName(type);
    }

    private void WriteResourceHostTerm(ref bool hasTerm)
    {
        WriteAnd(ref hasTerm);
        _writer.Write("__target is global::Avalonia.Controls.IResourceHost");
    }

    private void WriteBooleanTerm(bool value, ref bool hasTerm)
    {
        WriteAnd(ref hasTerm);
        _writer.WriteBooleanLiteral(value);
    }

    private void WriteExpressionTerm(string expression, ref bool hasTerm)
    {
        WriteAnd(ref hasTerm);

        _writer.Write("(");
        _writer.Write(expression);
        _writer.Write(")");
    }

    private void WriteAnd(ref bool hasTerm)
    {
        if (hasTerm)
        {
            _writer.Write(" && ");
        }

        hasTerm = true;
    }

    private static bool TryGetAvaloniaPropertyTarget(
        AkburaPropertySymbol property,
        out MarkupTargetPropertyPlan targetProperty)
    {
        var symbol =
            GetStaticMember(property.WriteDefinition.Symbol) ??
            GetStaticMember(property.AvaloniaPropertyDefinition.Symbol) ??
            GetStaticMember(property.AttachedPropertyDefinition.Symbol);

        if (symbol == null)
        {
            targetProperty = default;
            return false;
        }

        targetProperty = MarkupTargetPropertyPlan.CreateStaticMember(symbol);
        return true;
    }

    private static RoslynSymbol? GetStaticMember(RoslynSymbol? symbol)
    {
        return symbol is
            RoslynFieldSymbol { IsStatic: true } or
            RoslynPropertySymbol { IsStatic: true }
                ? symbol
                : null;
    }

    private static string GetPriorityName(GeneratedAkcssOperationPriority priority)
    {
        return priority switch
        {
            GeneratedAkcssOperationPriority.Style => "Style",
            GeneratedAkcssOperationPriority.StyleTrigger => "StyleTrigger",
            _ => "Style",
        };
    }
}
