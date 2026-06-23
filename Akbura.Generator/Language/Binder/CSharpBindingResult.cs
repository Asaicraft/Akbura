using Akbura.Language.Operations;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language.Binder;

internal readonly struct CSharpBindingResult
{
    public static CSharpBindingResult Empty { get; } = new(
        typeSymbol: null,
        symbol: null,
        receiverType: null,
        isBindingPath: false,
        candidateSymbols: ImmutableArray<RoslynSymbol>.Empty,
        candidateReason: AkburaCandidateReason.NotFound,
        operationDefinition: default,
        diagnostics: ImmutableArray<Diagnostic>.Empty);

    public CSharpBindingResult(
        ITypeSymbol? typeSymbol,
        RoslynSymbol? symbol,
        ITypeSymbol? receiverType,
        bool isBindingPath,
        ImmutableArray<RoslynSymbol> candidateSymbols,
        AkburaCandidateReason candidateReason,
        CSharpOperationDefinition operationDefinition,
        ImmutableArray<Diagnostic> diagnostics = default)
    {
        TypeSymbol = typeSymbol;
        Symbol = symbol;
        ReceiverType = receiverType;
        IsBindingPath = isBindingPath;
        CandidateSymbols = candidateSymbols.IsDefault
            ? ImmutableArray<RoslynSymbol>.Empty
            : candidateSymbols;
        CandidateReason = candidateReason;
        OperationDefinition = operationDefinition;
        Diagnostics = diagnostics.IsDefault
            ? ImmutableArray<Diagnostic>.Empty
            : diagnostics;
    }

    public ITypeSymbol? TypeSymbol { get; }

    public RoslynSymbol? Symbol { get; }

    public ITypeSymbol? ReceiverType { get; }

    public bool IsBindingPath { get; }

    public ImmutableArray<RoslynSymbol> CandidateSymbols { get; }

    public AkburaCandidateReason CandidateReason { get; }

    public CSharpOperationDefinition OperationDefinition { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
