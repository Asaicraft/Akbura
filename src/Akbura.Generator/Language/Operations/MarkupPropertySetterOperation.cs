using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupPropertySetterOperation : IMarkupPropertySetterOperation
{
    public MarkupPropertySetterOperation(
        MarkupAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        IPropertySymbol? property,
        ImmutableArray<IAkcssSymbol> appliedAkcssSymbols,
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        AkburaConversion valueConversion,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        string? literalValue,
        object? convertedValue,
        bool hasErrors,
        ICSharpOperation? valueOperationTree = null)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        Property = property;
        AppliedAkcssSymbols = appliedAkcssSymbols.IsDefault
            ? ImmutableArray<IAkcssSymbol>.Empty
            : appliedAkcssSymbols;
        ValueType = valueType;
        ValueOperation = valueOperation;
        ValueConversion = valueConversion;
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        LiteralValue = literalValue;
        ConvertedValue = convertedValue;
        HasErrors = hasErrors;
        ValueOperationTree = valueOperationTree;
        AdoptCSharpOperationTree(ValueOperationTree);
        Children = ValueOperationTree == null
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(ValueOperationTree);
    }

    public OperationKind Kind => OperationKind.MarkupAttribute;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

    public ISymbol? TargetSymbol => Property;

    public ISymbol? TypeSymbol => Property;

    public CSharpOperationDefinition CSharpDefinition => ValueOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => ConvertedValue ?? LiteralValue;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public ImmutableArray<IAkcssSymbol> AppliedAkcssSymbols { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public AkburaConversion ValueConversion { get; }

    public ICSharpOperation? ValueOperationTree { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public string? LiteralValue { get; }

    public object? ConvertedValue { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitMarkupPropertySetter(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitMarkupPropertySetter(this, parameter);
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
            : $"{Property.Name}={ValueSyntax?.ToFullString() ?? string.Empty}";
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
