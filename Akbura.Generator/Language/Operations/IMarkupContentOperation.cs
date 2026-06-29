using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;

namespace Akbura.Language.Operations;

internal interface IMarkupContentOperation : IPropertySetterOperation
{
    new MarkupElementSyntax Syntax { get; }

    IMarkupComponentSymbol? ContainingComponent { get; }

    MarkupContentModel ContentModel { get; }

    ImmutableArray<MarkupChildContent> Content { get; }

    string? LiteralValue { get; }

    bool IsSynthesizedString { get; }
}
