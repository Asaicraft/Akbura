using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkcssResetPropertyPlan
{
    public AkcssResetPropertyPlan(
        MarkupTargetPropertyPlan targetProperty,
        ITypeSymbol? receiverType)
    {
        TargetProperty = targetProperty;
        ReceiverType = receiverType;
    }

    public MarkupTargetPropertyPlan TargetProperty { get; }

    public ITypeSymbol? ReceiverType { get; }
}

/// <summary>
/// Writes executable AKCSS operations directly into a generated runtime style.
/// </summary>
internal ref struct AkcssRuntimeOperationWriter
{
    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly PropertyWriter _propertyWriter;
    private readonly MarkupTargetPropertyWriter _targetPropertyWriter;
    private readonly AkcssSourceMappingWriter _sourceMappingWriter;
    private readonly AkcssGenerationSourceMap _sourceMap;
    private readonly ArrayBuilder<AkcssIdentifierValue> _identifierValues;
    private readonly PooledHashSet<IAkcssSymbol> _expansionPath;

    public AkcssRuntimeOperationWriter(
        CodeWriter writer,
        AkcssGenerationSourceMap sourceMap,
        ArrayBuilder<AkcssIdentifierValue> identifierValues,
        PooledHashSet<IAkcssSymbol> expansionPath)
    {
        AkburaDebug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);
        AkburaDebug.Assert(identifierValues != null);
        AkburaDebug.Assert(expansionPath != null);

        _writer = writer;
        _valueWriter = new CSharpValueWriter(writer);
        _propertyWriter = new PropertyWriter(writer);
        _targetPropertyWriter = new MarkupTargetPropertyWriter(writer);
        _sourceMappingWriter = new AkcssSourceMappingWriter(writer, sourceMap);
        _sourceMap = sourceMap;
        _identifierValues = identifierValues;
        _expansionPath = expansionPath;
    }

    public void Write(IAkcssSymbol symbol, string targetExpression)
    {
        AkburaDebug.Assert(_expansionPath.Count == 0);
        AkburaDebug.Assert(!string.IsNullOrEmpty(targetExpression));

        var previousValueCount = _identifierValues.Count;

        _expansionPath.Add(_sourceMap.GetGenerationSymbol(symbol));

        try
        {
            WriteOperations(symbol.Operations, targetExpression);
        }
        finally
        {
            _identifierValues.Count = previousValueCount;
            _expansionPath.Clear();
        }
    }

    public void CollectResetProperties(
        IAkcssSymbol symbol,
        ArrayBuilder<AkcssResetPropertyPlan> properties)
    {
        AkburaDebug.Assert(_expansionPath.Count == 0);
        AkburaDebug.Assert(properties != null);

        _expansionPath.Add(_sourceMap.GetGenerationSymbol(symbol));

        try
        {
            CollectResetProperties(symbol.Operations, properties);
        }
        finally
        {
            _expansionPath.Clear();
        }
    }

    private void WriteOperations(
        ImmutableArray<IAkcssOperation> operations,
        string targetExpression)
    {
        for (var i = 0; i < operations.Length; i++)
        {
            switch (operations[i])
            {
                case IAkcssPropertySetterOperation setter:
                    WritePropertySetter(setter, targetExpression);
                    break;

                case IAkcssIfOperation ifOperation:
                    WriteIfOperation(ifOperation, targetExpression);
                    break;

                case IAkcssApplyOperation applyOperation:
                    WriteApplyOperation(applyOperation, targetExpression);
                    break;

                case IAkcssInterceptOperation:
                    break;
            }
        }
    }

    private void WritePropertySetter(
        IAkcssPropertySetterOperation operation,
        string targetExpression)
    {
        var property = operation.Property;

        if (operation.HasErrors ||
            property == null ||
            !property.CanWrite)
        {
            return;
        }

        var writePlan = PropertyWritePlan.Create(property);

        if (writePlan.Kind != PropertyWriteKind.ClrProperty &&
            writePlan.Kind != PropertyWriteKind.AvaloniaProperty &&
            writePlan.Kind != PropertyWriteKind.AttachedAccessor)
        {
            return;
        }

        var hasAvaloniaTarget = TryGetAvaloniaPropertyTarget(
            property,
            out var avaloniaTarget);

        var value = AkcssExpressionGenerator.GetValueExpression(
            operation,
            targetExpression,
            observeDynamicResource: hasAvaloniaTarget,
            _identifierValues,
            _identifierValues.Count);

        var receiverType = AkcssExpressionGenerator.GetPropertyReceiverType(property);
        var hasGuard = WriteTargetGuardStart(
            targetExpression,
            receiverType,
            value.RequiresResourceHost);

        try
        {
            if (value.DynamicResource is { } dynamicResource &&
                hasAvaloniaTarget)
            {
                WriteDynamicResourceBinding(
                    avaloniaTarget,
                    dynamicResource,
                    targetExpression,
                    value.Expression,
                    operation.Syntax?.Expression);

                return;
            }

            WritePropertyAssignment(
                writePlan,
                targetExpression,
                value.Expression,
                operation.Syntax?.Expression);
        }
        finally
        {
            WriteTargetGuardEnd(hasGuard);
        }
    }

    private void WritePropertyAssignment(
        in PropertyWritePlan plan,
        string targetExpression,
        string valueExpression,
        AkburaSyntax? sourceSyntax)
    {
        var end = _propertyWriter.WriteStart(plan, targetExpression);

        if (end == PropertyWriteEnd.None)
        {
            return;
        }

        _writer.WriteLine();

        var mapping = _sourceMappingWriter.WriteStart(sourceSyntax);

        try
        {
            _writer.Write(valueExpression);
            _propertyWriter.WriteEnd(end);
            _writer.WriteLine();
        }
        finally
        {
            mapping.Dispose();
        }
    }

    private void WriteDynamicResourceBinding(
        in MarkupTargetPropertyPlan targetProperty,
        in AkcssDynamicResourceBinding dynamicResource,
        string targetExpression,
        string valueExpression,
        AkburaSyntax? sourceSyntax)
    {
        var indent = _writer.CurrentIndent;

        try
        {
            _writer.WriteLine("TrackSubscription(");
            _writer.CurrentIndent = indent + _writer.TabSize;

            _writer.Write(targetExpression);
            _writer.WriteLine(",");

            _writer.Write("((global::Avalonia.AvaloniaObject)");
            _writer.Write(targetExpression);
            _writer.WriteLine(").Bind(");
            _writer.CurrentIndent = indent + _writer.TabSize * 2;

            _targetPropertyWriter.Write(targetProperty);
            _writer.WriteLine(",");

            _writer.WriteLine(
                "global::Avalonia.Controls.ResourceNodeExtensions.GetResourceObservable(");

            _writer.CurrentIndent = indent + _writer.TabSize * 3;
            _writer.Write("(global::Avalonia.Controls.IResourceHost)");
            _writer.Write(targetExpression);
            _writer.WriteLine(",");

            _writer.Write(dynamicResource.KeyExpression);
            _writer.WriteLine(",");

            _writer.Write("converter: ");
            _writer.Write(dynamicResource.ValueParameterName);
            _writer.WriteLine(" =>");

            _writer.CurrentIndent = indent + _writer.TabSize * 4;
            _writer.Write("global::System.Object.ReferenceEquals(");
            _writer.Write(dynamicResource.ValueParameterName);
            _writer.WriteLine(", global::Avalonia.AvaloniaProperty.UnsetValue)");

            _writer.WriteLine(
                "? global::Avalonia.AvaloniaProperty.UnsetValue");

            _writer.WriteLine(": (object?)(");
            _writer.CurrentIndent = indent + _writer.TabSize * 5;

            var mapping = _sourceMappingWriter.WriteStart(sourceSyntax);

            try
            {
                _writer.Write(valueExpression);
                _writer.WriteLine();
            }
            finally
            {
                mapping.Dispose();
            }

            _writer.CurrentIndent = indent;
            _writer.WriteLine("))));");
        }
        finally
        {
            _writer.CurrentIndent = indent;
        }
    }

    private void WriteIfOperation(
        IAkcssIfOperation operation,
        string targetExpression)
    {
        if (operation.HasErrors)
        {
            return;
        }

        var condition = AkcssExpressionGenerator.GetIfConditionExpression(
            operation,
            targetExpression,
            _identifierValues,
            _identifierValues.Count);

        var indent = _writer.CurrentIndent;

        _writer.WriteLine("if (");
        _writer.CurrentIndent = indent + _writer.TabSize;

        var mapping = _sourceMappingWriter.WriteStart(operation.Syntax?.Condition);

        try
        {
            _writer.Write(condition);
            _writer.WriteLine(")");
        }
        finally
        {
            mapping.Dispose();
        }

        _writer.CurrentIndent = indent;
        _writer.WriteLine("{");
        _writer.CurrentIndent = indent + _writer.TabSize;

        WriteOperations(operation.Operations, targetExpression);

        _writer.CurrentIndent = indent;
        _writer.WriteLine("}");
    }

    private void WriteApplyOperation(
        IAkcssApplyOperation operation,
        string targetExpression)
    {
        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            WriteOperations(metadataApply.ExpandedOperations, targetExpression);
            return;
        }

        for (var i = 0; i < operation.AppliedSymbols.Length; i++)
        {
            var symbol = _sourceMap.GetGenerationSymbol(operation.AppliedSymbols[i]);

            if (!_expansionPath.Add(symbol))
            {
                continue;
            }

            var previousValueCount = _identifierValues.Count;

            if (symbol is ITailwindUtilitySymbol { Parameters.Length: > 0 } utility)
            {
                var item = i < operation.Items.Length
                    ? operation.Items[i]
                    : string.Empty;

                if (!AkcssExpressionGenerator.TryPushApplyParameterValues(
                        item,
                        utility,
                        operation,
                        targetExpression,
                        _identifierValues,
                        out previousValueCount))
                {
                    _expansionPath.Remove(symbol);
                    continue;
                }
            }

            try
            {
                WriteOperations(symbol.Operations, targetExpression);
            }
            finally
            {
                _identifierValues.Count = previousValueCount;
                _expansionPath.Remove(symbol);
            }
        }
    }

    private bool WriteTargetGuardStart(
        string targetExpression,
        ITypeSymbol? receiverType,
        bool requiresResourceHost)
    {
        var requiresReceiverType =
            receiverType is { SpecialType: not SpecialType.System_Object };

        if (!requiresReceiverType && !requiresResourceHost)
        {
            return false;
        }

        _writer.Write("if (");

        if (requiresReceiverType)
        {
            _writer.Write(targetExpression);
            _writer.Write(" is ");
            _valueWriter.WriteTypeName(receiverType);
        }

        if (requiresReceiverType && requiresResourceHost)
        {
            _writer.Write(" && ");
        }

        if (requiresResourceHost)
        {
            _writer.Write(targetExpression);
            _writer.Write(" is global::Avalonia.Controls.IResourceHost");
        }

        _writer.WriteLine(")");
        _writer.WriteLine("{");
        _writer.CurrentIndent += _writer.TabSize;

        return true;
    }

    private void WriteTargetGuardEnd(bool hasGuard)
    {
        if (!hasGuard)
        {
            return;
        }

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine("}");
    }

    private void CollectResetProperties(
        ImmutableArray<IAkcssOperation> operations,
        ArrayBuilder<AkcssResetPropertyPlan> properties)
    {
        for (var i = 0; i < operations.Length; i++)
        {
            switch (operations[i])
            {
                case IAkcssPropertySetterOperation setter:
                    AddResetProperty(setter, properties);
                    break;

                case IAkcssIfOperation ifOperation:
                    CollectResetProperties(ifOperation.Operations, properties);
                    break;

                case IAkcssApplyOperation applyOperation:
                    CollectApplyResetProperties(applyOperation, properties);
                    break;
            }
        }
    }

    private void CollectApplyResetProperties(
        IAkcssApplyOperation operation,
        ArrayBuilder<AkcssResetPropertyPlan> properties)
    {
        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            CollectResetProperties(metadataApply.ExpandedOperations, properties);
            return;
        }

        for (var i = 0; i < operation.AppliedSymbols.Length; i++)
        {
            var symbol = _sourceMap.GetGenerationSymbol(operation.AppliedSymbols[i]);

            if (!_expansionPath.Add(symbol))
            {
                continue;
            }

            try
            {
                CollectResetProperties(symbol.Operations, properties);
            }
            finally
            {
                _expansionPath.Remove(symbol);
            }
        }
    }

    private static void AddResetProperty(
        IAkcssPropertySetterOperation operation,
        ArrayBuilder<AkcssResetPropertyPlan> properties)
    {
        var property = operation.Property;

        if (operation.HasErrors ||
            property == null ||
            !property.CanWrite ||
            !TryGetAvaloniaPropertyTarget(property, out var targetProperty))
        {
            return;
        }

        for (var i = 0; i < properties.Count; i++)
        {
            if (AreSameTarget(properties[i].TargetProperty, targetProperty))
            {
                return;
            }
        }

        properties.Add(new AkcssResetPropertyPlan(
            targetProperty,
            AkcssExpressionGenerator.GetPropertyReceiverType(property)));
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

    private static bool AreSameTarget(
        in MarkupTargetPropertyPlan left,
        in MarkupTargetPropertyPlan right)
    {
        return left.Kind == right.Kind &&
            SymbolEqualityComparer.Default.Equals(left.Symbol, right.Symbol) &&
            StringComparer.Ordinal.Equals(left.Text, right.Text);
    }
}
