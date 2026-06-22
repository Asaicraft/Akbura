using Akbura.Language.Declarations;

namespace Akbura.Language.Binding;

internal sealed class AkcssModuleBinder : Binder
{
    public AkcssModuleBinder(
        AkburaCompilation compilation,
        Binder parent,
        AkburaDeclaration declaration)
        : base(compilation, parent, declaration)
    {
    }
}
