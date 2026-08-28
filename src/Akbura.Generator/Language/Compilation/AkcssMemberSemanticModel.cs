using Akbura.Language.Syntax;

namespace Akbura.Language;

internal sealed class AkcssMemberSemanticModel : BinderBackedMemberSemanticModel
{
    public AkcssMemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax root)
        : base(semanticModel, scope, root)
    {
    }
}
