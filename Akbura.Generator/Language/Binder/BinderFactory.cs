using Akbura.Language.Syntax;

namespace Akbura.Language.Binder;

internal sealed class BinderFactory
{
    private readonly BindingSession _bindingSession;

    public BinderFactory(AkburaSemanticModel semanticModel)
    {
        _bindingSession = new BindingSession(semanticModel);
    }

    public Binder GetBinder(AkburaSyntax syntax)
    {
        return _bindingSession.GetBinder(syntax);
    }
}
