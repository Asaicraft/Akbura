using Akbura.Language.Declarations;

namespace Akbura.Language.Binding;

internal sealed class MarkupBinder : Binder
{
    public MarkupBinder(
        AkburaCompilation compilation,
        Binder parent,
        AkburaDeclaration declaration)
        : base(compilation, parent, declaration)
    {
    }
}
