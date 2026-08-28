using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal interface IAkcssOperation : IOperation
{
    IAkcssSymbol ContainingAkcssSymbol { get; }
}
