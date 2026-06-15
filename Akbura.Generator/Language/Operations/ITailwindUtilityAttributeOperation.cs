using Akbura.Language.Symbols;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface ITailwindUtilityAttributeOperation : IMarkupAttributeOperation
{
    string UtilityName { get; }

    ITailwindUtilitySymbol? Utility { get; }

    ImmutableArray<TailwindUtilityArgument> Arguments { get; }

    bool HasCondition { get; }

    string? ConditionText { get; }

    CSharpSymbolDefinition ConditionType { get; }

    CSharpOperationDefinition ConditionOperation { get; }
}
