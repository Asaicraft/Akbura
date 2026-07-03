using Akbura.Language.Syntax.Green;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language.Syntax;

partial class CSharpParameterListSyntax
{
    public CSharp.ParameterListSyntax? GetRawCSharpParameterList()
    {
        if (Parameters.Node is GreenSyntaxToken.CSharpRawToken rawToken)
        {
            Debug.Assert(rawToken.RawNode is CSharp.ParameterListSyntax);
            return Unsafe.As<CSharp.ParameterListSyntax>(rawToken.RawNode);
        }

        try
        {
            return CSharpSyntaxFactory.ParseParameterList(Parameters.ToFullString());
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
