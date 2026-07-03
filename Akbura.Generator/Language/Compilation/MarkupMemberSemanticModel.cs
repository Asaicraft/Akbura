using Akbura.Language.Syntax;

namespace Akbura.Language;

internal sealed class MarkupMemberSemanticModel : BinderBackedMemberSemanticModel
{
    public MarkupMemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax root)
        : base(semanticModel, scope, root)
    {
    }
}
