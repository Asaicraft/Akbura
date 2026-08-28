
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

    public DeclarationTable DeclarationTable => Compilation.DeclarationTable;
}
