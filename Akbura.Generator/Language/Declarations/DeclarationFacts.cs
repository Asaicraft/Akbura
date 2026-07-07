using Akbura.Language.Syntax;
using System.Diagnostics.CodeAnalysis;

namespace Akbura.Language;

internal static class DeclarationFacts
{
    public static AkburaSyntax GetSyntax(Declaration declaration)
    {
        return declaration switch
        {
            SingleDeclaration singleDeclaration => singleDeclaration.Syntax,
            _ => ThrowHelper.UnexpectedValue<AkburaSyntax>(declaration.GetType().Name),
        };
    }

    public static bool TryGetSyntax(
        Declaration declaration,
        [NotNullWhen(true)] out AkburaSyntax? syntax)
    {
        switch (declaration)
        {
            case SingleDeclaration singleDeclaration:
                syntax = singleDeclaration.Syntax;
                return true;
            default:
                syntax = null;
                return false;
        }
    }

    public static AkburaSyntaxTree? GetAkburaSyntaxTree(Declaration declaration)
    {
        return declaration is SingleSyntaxDeclaration singleDeclaration
            ? singleDeclaration.SyntaxTree
            : null;
    }

    public static AkcssSyntaxTree? GetAkcssSyntaxTree(Declaration declaration)
    {
        return declaration is SingleSyntaxDeclaration singleDeclaration
            ? singleDeclaration.AkcssSyntaxTree
            : null;
    }
}
