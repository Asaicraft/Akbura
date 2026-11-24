using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    internal AkcssBindingContext GetEditorContext(IAkcssDeclarationOwner owner, AkburaSyntax site)
    {
        var context = CreateAkcssContext(owner);
        if (ReferenceEquals(site, owner.DeclarationSyntax) && owner is not AkcssSelectorRuleSymbol)
        {
            return context;
        }

        ArrayBuilder<AkcssNestedStyleRuleSyntax>? selectors = null;
        try
        {
            MarkupElementSyntax? element = null;
            for (var current = site; current != null && !ReferenceEquals(current, owner.DeclarationSyntax); current = current.Parent)
            {
                switch (current)
                {
                    case AkcssNestedStyleRuleSyntax rule:
                        (selectors ??= ArrayBuilder<AkcssNestedStyleRuleSyntax>.GetInstance()).Add(rule);
                        break;
                    case MarkupElementSyntax markup:
                        element ??= markup;
                        break;
                    case AkcssIfDirectiveSyntax:
                        context = context with { IsConditional = true };
                        break;
                }
            }

            if (element is not null)
            {
                var markupContext = EnterAkcssElement(context, element);
                if (markupContext.ActiveTemplate is null && owner is AkcssThemeSymbol markupTheme &&
                    AkcssSemanticFacts.FindTemplate(element) is { } markupTemplate)
                {
                    markupContext = markupContext with { SubjectTemplate = DescribeAkcssTemplate(markupTheme, markupTemplate).Template };
                }

                return markupContext;
            }

            if (owner is AkcssSelectorRuleSymbol general)
            {
                context = Select(context, general.DeclarationSyntax, general.DeclarationSyntax.Selector);
            }
            else if (owner is AkcssThemeSymbol theme && selectors is not null)
            {
                GetThemeDirectives(theme.DeclarationSyntax, out var baseDirective, out var directive);
                var local = DescribeAkcssTemplate(theme, directive).Template;
                var baseReference = BindAkcssBaseReference(theme, baseDirective);
                var (template, _) = GetEffectiveAkcssTemplate(theme, directive, local,
                    baseReference.Diagnostics.IsEmpty ? baseReference.Data : null);
                context = context with { SubjectTemplate = template };
            }

            if (selectors is not null)
            {
                for (var i = selectors.Count - 1; i >= 0; i--)
                {
                    var rule = selectors[i];
                    context = Select(context, rule, rule.Selector);
                }
            }

            return context;
        }
        finally
        {
            selectors?.Free();
        }

        AkcssBindingContext Select(AkcssBindingContext current, AkburaSyntax rule, AkcssSelectorSyntax selector) =>
            EnterAkcssSelector(current, rule, BindAkcssSelector(selector, current, diagnostics: null));
    }
}
