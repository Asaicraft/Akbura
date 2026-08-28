using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundCSharpStatement : BoundStatement
{
    public BoundCSharpStatement(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default,
        ImmutableArray<BoundNode> children = default)
        : base(
            BoundKind.CSharpStatement,
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            diagnostics,
            children,
            hasErrors: bindingResult.Diagnostics.Length != 0)
    {
        BindingResult = bindingResult;
    }

    public CSharpBindingResult BindingResult { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public BoundCSharpStatement Update(
        CSharpBindingResult bindingResult,
        ImmutableArray<BoundNode> children)
    {
        if (bindingResult.Equals(BindingResult) && children == Children)
        {
            return this;
        }

        return new BoundCSharpStatement(
            Syntax,
            Binder,
            bindingResult,
            Diagnostics,
            children);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitCSharpStatement(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitCSharpStatement(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitCSharpStatement(this, parameter);
    }
}
