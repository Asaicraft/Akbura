using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

internal sealed partial class AkcssUsingDirectiveSyntax
{
    public bool IsAkcssModuleImport
        => Name.ToFullString()
            .Trim()
            .EndsWith(".akcss", StringComparison.Ordinal);

    public CSharp.UsingDirectiveSyntax ToCSharp()
    {
        if (IsAkcssModuleImport)
        {
            throw new InvalidOperationException(
                "An AKCSS module import cannot be converted to a C# using directive.");
        }

        return CSharpSyntaxFactory.UsingDirective(
            CSharpSyntaxFactory.ParseName(Name.ToFullString().Trim()));
    }
}
