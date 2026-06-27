using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IMarkupCommandBindingOperation : IMarkupAttributeOperation
{
    IPropertySymbol Property { get; }

    ICommandSymbol Command { get; }

    ImmutableArray<ICommandParameterSymbol> Parameters { get; }

    CSharpSymbolDefinition ReturnType { get; }

    CSharpSymbolDefinition ResultType { get; }

    MarkupAttributeBindingKind BindingKind { get; }

    MarkupAttributeValueKind ValueKind { get; }

    MarkupAttributeValueSyntax? ValueSyntax { get; }

    MarkupCommandHandlerKind HandlerKind { get; }

    MarkupCommandArgumentMode ArgumentMode { get; }

    MarkupCommandResultMode ResultMode { get; }

    int HandlerParameterCount { get; }

    bool IsAsync { get; }

    bool ContainsAwait { get; }

    CSharpSymbolDefinition HandlerType { get; }

    CSharpSymbolDefinition HandlerResultType { get; }

    CSharpOperationDefinition HandlerOperation { get; }

    ICSharpOperation? HandlerOperationTree { get; }
}
