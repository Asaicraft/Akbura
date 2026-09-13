using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupDictionaryKeyOperation : IMarkupDictionaryKeyOperation
{
    public MarkupDictionaryKeyOperation(MarkupAttachedPropertyAttributeSyntax syntax,
        IMarkupComponentSymbol? component, MarkupDictionaryShape shape,
        CSharpBindingResult binding, string? literalValue, bool hasErrors,
        ICSharpOperation? valueOperationTree)
    {
        Syntax = syntax;
        ContainingComponent = component;
        DictionaryShape = shape;
        ValueOperation = binding.OperationDefinition;
        ValueConversion = binding.Conversion;
        LiteralValue = literalValue;
        HasErrors = hasErrors;
        ValueOperationTree = valueOperationTree;
        if (valueOperationTree is CSharpOperation operation)
        {
            operation.SetParent(this);
        }

        Children = valueOperationTree == null ? ImmutableArray<IOperation>.Empty
            : ImmutableArray.Create<IOperation>(valueOperationTree);
    }

    public OperationKind Kind => OperationKind.MarkupDictionaryKey;
    public OperationLanguage Language => OperationLanguage.Markup;
    public MarkupAttachedPropertyAttributeSyntax Syntax { get; }
    AkburaSyntax IOperation.Syntax => Syntax;
    MarkupAttributeSyntax IMarkupAttributeOperation.Syntax => Syntax;
    public IOperation? Parent => null;
    public ImmutableArray<IOperation> Children { get; }
    public ISymbol? TargetSymbol => null;
    public ISymbol? TypeSymbol => null;
    public CSharpOperationDefinition CSharpDefinition => ValueOperation;
    public bool IsImplicit => false;
    public bool HasErrors { get; }
    public bool HasConstantValue => LiteralValue != null || ValueOperation.ConstantValue.HasValue;
    public object? ConstantValue => LiteralValue ??
        (ValueOperation.ConstantValue.HasValue ? ValueOperation.ConstantValue.Value : null);
    public IMarkupComponentSymbol? ContainingComponent { get; }
    public MarkupDictionaryShape DictionaryShape { get; }
    public CSharpSymbolDefinition KeyType => DictionaryShape.KeyType == null ? default
        : new CSharpSymbolDefinition(DictionaryShape.KeyType);
    public CSharpSymbolDefinition ValueType => DictionaryShape.ValueType == null ? default
        : new CSharpSymbolDefinition(DictionaryShape.ValueType);
    public CSharpOperationDefinition ValueOperation { get; }
    public AkburaConversion ValueConversion { get; }
    public ICSharpOperation? ValueOperationTree { get; }
    public MarkupAttributeValueSyntax? ValueSyntax => AkburaSemanticModel.GetMarkupAttributeValue(Syntax);
    public string? LiteralValue { get; }
    public void Accept(OperationVisitor visitor) => visitor.VisitMarkupDictionaryKey(this);
    public TResult? Accept<TParameter, TResult>(OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter) => visitor.VisitMarkupDictionaryKey(this, parameter);
    public bool Equals(IOperation? other) => ReferenceEquals(this, other);
    public override bool Equals(object? obj) => obj is IOperation operation && Equals(operation);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public string ToDisplayString() => Syntax.ToFullString();
    public override string ToString() => ToDisplayString();
}
