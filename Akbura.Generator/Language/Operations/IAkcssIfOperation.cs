using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IAkcssIfOperation : IAkcssOperation
{
    new AkcssIfDirectiveSyntax Syntax { get; }

    CSharpSymbolDefinition ConditionType { get; }

    CSharpOperationDefinition ConditionOperation { get; }

    ICSharpOperation? ConditionOperationTree { get; }

    ImmutableArray<IAkcssOperation> Operations { get; }
}
