using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
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
        bool hasErrors)
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
    }

    public OperationKind Kind => OperationKind.TailwindUtility;

    public OperationLanguage Language => OperationLanguage.Markup;

    AkburaSyntax IOperation.Syntax => Syntax;

    public MarkupAttributeSyntax Syntax { get; }

    public IOperation? Parent => null;

    public ImmutableArray<IOperation> Children => ImmutableArray<IOperation>.Empty;

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
}
