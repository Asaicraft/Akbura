using Akbura.Language.Declarations;

namespace Akbura.Language.Binding;

internal sealed class CompilationBinder : Binder
{
    public CompilationBinder(AkburaCompilation compilation)
        : base(compilation, parent: null, declaration: null)
    {
    }

    public AkburaDeclarationTable DeclarationTable => Compilation.DeclarationTable;
}
