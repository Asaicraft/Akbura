using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.BoundTree;

internal sealed class BoundBlock : BoundStatement
{
    public BoundBlock(
        AkburaSyntax syntax,
        BinderType binder,
        ImmutableArray<AkburaSymbol> declaredSymbols,
        ImmutableArray<BoundNode> statements,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.Block,
            syntax,
            binder,
            AkburaSymbolInfo.None(CandidateReason.None),
            operation: null,
            diagnostics,
            statements)
    {
        DeclaredSymbols = declaredSymbols.IsDefault
            ? ImmutableArray<AkburaSymbol>.Empty
            : declaredSymbols;
        Statements = Children;
    }

    public ImmutableArray<AkburaSymbol> DeclaredSymbols { get; }

    public ImmutableArray<BoundNode> Statements { get; }

    public BoundBlock Update(
        ImmutableArray<AkburaSymbol> declaredSymbols,
        ImmutableArray<BoundNode> statements)
    {
        if (declaredSymbols == DeclaredSymbols &&
            statements == Statements)
        {
            return this;
        }

        return new BoundBlock(
            Syntax,
            Binder,
            declaredSymbols,
            statements,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitBlock(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitBlock(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitBlock(this, parameter);
    }
}
