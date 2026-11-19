using Akbura.Language.Syntax;

namespace Akbura.Language.Binder;

internal static class AkcssSemanticFacts
{
    public static bool IsSourceDeclaration(AkburaSyntax syntax) =>
        syntax is AkcssSelectorRuleSyntax or AkcssThemeDeclarationSyntax or AkcssMixinDeclarationSyntax;

    public static bool IsContextualMember(AkburaSyntax syntax) =>
        syntax is AkcssNestedStyleRuleSyntax or AkcssIncludeDirectiveSyntax or
            AkcssTemplateDirectiveSyntax or AkcssTemplateBindDirectiveSyntax or
            AkcssBasedOnDirectiveSyntax or AkcssPseudoBlockSyntax or
            MarkupIncludeDirectiveSyntax or MarkupTemplateBindDirectiveSyntax;

    public static bool HasContextualBody(AkburaSyntax declaration)
    {
        var members = declaration switch
        {
            AkcssStyleRuleSyntax style => style.Members,
            AkcssUtilityDeclarationSyntax utility => utility.Members,
            _ => default,
        };
        return FindContextualMember(members) is not null;
    }

    private static AkcssBodyMemberSyntax? FindContextualMember(SyntaxList<AkcssBodyMemberSyntax> members)
    {
        foreach (var member in members)
        {
            if (IsContextualMember(member))
            {
                return member;
            }

            if (member is AkcssIfDirectiveSyntax condition && FindContextualMember(condition.Members) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }


    public static AkburaSyntax? FindContextualModuleMember(AkburaSyntax module)
    {
        var members = module switch
        {
            AkcssDocumentSyntax document => document.Members,
            InlineAkcssBlockSyntax inline => inline.Members,
            _ => default,
        };
        foreach (var member in members)
        {
            if (IsSourceDeclaration(member))
            {
                return member;
            }

            if (member is AkcssStyleRuleSyntax style && FindContextualMember(style.Members) is { } styleMember)
            {
                return styleMember;
            }

            if (member is AkcssUtilitiesSectionSyntax utilities)
            {
                foreach (var utility in utilities.Utilities)
                {
                    if (FindContextualMember(utility.Members) is { } utilityMember)
                    {
                        return utilityMember;
                    }
                }
            }
        }

        return null;
    }

    public static AkcssTemplateDirectiveSyntax? FindTemplate(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (current is AkcssTemplateDirectiveSyntax template)
            {
                return template;
            }
        }

        return null;
    }

    public static AkburaSyntax? FindOwner(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (IsSourceDeclaration(current) || current is AkcssStyleRuleSyntax or AkcssUtilityDeclarationSyntax)
            {
                return current;
            }
        }

        return null;
    }
}
