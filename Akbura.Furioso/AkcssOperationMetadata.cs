using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Furioso;

internal sealed class AkcssOperationMetadata
{
    public int Order { get; init; }

    public int ParentOrder { get; init; } = -1;

    public int Depth { get; init; }

    public GeneratedAkcssOperationKind Kind { get; init; }

    public GeneratedAkcssOperationOriginKind Origin { get; init; }

    public ITypeSymbol? TargetType { get; init; }

    public GeneratedAkcssPropertyAccessKind PropertyAccessKind { get; init; }

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

    public GeneratedAkcssOperationPriority Priority { get; init; }

    public bool HasErrors { get; init; }

    public int IfStartOrder { get; init; } = -1;

    public int IfEndOrder { get; init; } = -1;

    public string? DeclaringSymbol { get; init; }

    public ImmutableArray<string> ApplyItems { get; init; } = [];

    public ImmutableArray<string> AppliedSymbols { get; init; } = [];

    public int ExpansionStartOrder { get; init; } = -1;

    public int ExpansionEndOrder { get; init; } = -1;

    public int ExpandedFromOrder { get; init; } = -1;

    public ITypeSymbol? InterceptType { get; init; }
}

internal enum GeneratedAkcssOperationKind
{
    Set,
    If,
    Apply,
    Intercept,
}

internal enum GeneratedAkcssOperationOriginKind
{
    Direct,
    ApplyExpansion,
    Synthesized,
}

internal enum GeneratedAkcssPropertyAccessKind
{
    None,
    ClrProperty,
    AvaloniaProperty,
    AttachedAccessor,
    Parameter,
    Command,
}

internal enum GeneratedAkcssPropertyValueKind
{
    None,
    CSharpExpression,
    ColorLiteral,
    ThicknessTuple,
    AmxInvocation,
    Error,
}

internal enum GeneratedAkcssOperationPriority
{
    Style,
    StyleTrigger,
}