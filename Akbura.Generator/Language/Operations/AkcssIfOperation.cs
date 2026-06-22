using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class AkcssIfOperation : IAkcssIfOperation
{
    private readonly ImmutableArray<IOperation> _children;

    public AkcssIfOperation(
        AkcssIfDirectiveSyntax syntax,
        IAkcssSymbol containingAkcssSymbol,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation,
        ImmutableArray<IAkcssOperation> operations,
        bool hasErrors)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingAkcssSymbol = containingAkcssSymbol ?? throw new ArgumentNullException(nameof(containingAkcssSymbol));
        ConditionType = conditionType;
        ConditionOperation = conditionOperation;
        Operations = operations.IsDefault
            ? ImmutableArray<IAkcssOperation>.Empty
            : operations;
        HasErrors = hasErrors;

        _children = Operations
            .Cast<IOperation>()
            .ToImmutableArray();
    }

    public OperationKind Kind => OperationKind.AkcssIf;

    public OperationLanguage Language => OperationLanguage.Akcss;

    AkburaSyntax IOperation.Syntax => Syntax;

    public AkcssIfDirectiveSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => _children;

    public ISymbol? TargetSymbol => null;

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => ConditionOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => ConditionOperation.ConstantValue.HasValue
        ? ConditionOperation.ConstantValue.Value
        : null;

    public IAkcssSymbol ContainingAkcssSymbol { get; }

    public CSharpSymbolDefinition ConditionType { get; }

    public CSharpOperationDefinition ConditionOperation { get; }

    public ImmutableArray<IAkcssOperation> Operations { get; }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitAkcssIf(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitAkcssIf(this, parameter);
    }

    public bool Equals(IOperation? other)
    {
        return ReferenceEquals(this, other);
    }

    public override bool Equals(object? obj)
    {
        return obj is IOperation operation && Equals(operation);
    }

    public override int GetHashCode()
    {
        return RuntimeHelpers.GetHashCode(this);
    }

    public string ToDisplayString()
    {
        return $"@if({Syntax.Condition.ToFullString().Trim()})";
    }

    public override string ToString()
    {
        return ToDisplayString();
    }
}
