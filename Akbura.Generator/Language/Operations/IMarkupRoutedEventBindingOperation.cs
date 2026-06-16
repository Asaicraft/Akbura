using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IMarkupRoutedEventBindingOperation : IMarkupAttributeOperation
{
    IRoutedEventSymbol Event { get; }

    MarkupAttributeBindingKind BindingKind { get; }

    MarkupAttributeValueKind ValueKind { get; }

    MarkupAttributeValueSyntax? ValueSyntax { get; }

    MarkupCommandHandlerKind HandlerKind { get; }

    MarkupCommandArgumentMode ArgumentMode { get; }

    int HandlerParameterCount { get; }

    bool IsAsync { get; }

    bool ContainsAwait { get; }

    CSharpSymbolDefinition HandlerType { get; }

    CSharpSymbolDefinition EventArgsType { get; }

    CSharpOperationDefinition HandlerOperation { get; }
}
