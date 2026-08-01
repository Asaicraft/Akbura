using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MetadataAkcssStyleSymbol : Symbol, IMetadataAkcssSymbol
{
    private readonly MetadataAkcssSymbolData _data;
    private ImmutableArray<IAkcssOperation> _operations;

    public MetadataAkcssStyleSymbol(
        IMetadataAkcssModuleSymbol module,
        INamedTypeSymbol carrierType,
        MetadataAkcssSymbolData data)
        : base(
            module,
            carrierType.Locations,
            isImplicitlyDeclared: true)
    {
        MetadataModule = module;
        MetadataCarrierType = carrierType;
        _data = data;
        _operations = ImmutableArray<IAkcssOperation>.Empty;
    }

    public override SymbolKind Kind => SymbolKind.AkcssClass;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name => _data.Name;

    public override string MetadataName => _data.MetadataName;

    public AkburaSyntax? DeclarationSyntax => null;

    public ImmutableArray<IAkcssOperation> Operations => _operations;

    public string? ClassName => _data.ClassName;

    public bool HasTargetType => _data.TargetType != null;

    public CSharpSymbolDefinition TargetType => _data.TargetType == null
        ? default
        : new CSharpSymbolDefinition(_data.TargetType);

    public bool IsIntercepted => _data.Kind == MetadataAkcssSymbolKind.Intercept;

    public CSharpSymbolDefinition InterceptType => _data.InterceptType == null
        ? default
        : new CSharpSymbolDefinition(_data.InterceptType);

    public IMetadataAkcssModuleSymbol MetadataModule { get; }

    public INamedTypeSymbol MetadataCarrierType { get; }

    public int RuntimeStyleIndex => _data.RuntimeStyleIndex;

    public bool HasErrors => _data.HasErrors;

    public ImmutableArray<string> ObservedProperties => _data.ObservedProperties;

    public ImmutableArray<AttributeData> OperationAttributes =>
        _data.OperationAttributes;

    public void SetOperations(ImmutableArray<IAkcssOperation> operations)
    {
        _operations = operations.IsDefault
            ? ImmutableArray<IAkcssOperation>.Empty
            : operations;
    }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitAkcss(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitAkcss(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcss(this, parameter);
    }
}
