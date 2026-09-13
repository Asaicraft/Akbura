using Akbura.Language.Symbols;
using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IMarkupIfOperation : IOperation
{
    new MarkupIfStatementSyntax Syntax { get; }

    ImmutableArray<MarkupConditionalBranch> Branches { get; }

    MarkupContentCardinality Cardinality { get; }
}

internal readonly struct MarkupConditionalBranch
{
    public MarkupConditionalBranch(AkburaSyntax syntax, CSharpExpressionSyntax? conditionSyntax,
        CSharpOperationDefinition condition, ImmutableArray<MarkupChildContent> content,
        CSharpOperationDefinition valueOperation = default, string? literalValue = null,
        bool isSynthesizedString = false)
    {
        Syntax = syntax;
        ConditionSyntax = conditionSyntax;
        Condition = condition;
        Content = content;
        ValueOperation = valueOperation;
        LiteralValue = literalValue;
        IsSynthesizedString = isSynthesizedString;
        Cardinality = !valueOperation.IsDefault || literalValue != null
            ? new(1, 1) : MarkupContentCardinality.FromSequence(content);
    }

    public AkburaSyntax Syntax { get; }

    public CSharpExpressionSyntax? ConditionSyntax { get; }

    public CSharpOperationDefinition Condition { get; }

    public ImmutableArray<MarkupChildContent> Content { get; }

    public CSharpOperationDefinition ValueOperation { get; }

    public string? LiteralValue { get; }

    public bool IsSynthesizedString { get; }

    public MarkupContentCardinality Cardinality { get; }
}
