using Akbura.Language.Symbols;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class MarkupIfOperation : IMarkupIfOperation
{
    private IOperation? _parent;

    public MarkupIfOperation(MarkupIfStatementSyntax syntax,
        ImmutableArray<MarkupConditionalBranch> branches, ImmutableArray<IOperation> children,
        bool hasErrors)
    {
        Syntax = syntax;
        Branches = branches;
        Children = children;
        HasErrors = hasErrors;
        var minimum = syntax.ElseClause == null ? 0 : int.MaxValue;
        var maximum = 0;
        foreach (var branch in branches)
        {
            minimum = Math.Min(minimum, branch.Cardinality.Minimum);
            maximum = Math.Max(maximum, branch.Cardinality.Maximum);
            foreach (var child in branch.Content)
            {
                if (child.ConditionalOperation is MarkupIfOperation conditional)
                {
                    conditional.SetParent(this);
                }
            }
        }

        Cardinality = new(minimum == int.MaxValue ? 0 : minimum, maximum);
    }

    public MarkupIfStatementSyntax Syntax { get; }

    AkburaSyntax IOperation.Syntax => Syntax;

    public ImmutableArray<MarkupConditionalBranch> Branches { get; }

    public MarkupContentCardinality Cardinality { get; }

    public OperationKind Kind => OperationKind.MarkupIf;

    public OperationLanguage Language => OperationLanguage.Markup;

    public IOperation? Parent => _parent;

    public ImmutableArray<IOperation> Children { get; }

    public ISymbol? TargetSymbol => null;

    public ISymbol? TypeSymbol => null;

    public CSharpOperationDefinition CSharpDefinition => default;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    internal void SetParent(IOperation parent) => _parent = parent;

    public void Accept(OperationVisitor visitor) => visitor.VisitMarkupIf(this);

    public TResult? Accept<TParameter, TResult>(OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter) => visitor.VisitMarkupIf(this, parameter);

    public bool Equals(IOperation? other) => ReferenceEquals(this, other);

    public override bool Equals(object? other) => ReferenceEquals(this, other);

    public override int GetHashCode() => RuntimeHelpers.GetHashCode(this);

    public string ToDisplayString() => Syntax.ToFullString();
}
