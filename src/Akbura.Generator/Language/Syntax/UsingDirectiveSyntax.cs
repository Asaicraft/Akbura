using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using CSharpSyntaxKind = Microsoft.CodeAnalysis.CSharp.SyntaxKind;
using CSharpSyntaxToken = Microsoft.CodeAnalysis.SyntaxToken;

namespace Akbura.Language.Syntax;

internal sealed partial class UsingAliasSyntax
{
    public CSharp.NameEqualsSyntax ToCSharp()
    {
        return CSharpSyntaxFactory.NameEquals(Name.Identifier.ValueText);
    }
}

internal sealed partial class UsingDirectiveSyntax
{
    public CSharp.UsingDirectiveSyntax ToCSharp()
    {
        return CSharpSyntaxFactory.UsingDirective(
            ToCSharpToken(GlobalKeyword, CSharpSyntaxKind.GlobalKeyword),
            ToCSharpToken(UsingKeyword, CSharpSyntaxKind.UsingKeyword),
            ToCSharpToken(StaticKeyword, CSharpSyntaxKind.StaticKeyword),
            ToCSharpToken(UnsafeKeyword, CSharpSyntaxKind.UnsafeKeyword),
            Alias?.ToCSharp(),
            Name.ToCSharp(),
            ToCSharpToken(Semicolon, CSharpSyntaxKind.SemicolonToken));
    }

    private static CSharpSyntaxToken ToCSharpToken(SyntaxToken token, CSharpSyntaxKind kind)
    {
        if (token.RawKind == 0)
        {
            return default;
        }

        return token.IsMissing
            ? CSharpSyntaxFactory.MissingToken(kind)
            : CSharpSyntaxFactory.Token(kind);
    }
}
