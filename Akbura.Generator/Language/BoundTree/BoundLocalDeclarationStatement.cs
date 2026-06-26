using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundLocalDeclarationStatement : BoundStatement
{
    public BoundLocalDeclarationStatement(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        ImmutableArray<ILocalSymbol> locals,
        ImmutableArray<BoundExpression> initializers,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.LocalDeclarationStatement,
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            operation: null,
            diagnostics,
            BuildChildren(initializers))
    {
        BindingResult = bindingResult;
        Locals = locals.IsDefault
            ? ImmutableArray<ILocalSymbol>.Empty
            : locals;
        Initializers = initializers.IsDefault
            ? ImmutableArray<BoundExpression>.Empty
            : initializers;
    }

    public CSharpBindingResult BindingResult { get; }

    public ImmutableArray<ILocalSymbol> Locals { get; }

    public ImmutableArray<BoundExpression> Initializers { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public BoundLocalDeclarationStatement Update(
        ImmutableArray<ILocalSymbol> locals,
        ImmutableArray<BoundExpression> initializers)
    {
        if (locals == Locals &&
            initializers == Initializers)
        {
            return this;
        }

        return new BoundLocalDeclarationStatement(
            Syntax,
            Binder,
            BindingResult,
            locals,
            initializers,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitLocalDeclarationStatement(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitLocalDeclarationStatement(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitLocalDeclarationStatement(this, parameter);
    }

    private static ImmutableArray<BoundNode> BuildChildren(ImmutableArray<BoundExpression> initializers)
    {
        if (initializers.IsDefaultOrEmpty)
        {
            return ImmutableArray<BoundNode>.Empty;
        }

        var builder = ArrayBuilder<BoundNode>.GetInstance(initializers.Length);
        foreach (var initializer in initializers)
        {
            builder.Add(initializer);
        }

        return builder.ToImmutableAndFree();
    }
}
