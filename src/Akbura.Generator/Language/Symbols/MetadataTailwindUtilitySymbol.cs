using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MetadataTailwindUtilitySymbol : Symbol, ITailwindUtilitySymbol, IMetadataAkcssSymbol
{
    private const string ParameterAttributeName =
        "Akbura.CompilerAnotations.AkcssUtilityParameterAttribute";
    private readonly MetadataAkcssSymbolData _data;
    private ImmutableArray<IAkcssOperation> _operations;

    public MetadataTailwindUtilitySymbol(
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
        Parameters = CreateParameters(carrierType, this);
    }

    public override SymbolKind Kind => SymbolKind.AkcssUtility;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name => _data.Name;

    public override string MetadataName => _data.MetadataName;

    public AkburaSyntax? DeclarationSyntax => null;

    public ImmutableArray<IAkcssOperation> Operations => _operations;

    public string? ClassName => null;

    public bool HasTargetType => _data.TargetType != null;

    public CSharpSymbolDefinition TargetType => _data.TargetType == null
        ? default
        : new CSharpSymbolDefinition(_data.TargetType);

    public bool IsIntercepted => _data.Kind == MetadataAkcssSymbolKind.Intercept;

    public CSharpSymbolDefinition InterceptType => _data.InterceptType == null
        ? default
        : new CSharpSymbolDefinition(_data.InterceptType);

    public ImmutableArray<ITailwindUtilityParameterSymbol> Parameters { get; }

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
        visitor.VisitTailwindUtility(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitTailwindUtility(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitTailwindUtility(this, parameter);
    }

    private static ImmutableArray<ITailwindUtilityParameterSymbol> CreateParameters(
        INamedTypeSymbol carrierType,
        ISymbol containingSymbol)
    {
        using var builder = ImmutableArrayBuilder<ITailwindUtilityParameterSymbol>.Rent();
        foreach (var attribute in carrierType.GetAttributes())
        {
            if (!string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    ParameterAttributeName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var ordinal = MetadataAkcssModuleSymbol.GetNamedInt32(attribute, "Ordinal", -1);
            var name = MetadataAkcssModuleSymbol.GetNamedString(attribute, "Name");
            var type = MetadataAkcssModuleSymbol.GetNamedType(attribute, "Type");
            if (ordinal < 0 || string.IsNullOrWhiteSpace(name) || type == null)
            {
                continue;
            }

            builder.Add(new MetadataTailwindUtilityParameterSymbol(
                containingSymbol,
                ordinal,
                name!,
                MetadataAkcssModuleSymbol.GetNamedString(attribute, "CSharpName"),
                type,
                MetadataAkcssModuleSymbol.GetNamedBoolean(attribute, "IsOptional"),
                carrierType.Locations));
        }

        var parameters = builder.ToImmutable();
        return parameters.Sort(static (left, right) => left.Ordinal.CompareTo(right.Ordinal));
    }
}
