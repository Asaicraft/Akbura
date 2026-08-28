using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal sealed class MetadataAkcssIfOperation
    : MetadataAkcssOperation, IAkcssIfOperation
{
    public MetadataAkcssIfOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data)
        : base(containingSymbol, data)
    {
        ConditionType = data.ExpressionType == null
            ? default
            : new CSharpSymbolDefinition(data.ExpressionType);
    }

    public override OperationKind Kind => OperationKind.AkcssIf;

    AkcssIfDirectiveSyntax? IAkcssIfOperation.Syntax => null;

    public override ISymbol? TargetSymbol => null;

    public override bool HasErrors => base.HasErrors ||
        string.IsNullOrWhiteSpace(Expression) ||
        ConditionType.IsDefault;

    public override object? ConstantValue => MetadataAkcssConstantValue.Parse(
        Data.ConstantValue,
        Data.ConstantValueType);

    public CSharpSymbolDefinition ConditionType { get; }

    public CSharpOperationDefinition ConditionOperation => default;

    public ICSharpOperation? ConditionOperationTree => null;

    public ImmutableArray<IAkcssOperation> Operations => AkcssChildren;

    public override void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssIf(this);
    }

    public override TResult Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssIf(this, parameter)!;
    }

    public override string ToDisplayString()
    {
        return $"@if({Expression ?? "<condition>"})";
    }
}
