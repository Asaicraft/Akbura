using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class AkcssApplyOperation : IAkcssApplyOperation
{
    private readonly ImmutableArray<IOperation> _children;

    public AkcssApplyOperation(
        AkcssApplyDirectiveSyntax syntax,
        IAkcssSymbol containingAkcssSymbol,
        ImmutableArray<string> items,
        ImmutableArray<IAkcssSymbol> appliedSymbols,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
        Items = items.IsDefault ? ImmutableArray<string>.Empty : items;
        AppliedSymbols = appliedSymbols.IsDefault ? ImmutableArray<IAkcssSymbol>.Empty : appliedSymbols;
        HasErrors = hasErrors;
        _children = ImmutableArray<IOperation>.Empty;
    }

    public OperationKind Kind => OperationKind.AkcssApply;

    public OperationLanguage Language => OperationLanguage.Akcss;

    AkburaSyntax IOperation.Syntax => Syntax;

    public AkcssApplyDirectiveSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => _children;

    public ISymbol? TargetSymbol => null;

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IAkcssSymbol ContainingAkcssSymbol { get; }

    public ImmutableArray<string> Items { get; }

    public ImmutableArray<IAkcssSymbol> AppliedSymbols { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssApply(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssApply(this, parameter);
    }

    public bool Equals(IOperation? other) => ReferenceEquals(this, other);

    public override bool Equals(object? obj) => obj is IOperation operation && Equals(operation);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public string ToDisplayString() => "@apply " + string.Join(" ", Items);

    public override string ToString() => ToDisplayString();
}
