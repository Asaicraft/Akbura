using Akbura.Language.Syntax.Green;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpArgumentListSyntax
{
    public CSharp.ArgumentListSyntax? GetRawCSharpArgumentList()
    {
        if (Parameters.Node is GreenSyntaxToken.CSharpRawToken rawToken)
        {
            Debug.Assert(rawToken.RawNode is CSharp.ArgumentListSyntax);
            return Unsafe.As<CSharp.ArgumentListSyntax>(rawToken.RawNode);
        }

        try
        {
            return CSharpSyntaxFactory.ParseArgumentList(Parameters.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
