using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundErrorExpression : BoundBadExpression
{
    public BoundErrorExpression(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(
            BoundKind.ErrorExpression,
            syntax,
            binder,
            diagnostics,
            children)
    {
    }

    public override BoundBadExpression Update(ImmutableArray<BoundNode> children)
    {
        if (children == Children)
        {
            return this;
        }

        return new BoundErrorExpression(
            Syntax,
            Binder,
            Diagnostics,
            children);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitErrorExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitErrorExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitErrorExpression(this, parameter);
    }
}
