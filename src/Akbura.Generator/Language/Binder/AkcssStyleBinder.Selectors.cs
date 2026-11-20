using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language.Binder;

internal sealed partial class AkcssStyleBinder
{
    private AkcssBindingContext CreateAkcssContext(IAkcssDeclarationOwner owner)
    {
        var syntax = owner.DeclarationSyntax!;
        var receiver = owner.TargetType.IsDefault
            ? default
            : new AkcssReceiver(owner.TargetType, AkcssReceiverOrigin.DeclaredTarget, syntax);
        if (owner is IAkcssSymbol && receiver.Type.IsDefault &&
            SemanticModel.TryGetDefaultAkcssStyleTargetType(out var legacyDefault))
        {
            receiver = new AkcssReceiver(new CSharpSymbolDefinition(legacyDefault), AkcssReceiverOrigin.LegacyDefault, syntax);
        }

        var context = new AkcssBindingContext(owner, SemanticModel) { Subject = receiver };
        return syntax switch
        {
            AkcssStyleRuleSyntax style => context with
            {
                Selector = BindLegacyAkcssSelector(style, receiver),
                StructuralParent = new AkcssStyleParent(AkcssStyleParentKind.Style, style),
            },
            AkcssUtilityDeclarationSyntax utility => context with
            {
                Application = new AkcssApplicationOrigin(utility, (IAkcssSymbol)owner, owner.TargetType),
                StructuralParent = new AkcssStyleParent(AkcssStyleParentKind.Utility, utility),
            },
            AkcssThemeDeclarationSyntax => context with
            {
                StructuralParent = new AkcssStyleParent(AkcssStyleParentKind.Theme, syntax),
            },
            AkcssMixinDeclarationSyntax => context with
            {
                IsMixinDefinition = true,
                IncludeChain = [(AkcssMixinSymbol)owner],
                StructuralParent = new AkcssStyleParent(AkcssStyleParentKind.MixinDefinition, syntax),
            },
            _ => context,
        };
    }

