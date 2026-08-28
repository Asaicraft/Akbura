using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundUseHookStatement : BoundStatement
{
    public BoundUseHookStatement(
        CSharpStatementSyntax syntax,
        BinderType binder,
        BoundUseHookInvocation invocation,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.UseHookStatement,
            syntax,
            binder,
            invocation.SymbolInfo,
            diagnostics,
            ImmutableArray.Create<BoundNode>(invocation),
            hasErrors: invocation.HasErrors)
    {
        Invocation = invocation;
    }

    public new CSharpStatementSyntax Syntax => (CSharpStatementSyntax)base.Syntax;

    public BoundUseHookInvocation Invocation { get; }

    public BoundUseHookStatement Update(BoundUseHookInvocation invocation)
    {
        if (ReferenceEquals(invocation, Invocation))
        {
            return this;
        }

        return new BoundUseHookStatement(
            Syntax,
            Binder,
            invocation,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitUseHookStatement(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitUseHookStatement(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitUseHookStatement(this, parameter);
    }
}
