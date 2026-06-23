using BinderType = Akbura.Language.Binder.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.BoundTree;

internal sealed class BoundConversionExpression : BoundExpression
{
    public BoundConversionExpression(
        AkburaSyntax syntax,
        BinderType binder,
        BoundExpression operand,
        AkburaConversion conversion,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics = default)
        : base(
            syntax,
            binder,
            operand.SymbolInfo,
            operand.Operation,
            diagnostics,
            ImmutableArray.Create<BoundNode>(operand))
    {
        Operand = operand;
        Conversion = conversion;
    }

    public BoundExpression Operand { get; }

    public AkburaConversion Conversion { get; }

    public override ITypeSymbol? Type => Conversion.TargetType ?? Operand.Type;

    public override bool IsError => !Conversion.Exists || base.IsError;

    public override void Accept(BoundTreeVisitor visitor)
    {
        visitor.VisitConversionExpression(this);
    }

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default
    {
        return visitor.VisitConversionExpression(this);
    }

    public override TResult? Accept<TParameter, TResult>(
        BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default
    {
        return visitor.VisitConversionExpression(this, parameter);
    }
}
