using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;

namespace Akbura.Language.Binder;

internal readonly struct OverloadResolutionResult
{
    private OverloadResolutionResult(
        IMethodSymbol? selectedMethod,
        ImmutableArray<IMethodSymbol> candidateMethods,
        AkburaCandidateReason candidateReason)
    {
        SelectedMethod = selectedMethod;
        CandidateMethods = candidateMethods.IsDefault
            ? ImmutableArray<IMethodSymbol>.Empty
            : candidateMethods;
        CandidateReason = candidateReason;
    }

    public static OverloadResolutionResult Success(IMethodSymbol selectedMethod)
    {
        return new OverloadResolutionResult(
            selectedMethod,
            ImmutableArray.Create(selectedMethod),
            AkburaCandidateReason.None);
    }

    public static OverloadResolutionResult NotFound(ImmutableArray<IMethodSymbol> candidateMethods)
    {
        return new OverloadResolutionResult(
            selectedMethod: null,
            candidateMethods,
            AkburaCandidateReason.NotFound);
    }

    public static OverloadResolutionResult Ambiguous(ImmutableArray<IMethodSymbol> candidateMethods)
    {
        return new OverloadResolutionResult(
            selectedMethod: null,
            candidateMethods,
            AkburaCandidateReason.Ambiguous);
    }

    public IMethodSymbol? SelectedMethod { get; }

    public ImmutableArray<IMethodSymbol> CandidateMethods { get; }

    public AkburaCandidateReason CandidateReason { get; }

    public bool IsSuccessful =>
        SelectedMethod != null &&
        CandidateReason == AkburaCandidateReason.None;
}
