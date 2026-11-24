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

        var text = Name.ToFullString().Trim();
        var usings = CSharpSyntaxFactory.ParseCompilationUnit("using " + text + ";").Usings;
        return usings.Count == 1 ? usings[0] : CSharpSyntaxFactory.UsingDirective(CSharpSyntaxFactory.ParseName(text));
    }
}
