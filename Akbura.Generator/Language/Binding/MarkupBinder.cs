using Akbura.Language.Declarations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;

namespace Akbura.Language.Binding;

internal sealed class MarkupBinder : Binder
{
    public MarkupBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InMarkup)
    {
    }

    public IMarkupComponentSymbol? TargetComponentSymbol
    {
        get
        {
            return Declaration?.Syntax switch
            {
                MarkupRootSyntax markupRoot => SemanticModel.GetSymbolInfo(markupRoot.Element).Symbol as IMarkupComponentSymbol,
                MarkupElementSyntax markupElement => SemanticModel.GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol,
                _ => null,
            };
        }
    }
}
