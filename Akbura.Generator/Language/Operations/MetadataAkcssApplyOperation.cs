using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal sealed class MetadataAkcssApplyOperation
    : MetadataAkcssOperation, IAkcssApplyOperation, IMetadataAkcssApplyOperation
{
    public MetadataAkcssApplyOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data,
        ImmutableArray<IAkcssSymbol> appliedSymbols)
        : base(containingSymbol, data)
    {
        AppliedSymbols = appliedSymbols.IsDefault
            ? []
            : appliedSymbols;
    }

    public override OperationKind Kind => OperationKind.AkcssApply;

    AkcssApplyDirectiveSyntax? IAkcssApplyOperation.Syntax => null;

    public override ISymbol? TargetSymbol => null;

    public override bool HasErrors => base.HasErrors ||
        AppliedSymbols.Length != AppliedSymbolMetadataNames.Length;

    public override object? ConstantValue => null;

    public ImmutableArray<string> Items => Data.ApplyItems;

    public ImmutableArray<IAkcssSymbol> AppliedSymbols { get; }

    public ImmutableArray<string> AppliedSymbolMetadataNames => Data.AppliedSymbols;

    public ImmutableArray<IAkcssOperation> ExpandedOperations => AkcssChildren;

    public override void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssApply(this);
    }

    public override TResult Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssApply(this, parameter)!;
    }

    public override string ToDisplayString()
    {
        return "@apply " + string.Join(" ", Items);
    }
}
