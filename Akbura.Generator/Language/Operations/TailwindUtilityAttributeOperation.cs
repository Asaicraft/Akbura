using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using System;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace Akbura.Language.Operations;

internal sealed class TailwindUtilityAttributeOperation : ITailwindUtilityAttributeOperation
{
    public TailwindUtilityAttributeOperation(
        MarkupAttributeSyntax syntax,
        IMarkupComponentSymbol? containingComponent,
        string utilityName,
        ITailwindUtilitySymbol? utility,
        ImmutableArray<TailwindUtilityArgument> arguments,
        bool hasCondition,
        string? conditionText,
        CSharpSymbolDefinition conditionType,
        CSharpOperationDefinition conditionOperation,
        bool hasErrors,
        ICSharpOperation? conditionOperationTree = null)
    {
        Syntax = syntax ?? throw new ArgumentNullException(nameof(syntax));
        ContainingComponent = containingComponent;
        UtilityName = string.IsNullOrWhiteSpace(utilityName)
            ? throw new ArgumentException("Tailwind utility name cannot be empty.", nameof(utilityName))
            : utilityName;
        Utility = utility;
        Arguments = arguments.IsDefault ? ImmutableArray<TailwindUtilityArgument>.Empty : arguments;
        HasCondition = hasCondition;
        ConditionText = conditionText;
        ConditionType = conditionType;
        ConditionOperation = conditionOperation;
        HasErrors = hasErrors;
        ConditionOperationTree = conditionOperationTree;
        AdoptCSharpOperationTree(ConditionOperationTree);
        AdoptArgumentOperationTrees(Arguments);
        Children = CreateChildren(ConditionOperationTree, Arguments);
    }

    public OperationKind Kind => OperationKind.TailwindUtility;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children { get; }

    public ISymbol? TargetSymbol => Utility;

    public ISymbol? TypeSymbol => ContainingComponent;

    public CSharpOperationDefinition CSharpDefinition => ConditionOperation;

    public bool IsImplicit => false;

    public bool HasErrors { get; }

    public object? ConstantValue => null;

    public IMarkupComponentSymbol? ContainingComponent { get; }

    public string UtilityName { get; }

    public ITailwindUtilitySymbol? Utility { get; }

    public ImmutableArray<TailwindUtilityArgument> Arguments { get; }

    public bool HasCondition { get; }

    public string? ConditionText { get; }

    public CSharpSymbolDefinition ConditionType { get; }

    public CSharpOperationDefinition ConditionOperation { get; }

    public ICSharpOperation? ConditionOperationTree { get; }

    private static ImmutableArray<IOperation> CreateChildren(
        ICSharpOperation? conditionOperationTree,
        ImmutableArray<TailwindUtilityArgument> arguments)
    {
        var count = conditionOperationTree == null ? 0 : 1;
        foreach (var argument in arguments)
        {
            if (argument.ValueOperationTree != null)
            {
                count++;
            }
        }

        if (count == 0)
        {
            return ImmutableArray<IOperation>.Empty;
        }

        var builder = ArrayBuilder<IOperation>.GetInstance(count);
        if (conditionOperationTree != null)
        {
            builder.Add(conditionOperationTree);
        }

        foreach (var argument in arguments)
        {
            if (argument.ValueOperationTree != null)
            {
                builder.Add(argument.ValueOperationTree);
            }
        }

        return builder.ToImmutableAndFree();
    }

    public void Accept(OperationVisitor visitor)
    {
        visitor.VisitTailwindUtilityAttribute(this);
    }


    public TResult? Accept<TParameter, TResult>(
        OperationVisitor<TParameter, TResult> visitor,
        TParameter parameter)
    {
        return visitor.VisitTailwindUtilityAttribute(this, parameter);
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
        return Utility == null
            ? Syntax.ToFullString()
            : Utility.ToDisplayString();
    }

    public override string ToString()
    {
        return ToDisplayString();
    }

    private void AdoptArgumentOperationTrees(ImmutableArray<TailwindUtilityArgument> arguments)
    {
        foreach (var argument in arguments)
        {
            AdoptCSharpOperationTree(argument.ValueOperationTree);
        }
    }

    private void AdoptCSharpOperationTree(ICSharpOperation? operation)
    {
        if (operation is CSharpOperation csharpOperation)
        {
            csharpOperation.SetParent(this);
        }
    }
}
