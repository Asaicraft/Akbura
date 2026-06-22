using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class AkcssInterceptOperation : IAkcssInterceptOperation
{
    public AkcssInterceptOperation(
        AkcssInterceptDirectiveSyntax syntax,
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition interceptType,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
        InterceptType = interceptType;
        HasErrors = hasErrors;
    }

    public OperationKind Kind => OperationKind.AkcssIntercept;

    public OperationLanguage Language => OperationLanguage.Akcss;

    AkburaSyntax IOperation.Syntax => Syntax;

    public AkcssInterceptDirectiveSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

    public ISymbol? TargetSymbol => null;

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IAkcssSymbol ContainingAkcssSymbol { get; }

    public CSharpSymbolDefinition InterceptType { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssIntercept(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssIntercept(this, parameter);
    }

    public bool Equals(IOperation? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => obj is IOperation operation && Equals(operation);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public string ToDisplayString() => "@intercept " + InterceptType.ToDisplayString();

    public override string ToString() => ToDisplayString();
}
