using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal abstract class BoundStatement : BoundNode
{
    protected BoundStatement(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
        : base(kind, syntax, binder, symbolInfo, diagnostics, children, hasErrors)
    {
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitStatement(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitStatement(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitStatement(this, parameter);
    }
}
