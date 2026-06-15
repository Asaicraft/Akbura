using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class AkcssPropertySetterOperation : IAkcssPropertySetterOperation
{
    public AkcssPropertySetterOperation(
        AkcssAssignmentSyntax syntax,
        IAkcssSymbol containingAkcssSymbol,
        IPropertySymbol? property,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkcssPropertyValueKind valueKind,
        bool requiresBrushConversion,
        object? convertedValue,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
        Property = property;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueKind = valueKind;
        RequiresBrushConversion = requiresBrushConversion;
        ConvertedValue = convertedValue;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.AkcssAssignment;

    public OperationLanguage Language => OperationLanguage.Akcss;

    AkburaSyntax IOperation.Syntax => Syntax;

    public AkcssAssignmentSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => Property;

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => ValueOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => ConvertedValue;

    public IAkcssSymbol ContainingAkcssSymbol { get; }

    public IPropertySymbol? Property { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkcssPropertyValueKind ValueKind { get; }

    public bool RequiresBrushConversion { get; }

    public object? ConvertedValue { get; }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return Property == null
            ? Syntax.ToFullString()
            : $"{Property.Name}: {Syntax.Expression.ToFullString().Trim()}";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
