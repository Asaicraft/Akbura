using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;

namespace Akbura.Language.CodeGeneration;

internal enum AkcssSymbolGenerationKind : byte
{
    Style,
    Utility,
    InterceptMetadata,
}

internal enum AkcssRuntimeStyleKind : byte
{
    Generated,
    Interceptor,
}

/// <summary>
/// Describes one declared AKCSS symbol and its emitted runtime position.
/// </summary>
internal readonly struct AkcssSymbolGenerationPlan
{
    public AkcssSymbolGenerationPlan(
        int symbolIndex,
        int runtimeStyleIndex,
        IAkcssSymbol symbol,
        AkcssSymbolGenerationKind kind,
        bool hasErrors)
    {
        SymbolIndex = symbolIndex;
        RuntimeStyleIndex = runtimeStyleIndex;
        Symbol = symbol;
        Kind = kind;
        HasErrors = hasErrors;
    }

    public int SymbolIndex { get; }

    /// <summary>
    /// Index in the generated Styles collection, or -1 when the symbol
    /// has no runtime instance.
    /// </summary>
    public int RuntimeStyleIndex { get; }

    public IAkcssSymbol Symbol { get; }

    public AkcssSymbolGenerationKind Kind { get; }

    public bool HasErrors { get; }

    public bool EmitsRuntimeStyle => RuntimeStyleIndex >= 0;
}

/// <summary>
/// Describes one entry emitted into the runtime Styles collection.
/// </summary>
internal readonly struct AkcssRuntimeStylePlan
{
    public AkcssRuntimeStylePlan(
        int runtimeStyleIndex,
        int symbolIndex,
        AkcssRuntimeStyleKind kind,
        INamedTypeSymbol? interceptorType)
    {
        RuntimeStyleIndex = runtimeStyleIndex;
        SymbolIndex = symbolIndex;
        Kind = kind;
        InterceptorType = interceptorType;
    }

    public int RuntimeStyleIndex { get; }

    public int SymbolIndex { get; }

    public AkcssRuntimeStyleKind Kind { get; }

    public INamedTypeSymbol? InterceptorType { get; }
}

/// <summary>
/// Contains the immutable generation plan for one AKCSS module.
/// </summary>
internal readonly struct AkcssModulePlan
{
    public AkcssModulePlan(
        string sourcePath,
        string moduleIdentity,
        string generatedNamespace,
        string generatedTypeName,
        string metadataName,
        bool isInlined,
        PooledImmutableList<AkcssSymbolGenerationPlan> symbols,
        PooledImmutableList<AkcssRuntimeStylePlan> runtimeStyles)
    {
        SourcePath = sourcePath;
        ModuleIdentity = moduleIdentity;
        GeneratedNamespace = generatedNamespace;
        GeneratedTypeName = generatedTypeName;
        MetadataName = metadataName;
        IsInlined = isInlined;
        Symbols = symbols;
        RuntimeStyles = runtimeStyles;
    }

    public string SourcePath { get; }

    public string ModuleIdentity { get; }

    public string GeneratedNamespace { get; }

    public string GeneratedTypeName { get; }

    public string MetadataName { get; }

    public bool IsInlined { get; }

    public PooledImmutableList<AkcssSymbolGenerationPlan> Symbols { get; }

    public PooledImmutableList<AkcssRuntimeStylePlan> RuntimeStyles { get; }

    internal void ReturnToPool()
    {
        Symbols.ReturnToPool();
        RuntimeStyles.ReturnToPool();
    }
}
