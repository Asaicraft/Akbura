using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupNameAssignment : BoundMarkupAttribute
{
    public BoundMarkupNameAssignment(
        MarkupAttachedPropertyAttributeSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IMarkupComponentSymbol? containingComponent,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        bool hasErrors = false)
        : base(
            BoundKind.MarkupNameAssignment,
            syntax,
            binder,
            symbolInfo,
            containingComponent,
            diagnostics,
            hasErrors: hasErrors)
    {
        NameSymbol = symbolInfo.Symbol as IMarkupNameSymbol;
    }

    public new MarkupAttachedPropertyAttributeSyntax Syntax =>
        (MarkupAttachedPropertyAttributeSyntax)base.Syntax;

    public IMarkupNameSymbol? NameSymbol { get; }

    public bool IsAssignedDuringFirstUpdate => true;

    public BoundMarkupNameAssignment Update(
        AkburaSymbolInfo symbolInfo,
        IMarkupComponentSymbol? containingComponent)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            ReferenceEquals(containingComponent, ContainingComponent))
        {
            return this;
        }

        return new BoundMarkupNameAssignment(
            Syntax,
            Binder,
            symbolInfo,
            containingComponent,
            Diagnostics,
            HasErrors);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupNameAssignment(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default =>
        visitor.VisitMarkupNameAssignment(this);

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default =>
        visitor.VisitMarkupNameAssignment(this, parameter);
}
