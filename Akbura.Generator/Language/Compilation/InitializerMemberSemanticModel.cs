using Akbura.Language.BoundTree;
using Akbura.Language.Syntax;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed class InitializerMemberSemanticModel : BinderBackedMemberSemanticModel
{
    public InitializerMemberSemanticModel(
        AkburaSemanticModel semanticModel,
        AkburaDocumentSyntax scope,
        AkburaSyntax root)
        : base(semanticModel, scope, root)
    {
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        if (TryGetBoundNodeFromMap(syntax, out var cached))
        {
            return cached;
        }

        var parentDeclaration = GetParentDeclaration(syntax);
        if (parentDeclaration != null)
        {
            GetMemberSemanticModel(parentDeclaration)
                .BindSemanticSyntax(parentDeclaration);
            if (TryGetBoundNodeFromMap(syntax, out cached))
            {
                return cached;
            }
        }

        return base.BindSemanticSyntax(syntax);
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return BindSemanticSyntax(syntax);
    }

    private static AkburaSyntax? GetParentDeclaration(AkburaSyntax syntax)
    {
        return syntax.Parent?.Kind switch
        {
            AkburaSyntaxKind.StateDeclarationSyntax => syntax.Parent,
            AkburaSyntaxKind.ParamDeclarationSyntax when ReferenceEquals(
                Unsafe.As<ParamDeclarationSyntax>(syntax.Parent).DefaultValue?.Green,
                syntax.Green) => syntax.Parent,
            _ => null,
        };
    }
}
