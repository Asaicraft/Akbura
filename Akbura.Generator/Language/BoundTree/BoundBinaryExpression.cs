using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.BoundTree;

internal sealed class BoundBinaryExpression : BoundExpression
{
    public BoundBinaryExpression(
        AkburaSyntax syntax,
        BinderType binder,
        CSharpBindingResult bindingResult,
        CSharpSyntaxKind operatorKind,
        BoundExpression left,
        BoundExpression right,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            BoundKind.BinaryExpression,
            syntax,
            binder,
            AkburaSymbolInfo.None(bindingResult.CandidateReason),
            diagnostics,
            ImmutableArray.Create<BoundNode>(left, right),
            hasErrors: bindingResult.Diagnostics.Length != 0)
    {
        BindingResult = bindingResult;
        OperatorKind = operatorKind;
        Left = left;
        Right = right;
    }

    public CSharpBindingResult BindingResult { get; }

    public CSharpSyntaxKind OperatorKind { get; }

    public BoundExpression Left { get; }

    public BoundExpression Right { get; }

    public ImmutableArray<Diagnostic> RoslynDiagnostics => BindingResult.Diagnostics;

    public override ITypeSymbol? Type => BindingResult.TypeSymbol;

    public override bool IsError => RoslynDiagnostics.Length != 0 || base.IsError;

    public BoundExpression Update(
        CSharpBindingResult bindingResult,
        CSharpSyntaxKind operatorKind,
        BoundExpression left,
        BoundExpression right)
    {
        if (bindingResult.Equals(BindingResult) &&
            operatorKind == OperatorKind &&
            ReferenceEquals(left, Left) &&
            ReferenceEquals(right, Right))
        {
            return this;
        }

        return new BoundBinaryExpression(
            Syntax,
            Binder,
            bindingResult,
            operatorKind,
            left,
            right,
            Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitBinaryExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitBinaryExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitBinaryExpression(this, parameter);
    }
}
