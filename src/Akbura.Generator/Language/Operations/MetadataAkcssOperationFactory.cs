using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal static class MetadataAkcssOperationFactory
{
    public static ImmutableArray<IAkcssOperation> CreateOperations(
        IMetadataAkcssSymbol containingSymbol,
        CSharpCompilation compilation,
        ImmutableArray<IAkcssSymbol> availableSymbols)
    {
        var data = new List<MetadataAkcssOperationData>(
            containingSymbol.OperationAttributes.Length);
        foreach (var attribute in containingSymbol.OperationAttributes)
        {
            if (MetadataAkcssOperationData.TryCreate(attribute, out var operationData))
            {
                data.Add(operationData);
            }
        }

        data.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        var symbolsByMetadataName = CreateSymbolLookup(availableSymbols);
        var operationsByOrder = new Dictionary<int, MetadataAkcssOperation>();
        foreach (var operationData in data)
        {
            if (operationsByOrder.ContainsKey(operationData.Order))
            {
                continue;
            }

            operationsByOrder.Add(
                operationData.Order,
                CreateOperation(
                    containingSymbol,
                    operationData,
                    compilation,
                    symbolsByMetadataName));
        }

        var childrenByOrder = new Dictionary<int, List<IAkcssOperation>>();
        foreach (var operation in operationsByOrder.Values)
        {
            if (operation.ParentOrder < 0 ||
                !operationsByOrder.ContainsKey(operation.ParentOrder))
            {
                continue;
            }

            if (!childrenByOrder.TryGetValue(operation.ParentOrder, out var children))
            {
                children = [];
                childrenByOrder.Add(operation.ParentOrder, children);
            }

            children.Add(operation);
        }

        using var roots = ImmutableArrayBuilder<IAkcssOperation>.Rent();
        foreach (var operationData in data)
        {
            if (!operationsByOrder.TryGetValue(operationData.Order, out var operation))
            {
                continue;
            }

            var parent = operation.ParentOrder >= 0 &&
                operationsByOrder.TryGetValue(operation.ParentOrder, out var parentOperation)
                    ? parentOperation
                    : null;
            var children = childrenByOrder.TryGetValue(operation.Order, out var childList)
                ? ImmutableArray.CreateRange(childList)
                : ImmutableArray<IAkcssOperation>.Empty;
            operation.SetTree(parent, children);

            if (parent == null)
            {
                roots.Add(operation);
            }
        }

        return roots.ToImmutable();
    }

    private static MetadataAkcssOperation CreateOperation(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data,
        CSharpCompilation compilation,
        IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> symbolsByMetadataName)
    {
        return data.Kind switch
        {
            MetadataAkcssOperationKind.Set =>
                new MetadataAkcssPropertySetterOperation(
                    containingSymbol,
                    data,
                    ClassifyConversion(
                        compilation,
                        data.ExpressionType,
                        data.PropertyType)),

            MetadataAkcssOperationKind.If =>
                new MetadataAkcssIfOperation(containingSymbol, data),

            MetadataAkcssOperationKind.Apply =>
                new MetadataAkcssApplyOperation(
                    containingSymbol,
                    data,
                    ResolveAppliedSymbols(
                        containingSymbol,
                        data,
                        symbolsByMetadataName)),

            MetadataAkcssOperationKind.Intercept =>
                new MetadataAkcssInterceptOperation(containingSymbol, data),

            _ => throw new InvalidOperationException(
                $"Unsupported metadata AKCSS operation kind '{data.Kind}'."),
        };
    }

    private static IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> CreateSymbolLookup(
        ImmutableArray<IAkcssSymbol> symbols)
    {
        var grouped = new Dictionary<string, List<IAkcssSymbol>>(StringComparer.Ordinal);
        foreach (var symbol in symbols)
        {
            if (!grouped.TryGetValue(symbol.MetadataName, out var candidates))
            {
                candidates = [];
                grouped.Add(symbol.MetadataName, candidates);
            }

            candidates.Add(symbol);
        }

        var result = new Dictionary<string, ImmutableArray<IAkcssSymbol>>(
            grouped.Count,
            StringComparer.Ordinal);
        foreach (var pair in grouped)
        {
            result.Add(pair.Key, ImmutableArray.CreateRange(pair.Value));
        }

        return result;
    }

    private static ImmutableArray<IAkcssSymbol> ResolveAppliedSymbols(
        IMetadataAkcssSymbol containingSymbol,
        MetadataAkcssOperationData data,
        IReadOnlyDictionary<string, ImmutableArray<IAkcssSymbol>> symbolsByMetadataName)
    {
        using var result = ImmutableArrayBuilder<IAkcssSymbol>.Rent(
            data.AppliedSymbols.Length);
        foreach (var metadataName in data.AppliedSymbols)
        {
            if (!symbolsByMetadataName.TryGetValue(metadataName, out var candidates))
            {
                continue;
            }

            if (candidates.Length == 1)
            {
                result.Add(candidates[0]);
                continue;
            }

            IAkcssSymbol? sameModule = null;
            var isAmbiguous = false;
            foreach (var candidate in candidates)
            {
                if (candidate is not IMetadataAkcssSymbol metadataCandidate ||
                    !ReferenceEquals(
                        metadataCandidate.MetadataModule,
                        containingSymbol.MetadataModule))
                {
                    continue;
                }

                if (sameModule != null)
                {
                    isAmbiguous = true;
                    break;
                }

                sameModule = candidate;
            }

            if (!isAmbiguous && sameModule != null)
            {
                result.Add(sameModule);
            }
        }

        return result.ToImmutable();
    }

    private static AkburaConversion ClassifyConversion(
        CSharpCompilation compilation,
        ITypeSymbol? sourceType,
        ITypeSymbol? targetType)
    {
        if (sourceType == null || targetType == null)
        {
            return AkburaConversion.None(sourceType, targetType);
        }

        var conversion = compilation.ClassifyConversion(sourceType, targetType);
        var kind = !conversion.Exists
            ? AkburaConversionKind.None
            : conversion.IsIdentity
                ? AkburaConversionKind.Identity
                : conversion.IsImplicit
                    ? AkburaConversionKind.Implicit
                    : AkburaConversionKind.Explicit;
        return new AkburaConversion(
            kind,
            sourceType,
            targetType,
            conversion);
    }
}
