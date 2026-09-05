using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;

namespace Akbura.Language.CodeGeneration;

/// <summary>
/// Creates the top-level generation plan for one AKCSS module.
/// </summary>
internal static class AkcssModulePlanner
{
    public static AkcssModulePlan Create(
        in AkcssGenerationInput input,
        string rootNamespace,
        CancellationToken cancellationToken = default)
    {
        var module = input.Module;
        var moduleSymbols = module.AkcssSymbols;

        using var symbols = ImmutableArrayBuilder<AkcssSymbolGenerationPlan>.Rent(moduleSymbols.Length);

        using var runtimeStyles = ImmutableArrayBuilder<AkcssRuntimeStylePlan>.Rent(moduleSymbols.Length);

        for (var symbolIndex = 0; symbolIndex < moduleSymbols.Length; symbolIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var symbol = moduleSymbols[symbolIndex];
            var kind = GetGenerationKind(symbol);
            var runtimeStyleIndex = -1;

            if (symbol.IsIntercepted)
            {
                if (TryGetInterceptorType(symbol, out var interceptorType))
                {
                    runtimeStyleIndex = runtimeStyles.Count;

                    runtimeStyles.Add(
                        new AkcssRuntimeStylePlan(
                            runtimeStyleIndex,
                            symbolIndex,
                            AkcssRuntimeStyleKind.Interceptor,
                            interceptorType));
                }
            }
            else
            {
                runtimeStyleIndex = runtimeStyles.Count;

                runtimeStyles.Add(
                    new AkcssRuntimeStylePlan(
                        runtimeStyleIndex,
                        symbolIndex,
                        AkcssRuntimeStyleKind.Generated,
                        interceptorType: null));
            }

            symbols.Add(
                new AkcssSymbolGenerationPlan(
                    symbolIndex,
                    runtimeStyleIndex,
                    symbol,
                    kind,
                    HasErrors(symbol.Operations)));
        }

        var sourcePath = AkcssGeneratedModuleNames.NormalizeSourcePath(input.SourcePath);
        var moduleIdentity = AkcssGeneratedModuleNames.NormalizeSourcePath(input.ModuleIdentity);
        var generatedNamespace = AkcssGeneratedModuleNames.GetNamespaceName(rootNamespace);
        var generatedTypeName = AkcssGeneratedModuleNames.GetTypeName(moduleIdentity);

        var pooledSymbols = default(PooledImmutableList<AkcssSymbolGenerationPlan>);
        var pooledRuntimeStyles = default(PooledImmutableList<AkcssRuntimeStylePlan>);

        try
        {
            pooledSymbols = symbols.ToPooledImmutableList();

            pooledRuntimeStyles = runtimeStyles.ToPooledImmutableList();

            return new AkcssModulePlan(
                sourcePath,
                moduleIdentity,
                generatedNamespace,
                generatedTypeName,
                module.MetadataName,
                module.IsInlined,
                pooledSymbols,
                pooledRuntimeStyles);
        }
        catch
        {
            pooledSymbols.ReturnToPool();
            pooledRuntimeStyles.ReturnToPool();
            throw;
        }
    }

    private static AkcssSymbolGenerationKind GetGenerationKind(IAkcssSymbol symbol)
    {
        if (symbol.IsIntercepted)
        {
            return AkcssSymbolGenerationKind.InterceptMetadata;
        }

        return symbol is ITailwindUtilitySymbol
            ? AkcssSymbolGenerationKind.Utility
            : AkcssSymbolGenerationKind.Style;
    }

    private static bool TryGetInterceptorType(
        IAkcssSymbol symbol,
        out INamedTypeSymbol interceptorType)
    {
        interceptorType = null!;

        if (symbol.InterceptType.Symbol is not
                INamedTypeSymbol type ||
            type.IsAbstract)
        {
            return false;
        }

        var constructors = type.InstanceConstructors;

        for (var i = 0; i < constructors.Length; i++)
        {
            var constructor = constructors[i];

            if (constructor.Parameters.Length == 0 &&
                constructor.DeclaredAccessibility is
                    Accessibility.Public or
                    Accessibility.Internal)
            {
                interceptorType = type;
                return true;
            }
        }

        return false;
    }

    private static bool HasErrors(ImmutableArray<IAkcssOperation> operations)
    {
        for (var i = 0; i < operations.Length; i++)
        {
            var operation = operations[i];

            if (operation.HasErrors)
            {
                return true;
            }

            if (operation is IAkcssIfOperation ifOperation &&
                HasErrors(ifOperation.Operations))
            {
                return true;
            }
        }

        return false;
    }
}
