using BinderType = Akbura.Language.Binder.Binder;
using AkburaOperation = Akbura.Language.Operations.IOperation;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal class BoundExpression : BoundNode
{
    public BoundExpression(
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        AkburaOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(syntax, binder, symbolInfo, operation, diagnostics, children)
    {
    }

    public virtual ITypeSymbol? Type => null;

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitExpression(this, parameter);
    }
}
