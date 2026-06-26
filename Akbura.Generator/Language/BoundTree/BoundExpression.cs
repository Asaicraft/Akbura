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
        : this(
            BoundKind.Expression,
            syntax,
            binder,
            symbolInfo,
            operation,
            diagnostics,
            children)
    {
    }

    protected BoundExpression(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        AkburaOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(kind, syntax, binder, symbolInfo, operation, diagnostics, children)
    {
    }

    public virtual ITypeSymbol? Type => null;

    public BoundExpression Update(
        AkburaSymbolInfo symbolInfo,
        AkburaOperation? operation,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            ReferenceEquals(operation, Operation) &&
            children == Children)
        {
            return this;
        }

        return new BoundExpression(
            Syntax,
            Binder,
            symbolInfo,
            operation,
            Diagnostics,
            children);
    }

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
