using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal enum MetadataAkcssOperationKind
{
    Set,
    If,
    Apply,
    Intercept,
}

internal readonly struct MetadataAkcssOperationData
{
    private MetadataAkcssOperationData(
        AttributeData attribute,
        int order,
        int parentOrder,
        int depth,
        MetadataAkcssOperationKind kind,
        MetadataAkcssOperationOrigin origin,
        MetadataAkcssOperationPriority priority,
        bool hasErrors,
        string? declaringSymbol,
        ITypeSymbol? targetType,
        PropertyAccessKind propertyAccessKind,
        string? property,
        string? avaloniaProperty,
        string? attachedGetter,
        string? attachedSetter,
        ITypeSymbol? propertyOwnerType,
        ITypeSymbol? propertyType,
        ITypeSymbol? attachedTargetType,
        bool canRead,
        bool canWrite,
        AkcssPropertyValueKind valueKind,
        string? expression,
        ITypeSymbol? expressionType,
        bool requiresBrushConversion,
        string? constantValue,
        ITypeSymbol? constantValueType,
        int ifStartOrder,
        int ifEndOrder,
        ImmutableArray<string> applyItems,
        ImmutableArray<string> appliedSymbols,
        int expansionStartOrder,
        int expansionEndOrder,
        int expandedFromOrder,
        ITypeSymbol? interceptType,
        string? sourcePath,
        int sourceStart,
        int sourceLength)
    {
        Attribute = attribute;
        Order = order;
        ParentOrder = parentOrder;
        Depth = depth;
        Kind = kind;
        Origin = origin;
        Priority = priority;
        HasErrors = hasErrors;
        DeclaringSymbol = declaringSymbol;
        TargetType = targetType;
        PropertyAccessKind = propertyAccessKind;
        Property = property;
        AvaloniaProperty = avaloniaProperty;
        AttachedGetter = attachedGetter;
        AttachedSetter = attachedSetter;
        PropertyOwnerType = propertyOwnerType;
        PropertyType = propertyType;
        AttachedTargetType = attachedTargetType;
        CanRead = canRead;
        CanWrite = canWrite;
        ValueKind = valueKind;
        Expression = expression;
        ExpressionType = expressionType;
        RequiresBrushConversion = requiresBrushConversion;
        ConstantValue = constantValue;
        ConstantValueType = constantValueType;
        IfStartOrder = ifStartOrder;
        IfEndOrder = ifEndOrder;
        ApplyItems = applyItems;
        AppliedSymbols = appliedSymbols;
        ExpansionStartOrder = expansionStartOrder;
        ExpansionEndOrder = expansionEndOrder;
        ExpandedFromOrder = expandedFromOrder;
        InterceptType = interceptType;
        SourcePath = sourcePath;
        SourceStart = sourceStart;
        SourceLength = sourceLength;
    }

    public AttributeData Attribute { get; }

    public int Order { get; }

    public int ParentOrder { get; }

    public int Depth { get; }

    public MetadataAkcssOperationKind Kind { get; }

    public MetadataAkcssOperationOrigin Origin { get; }

    public MetadataAkcssOperationPriority Priority { get; }

    public bool HasErrors { get; }

    public string? DeclaringSymbol { get; }

    public ITypeSymbol? TargetType { get; }

    public PropertyAccessKind PropertyAccessKind { get; }

    public string? Property { get; }

    public string? AvaloniaProperty { get; }

    public string? AttachedGetter { get; }

    public string? AttachedSetter { get; }

    public ITypeSymbol? PropertyOwnerType { get; }

    public ITypeSymbol? PropertyType { get; }

    public ITypeSymbol? AttachedTargetType { get; }

    public bool CanRead { get; }

    public bool CanWrite { get; }

    public AkcssPropertyValueKind ValueKind { get; }

    public string? Expression { get; }

    public ITypeSymbol? ExpressionType { get; }

    public bool RequiresBrushConversion { get; }

    public string? ConstantValue { get; }

    public ITypeSymbol? ConstantValueType { get; }

    public int IfStartOrder { get; }

    public int IfEndOrder { get; }

    public ImmutableArray<string> ApplyItems { get; }

    public ImmutableArray<string> AppliedSymbols { get; }

    public int ExpansionStartOrder { get; }

    public int ExpansionEndOrder { get; }

    public int ExpandedFromOrder { get; }

    public ITypeSymbol? InterceptType { get; }

    public string? SourcePath { get; }

    public int SourceStart { get; }

    public int SourceLength { get; }

    public static bool TryCreate(
        AttributeData attribute,
        out MetadataAkcssOperationData data)
    {
        var order = GetInt32(attribute, "Order", -1);
        var kind = GetInt32(attribute, "Kind", -1);
        var origin = GetInt32(attribute, "Origin", 0);
        var priority = GetInt32(attribute, "Priority", 0);
        var propertyAccessKind = GetInt32(attribute, "PropertyAccessKind", 0);
        var valueKind = GetInt32(attribute, "ValueKind", 0);
        if (order < 0 ||
            !IsDefined(kind, MetadataAkcssOperationKind.Set, MetadataAkcssOperationKind.Intercept) ||
            !IsDefined(origin, MetadataAkcssOperationOrigin.Direct, MetadataAkcssOperationOrigin.Synthesized) ||
            !IsDefined(priority, MetadataAkcssOperationPriority.Style, MetadataAkcssOperationPriority.StyleTrigger) ||
            !IsDefined(propertyAccessKind, PropertyAccessKind.None, PropertyAccessKind.Command) ||
            !IsDefined(valueKind, AkcssPropertyValueKind.None, AkcssPropertyValueKind.Error))
        {
            data = default;
            return false;
        }

        data = new MetadataAkcssOperationData(
            attribute,
            order,
            GetInt32(attribute, "ParentOrder", -1),
            GetInt32(attribute, "Depth"),
            (MetadataAkcssOperationKind)kind,
            (MetadataAkcssOperationOrigin)origin,
            (MetadataAkcssOperationPriority)priority,
            GetBoolean(attribute, "HasErrors"),
            GetString(attribute, "DeclaringSymbol"),
            GetType(attribute, "TargetType"),
            (PropertyAccessKind)propertyAccessKind,
            GetString(attribute, "Property"),
            GetString(attribute, "AvaloniaProperty"),
            GetString(attribute, "AttachedGetter"),
            GetString(attribute, "AttachedSetter"),
            GetType(attribute, "PropertyOwnerType"),
            GetType(attribute, "PropertyType"),
            GetType(attribute, "AttachedTargetType"),
            GetBoolean(attribute, "CanRead", defaultValue: true),
            GetBoolean(attribute, "CanWrite", defaultValue: true),
            (AkcssPropertyValueKind)valueKind,
            GetString(attribute, "Expression"),
            GetType(attribute, "ExpressionType"),
            GetBoolean(attribute, "RequiresBrushConversion"),
            GetString(attribute, "ConstantValue"),
            GetType(attribute, "ConstantValueType"),
            GetInt32(attribute, "IfStartOrder", -1),
            GetInt32(attribute, "IfEndOrder", -1),
            GetStringArray(attribute, "ApplyItems"),
            GetStringArray(attribute, "AppliedSymbols"),
            GetInt32(attribute, "ExpansionStartOrder", -1),
            GetInt32(attribute, "ExpansionEndOrder", -1),
            GetInt32(attribute, "ExpandedFromOrder", -1),
            GetType(attribute, "InterceptType"),
            GetString(attribute, "SourcePath"),
            GetInt32(attribute, "SourceStart", -1),
            GetInt32(attribute, "SourceLength"));
        return true;
    }

    private static bool IsDefined<TEnum>(int value, TEnum first, TEnum last)
        where TEnum : struct
    {
        return value >= Convert.ToInt32(first) && value <= Convert.ToInt32(last);
    }

    private static TypedConstant GetValue(AttributeData attribute, string name)
    {
        foreach (var argument in attribute.NamedArguments)
        {
            if (string.Equals(argument.Key, name, StringComparison.Ordinal))
            {
                return argument.Value;
            }
        }

        return default;
    }

    private static int GetInt32(
        AttributeData attribute,
        string name,
        int defaultValue = 0)
    {
        return GetValue(attribute, name).Value is int value
            ? value
            : defaultValue;
    }

    private static bool GetBoolean(
        AttributeData attribute,
        string name,
        bool defaultValue = false)
    {
        return GetValue(attribute, name).Value is bool value
            ? value
            : defaultValue;
    }

    private static string? GetString(AttributeData attribute, string name)
    {
        return GetValue(attribute, name).Value as string;
    }

    private static ITypeSymbol? GetType(AttributeData attribute, string name)
    {
        return GetValue(attribute, name).Value as ITypeSymbol;
    }

    private static ImmutableArray<string> GetStringArray(
        AttributeData attribute,
        string name)
    {
        var value = GetValue(attribute, name);
        if (value.Kind != TypedConstantKind.Array || value.Values.IsDefaultOrEmpty)
        {
            return ImmutableArray<string>.Empty;
        }

        using var builder =
            ImmutableArrayBuilder<string>.Rent(value.Values.Length);
        foreach (var item in value.Values)
        {
            if (item.Value is string text)
            {
                builder.Add(text);
            }
        }

        return builder.ToImmutable();
    }
}
