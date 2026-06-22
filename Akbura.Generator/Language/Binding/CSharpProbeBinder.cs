using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.Language.Binding;

internal sealed class CSharpProbeBinder : Binder
{
    public CSharpProbeBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration: null,
            scopeDesignator: next.ScopeDesignator,
            flags: flags | AkburaBinderFlags.InCSharpProbe)
    {
    }

    public CSharpCompilation CSharpCompilation => Compilation.CSharpCompilation;
}
