using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal enum GeneratedAkcssOperationKind : byte
{
    Set,
    If,
    Apply,
    Intercept,
}

internal enum GeneratedAkcssOperationOriginKind : byte
{
    Direct,
    ApplyExpansion,
    Synthesized,
}

internal enum GeneratedAkcssPropertyAccessKind : byte
{
    None,
    ClrProperty,
    AvaloniaProperty,
    AttachedAccessor,
    Parameter,
    Command,
}

internal enum GeneratedAkcssPropertyValueKind : byte
{
    None,
    CSharpExpression,
    ColorLiteral,
    ThicknessTuple,
    AmxInvocation,
    Error,
}

internal enum GeneratedAkcssOperationPriority : byte
{
    Style,
    StyleTrigger,
}

internal struct AkcssOperationMetadataPlan
{
    public AkcssOperationMetadataPlan()
    {
        ParentOrder = -1;
        IfStartOrder = -1;
        IfEndOrder = -1;
        ExpansionStartOrder = -1;
        ExpansionEndOrder = -1;
        ExpandedFromOrder = -1;
        SourceStart = -1;
    }

    public int Order { get; init; }

    public int ParentOrder { get; init; }

    public int Depth { get; init; }

    public GeneratedAkcssOperationKind Kind { get; init; }

    public GeneratedAkcssOperationOriginKind Origin { get; init; }

    public ITypeSymbol? TargetType { get; init; }

    public GeneratedAkcssPropertyAccessKind PropertyAccessKind { get; init; }

    public IAkcssPropertySetterOperation? Setter { get; init; }

    public string? Property { get; init; }

    public string? AvaloniaProperty { get; init; }

    public string? AttachedGetter { get; init; }

    public string? AttachedSetter { get; init; }

    public ITypeSymbol? PropertyOwnerType { get; init; }

    public ITypeSymbol? PropertyType { get; init; }

    public ITypeSymbol? AttachedTargetType { get; init; }

    public bool CanRead { get; init; }

    public bool CanWrite { get; init; }

    public GeneratedAkcssPropertyValueKind ValueKind { get; init; }

    public string? Expression { get; init; }

    public ITypeSymbol? ExpressionType { get; init; }

    public bool RequiresBrushConversion { get; init; }

    public string? ConstantValue { get; init; }

    public ITypeSymbol? ConstantValueType { get; init; }

    public GeneratedAkcssOperationPriority Priority { get; init; }

    public bool HasErrors { get; init; }

    public int IfStartOrder { get; init; }

    public int IfEndOrder { get; init; }

    public string? DeclaringSymbol { get; init; }

    public IAkcssApplyOperation? ApplyOperation { get; init; }

    public int ExpansionStartOrder { get; init; }

    public int ExpansionEndOrder { get; init; }

    public int ExpandedFromOrder { get; init; }

    public ITypeSymbol? InterceptType { get; init; }

    public string? SourcePath { get; init; }

    public int SourceStart { get; init; }

    public int SourceLength { get; init; }
}
