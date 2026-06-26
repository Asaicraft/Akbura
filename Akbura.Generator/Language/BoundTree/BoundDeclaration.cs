using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundDeclaration : BoundNode
{
    public BoundDeclaration(
        AkburaSyntax syntax,
        BinderType binder,
        AkburaSymbolInfo symbolInfo,
        IOperation? operation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        ImmutableArray<BoundNode> children = default)
        : base(BoundKind.Declaration, syntax, binder, symbolInfo, operation, diagnostics, children)
    {
    }

    public BoundDeclaration Update(
        AkburaSymbolInfo symbolInfo,
        IOperation? operation,
        ImmutableArray<BoundNode> children)
    {
        if (symbolInfo.Equals(SymbolInfo) &&
            ReferenceEquals(operation, Operation) &&
            children == Children)
        {
            return this;
        }

        return new BoundDeclaration(
            Syntax,
            Binder,
            symbolInfo,
            operation,
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