    private static AkcssSelectorBinding BindLegacyAkcssSelector(AkcssStyleRuleSyntax rule, AkcssReceiver receiver)
    {
        var selector = rule.Selector;
        var predicates = ImmutableArray.CreateBuilder<AkcssSelectorPredicate>();
        if (selector.TargetType is { } target)
        {
            predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.ExactType, target, null, receiver.Type));
        }

        if (selector.Name is { } name)
        {
            predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.Class, name, name.Identifier.ValueText));
        }

        var initial = new AkcssSelectorCompound(selector, predicates.ToImmutable(), receiver);
        return new AkcssSelectorBinding(selector, initial, [], default, selector.Name is not null);
    }

    private static AkcssBindingContext EnterAkcssSelector(
        AkcssBindingContext context, AkburaSyntax rule, AkcssSelectorBinding selector) =>
        context with
        {
            Subject = selector.Subject,
            DefinitionReceiver = selector.Initial.Anchor is not null && selector.Steps.IsEmpty
                ? context.DefinitionReceiver
                : default,
            Selector = selector,
            StructuralParent = new AkcssStyleParent(AkcssStyleParentKind.Style, rule),
            SubjectTemplate = selector.Steps.IsEmpty ? context.SubjectTemplate : null,
        };

    private AkcssSelectorBinding BindAkcssSelector(
        AkcssSelectorSyntax selector,
        AkcssBindingContext context,
        ImmutableArray<AkburaSemanticDiagnostic>.Builder diagnostics)
    {
        AkcssSelectorCompound initial;
        if (selector.Initial is AkcssParentCompoundSelectorSyntax parent)
        {
            var parentKind = context.StructuralParent.Kind switch
            {
                AkcssStyleParentKind.Style => AkcssParentAnchorKind.Style,
                AkcssStyleParentKind.Theme => AkcssParentAnchorKind.ThemeRoot,
                AkcssStyleParentKind.Utility => AkcssParentAnchorKind.Application,
                AkcssStyleParentKind.MixinDefinition => AkcssParentAnchorKind.Deferred,
                _ => AkcssParentAnchorKind.Missing,
            };
            var anchor = new AkcssParentAnchor(parent.Anchor, parentKind, context.StructuralParent, context.Selector, context.Application);
            if (parentKind == AkcssParentAnchorKind.Missing)
            {
                diagnostics.Add(CreateContextDiagnostic(parent.Anchor, context, ErrorCodes.AKBURA_SEMANTIC_AkcssSelector,
                    "A parent selector requires an enclosing selector or a utility application."));
            }

            initial = BindAkcssCompound(parent, parent.Components, context, diagnostics, anchor);
        }
        else
        {
            var compound = (AkcssCompoundSelectorSyntax)selector.Initial;
            initial = BindAkcssCompound(compound, compound.Components, context, diagnostics);
        }

        var steps = ImmutableArray.CreateBuilder<AkcssSelectorStep>(selector.Steps.Count);
        var hasActivator = HasActivationPredicate(initial) || initial.Anchor?.EnclosingSelector?.HasActivator == true;
        var sawTemplate = false;
        var isDirectThemeStyle = context.StructuralParent.Kind == AkcssStyleParentKind.Theme;
        foreach (var step in selector.Steps)
        {
            var (relationship, target) = step switch
            {
                AkcssChildSelectorSyntax child => (AkcssSelectorRelationship.Child, child.Target),
                AkcssTemplateTraversalSyntax template => (AkcssSelectorRelationship.Template, template.Target),
                AkcssDescendantSelectorSyntax descendant => (AkcssSelectorRelationship.Descendant, descendant.Target),
                _ => throw new System.InvalidOperationException("Unexpected AKCSS selector step."),
            };

            if (isDirectThemeStyle && sawTemplate == (relationship == AkcssSelectorRelationship.Template))
            {
                diagnostics.Add(CreateContextDiagnostic(step, context, ErrorCodes.AKBURA_SEMANTIC_AkcssSelector,
                    sawTemplate
                        ? "A direct theme child can contain only one template traversal."
                        : "A descendant or child step in a direct theme child must follow a template traversal."));
            }

            var sourceTemplate = relationship == AkcssSelectorRelationship.Template && steps.Count == 0 && initial.Anchor is not null
                ? context.SubjectTemplate
                : null;
            var boundTarget = BindAkcssCompound(target, target.Components, context, diagnostics, sourceTemplate: sourceTemplate);
            steps.Add(new AkcssSelectorStep(relationship, step, boundTarget));
            sawTemplate |= relationship == AkcssSelectorRelationship.Template;
            hasActivator |= HasActivationPredicate(boundTarget);
        }

        return new AkcssSelectorBinding(selector, initial, steps.MoveToImmutable(), context.StructuralParent, hasActivator);
    }

    private static AkcssSelectorCompound BindAkcssCompound(
        AkcssSelectorCompoundSyntax syntax,
        SyntaxList<AkcssSelectorComponentSyntax> components,
        AkcssBindingContext context,
        ImmutableArray<AkburaSemanticDiagnostic>.Builder diagnostics,
        AkcssParentAnchor? anchor = null,
        AkcssTemplateContext? sourceTemplate = null)
    {
        var predicates = ImmutableArray.CreateBuilder<AkcssSelectorPredicate>(components.Count);
        var receiver = anchor is null ? default : context.Subject;
        foreach (var component in components)
        {
            switch (component)
            {
                case AkcssTypeSelectorSyntax type:
                    var typeSyntax = type.Type switch
                    {
                        CSharpTypeSyntax bare => bare,
                        AkcssParenthesizedSelectorTypeSyntax typed => typed.Type,
                        _ => null,
                    };
                    if (!context.DefinitionModel.TryResolveAkcssTargetType(typeSyntax, out var boundType))
                    {
                        diagnostics.Add(CreateContextDiagnostic(type, context, ErrorCodes.AKBURA_SEMANTIC_AkcssSelector,
                            $"Cannot resolve selector type '{type.ToFullString().Trim()}'."));
                    }

                    predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.ExactType, type, null, boundType));
                    receiver = new AkcssReceiver(boundType, AkcssReceiverOrigin.WrittenType, type);
                    break;
                case AkcssClassSelectorSyntax @class:
                    predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.Class, @class, @class.Name.Identifier.ValueText));
                    break;
                case AkcssNameSelectorSyntax name:
                    predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.Name, name, name.Name.Identifier.ValueText));
                    if (sourceTemplate is not null &&
                        !components.Any(static component => component is AkcssTypeSelectorSyntax) &&
                        components.OfType<AkcssNameSelectorSyntax>().Count() == 1 &&
                        sourceTemplate.NameScope.FindUniqueRootDeclaration(name.Name.Identifier.ValueText) is { } part &&
                        sourceTemplate.Theme.DefinitionModel.GetSymbolInfo(part.Element).Symbol is IMarkupComponentSymbol
                            { ComponentType: { } partType })
                    {
                        receiver = new AkcssReceiver(new CSharpSymbolDefinition(partType), AkcssReceiverOrigin.TemplatePart, part.Attribute);
                    }

                    break;
                case AkcssPseudoClassSelectorSyntax pseudo:
                    predicates.Add(new AkcssSelectorPredicate(AkcssPredicateKind.PseudoClass, pseudo, pseudo.Name.Identifier.ValueText));
                    break;
            }
        }

        return new AkcssSelectorCompound(syntax, predicates.MoveToImmutable(), receiver, anchor);
    }

    private static bool HasActivationPredicate(AkcssSelectorCompound compound) =>
        compound.Predicates.Any(static predicate => predicate.Kind is AkcssPredicateKind.Class or AkcssPredicateKind.PseudoClass);

    private static AkburaSemanticDiagnostic CreateContextDiagnostic(
        AkburaSyntax syntax, AkcssBindingContext context, string code, string message, AkburaSyntax? related = null)
    {
        if (context.Inclusion is { } inclusion)
        {
            return new AkburaSemanticDiagnostic(inclusion.OutermostSite, code, [message],
                relatedSyntax: related is null ? [syntax] : [syntax, related]);
        }

        return new AkburaSemanticDiagnostic(syntax, code, [message],
            relatedSyntax: related is null ? [] : [related]);
    }
}
