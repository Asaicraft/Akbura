using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupDictionaryKey : BoundNode
{
    public BoundMarkupDictionaryKey(
        MarkupAttachedPropertyAttributeSyntax syntax,
        BinderType binder,
        IMarkupComponentSymbol? containingComponent,
        MarkupDictionaryShape dictionaryShape,
        CSharpBindingResult binding,
        string? literalValue,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        : base(BoundKind.MarkupDictionaryKey, syntax, binder,
            AkburaSymbolInfo.None(CandidateReason.NotFound), diagnostics,
            children: binding.OperationDefinition.IsDefault ? default
                : ImmutableArray.Create<BoundNode>(new BoundCSharpExpression(syntax, binder, binding)))
    {
        ContainingComponent = containingComponent;
        DictionaryShape = dictionaryShape;
        Binding = binding;
        LiteralValue = literalValue;
    }

    public new MarkupAttachedPropertyAttributeSyntax Syntax => (MarkupAttachedPropertyAttributeSyntax)base.Syntax;
    public IMarkupComponentSymbol? ContainingComponent { get; }
    public MarkupDictionaryShape DictionaryShape { get; }
    public CSharpBindingResult Binding { get; }
    public string? LiteralValue { get; }

    public BoundMarkupDictionaryKey Update(IMarkupComponentSymbol? component, CSharpBindingResult binding) =>
        ReferenceEquals(component, ContainingComponent) && binding.Equals(Binding)
            ? this : new BoundMarkupDictionaryKey(Syntax, Binder, component, DictionaryShape,
                binding, LiteralValue, Diagnostics);

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupDictionaryKey(this);
    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default => visitor.VisitMarkupDictionaryKey(this);
    public override TResult? Accept<TParameter, TResult>(BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default => visitor.VisitMarkupDictionaryKey(this, parameter);
}
