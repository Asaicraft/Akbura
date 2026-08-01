using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using AkburaSymbol = Akbura.Language.Symbols.ISymbol;

namespace Akbura.Language.Operations;

internal abstract class MetadataAkcssOperation : IMetadataAkcssOperation
{
    private ImmutableArray<IAkcssOperation> _akcssChildren =
        ImmutableArray<IAkcssOperation>.Empty;
    private ImmutableArray<IOperation> _children = ImmutableArray<IOperation>.Empty;

    protected MetadataAkcssOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data)
    {
        ContainingAkcssSymbol = containingSymbol;
        Data = data;
    }

    protected MetadataAkcssOperationData Data { get; }

    protected ImmutableArray<IAkcssOperation> AkcssChildren => _akcssChildren;

    public abstract OperationKind Kind { get; }

    public OperationLanguage Language => OperationLanguage.Akcss;

    public AkburaSyntax? Syntax => null;

    public IOperation? Parent { get; private set; }

    public ImmutableArray<IOperation> Children => _children;

    public abstract AkburaSymbol? TargetSymbol { get; }

    public AkburaSymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => Data.Origin != MetadataAkcssOperationOrigin.Direct;

    public virtual bool HasErrors => Data.HasErrors;

    public abstract object? ConstantValue { get; }

    public IAkcssSymbol ContainingAkcssSymbol { get; }

    public AttributeData MetadataAttribute => Data.Attribute;

    public int Order => Data.Order;

    public int ParentOrder => Data.ParentOrder;

    public int Depth => Data.Depth;

    public MetadataAkcssOperationOrigin Origin => Data.Origin;

    public MetadataAkcssOperationPriority Priority => Data.Priority;

    public string? DeclaringSymbolMetadataName => Data.DeclaringSymbol;

    public CSharpSymbolDefinition MetadataTargetType => Data.TargetType == null
        ? default
        : new CSharpSymbolDefinition(Data.TargetType);

    public string? Expression => Data.Expression;

    public CSharpSymbolDefinition ExpressionType => Data.ExpressionType == null
        ? default
        : new CSharpSymbolDefinition(Data.ExpressionType);

    public int ExpandedFromOrder => Data.ExpandedFromOrder;

    public string? SourcePath => Data.SourcePath;

    public TextSpan SourceSpan => Data.SourceStart < 0
        ? default
        : new TextSpan(Data.SourceStart, Data.SourceLength);

    internal void SetTree(
        IOperation? parent,
        ImmutableArray<IAkcssOperation> children)
    {
        Parent = parent;
        _akcssChildren = children.IsDefault
            ? ImmutableArray<IAkcssOperation>.Empty
            : children;
        _children = _akcssChildren.IsEmpty
            ? ImmutableArray<IOperation>.Empty
            : ImmutableArray<IOperation>.CastUp(_akcssChildren);
    }

    public abstract void Accept(OperationVisitor visitor);

    public abstract TResult Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter);

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public abstract string ToDisplayString();

    public override string ToString()
    {
        return ToDisplayString();
    }
}
