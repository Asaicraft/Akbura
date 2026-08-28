using Akbura.Language.Syntax;
using Akbura.Language.Operations;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Symbols;

internal sealed class MetadataAkcssModuleSymbol : Symbol, IMetadataAkcssModuleSymbol
{
    private const string ModuleAttributeName =
        "Akbura.CompilerAnotations.AkcssModuleAttribute";
    private const string SymbolAttributeName =
        "Akbura.CompilerAnotations.AkcssSymbolAttribute";

    private ImmutableArray<IAkcssSymbol> _akcssSymbols;

    private MetadataAkcssModuleSymbol(
        INamedTypeSymbol runtimeModuleType,
        string sourcePath,
        string metadataName,
        int formatVersion)
        : base(
            locations: runtimeModuleType.Locations,
            isImplicitlyDeclared: true)
    {
        RuntimeModuleType = runtimeModuleType;
        SourcePath = sourcePath;
        MetadataName = metadataName;
        FormatVersion = formatVersion;
        _akcssSymbols = ImmutableArray<IAkcssSymbol>.Empty;
    }

    public override SymbolKind Kind => SymbolKind.AkcssModule;

    public override SymbolLanguage Language => SymbolLanguage.Akcss;

    public override string Name => SourcePath;

    public override string MetadataName { get; }

    public bool IsInlined => false;

    public new IAkburaComponentSymbol? ContainingSymbol => null;

    public ImmutableArray<IAkcssSymbol> AkcssSymbols => _akcssSymbols;

    public string? Path => SourcePath;

    public AkburaSyntax? DeclaringSyntax => null;

    public INamedTypeSymbol RuntimeModuleType { get; }

    public string SourcePath { get; }

    public int FormatVersion { get; }

    public static bool TryCreate(
        INamedTypeSymbol runtimeModuleType,
        out MetadataAkcssModuleSymbol module)
    {
        module = null!;
        var moduleAttribute = FindAttribute(
            runtimeModuleType.GetAttributes(),
            ModuleAttributeName);
        if (moduleAttribute == null ||
            !TryGetConstructorString(moduleAttribute, 0, out var sourcePath))
        {
            return false;
        }

        var metadataName = GetNamedString(moduleAttribute, "MetadataName");
        if (string.IsNullOrWhiteSpace(metadataName))
        {
            metadataName = sourcePath;
        }

        var formatVersion = GetNamedInt32(moduleAttribute, "FormatVersion", 1);
        module = new MetadataAkcssModuleSymbol(
            runtimeModuleType,
            sourcePath,
            metadataName!,
            formatVersion);

        using var symbols = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
        foreach (var nestedType in runtimeModuleType.GetTypeMembers())
        {
            var symbolAttribute = FindAttribute(
                nestedType.GetAttributes(),
                SymbolAttributeName);
            if (symbolAttribute == null ||
                !MetadataAkcssSymbolData.TryCreate(
                    nestedType,
                    symbolAttribute,
                    out var data))
            {
                continue;
            }

            symbols.Add(data.Kind == MetadataAkcssSymbolKind.Utility
                ? new MetadataTailwindUtilitySymbol(module, nestedType, data)
                : new MetadataAkcssStyleSymbol(module, nestedType, data));
        }

        module._akcssSymbols = symbols.ToImmutable();
        return true;
    }

    internal void InitializeOperations(
        CSharpCompilation compilation,
        ImmutableArray<IAkcssSymbol> availableSymbols)
    {
        foreach (var symbol in _akcssSymbols)
        {
            if (symbol is IMetadataAkcssSymbol metadataSymbol)
            {
                metadataSymbol.SetOperations(
                    MetadataAkcssOperationFactory.CreateOperations(
                        metadataSymbol,
                        compilation,
                        availableSymbols));
            }
        }
    }

    public override void Accept(SymbolVisitor visitor)
    {
        visitor.VisitAkcssModule(this);
    }

    public override TResult Accept<TResult>(SymbolVisitor<TResult> visitor)
    {
        return visitor.VisitAkcssModule(this);
    }

    public override TResult Accept<TParameter, TResult>(
        SymbolVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssModule(this, parameter);
    }

    public override string ToDisplayString()
    {
        return $"metadata akcss {MetadataName}";
    }

    internal static AttributeData? FindAttribute(
        ImmutableArray<AttributeData> attributes,
        string metadataName)
    {
        foreach (var attribute in attributes)
        {
            if (string.Equals(
                    attribute.AttributeClass?.ToDisplayString(),
                    metadataName,
                    StringComparison.Ordinal))
            {
                return attribute;
            }
        }

        return null;
    }

    internal static bool TryGetConstructorString(
        AttributeData attribute,
        int index,
        out string value)
    {
        if (index < attribute.ConstructorArguments.Length &&
            attribute.ConstructorArguments[index].Value is string text)
        {
            value = text;
            return true;
        }

        value = string.Empty;
        return false;
    }

