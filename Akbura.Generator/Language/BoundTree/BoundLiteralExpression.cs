using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundLiteralExpression : BoundExpression
{
    public BoundLiteralExpression(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        object? constantValue,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.LiteralExpression,
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            operation: null,
            diagnostics)
    {
        BindingResult = bindingResult;
        ConstantValue = constantValue;
    }

    public CSharpBindingResult BindingResult { get; }

    public object? ConstantValue { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override ITypeSymbol? Type => BindingResult.TypeSymbol;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public BoundLiteralExpression Update(
        CSharpBindingResult bindingResult,
        object? constantValue)
    {
        if (bindingResult.Equals(BindingResult) &&
            Equals(constantValue, ConstantValue))
        {
            return this;
        }

        return new BoundLiteralExpression(
            Syntax,
            Binder,
            bindingResult,
            constantValue,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitLiteralExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitLiteralExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitLiteralExpression(this, parameter);
    }
}
