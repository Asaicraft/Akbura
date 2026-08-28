using System;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;

namespace Akbura.Language.Syntax;

internal sealed partial class NamespaceDeclarationSyntax
{
    public CSharp.FileScopedNamespaceDeclarationSyntax ToCSharp()
    {
        if (Name.ToCSharp() is not CSharp.NameSyntax name)
        {
            throw new InvalidOperationException("Namespace declaration name must be a C# name syntax.");
        }

        var declaration = CSharpSyntaxFactory.FileScopedNamespaceDeclaration(name);

        return declaration.WithSemicolonToken(
            Semicolon.IsMissing
                ? CSharpSyntaxFactory.MissingToken(CSharpSyntaxKind.SemicolonToken)
                : CSharpSyntaxFactory.Token(CSharpSyntaxKind.SemicolonToken));
    }
}
