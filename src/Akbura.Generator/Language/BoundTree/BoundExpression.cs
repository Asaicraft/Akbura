using BinderType = Akbura.Language.Binder.Binder;
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
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default,
        bool hasErrors = false)
        : this(
            BoundKind.Expression,
            syntax,
            binder,
            symbolInfo,
            diagnostics,
            children,
            hasErrors)
    {
    }

    protected BoundExpression(
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

    public virtual ITypeSymbol? Type => null;

    public BoundExpression Update(
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            children == Children)
        {
            return this;
        }

        return new BoundExpression(
            Syntax,
            Binder,
            symbolInfo,
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
