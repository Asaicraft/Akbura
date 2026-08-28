using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IMarkupAttributeOperation : IOperation
{
    new MarkupAttributeSyntax Syntax { get; }

    IMarkupComponentSymbol? ContainingComponent { get; }
}
