using Akbura.Language.Syntax;
using System;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language;

internal sealed class MemberSemanticModelFactory
{
    private readonly AkburaSemanticModel _semanticModel;

    public MemberSemanticModelFactory(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public MemberSemanticModel CreateMemberSemanticModel(
        AkburaSyntax root,
        MemberSemanticModelKind kind,
        AkburaDocumentSyntax scope)
    {
        if (root == null)
        {
            throw new ArgumentNullException(nameof(root));
        }

        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        return kind switch
        {
            MemberSemanticModelKind.Component => new ComponentMemberSemanticModel(_semanticModel, scope),
            MemberSemanticModelKind.Initializer => new InitializerMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Executable => new ExecutableMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Markup => new MarkupMemberSemanticModel(_semanticModel, scope, root),
            MemberSemanticModelKind.Akcss => new AkcssMemberSemanticModel(_semanticModel, scope, root),
            _ => new ComponentMemberSemanticModel(_semanticModel, scope),
        };
    }

    public AkburaDocumentSyntax FindDocumentScope(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.AkburaDocumentSyntax)
            {
                return Unsafe.As<AkburaDocumentSyntax>(node);
            }
        }

        return _semanticModel.SyntaxTree.GetRoot();
    }

    public static MemberSemanticModelKind GetModelKind(AkburaSyntax syntax)
    {
        if (IsParamDefaultValue(syntax))
        {
            return MemberSemanticModelKind.Initializer;
        }

        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.InlineAkcssBlockSyntax:
                case AkburaSyntaxKind.AkcssDocumentSyntax:
                case AkburaSyntaxKind.AkcssStyleRuleSyntax:
                case AkburaSyntaxKind.AkcssUtilityDeclarationSyntax:
                case AkburaSyntaxKind.AkcssAssignmentSyntax:
                case AkburaSyntaxKind.AkcssIfDirectiveSyntax:
                case AkburaSyntaxKind.AkcssApplyDirectiveSyntax:
                case AkburaSyntaxKind.AkcssInterceptDirectiveSyntax:
                    return MemberSemanticModelKind.Akcss;

                case AkburaSyntaxKind.MarkupRootSyntax:
                case AkburaSyntaxKind.MarkupElementSyntax:
                case AkburaSyntaxKind.MarkupElementContentSyntax:
                case AkburaSyntaxKind.MarkupInlineExpressionSyntax:
                case AkburaSyntaxKind.MarkupTextLiteralSyntax:
                case AkburaSyntaxKind.MarkupPlainAttributeSyntax:
                case AkburaSyntaxKind.MarkupPrefixedAttributeSyntax:
                case AkburaSyntaxKind.TailwindFlagAttributeSyntax:
                case AkburaSyntaxKind.TailwindFullAttributeSyntax:
                    return MemberSemanticModelKind.Markup;

                case AkburaSyntaxKind.CSharpBlockSyntax:
                case AkburaSyntaxKind.CSharpStatementSyntax:
                    return MemberSemanticModelKind.Executable;

                case AkburaSyntaxKind.SimpleStateInitializer:
                case AkburaSyntaxKind.BindableStateInitializer:
                    return MemberSemanticModelKind.Initializer;

                case AkburaSyntaxKind.AkburaDocumentSyntax:
                case AkburaSyntaxKind.StateDeclarationSyntax:
                case AkburaSyntaxKind.ParamDeclarationSyntax:
                case AkburaSyntaxKind.InjectDeclarationSyntax:
                case AkburaSyntaxKind.CommandDeclarationSyntax:
                case AkburaSyntaxKind.UseEffectDeclarationSyntax:
                case AkburaSyntaxKind.UserHook:
                    return MemberSemanticModelKind.Component;
            }
        }

        return MemberSemanticModelKind.Component;
    }

    public static AkburaSyntax GetModelRoot(
        AkburaSyntax syntax,
        MemberSemanticModelKind kind,
        AkburaDocumentSyntax scope)
    {
        if (kind == MemberSemanticModelKind.Initializer &&
            IsParamDefaultValue(syntax))
        {
            return syntax;
        }

        if (kind == MemberSemanticModelKind.Component)
        {
            return scope;
        }

        for (var node = syntax; node != null; node = node.Parent)
        {
            if (IsModelRoot(node.Kind, kind))
            {
                return node;
            }
        }

        return scope;
    }

    private static bool IsParamDefaultValue(AkburaSyntax syntax)
    {
        return syntax.Parent?.Kind == AkburaSyntaxKind.ParamDeclarationSyntax &&
               SemanticSyntaxIdentity.Equals(
                   Unsafe.As<ParamDeclarationSyntax>(syntax.Parent).DefaultValue,
                   syntax);
    }

    private static bool IsModelRoot(
        AkburaSyntaxKind syntaxKind,
        MemberSemanticModelKind modelKind)
    {
        return modelKind switch
        {
            MemberSemanticModelKind.Initializer =>
                syntaxKind is AkburaSyntaxKind.SimpleStateInitializer or
                    AkburaSyntaxKind.BindableStateInitializer,
            MemberSemanticModelKind.Executable =>
                syntaxKind is AkburaSyntaxKind.CSharpBlockSyntax or
                    AkburaSyntaxKind.CSharpStatementSyntax,
            MemberSemanticModelKind.Markup =>
                syntaxKind is AkburaSyntaxKind.MarkupRootSyntax or
                    AkburaSyntaxKind.MarkupElementSyntax,
            MemberSemanticModelKind.Akcss =>
                syntaxKind is AkburaSyntaxKind.InlineAkcssBlockSyntax or
                    AkburaSyntaxKind.AkcssDocumentSyntax or
                    AkburaSyntaxKind.AkcssStyleRuleSyntax or
                    AkburaSyntaxKind.AkcssUtilityDeclarationSyntax,
            _ => syntaxKind == AkburaSyntaxKind.AkburaDocumentSyntax,
        };
    }
}
