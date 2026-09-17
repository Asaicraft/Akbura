using Akbura.Language.Binder;
using Akbura.Language.Syntax;
using Akbura.Language.Symbols;

namespace Akbura.Language.Operations;

internal interface IMarkupForeachKeyOperation : IMarkupAttributeOperation
{
    new MarkupAttachedPropertyAttributeSyntax Syntax { get; }
    CSharpOperationDefinition ValueOperation { get; }
    CSharpSymbolDefinition KeyType { get; }
    MarkupAttributeValueSyntax? ValueSyntax { get; }
    ICSharpOperation? ValueOperationTree { get; }
    bool IsIterationKey { get; }
}
