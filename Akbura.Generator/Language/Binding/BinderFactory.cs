using Akbura.Language.Declarations;
using Akbura.Language.Syntax;

namespace Akbura.Language.Binding;

internal sealed class BinderFactory
{
    private readonly AkburaSemanticModel _semanticModel;
    private readonly CompilationBinder _compilationBinder;

    public BinderFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel;
        _compilationBinder = new CompilationBinder(semanticModel.Compilation);
    }

    public Binder GetBinder(AkburaSyntax syntax)
    {
        if (!_semanticModel.Compilation.DeclarationTable.TryGetDeclaration(syntax, out var declaration))
        {
            return _compilationBinder;
        }

        return declaration.Kind switch
        {
            AkburaDeclarationKind.Component => new ComponentBinder(_semanticModel.Compilation, _compilationBinder, declaration),
            AkburaDeclarationKind.MarkupRoot or AkburaDeclarationKind.MarkupElement => new MarkupBinder(_semanticModel.Compilation, _compilationBinder, declaration),
            AkburaDeclarationKind.AkcssModule => new AkcssModuleBinder(_semanticModel.Compilation, _compilationBinder, declaration),
            AkburaDeclarationKind.AkcssStyle or AkburaDeclarationKind.AkcssUtility => new AkcssStyleBinder(_semanticModel.Compilation, _compilationBinder, declaration),
            _ => _compilationBinder,
        };
    }
}
