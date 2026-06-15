using Akbura.Language.Syntax;

namespace Akbura.Language.Operations;

internal interface IMarkupPropertySetterOperation : IPropertySetterOperation, IMarkupAttributeOperation
{
    MarkupAttributeBindingKind BindingKind { get; }

    MarkupAttributeValueKind ValueKind { get; }

    MarkupAttributeValueSyntax? ValueSyntax { get; }

    string? LiteralValue { get; }
}
