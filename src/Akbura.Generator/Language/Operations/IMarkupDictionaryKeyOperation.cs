using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IMarkupDictionaryKeyOperation : IMarkupAttributeOperation
{
    new MarkupAttachedPropertyAttributeSyntax Syntax { get; }
    MarkupDictionaryShape DictionaryShape { get; }
    CSharpSymbolDefinition KeyType { get; }
    CSharpSymbolDefinition ValueType { get; }
    CSharpOperationDefinition ValueOperation { get; }
    AkburaConversion ValueConversion { get; }
    ICSharpOperation? ValueOperationTree { get; }
    MarkupAttributeValueSyntax? ValueSyntax { get; }
    string? LiteralValue { get; }
    bool HasConstantValue { get; }
}
