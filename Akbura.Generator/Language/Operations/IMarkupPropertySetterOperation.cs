using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IMarkupPropertySetterOperation : IPropertySetterOperation, IMarkupAttributeOperation
{
    ImmutableArray<IAkcssSymbol> AppliedAkcssSymbols { get; }

    MarkupAttributeBindingKind BindingKind { get; }

    MarkupAttributeValueKind ValueKind { get; }

    MarkupAttributeValueSyntax? ValueSyntax { get; }

    string? LiteralValue { get; }
}
