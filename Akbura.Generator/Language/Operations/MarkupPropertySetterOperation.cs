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
        CSharpSymbolDefinition valueType,
        CSharpOperationDefinition valueOperation,
        MarkupAttributeBindingKind bindingKind,
        MarkupAttributeValueKind valueKind,
        MarkupAttributeValueSyntax? valueSyntax,
        string? literalValue,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        Property = property;
        ValueType = valueType;
        ValueOperation = valueOperation;
        BindingKind = bindingKind;
        ValueKind = valueKind;
        ValueSyntax = valueSyntax;
        LiteralValue = literalValue;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.MarkupAttribute;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => Property;

    public ISymbol? TypeSymbol => Property;

    public CSharpOperationDefinition CSharpDefinition => ValueOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => LiteralValue;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public IPropertySymbol? Property { get; }

    public CSharpSymbolDefinition ValueType { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public MarkupAttributeBindingKind BindingKind { get; }

    public MarkupAttributeValueKind ValueKind { get; }

    public MarkupAttributeValueSyntax? ValueSyntax { get; }

    public string? LiteralValue { get; }

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
}
