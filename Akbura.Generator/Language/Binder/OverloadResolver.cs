using Akbura.Language.BoundTree;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;

namespace Akbura.Language.Binder;

internal sealed class OverloadResolver
{
    private readonly Binder _binder;

    public OverloadResolver(Binder binder)
    {
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
    }

    public OverloadResolutionResult ResolveMethodGroup(
        ImmutableArray<IMethodSymbol> candidates,
        ImmutableArray<ITypeSymbol?> argumentTypes)
    {
        candidates = candidates.IsDefault
            ? ImmutableArray<IMethodSymbol>.Empty
            : candidates;
        argumentTypes = argumentTypes.IsDefault
            ? ImmutableArray<ITypeSymbol?>.Empty
            : argumentTypes;

        if (candidates.IsEmpty)
        {
            return OverloadResolutionResult.NotFound(candidates);
        }

        var bestScore = int.MaxValue;
        var bestCandidates = ImmutableArray.CreateBuilder<IMethodSymbol>();

        foreach (var candidate in candidates)
        {
            if (!TryGetApplicabilityScore(candidate, argumentTypes, out var score))
            {
                continue;
            }

            if (score < bestScore)
            {
                bestScore = score;
                bestCandidates.Clear();
                bestCandidates.Add(candidate);
                continue;
            }

            if (score == bestScore)
            {
                bestCandidates.Add(candidate);
            }
        }

        return bestCandidates.Count switch
        {
            0 => OverloadResolutionResult.NotFound(candidates),
            1 => OverloadResolutionResult.Success(bestCandidates[0]),
            _ => OverloadResolutionResult.Ambiguous(bestCandidates.ToImmutable()),
        };
    }

    private bool TryGetApplicabilityScore(
        IMethodSymbol method,
        ImmutableArray<ITypeSymbol?> argumentTypes,
        out int score)
    {
        score = 0;
        var parameters = method.Parameters;
        if (parameters.Length != argumentTypes.Length)
        {
            return false;
        }

        for (var i = 0; i < argumentTypes.Length; i++)
        {
            var conversion = _binder.Conversions.ClassifyConversion(
                argumentTypes[i],
                parameters[i].Type);
            if (!conversion.IsImplicit)
            {
                return false;
            }

            score += conversion.Kind == AkburaConversionKind.Identity ? 0 : 1;
        }

        return true;
    }
}
