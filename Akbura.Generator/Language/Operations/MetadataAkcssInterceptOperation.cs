using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal sealed class MetadataAkcssInterceptOperation
    : MetadataAkcssOperation, IAkcssInterceptOperation
{
    public MetadataAkcssInterceptOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data)
        : base(containingSymbol, data)
    {
        InterceptType = data.InterceptType == null
            ? default
            : new CSharpSymbolDefinition(data.InterceptType);
    }

    public override OperationKind Kind => OperationKind.AkcssIntercept;

    AkcssInterceptDirectiveSyntax? IAkcssInterceptOperation.Syntax => null;

    public override ISymbol? TargetSymbol => null;

    public override bool HasErrors => base.HasErrors || InterceptType.IsDefault;

    public override object? ConstantValue => null;

    public CSharpSymbolDefinition InterceptType { get; }

    public override void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssIntercept(this);
    }

    public override TResult Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssIntercept(this, parameter)!;
    }

    public override string ToDisplayString()
    {
        return "@intercept " + InterceptType.ToDisplayString();
    }
}
