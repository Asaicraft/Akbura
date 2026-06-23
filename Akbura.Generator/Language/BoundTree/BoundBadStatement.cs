using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundBadStatement : BoundStatement
{
    public BoundBadStatement(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(
            syntax,
            binder,
            AkburaSymbolInfo.None(CandidateReason.NotFound),
            operation: null,
            diagnostics,
            children)
    {
    }

    public override bool IsError => true;

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitBadStatement(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitBadStatement(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitBadStatement(this, parameter);
    }
}