    internal static string? GetNamedString(
        AttributeData attribute,
        string name)
    {
        return GetNamedValue(attribute, name).Value as string;
    }

    internal static int GetNamedInt32(
        AttributeData attribute,
        string name,
        int defaultValue = 0)
    {
        return GetNamedValue(attribute, name).Value is int value
            ? value
            : defaultValue;
    }

    internal static bool GetNamedBoolean(
        AttributeData attribute,
        string name)
    {
        return GetNamedValue(attribute, name).Value is true;
    }

    internal static ITypeSymbol? GetNamedType(
        AttributeData attribute,
        string name)
    {
        return GetNamedValue(attribute, name).Value as ITypeSymbol;
    }

    private static TypedConstant GetNamedValue(
        AttributeData attribute,
        string name)
    {
        foreach (var pair in attribute.NamedArguments)
        {
            if (string.Equals(pair.Key, name, StringComparison.Ordinal))
            {
                return pair.Value;
            }
        }

        return default;
    }
}

internal enum MetadataAkcssSymbolKind
{
    Style,
    Utility,
    Intercept,
}

internal readonly struct MetadataAkcssSymbolData
{
    private const string OperationAttributeName =
        "Akbura.CompilerAnotations.AkcssOperationAttribute";
    private const string ObservesPropertyAttributeName =
        "Akbura.CompilerAnotations.ObservesPropertyAttribute";

    private MetadataAkcssSymbolData(
        string name,
        string metadataName,
        MetadataAkcssSymbolKind kind,
        ITypeSymbol? targetType,
        ITypeSymbol? interceptType,
        string? className,
        int runtimeStyleIndex,
        bool hasErrors,
        ImmutableArray<string> observedProperties,
        ImmutableArray<AttributeData> operationAttributes)
    {
        Name = name;
        MetadataName = metadataName;
        Kind = kind;
        TargetType = targetType;
        InterceptType = interceptType;
        ClassName = className;
        RuntimeStyleIndex = runtimeStyleIndex;
        HasErrors = hasErrors;
        ObservedProperties = observedProperties;
        OperationAttributes = operationAttributes;
    }

    public string Name { get; }

    public string MetadataName { get; }

    public MetadataAkcssSymbolKind Kind { get; }

    public ITypeSymbol? TargetType { get; }

    public ITypeSymbol? InterceptType { get; }

    public string? ClassName { get; }

    public int RuntimeStyleIndex { get; }

    public bool HasErrors { get; }

    public ImmutableArray<string> ObservedProperties { get; }

    public ImmutableArray<AttributeData> OperationAttributes { get; }

    public static bool TryCreate(
        INamedTypeSymbol carrierType,
        AttributeData attribute,
        out MetadataAkcssSymbolData data)
    {
        var name = MetadataAkcssModuleSymbol.GetNamedString(attribute, "Name");
        var metadataName = MetadataAkcssModuleSymbol.GetNamedString(
            attribute,
            "MetadataName");
        var kindValue = MetadataAkcssModuleSymbol.GetNamedInt32(attribute, "Kind");
        if (string.IsNullOrWhiteSpace(name) ||
            string.IsNullOrWhiteSpace(metadataName) ||
            !Enum.IsDefined(typeof(MetadataAkcssSymbolKind), kindValue))
        {
            data = default;
            return false;
        }

        using var operations = ImmutableArrayBuilder<AttributeData>.Rent();
        using var observedProperties = ImmutableArrayBuilder<string>.Rent();
        foreach (var candidate in carrierType.GetAttributes())
        {
            if (string.Equals(
                    candidate.AttributeClass?.ToDisplayString(),
                    OperationAttributeName,
                    StringComparison.Ordinal))
            {
                operations.Add(candidate);
            }

            if (string.Equals(
                    candidate.AttributeClass?.ToDisplayString(),
                    ObservesPropertyAttributeName,
                    StringComparison.Ordinal) &&
                MetadataAkcssModuleSymbol.TryGetConstructorString(
                    candidate,
                    0,
                    out var propertyName) &&
                !string.IsNullOrWhiteSpace(propertyName))
            {
                observedProperties.Add(propertyName);
            }
        }

        data = new MetadataAkcssSymbolData(
            name!,
            metadataName!,
            (MetadataAkcssSymbolKind)kindValue,
            MetadataAkcssModuleSymbol.GetNamedType(attribute, "TargetType"),
            MetadataAkcssModuleSymbol.GetNamedType(attribute, "InterceptType"),
            MetadataAkcssModuleSymbol.GetNamedString(attribute, "ClassName"),
            MetadataAkcssModuleSymbol.GetNamedInt32(
                attribute,
                "RuntimeStyleIndex",
                -1),
            MetadataAkcssModuleSymbol.GetNamedBoolean(attribute, "HasErrors"),
            observedProperties.ToImmutable(),
            operations.ToImmutable());
        return true;
    }
}
