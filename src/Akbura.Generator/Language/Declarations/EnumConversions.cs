// This file is ported and adapted from the Roslyn (dotnet/roslyn)

#nullable disable

using Akbura.Language.Syntax;

namespace Akbura.Language;

internal static partial class EnumConversions
{
    internal static DeclarationKind ToDeclarationKind(this SyntaxKind kind)
    {
        switch (kind)
        {
            case SyntaxKind.NamespaceDeclarationSyntax:
                return DeclarationKind.Namespace;
            case SyntaxKind.AkburaDocumentSyntax:
                return DeclarationKind.Component;
            case SyntaxKind.AkcssDocumentSyntax:
            case SyntaxKind.InlineAkcssBlockSyntax:
                return DeclarationKind.AkcssModule;
            case SyntaxKind.AkcssStyleRuleSyntax:
                return DeclarationKind.AkcssStyle;
            case SyntaxKind.AkcssUtilityDeclarationSyntax:
                return DeclarationKind.AkcssUtility;
            case SyntaxKind.AkcssSelectorRuleSyntax:
                return DeclarationKind.AkcssSelectorRule;
            case SyntaxKind.AkcssThemeDeclarationSyntax:
                return DeclarationKind.AkcssTheme;
            case SyntaxKind.AkcssMixinDeclarationSyntax:
                return DeclarationKind.AkcssMixin;
            case SyntaxKind.AkcssMixinParameterSyntax:
                return DeclarationKind.AkcssMixinParameter;
            default:
                return ThrowHelper.UnexpectedValue<DeclarationKind>(kind);
        }
    }
}
