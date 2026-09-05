using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;
using System;
using System.Collections.Immutable;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Writes observed-property and flattened AKCSS operation metadata.
/// </summary>
internal readonly ref struct AkcssOperationMetadataWriter
{
    private const string ObservesPropertyAttribute =
        "global::Akbura.CompilerAnotations.ObservesPropertyAttribute";

    private const string OperationAttribute =
        "global::Akbura.CompilerAnotations.AkcssOperationAttribute";

    private const string RuntimeOperationKind =
        "global::Akbura.CompilerAnotations.AkcssOperationKind";

    private const string RuntimeOperationOrigin =
        "global::Akbura.CompilerAnotations.AkcssOperationOriginKind";

    private const string RuntimePropertyAccessKind =
        "global::Akbura.CompilerAnotations.AkcssPropertyAccessKind";

    private const string RuntimePropertyValueKind =
        "global::Akbura.CompilerAnotations.AkcssPropertyValueKind";

    private const string RuntimeOperationPriority =
        "global::Akbura.CompilerAnotations.AkcssOperationPriority";

    private readonly CodeWriter _writer;
    private readonly CSharpValueWriter _valueWriter;
    private readonly AkcssGenerationSourceMap _sourceMap;

    public AkcssOperationMetadataWriter(
        CodeWriter writer,
        AkcssGenerationSourceMap sourceMap)
    {
        AkburaDebug.Assert(writer != null);
        AkburaDebug.Assert(sourceMap != null);

        _writer = writer;
        _valueWriter = new CSharpValueWriter(writer);
        _sourceMap = sourceMap;
    }

    public void Write(IAkcssSymbol symbol)
    {
        var metadata = ArrayBuilder<AkcssOperationMetadataPlan>.GetInstance();
        var identifierValues = ArrayBuilder<AkcssIdentifierValue>.GetInstance();
        var propertyNames = ArrayBuilder<string>.GetInstance();
        var propertyNameSet = PooledHashSet<string>.GetInstance();
        var expansionPath = PooledHashSet<IAkcssSymbol>.GetInstance();

        try
        {
            WriteObservedPropertyAttributes(
                symbol,
                propertyNames,
                propertyNameSet,
                expansionPath);

            expansionPath.Clear();

            var planner = new AkcssOperationMetadataPlanner(
                metadata,
                identifierValues,
                _sourceMap);

            planner.Build(symbol, expansionPath);

            for (var i = 0; i < metadata.Count; i++)
            {
                WriteOperationAttribute(metadata[i]);
            }
        }
        finally
        {
            expansionPath.Free();
            propertyNameSet.Free();
            propertyNames.Free();
            identifierValues.Free();
            metadata.Free();
        }
    }

    public void WriteObservedPropertyAttributes(IAkcssSymbol symbol)
    {
        var propertyNames = ArrayBuilder<string>.GetInstance();
        var propertyNameSet = PooledHashSet<string>.GetInstance();
        var expansionPath = PooledHashSet<IAkcssSymbol>.GetInstance();

        try
        {
            WriteObservedPropertyAttributes(
                symbol,
                propertyNames,
                propertyNameSet,
                expansionPath);
        }
        finally
        {
            expansionPath.Free();
            propertyNameSet.Free();
            propertyNames.Free();
        }
    }

    private void WriteObservedPropertyAttributes(
        IAkcssSymbol symbol,
        ArrayBuilder<string> propertyNames,
        PooledHashSet<string> propertyNameSet,
        PooledHashSet<IAkcssSymbol> expansionPath)
    {
        AkburaDebug.Assert(propertyNames.Count == 0);
        AkburaDebug.Assert(propertyNameSet.Count == 0);
        AkburaDebug.Assert(expansionPath.Count == 0);

        expansionPath.Add(_sourceMap.GetGenerationSymbol(symbol));

        CollectObservedPropertyNames(
            symbol.Operations,
            propertyNames,
            propertyNameSet,
            expansionPath);

        propertyNames.Sort(StringComparer.Ordinal);

        for (var i = 0; i < propertyNames.Count; i++)
        {
            _writer.Write("[");
            _writer.Write(ObservesPropertyAttribute);
            _writer.Write("(");
            _writer.WriteStringLiteral(propertyNames[i]);
            _writer.WriteLine(")]");
        }
    }

    private void WriteOperationAttribute(in AkcssOperationMetadataPlan metadata)
    {
        var argumentCount = GetArgumentCount(metadata);

        _writer.Write("[");
        _writer.Write(OperationAttribute);
        _writer.WriteLine("(");
        _writer.CurrentIndent += _writer.TabSize;

        WriteIntegerArgument("Order", metadata.Order, ref argumentCount);

        WriteEnumArgument(
            "Kind",
            RuntimeOperationKind,
            GetOperationKindName(metadata.Kind),
            ref argumentCount);

        WriteEnumArgument(
            "Origin",
            RuntimeOperationOrigin,
            GetOperationOriginName(metadata.Origin),
            ref argumentCount);

        WriteEnumArgument(
            "PropertyAccessKind",
            RuntimePropertyAccessKind,
            GetPropertyAccessKindName(metadata.PropertyAccessKind),
            ref argumentCount);

        WriteEnumArgument(
            "ValueKind",
            RuntimePropertyValueKind,
            GetPropertyValueKindName(metadata.ValueKind),
            ref argumentCount);

        WriteEnumArgument(
            "Priority",
            RuntimeOperationPriority,
            GetOperationPriorityName(metadata.Priority),
            ref argumentCount);

        if (metadata.ParentOrder >= 0)
        {
            WriteIntegerArgument("ParentOrder", metadata.ParentOrder, ref argumentCount);
        }

        if (metadata.Depth != 0)
        {
            WriteIntegerArgument("Depth", metadata.Depth, ref argumentCount);
        }

        if (metadata.IfStartOrder >= 0)
        {
            WriteIntegerArgument("IfStartOrder", metadata.IfStartOrder, ref argumentCount);
        }

        if (metadata.IfEndOrder >= 0)
        {
            WriteIntegerArgument("IfEndOrder", metadata.IfEndOrder, ref argumentCount);
        }

        if (metadata.TargetType != null)
        {
            WriteTypeArgument("TargetType", metadata.TargetType, ref argumentCount);
        }

        if (metadata.Property != null)
        {
            WriteStringArgument("Property", metadata.Property, ref argumentCount);
        }

        if (metadata.AvaloniaProperty != null)
        {
            WriteStringArgument(
                "AvaloniaProperty",
                metadata.AvaloniaProperty,
                ref argumentCount);
        }

        if (metadata.AttachedGetter != null)
        {
            WriteStringArgument("AttachedGetter", metadata.AttachedGetter, ref argumentCount);
        }

        if (metadata.AttachedSetter != null)
        {
            WriteStringArgument("AttachedSetter", metadata.AttachedSetter, ref argumentCount);
        }

        if (metadata.PropertyOwnerType != null)
        {
            WriteTypeArgument(
                "PropertyOwnerType",
                metadata.PropertyOwnerType,
                ref argumentCount);
        }

        if (metadata.PropertyType != null)
        {
            WriteTypeArgument("PropertyType", metadata.PropertyType, ref argumentCount);
        }

        if (metadata.AttachedTargetType != null)
        {
            WriteTypeArgument(
                "AttachedTargetType",
                metadata.AttachedTargetType,
                ref argumentCount);
        }

        if (metadata.Kind == GeneratedAkcssOperationKind.Set)
        {
            WriteBooleanArgument("CanRead", metadata.CanRead, ref argumentCount);
            WriteBooleanArgument("CanWrite", metadata.CanWrite, ref argumentCount);
        }

        if (metadata.Expression != null)
        {
            WriteStringArgument("Expression", metadata.Expression, ref argumentCount);
        }

        if (metadata.ExpressionType != null)
        {
            WriteTypeArgument(
                "ExpressionType",
                metadata.ExpressionType,
                ref argumentCount);
        }

        if (metadata.RequiresBrushConversion)
        {
            WriteBooleanArgument(
                "RequiresBrushConversion",
                value: true,
                ref argumentCount);
        }

        if (metadata.ConstantValue != null)
        {
            WriteStringArgument(
                "ConstantValue",
                metadata.ConstantValue,
                ref argumentCount);
        }

        if (metadata.ConstantValueType != null)
        {
            WriteTypeArgument(
                "ConstantValueType",
                metadata.ConstantValueType,
                ref argumentCount);
        }

        if (metadata.ExpansionStartOrder >= 0)
        {
            WriteIntegerArgument(
                "ExpansionStartOrder",
                metadata.ExpansionStartOrder,
                ref argumentCount);
        }

        if (metadata.ExpansionEndOrder >= 0)
        {
            WriteIntegerArgument(
                "ExpansionEndOrder",
                metadata.ExpansionEndOrder,
                ref argumentCount);
        }

        if (metadata.ExpandedFromOrder >= 0)
        {
            WriteIntegerArgument(
                "ExpandedFromOrder",
                metadata.ExpandedFromOrder,
                ref argumentCount);
        }

        if (metadata.DeclaringSymbol != null)
        {
            WriteStringArgument(
                "DeclaringSymbol",
                metadata.DeclaringSymbol,
                ref argumentCount);
        }

        if (metadata.ApplyOperation is { } applyOperation)
        {
            if (!applyOperation.Items.IsDefaultOrEmpty)
            {
                WriteStringArrayArgument(
                    "ApplyItems",
                    applyOperation.Items,
                    ref argumentCount);
            }

            if (HasAppliedSymbols(applyOperation))
            {
                WriteAppliedSymbolsArgument(
                    applyOperation,
                    ref argumentCount);
            }
        }

        if (metadata.InterceptType != null)
        {
            WriteTypeArgument("InterceptType", metadata.InterceptType, ref argumentCount);
        }

        if (metadata.HasErrors)
        {
            WriteBooleanArgument("HasErrors", value: true, ref argumentCount);
        }

        if (metadata.SourcePath != null)
        {
            WriteStringArgument("SourcePath", metadata.SourcePath, ref argumentCount);
        }

        if (metadata.SourceStart >= 0)
        {
            WriteIntegerArgument("SourceStart", metadata.SourceStart, ref argumentCount);
            WriteIntegerArgument("SourceLength", metadata.SourceLength, ref argumentCount);
        }

        AkburaDebug.Assert(argumentCount == 0);

        _writer.CurrentIndent -= _writer.TabSize;
        _writer.WriteLine(")]");
    }

    private static int GetArgumentCount(in AkcssOperationMetadataPlan metadata)
    {
        var count = 6;

        if (metadata.ParentOrder >= 0)
        {
            count++;
        }

        if (metadata.Depth != 0)
        {
            count++;
        }

        if (metadata.IfStartOrder >= 0)
        {
            count++;
        }

        if (metadata.IfEndOrder >= 0)
        {
            count++;
        }

        if (metadata.TargetType != null)
        {
            count++;
        }

        if (metadata.Property != null)
        {
            count++;
        }

        if (metadata.AvaloniaProperty != null)
        {
            count++;
        }

        if (metadata.AttachedGetter != null)
        {
            count++;
        }

        if (metadata.AttachedSetter != null)
        {
            count++;
        }

        if (metadata.PropertyOwnerType != null)
        {
            count++;
        }

        if (metadata.PropertyType != null)
        {
            count++;
        }

        if (metadata.AttachedTargetType != null)
        {
            count++;
        }

        if (metadata.Kind == GeneratedAkcssOperationKind.Set)
        {
            count += 2;
        }

        if (metadata.Expression != null)
        {
            count++;
        }

        if (metadata.ExpressionType != null)
        {
            count++;
        }

        if (metadata.RequiresBrushConversion)
        {
            count++;
        }

        if (metadata.ConstantValue != null)
        {
            count++;
        }

        if (metadata.ConstantValueType != null)
        {
            count++;
        }

        if (metadata.ExpansionStartOrder >= 0)
        {
            count++;
        }

        if (metadata.ExpansionEndOrder >= 0)
        {
            count++;
        }

        if (metadata.ExpandedFromOrder >= 0)
        {
            count++;
        }

        if (metadata.DeclaringSymbol != null)
        {
            count++;
        }

        if (metadata.ApplyOperation is { } applyOperation)
        {
            if (!applyOperation.Items.IsDefaultOrEmpty)
            {
                count++;
            }

            if (HasAppliedSymbols(applyOperation))
            {
                count++;
            }
        }

        if (metadata.InterceptType != null)
        {
            count++;
        }

        if (metadata.HasErrors)
        {
            count++;
        }

        if (metadata.SourcePath != null)
        {
            count++;
        }

        if (metadata.SourceStart >= 0)
        {
            count += 2;
        }

        return count;
    }

    private void WriteEnumArgument(
        string name,
        string enumType,
        string value,
        ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.Write(enumType);
        _writer.Write(".");
        _writer.Write(value);

        WriteArgumentEnd(ref remaining);
    }

    private void WriteStringArgument(string name, string value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteStringLiteral(value);

        WriteArgumentEnd(ref remaining);
    }

    private void WriteIntegerArgument(string name, int value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteIntegerLiteral(value);

        WriteArgumentEnd(ref remaining);
    }

    private void WriteBooleanArgument(string name, bool value, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = ");
        _writer.WriteBooleanLiteral(value);

        WriteArgumentEnd(ref remaining);
    }

    private void WriteTypeArgument(string name, ITypeSymbol type, ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = typeof(");
        _valueWriter.WriteTypeName(type);
        _writer.Write(")");

        WriteArgumentEnd(ref remaining);
    }

    private void WriteStringArrayArgument(
        string name,
        ImmutableArray<string> values,
        ref int remaining)
    {
        _writer.Write(name);
        _writer.Write(" = new global::System.String[] { ");

        for (var i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                _writer.Write(", ");
            }

            _writer.WriteStringLiteral(values[i]);
        }

        _writer.Write(" }");

        WriteArgumentEnd(ref remaining);
    }

    private void WriteAppliedSymbolsArgument(
        IAkcssApplyOperation operation,
        ref int remaining)
    {
        _writer.Write("AppliedSymbols = new global::System.String[] { ");

        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            var names = metadataApply.AppliedSymbolMetadataNames;

            for (var i = 0; i < names.Length; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                _writer.WriteStringLiteral(names[i]);
            }
        }
        else
        {
            var symbols = operation.AppliedSymbols;

            for (var i = 0; i < symbols.Length; i++)
            {
                if (i > 0)
                {
                    _writer.Write(", ");
                }

                _writer.WriteStringLiteral(symbols[i].MetadataName);
            }
        }

        _writer.Write(" }");

        WriteArgumentEnd(ref remaining);
    }

    private void WriteArgumentEnd(ref int remaining)
    {
        AkburaDebug.Assert(remaining > 0);

        remaining--;

        _writer.WriteLine(remaining == 0 ? string.Empty : ",");
    }

    private void CollectObservedPropertyNames(
        ImmutableArray<IAkcssOperation> operations,
        ArrayBuilder<string> propertyNames,
        PooledHashSet<string> propertyNameSet,
        PooledHashSet<IAkcssSymbol> expansionPath)
    {
        for (var i = 0; i < operations.Length; i++)
        {
            switch (operations[i])
            {
                case IAkcssPropertySetterOperation setter:
                    CollectObservedPropertyNames(
                        setter.ValueOperation.Operation,
                        AkcssExpressionGenerator.GetTargetParameterName(
                            setter.ContainingAkcssSymbol),
                        propertyNames,
                        propertyNameSet);

                    break;

                case IAkcssIfOperation ifOperation:
                    CollectObservedPropertyNames(
                        ifOperation.ConditionOperation.Operation,
                        AkcssExpressionGenerator.GetTargetParameterName(
                            ifOperation.ContainingAkcssSymbol),
                        propertyNames,
                        propertyNameSet);

                    CollectObservedPropertyNames(
                        ifOperation.Operations,
                        propertyNames,
                        propertyNameSet,
                        expansionPath);

                    break;

                case IAkcssApplyOperation applyOperation:
                    CollectAppliedObservedPropertyNames(
                        applyOperation,
                        propertyNames,
                        propertyNameSet,
                        expansionPath);

                    break;
            }
        }
    }

    private void CollectAppliedObservedPropertyNames(
        IAkcssApplyOperation operation,
        ArrayBuilder<string> propertyNames,
        PooledHashSet<string> propertyNameSet,
        PooledHashSet<IAkcssSymbol> expansionPath)
    {
        for (var i = 0; i < operation.AppliedSymbols.Length; i++)
        {
            var symbol = _sourceMap.GetGenerationSymbol(
                operation.AppliedSymbols[i]);

            if (!expansionPath.Add(symbol))
            {
                continue;
            }

            if (symbol is IMetadataAkcssSymbol metadataSymbol)
            {
                var observedProperties = metadataSymbol.ObservedProperties;

                for (var j = 0; j < observedProperties.Length; j++)
                {
                    AddObservedPropertyName(
                        observedProperties[j],
                        propertyNames,
                        propertyNameSet);
                }
            }

            CollectObservedPropertyNames(
                symbol.Operations,
                propertyNames,
                propertyNameSet,
                expansionPath);

            expansionPath.Remove(symbol);
        }
    }

    private static void CollectObservedPropertyNames(
        Microsoft.CodeAnalysis.IOperation? operation,
        string targetParameterName,
        ArrayBuilder<string> propertyNames,
        PooledHashSet<string> propertyNameSet)
    {
        if (operation == null)
        {
            return;
        }

        if (operation is IPropertyReferenceOperation propertyReference &&
            IsTargetReference(propertyReference.Instance, targetParameterName) &&
            HasAvaloniaProperty(propertyReference.Property))
        {
            AddObservedPropertyName(
                propertyReference.Property.Name,
                propertyNames,
                propertyNameSet);
        }
        else if (operation is IInvocationOperation invocation &&
                 TryGetObservedAttachedPropertyName(
                     invocation,
                     targetParameterName,
                     out var attachedPropertyName))
        {
            AddObservedPropertyName(
                attachedPropertyName,
                propertyNames,
                propertyNameSet);
        }

        foreach (var child in operation.ChildOperations)
        {
            CollectObservedPropertyNames(
                child,
                targetParameterName,
                propertyNames,
                propertyNameSet);
        }
    }

    private static void AddObservedPropertyName(
        string propertyName,
        ArrayBuilder<string> propertyNames,
        PooledHashSet<string> propertyNameSet)
    {
        if (propertyNameSet.Add(propertyName))
        {
            propertyNames.Add(propertyName);
        }
    }

    private static bool TryGetObservedAttachedPropertyName(
        IInvocationOperation invocation,
        string targetParameterName,
        out string propertyName)
    {
        propertyName = string.Empty;

        var method = invocation.TargetMethod;

        if (method.IsStatic &&
            method.Name.StartsWith("Get", StringComparison.Ordinal) &&
            method.Name.Length > 3 &&
            invocation.Arguments.Length == 1 &&
            IsTargetReference(
                invocation.Arguments[0].Value,
                targetParameterName))
        {
            var candidateName = method.Name.Substring(3);

            if (HasAvaloniaPropertyField(
                    method.ContainingType,
                    candidateName))
            {
                propertyName = candidateName;
                return true;
            }
        }

        if (!StringComparer.Ordinal.Equals(method.Name, "GetValue") ||
            !IsTargetReference(invocation.Instance, targetParameterName) ||
            invocation.Arguments.Length != 1 ||
            invocation.Arguments[0].Value is not IFieldReferenceOperation fieldReference ||
            !fieldReference.Field.IsStatic ||
            !IsAvaloniaPropertyType(fieldReference.Field.Type))
        {
            return false;
        }

        const string suffix = "Property";

        var fieldName = fieldReference.Field.Name;

        if (!fieldName.EndsWith(suffix, StringComparison.Ordinal) ||
            fieldName.Length == suffix.Length)
        {
            return false;
        }

        propertyName = fieldName[..^suffix.Length];

        return true;
    }

    private static bool HasAvaloniaPropertyField(
        INamedTypeSymbol type,
        string propertyName)
    {
        var fieldName = propertyName + "Property";

        for (var current = type; current != null; current = current.BaseType)
        {
            var members = current.GetMembers(fieldName);

            for (var i = 0; i < members.Length; i++)
            {
                if (members[i] is RoslynFieldSymbol
                    {
                        IsStatic: true,
                        DeclaredAccessibility: Accessibility.Public,
                    } field &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsTargetReference(
        Microsoft.CodeAnalysis.IOperation? operation,
        string targetParameterName)
    {
        while (operation != null)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;

                case IParenthesizedOperation parenthesized:
                    operation = parenthesized.Operand;
                    continue;

                case IParameterReferenceOperation parameterReference:
                    return StringComparer.Ordinal.Equals(
                        parameterReference.Parameter.Name,
                        targetParameterName);

                default:
                    return false;
            }
        }

        return false;
    }

    private static bool HasAvaloniaProperty(RoslynPropertySymbol property)
    {
        var fieldName = property.Name + "Property";

        for (var type = property.ContainingType; type != null; type = type.BaseType)
        {
            var members = type.GetMembers(fieldName);

            for (var i = 0; i < members.Length; i++)
            {
                if (members[i] is RoslynFieldSymbol { IsStatic: true } field &&
                    IsAvaloniaPropertyType(field.Type))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsAvaloniaPropertyType(ITypeSymbol type)
    {
        for (var current = type as INamedTypeSymbol; current != null; current = current.BaseType)
        {
            if (current.Name == "AvaloniaProperty" &&
                current.ContainingNamespace.ToDisplayString() == "Avalonia")
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAppliedSymbols(IAkcssApplyOperation operation)
    {
        return operation is IMetadataAkcssApplyOperation metadataApply
            ? !metadataApply.AppliedSymbolMetadataNames.IsDefaultOrEmpty
            : !operation.AppliedSymbols.IsDefaultOrEmpty;
    }

    private static string GetOperationKindName(GeneratedAkcssOperationKind kind)
    {
        return kind switch
        {
            GeneratedAkcssOperationKind.Set => "Set",
            GeneratedAkcssOperationKind.If => "If",
            GeneratedAkcssOperationKind.Apply => "Apply",
            GeneratedAkcssOperationKind.Intercept => "Intercept",
            _ => "Set",
        };
    }

    private static string GetOperationOriginName(
        GeneratedAkcssOperationOriginKind origin)
    {
        return origin switch
        {
            GeneratedAkcssOperationOriginKind.Direct => "Direct",
            GeneratedAkcssOperationOriginKind.ApplyExpansion => "ApplyExpansion",
            GeneratedAkcssOperationOriginKind.Synthesized => "Synthesized",
            _ => "Direct",
        };
    }

    private static string GetPropertyAccessKindName(GeneratedAkcssPropertyAccessKind kind)
    {
        return kind switch
        {
            GeneratedAkcssPropertyAccessKind.None => "None",
            GeneratedAkcssPropertyAccessKind.ClrProperty => "ClrProperty",
            GeneratedAkcssPropertyAccessKind.AvaloniaProperty => "AvaloniaProperty",
            GeneratedAkcssPropertyAccessKind.AttachedAccessor => "AttachedAccessor",
            GeneratedAkcssPropertyAccessKind.Parameter => "Parameter",
            GeneratedAkcssPropertyAccessKind.Command => "Command",
            _ => "None",
        };
    }

    private static string GetPropertyValueKindName(GeneratedAkcssPropertyValueKind kind)
    {
        return kind switch
        {
            GeneratedAkcssPropertyValueKind.None => "None",
            GeneratedAkcssPropertyValueKind.CSharpExpression => "CSharpExpression",
            GeneratedAkcssPropertyValueKind.ColorLiteral => "ColorLiteral",
            GeneratedAkcssPropertyValueKind.ThicknessTuple => "ThicknessTuple",
            GeneratedAkcssPropertyValueKind.AmxInvocation => "AmxInvocation",
            GeneratedAkcssPropertyValueKind.Error => "Error",
            _ => "None",
        };
    }

    private static string GetOperationPriorityName(GeneratedAkcssOperationPriority priority)
    {
        return priority switch
        {
            GeneratedAkcssOperationPriority.Style => "Style",
            GeneratedAkcssOperationPriority.StyleTrigger => "StyleTrigger",
            _ => "Style",
        };
    }
}
