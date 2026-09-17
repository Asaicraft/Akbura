using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupForeachKeyOperation : IMarkupForeachKeyOperation
{
    public MarkupForeachKeyOperation(MarkupAttachedPropertyAttributeSyntax syntax,
        IMarkupComponentSymbol? component, CSharpBindingResult binding, bool isIterationKey,
        bool hasErrors, ICSharpOperation? valueOperationTree)
    {
        Syntax = syntax;
        ContainingComponent = component;
        ValueOperation = binding.OperationDefinition;
        IsIterationKey = isIterationKey;
        HasErrors = hasErrors;
        ValueOperationTree = valueOperationTree;
        if (valueOperationTree is CSharpOperation operation)
        {
            operation.SetParent(this);
        }
        Children = valueOperationTree == null ? ImmutableArray<IOperation>.Empty :
            ImmutableArray.Create<IOperation>(valueOperationTree);
    }

    public MarkupAttachedPropertyAttributeSyntax Syntax { get; }
    AkburaSyntax IOperation.Syntax => Syntax;
    MarkupAttributeSyntax IMarkupAttributeOperation.Syntax => Syntax;
    public IMarkupComponentSymbol? ContainingComponent { get; }
    public CSharpOperationDefinition ValueOperation { get; }
    public CSharpSymbolDefinition KeyType => ValueOperation.Type == null ? default : new(ValueOperation.Type);
    public MarkupAttributeValueSyntax? ValueSyntax => AkburaSemanticModel.GetMarkupAttributeValue(Syntax);
    public ICSharpOperation? ValueOperationTree { get; }
    public bool IsIterationKey { get; }
    public OperationKind Kind => OperationKind.MarkupForeachKey;
    public OperationLanguage Language => OperationLanguage.Markup;
    public IOperation? Parent => null;
    public ImmutableArray<IOperation> Children { get; }
    public ISymbol? TargetSymbol => null;
    public ISymbol? TypeSymbol => null;
    public CSharpOperationDefinition CSharpDefinition => ValueOperation;
    public bool IsImplicit => false;
    public bool HasErrors { get; }
    public object? ConstantValue => ValueOperation.ConstantValue.HasValue ? ValueOperation.ConstantValue.Value : null;
    public void Accept(OperationVisitor visitor) => visitor.VisitMarkupForeachKey(this);
    public TResult? Accept<TParameter, TResult>(OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter) => visitor.VisitMarkupForeachKey(this, parameter);
    public bool Equals(IOperation? other) => ReferenceEquals(this, other);
    public override bool Equals(object? obj) => obj is IOperation operation && Equals(operation);
    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);
    public string ToDisplayString() => Syntax.ToFullString();
}
