using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundCSharpExpression : BoundExpression
{
    public BoundCSharpExpression(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            operation: null,
            diagnostics)
    {
        BindingResult = bindingResult;
    }

    public CSharpBindingResult BindingResult { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override ITypeSymbol? Type => BindingResult.TypeSymbol;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitCSharpExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitCSharpExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitCSharpExpression(this, parameter);
    }
}
