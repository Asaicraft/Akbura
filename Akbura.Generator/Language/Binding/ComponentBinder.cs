using Akbura.Language.Declarations;

namespace Akbura.Language.Binding;

internal sealed class ComponentBinder : Binder
{
    public ComponentBinder(
        AkburaCompilation compilation,
        Binder parent,
        AkburaDeclaration declaration)
        : base(compilation, parent, declaration)
    {
    }

    public string ComponentName => Declaration?.Name ?? string.Empty;
}
