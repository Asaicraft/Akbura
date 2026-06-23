using Akbura.Language.Declarations;

namespace Akbura.Language.Binder;

internal sealed class CompilationBinder : Binder
{
    public CompilationBinder(AkburaSemanticModel semanticModel)
        : base(
            semanticModel,
            next: null,
            declaration: null,
            scopeDesignator: null,
            flags: AkburaBinderFlags.None)
    {
    }

    public AkburaDeclarationTable DeclarationTable => Compilation.DeclarationTable;
}
