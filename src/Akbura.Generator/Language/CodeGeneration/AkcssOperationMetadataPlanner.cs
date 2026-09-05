using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Globalization;
using AkburaPropertyAccessKind = Akbura.Language.Symbols.PropertyAccessKind;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynMethodSymbol = Microsoft.CodeAnalysis.IMethodSymbol;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Flattens semantic AKCSS operations into metadata attribute plans.
/// </summary>
internal ref struct AkcssOperationMetadataPlanner
{
    private readonly ArrayBuilder<AkcssOperationMetadataPlan> _metadata;
    private readonly ArrayBuilder<AkcssIdentifierValue> _identifierValues;
    private readonly AkcssGenerationSourceMap _sourceMap;

    private int _nextOrder;

    public AkcssOperationMetadataPlanner(
        ArrayBuilder<AkcssOperationMetadataPlan> metadata,
        ArrayBuilder<AkcssIdentifierValue> identifierValues,
        AkcssGenerationSourceMap sourceMap)
    {
        _metadata = metadata;
        _identifierValues = identifierValues;
        _sourceMap = sourceMap;
        _nextOrder = 0;
    }

    public void Build(
        IAkcssSymbol symbol,
        PooledHashSet<IAkcssSymbol> expansionPath)
    {
        AkburaDebug.Assert(expansionPath.Count == 0);
        AkburaDebug.Assert(_identifierValues.Count == 0);

        expansionPath.Add(_sourceMap.GetGenerationSymbol(symbol));

        if (symbol is ITailwindUtilitySymbol { Parameters.Length: > 0 } utility)
        {
            AkcssExpressionGenerator.AddDirectMetadataParameterValues(
                utility,
                _identifierValues);
        }

        AddOperations(
            symbol.Operations,
            expansionPath,
            MetadataScope.Direct,
            GeneratedAkcssOperationPriority.Style,
            parentOrder: -1,
            depth: 0);
    }

    private void AddOperations(
        ImmutableArray<IAkcssOperation> operations,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        for (var i = 0; i < operations.Length; i++)
        {
            AddOperation(
                operations[i],
                expansionPath,
                scope,
                priority,
                parentOrder,
                depth);
        }
    }

    private void AddOperation(
        IAkcssOperation operation,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        switch (operation)
        {
            case IAkcssPropertySetterOperation setter:
                _metadata.Add(
                    CreatePropertySetterMetadata(
                        setter,
                        _nextOrder++,
                        parentOrder,
                        depth,
                        scope,
                        priority));

                return;

            case IAkcssIfOperation ifOperation:
                AddIfOperation(
                    ifOperation,
                    expansionPath,
                    scope,
                    priority,
                    parentOrder,
                    depth);

                return;

            case IAkcssApplyOperation applyOperation:
                AddApplyOperation(
                    applyOperation,
                    expansionPath,
                    scope,
                    priority,
                    parentOrder,
                    depth);

                return;

            case IAkcssInterceptOperation interceptOperation:
                _metadata.Add(
                    CreateInterceptMetadata(
                        interceptOperation,
                        _nextOrder++,
                        parentOrder,
                        depth,
                        scope,
                        priority));

                return;

            default:
                return;
        }
    }

    private void AddIfOperation(
        IAkcssIfOperation operation,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        var order = _nextOrder++;
        var metadataIndex = _metadata.Count;
        var firstChildOrder = _nextOrder;

        _metadata.Add(default);

        AddOperations(
            operation.Operations,
            expansionPath,
            scope,
            GeneratedAkcssOperationPriority.StyleTrigger,
            parentOrder: order,
            depth: depth + 1);

        var hasChildren = _nextOrder > firstChildOrder;

        _metadata[metadataIndex] = CreateIfMetadata(
            operation,
            order,
            parentOrder,
            depth,
            scope,
            priority,
            hasChildren ? firstChildOrder : -1,
            hasChildren ? _nextOrder - 1 : -1);
    }

    private void AddApplyOperation(
        IAkcssApplyOperation operation,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int parentOrder,
        int depth)
    {
        var order = _nextOrder++;
        var metadataIndex = _metadata.Count;
        var firstExpansionOrder = _nextOrder;
        var hasExpansionErrors = false;

        _metadata.Add(default);

        if (operation is IMetadataAkcssApplyOperation metadataApply)
        {
            AddMetadataApplyExpansions(
                metadataApply,
                expansionPath,
                scope,
                priority,
                order,
                depth,
                ref hasExpansionErrors);
        }
        else
        {
            AddSourceApplyExpansions(
                operation,
                expansionPath,
                scope,
                priority,
                order,
                depth,
                ref hasExpansionErrors);
        }

        var hasExpansion = _nextOrder > firstExpansionOrder;

        _metadata[metadataIndex] = CreateApplyMetadata(
            operation,
            order,
            parentOrder,
            depth,
            scope,
            priority,
            hasExpansion ? firstExpansionOrder : -1,
            hasExpansion ? _nextOrder - 1 : -1,
            hasExpansionErrors);
    }

    private void AddMetadataApplyExpansions(
        IMetadataAkcssApplyOperation operation,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int order,
        int depth,
        ref bool hasExpansionErrors)
    {
        var firstExpansionOrder = _nextOrder;

        for (var i = 0; i < operation.ExpandedOperations.Length; i++)
        {
            var expandedOperation = operation.ExpandedOperations[i];

            var declaringSymbol =
                expandedOperation is IMetadataAkcssOperation metadataChild
                    ? metadataChild.DeclaringSymbolMetadataName
                    : null;

            var expansionScope = new MetadataScope(
                GeneratedAkcssOperationOriginKind.ApplyExpansion,
                order,
                declaringSymbol);

            AddOperation(
                expandedOperation,
                expansionPath,
                expansionScope,
                priority,
                parentOrder: order,
                depth: depth + 1);
        }

        hasExpansionErrors =
            !operation.HasErrors &&
            !operation.ExpandedOperations.IsEmpty &&
            _nextOrder == firstExpansionOrder;
    }

    private void AddSourceApplyExpansions(
        IAkcssApplyOperation operation,
        PooledHashSet<IAkcssSymbol> expansionPath,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int order,
        int depth,
        ref bool hasExpansionErrors)
    {
        for (var i = 0; i < operation.AppliedSymbols.Length; i++)
        {
            var symbol = _sourceMap.GetGenerationSymbol(operation.AppliedSymbols[i]);

            if (!expansionPath.Add(symbol))
            {
                hasExpansionErrors = true;
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
                        AkcssExpressionGenerator.MetadataTargetName,
                        _identifierValues,
                        out previousValueCount))
                {
                    hasExpansionErrors = true;
                    expansionPath.Remove(symbol);
                    continue;
                }
            }

            try
            {
                var expansionScope = new MetadataScope(
                    GeneratedAkcssOperationOriginKind.ApplyExpansion,
                    order,
                    symbol.MetadataName);

                AddOperations(
                    symbol.Operations,
                    expansionPath,
                    expansionScope,
                    priority,
                    parentOrder: order,
                    depth: depth + 1);
            }
            finally
            {
                _identifierValues.Count = previousValueCount;
                expansionPath.Remove(symbol);
            }
        }
    }

    private AkcssOperationMetadataPlan CreatePropertySetterMetadata(
        IAkcssPropertySetterOperation operation,
        int order,
        int parentOrder,
        int depth,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority)
    {
        var property = operation.Property;

        var value = AkcssExpressionGenerator.GetValueExpression(
            operation,
            AkcssExpressionGenerator.MetadataTargetName,
            observeDynamicResource: false,
            _identifierValues,
            _identifierValues.Count,
            preserveAmxResources: true);

        GetOperationSource(
            operation,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        TryGetConstantMetadata(
            operation,
            out var constantValue,
            out var constantValueType);

        return new AkcssOperationMetadataPlan
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,
            Kind = GeneratedAkcssOperationKind.Set,
            Origin = scope.Origin,
            TargetType = AkcssExpressionGenerator.GetAkcssTargetType(operation.ContainingAkcssSymbol),
            PropertyAccessKind = GetPropertyAccessKind(property),
            Property = property?.Name,
            AvaloniaProperty = GetAvaloniaPropertyName(property),
            AttachedGetter = GetAttachedGetterName(property),
            AttachedSetter = GetAttachedSetterName(property),
            PropertyOwnerType = property?.WriteDefinition.Symbol?.ContainingType,
            PropertyType = property?.Type.Symbol as ITypeSymbol,
            AttachedTargetType = AkcssExpressionGenerator.GetAttachedTargetType(property),
            CanRead = property?.CanRead ?? false,
            CanWrite = property?.CanWrite ?? false,
            ValueKind = GetPropertyValueKind(operation.ValueKind),
            Expression = value.Expression,
            ExpressionType = operation.ValueType.Symbol as ITypeSymbol,
            RequiresBrushConversion = operation.RequiresBrushConversion,
            ConstantValue = constantValue,
            ConstantValueType = constantValueType,
            Priority = priority,
            HasErrors = operation.HasErrors || property == null || !property.CanWrite,
            DeclaringSymbol = scope.DeclaringSymbol,
            ExpandedFromOrder = scope.ExpandedFromOrder,
            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private AkcssOperationMetadataPlan CreateIfMetadata(
        IAkcssIfOperation operation,
        int order,
        int parentOrder,
        int depth,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int ifStartOrder,
        int ifEndOrder)
    {
        var expression = AkcssExpressionGenerator.GetIfConditionExpression(
            operation,
            AkcssExpressionGenerator.MetadataTargetName,
            _identifierValues,
            _identifierValues.Count,
            preserveAmxResources: true);

        GetOperationSource(
            operation,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        return new AkcssOperationMetadataPlan
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,
            Kind = GeneratedAkcssOperationKind.If,
            Origin = scope.Origin,
            TargetType = AkcssExpressionGenerator.GetAkcssTargetType(operation.ContainingAkcssSymbol),
            PropertyAccessKind = GeneratedAkcssPropertyAccessKind.None,
            ValueKind = GeneratedAkcssPropertyValueKind.CSharpExpression,
            Expression = expression,
            ExpressionType = operation.ConditionType.Symbol as ITypeSymbol,
            Priority = priority,
            HasErrors = operation.HasErrors,
            IfStartOrder = ifStartOrder,
            IfEndOrder = ifEndOrder,
            DeclaringSymbol = scope.DeclaringSymbol,
            ExpandedFromOrder = scope.ExpandedFromOrder,
            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private AkcssOperationMetadataPlan CreateApplyMetadata(
        IAkcssApplyOperation operation,
        int order,
        int parentOrder,
        int depth,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority,
        int expansionStartOrder,
        int expansionEndOrder,
        bool hasExpansionErrors)
    {
        GetOperationSource(
            operation,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        return new AkcssOperationMetadataPlan
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,
            Kind = GeneratedAkcssOperationKind.Apply,
            Origin = scope.Origin,
            TargetType = AkcssExpressionGenerator.GetAkcssTargetType(operation.ContainingAkcssSymbol),
            PropertyAccessKind = GeneratedAkcssPropertyAccessKind.None,
            ValueKind = GeneratedAkcssPropertyValueKind.None,
            Priority = priority,
            HasErrors = operation.HasErrors || hasExpansionErrors,
            DeclaringSymbol = scope.DeclaringSymbol,
            ApplyOperation = operation,
            ExpansionStartOrder = expansionStartOrder,
            ExpansionEndOrder = expansionEndOrder,
            ExpandedFromOrder = scope.ExpandedFromOrder,
            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private AkcssOperationMetadataPlan CreateInterceptMetadata(
        IAkcssInterceptOperation operation,
        int order,
        int parentOrder,
        int depth,
        in MetadataScope scope,
        GeneratedAkcssOperationPriority priority)
    {
        GetOperationSource(
            operation,
            out var sourcePath,
            out var sourceStart,
            out var sourceLength);

        return new AkcssOperationMetadataPlan
        {
            Order = order,
            ParentOrder = parentOrder,
            Depth = depth,
            Kind = GeneratedAkcssOperationKind.Intercept,
            Origin = scope.Origin,
            TargetType = AkcssExpressionGenerator.GetAkcssTargetType(operation.ContainingAkcssSymbol),
            PropertyAccessKind = GeneratedAkcssPropertyAccessKind.None,
            ValueKind = GeneratedAkcssPropertyValueKind.None,
            Priority = priority,
            HasErrors = operation.HasErrors || operation.InterceptType.Symbol is not ITypeSymbol,
            DeclaringSymbol = scope.DeclaringSymbol,
            ExpandedFromOrder = scope.ExpandedFromOrder,
            InterceptType = operation.InterceptType.Symbol as ITypeSymbol,
            SourcePath = sourcePath,
            SourceStart = sourceStart,
            SourceLength = sourceLength,
        };
    }

    private void GetOperationSource(
        IAkcssOperation operation,
        out string? sourcePath,
        out int sourceStart,
        out int sourceLength)
    {
        if (operation is IMetadataAkcssOperation
            {
                SourcePath: { Length: > 0 } metadataPath,
                SourceSpan: { Length: > 0 } metadataSpan,
            })
        {
            sourcePath = metadataPath;
            sourceStart = metadataSpan.Start;
            sourceLength = metadataSpan.Length;
            return;
        }

        GetOperationSource(
            operation.Syntax,
            out sourcePath,
            out sourceStart,
            out sourceLength);
    }

    private void GetOperationSource(
        AkburaSyntax? syntax,
        out string? sourcePath,
        out int sourceStart,
        out int sourceLength)
    {
        if (syntax != null &&
            _sourceMap.TryGetSourceSpan(syntax, out var span, out var path))
        {
            sourcePath = AkcssGeneratedModuleNames.NormalizeSourcePath(path);
            sourceStart = span.Start;
            sourceLength = span.Length;
            return;
        }

        sourcePath = null;
        sourceStart = -1;
        sourceLength = 0;
    }

    private static GeneratedAkcssPropertyAccessKind GetPropertyAccessKind(
        AkburaPropertySymbol? property)
    {
        if (property == null)
        {
            return GeneratedAkcssPropertyAccessKind.None;
        }

        return property.WriteKind switch
        {
            AkburaPropertyAccessKind.AvaloniaProperty =>
                GeneratedAkcssPropertyAccessKind.AvaloniaProperty,

            AkburaPropertyAccessKind.AttachedAccessor =>
                GeneratedAkcssPropertyAccessKind.AttachedAccessor,

            AkburaPropertyAccessKind.ClrProperty =>
                GeneratedAkcssPropertyAccessKind.ClrProperty,

            AkburaPropertyAccessKind.Parameter =>
                GeneratedAkcssPropertyAccessKind.Parameter,

            AkburaPropertyAccessKind.Command =>
                GeneratedAkcssPropertyAccessKind.Command,

            _ =>
                GeneratedAkcssPropertyAccessKind.None,
        };
    }

    private static GeneratedAkcssPropertyValueKind GetPropertyValueKind(
        AkcssPropertyValueKind valueKind)
    {
        return valueKind switch
        {
            AkcssPropertyValueKind.CSharpExpression =>
                GeneratedAkcssPropertyValueKind.CSharpExpression,

            AkcssPropertyValueKind.ColorLiteral =>
                GeneratedAkcssPropertyValueKind.ColorLiteral,

            AkcssPropertyValueKind.ThicknessTuple =>
                GeneratedAkcssPropertyValueKind.ThicknessTuple,

            AkcssPropertyValueKind.AmxInvocation =>
                GeneratedAkcssPropertyValueKind.AmxInvocation,

            AkcssPropertyValueKind.Error =>
                GeneratedAkcssPropertyValueKind.Error,

            _ =>
                GeneratedAkcssPropertyValueKind.None,
        };
    }

    private static string? GetAvaloniaPropertyName(AkburaPropertySymbol? property)
    {
        if (property == null)
        {
            return null;
        }

        var definition = !property.AvaloniaPropertyDefinition.IsDefault
            ? property.AvaloniaPropertyDefinition
            : property.AttachedPropertyDefinition;

        return definition.Symbol is RoslynFieldSymbol field
            ? field.Name
            : null;
    }

    private static string? GetAttachedGetterName(AkburaPropertySymbol? property)
    {
        return property?.AttachedGetterDefinition.Symbol is RoslynMethodSymbol getter
            ? getter.Name
            : null;
    }

    private static string? GetAttachedSetterName(AkburaPropertySymbol? property)
    {
        return property?.AttachedSetterDefinition.Symbol is RoslynMethodSymbol setter
            ? setter.Name
            : null;
    }

    private static void TryGetConstantMetadata(
        IAkcssPropertySetterOperation operation,
        out string? value,
        out ITypeSymbol? type)
    {
        object? constant = null;
        var hasConstant = operation.ValueOperation.ConstantValue.HasValue;

        if (hasConstant)
        {
            constant = operation.ValueOperation.ConstantValue.Value;
        }
        else if (operation.ConvertedValue is
                 string or char or bool or
                 byte or sbyte or short or ushort or int or uint or long or ulong or
                 float or double or decimal)
        {
            constant = operation.ConvertedValue;
            hasConstant = true;
        }

        if (!hasConstant || constant == null)
        {
            value = null;
            type = null;
            return;
        }

        value = constant switch
        {
            bool boolean => boolean ? "true" : "false",
            char character => character.ToString(),
            string text => text,
            IFormattable formattable =>
                formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => constant.ToString(),
        };

        type = operation.ValueType.Symbol as ITypeSymbol;
    }

    private readonly struct MetadataScope
    {
        public static MetadataScope Direct { get; } = new(
            GeneratedAkcssOperationOriginKind.Direct,
            expandedFromOrder: -1,
            declaringSymbol: null);

        public MetadataScope(
            GeneratedAkcssOperationOriginKind origin,
            int expandedFromOrder,
            string? declaringSymbol)
        {
            Origin = origin;
            ExpandedFromOrder = expandedFromOrder;
            DeclaringSymbol = declaringSymbol;
        }

        public GeneratedAkcssOperationOriginKind Origin { get; }

        public int ExpandedFromOrder { get; }

        public string? DeclaringSymbol { get; }
    }
}
