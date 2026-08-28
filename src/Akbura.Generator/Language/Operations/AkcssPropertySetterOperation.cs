using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
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
        AkburaConversion valueConversion,
        AkcssPropertyValueKind valueKind,
        bool requiresBrushConversion,
        object? convertedValue,
        bool hasErrors,
        ICSharpOperation? valueOperationTree = null)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
        Property = property;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        ValueKind = valueKind;
        RequiresBrushConversion = requiresBrushConversion;
        ConvertedValue = convertedValue;
        HasErrors = hasErrors;
        ValueOperationTree = valueOperationTree;
        AdoptCSharpOperationTree(ValueOperationTree);
        Children = ValueOperationTree == null
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(ValueOperationTree);
    }

    public OperationKind Kind => OperationKind.AkcssAssignment;

    public OperationLanguage Language => OperationLanguage.Akcss;

    AkburaSyntax IOperation.Syntax => Syntax;

    public AkcssAssignmentSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

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

    public AkburaConversion ValueConversion { get; }

    public ICSharpOperation? ValueOperationTree { get; }

    public AkcssPropertyValueKind ValueKind { get; }

    public bool RequiresBrushConversion { get; }

    public object? ConvertedValue { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssPropertySetter(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssPropertySetter(this, parameter);
    }

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

    private void AdoptCSharpOperationTree(ICSharpOperation? operation)
    {
        if (operation is CSharpOperation csharpOperation)
        {
            csharpOperation.SetParent(this);
        }
    }
}
