using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupForeachKey : BoundNode
{
    public BoundMarkupForeachKey(MarkupAttachedPropertyAttributeSyntax syntax, BinderType binder,
        IMarkupComponentSymbol? component, CSharpBindingResult binding, bool isIterationKey,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        : base(BoundKind.MarkupForeachKey, syntax, binder,
            AkburaSymbolInfo.None(CandidateReason.None), diagnostics,
            children: binding.OperationDefinition.IsDefault ? default :
                ImmutableArray.Create<BoundNode>(new BoundCSharpExpression(syntax, binder, binding)))
    {
        ContainingComponent = component;
        Binding = binding;
        IsIterationKey = isIterationKey;
    }

    public new MarkupAttachedPropertyAttributeSyntax Syntax => (MarkupAttachedPropertyAttributeSyntax)base.Syntax;
    public IMarkupComponentSymbol? ContainingComponent { get; }
    public CSharpBindingResult Binding { get; }
    public bool IsIterationKey { get; }
    public BoundMarkupForeachKey Update(IMarkupComponentSymbol? component, CSharpBindingResult binding) =>
        ReferenceEquals(component, ContainingComponent) && binding.Equals(Binding) ? this :
            new(Syntax, Binder, component, binding, IsIterationKey, Diagnostics);
    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupForeachKey(this);
    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default => visitor.VisitMarkupForeachKey(this);
    public override TResult? Accept<TParameter, TResult>(BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default => visitor.VisitMarkupForeachKey(this, parameter);
}
