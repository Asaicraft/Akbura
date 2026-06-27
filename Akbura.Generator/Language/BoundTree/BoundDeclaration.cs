using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal class BoundDeclaration : BoundNode
{
    public BoundDeclaration(
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : this(BoundKind.Declaration, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    protected BoundDeclaration(
        BoundKind kind,
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(kind, syntax, binder, symbolInfo, diagnostics, children)
    {
    }

    public BoundDeclaration Update(
        AkburaSymbolInfo symbolInfo,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            children == Children)
        {
            return this;
        }

        return new BoundDeclaration(
            Syntax,
            Binder,
            symbolInfo,
            Diagnostics,
            children);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitDeclaration(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitDeclaration(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitDeclaration(this, parameter);
    }
}
