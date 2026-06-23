using Akbura.Language.Syntax;
using System;

namespace Akbura.Language.Binder;

internal sealed class BinderFactory
{
    private readonly AkburaSemanticModel _semanticModel;

    public BinderFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public Binder GetBinder(AkburaSyntax syntax)
    {
        return _semanticModel.BindingSession.GetBinder(syntax);
    }
}
