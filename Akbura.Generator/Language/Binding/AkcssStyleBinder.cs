using Akbura.Language.Declarations;

namespace Akbura.Language.Binding;

internal sealed class AkcssStyleBinder : Binder
{
    public AkcssStyleBinder(
        AkburaCompilation compilation,
        Binder parent,
        AkburaDeclaration declaration)
        : base(compilation, parent, declaration)
    {
    }
}
