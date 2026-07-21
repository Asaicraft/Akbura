using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal interface IMarkupNameAssignmentOperation : IMarkupAttributeOperation
{
    IMarkupNameSymbol? NameSymbol { get; }

    bool IsAssignedDuringFirstUpdate { get; }
}
