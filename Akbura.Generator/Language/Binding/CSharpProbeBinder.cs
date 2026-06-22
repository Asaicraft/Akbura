using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.Language.Binding;

internal sealed class CSharpProbeBinder : Binder
{
    public CSharpProbeBinder(
        AkburaCompilation compilation,
        Binder parent)
        : base(compilation, parent, declaration: null)
    {
    }

    public CSharpCompilation CSharpCompilation => Compilation.CSharpCompilation;
}
