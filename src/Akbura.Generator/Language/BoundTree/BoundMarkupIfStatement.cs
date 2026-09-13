using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using BinderType = Akbura.Language.Binder.Binder;

namespace Akbura.Language.BoundTree;

internal sealed class BoundMarkupIfStatement : BoundNode
{
    public BoundMarkupIfStatement(MarkupIfStatementSyntax syntax, BinderType binder,
        ImmutableArray<BoundMarkupConditionalBranch> branches,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics)
        : base(BoundKind.MarkupIf, syntax, binder, AkburaSymbolInfo.None(CandidateReason.None),
            diagnostics, ImmutableArray<BoundNode>.CastUp(branches))
    {
        Branches = branches;
    }

    public new MarkupIfStatementSyntax Syntax => (MarkupIfStatementSyntax)base.Syntax;

    public ImmutableArray<BoundMarkupConditionalBranch> Branches { get; }

    public BoundMarkupIfStatement Update(ImmutableArray<BoundMarkupConditionalBranch> branches)
    {
        return branches == Branches ? this : new(Syntax, Binder, branches, Diagnostics);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupIf(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default => visitor.VisitMarkupIf(this);

    public override TResult? Accept<TParameter, TResult>(BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default => visitor.VisitMarkupIf(this, parameter);
}

internal sealed class BoundMarkupConditionalBranch : BoundNode
{
    public BoundMarkupConditionalBranch(AkburaSyntax syntax, BinderType binder,
        CSharpExpressionSyntax? conditionSyntax, CSharpOperationDefinition condition,
        ImmutableArray<MarkupChildContent> content, ImmutableArray<BoundNode> children,
        ImmutableArray<AkburaSemanticDiagnostic> diagnostics,
        CSharpOperationDefinition valueOperation = default, string? literalValue = null,
        bool isSynthesizedString = false)
        : base(BoundKind.MarkupConditionalBranch, syntax, binder,
            AkburaSymbolInfo.None(CandidateReason.None), diagnostics, children)
    {
        ConditionSyntax = conditionSyntax;
        Condition = condition;
        Content = content;
        ValueOperation = valueOperation;
        LiteralValue = literalValue;
        IsSynthesizedString = isSynthesizedString;
    }

    public CSharpExpressionSyntax? ConditionSyntax { get; }

    public CSharpOperationDefinition Condition { get; }

    public ImmutableArray<MarkupChildContent> Content { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public string? LiteralValue { get; }

    public bool IsSynthesizedString { get; }

    public BoundMarkupConditionalBranch Update(CSharpOperationDefinition condition,
        ImmutableArray<MarkupChildContent> content, ImmutableArray<BoundNode> children,
        CSharpOperationDefinition valueOperation)
    {
        if (condition.Equals(Condition) && content == Content && children == Children &&
            valueOperation.Equals(ValueOperation))
        {
            return this;
        }

        return new(Syntax, Binder, ConditionSyntax, condition, content, children, Diagnostics,
            valueOperation, LiteralValue, IsSynthesizedString);
    }

    public override void Accept(BoundTreeVisitor visitor) => visitor.VisitMarkupConditionalBranch(this);

    public override TResult? Accept<TResult>(BoundTreeVisitor<TResult> visitor)
        where TResult : default => visitor.VisitMarkupConditionalBranch(this);

    public override TResult? Accept<TParameter, TResult>(BoundTreeVisitor<TParameter, TResult> visitor,
        TParameter parameter)
        where TResult : default => visitor.VisitMarkupConditionalBranch(this, parameter);
}
