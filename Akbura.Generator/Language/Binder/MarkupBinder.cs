using Akbura.Language.Declarations;
using Akbura.Language.BoundTree;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class MarkupBinder : Binder
{
    public MarkupBinder(
        AkburaSemanticModel semanticModel,
        Binder next,
        AkburaDeclaration declaration,
        AkburaBinderFlags flags = AkburaBinderFlags.None)
        : base(
            semanticModel,
            next,
            declaration,
            declaration.Syntax,
            flags | AkburaBinderFlags.InMarkup)
    {
    }

    public IMarkupComponentSymbol? TargetComponentSymbol
    {
        get
        {
            return Declaration?.Syntax switch
            {
                MarkupRootSyntax markupRoot => SemanticModel.GetSymbolInfo(markupRoot.Element).Symbol as IMarkupComponentSymbol,
                MarkupElementSyntax markupElement => SemanticModel.GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol,
                _ => null,
            };
        }
    }

    public override BoundNode BindOperationSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                SemanticModel.CreateBoundMarkupAttribute(Unsafe.As<MarkupAttributeSyntax>(syntax)),
            _ => base.BindOperationSyntax(syntax),
        };
    }

    public override BoundNode BindSemanticSyntax(AkburaSyntax syntax)
    {
        return syntax.Kind switch
        {
            AkburaSyntaxKind.MarkupRootSyntax or
                AkburaSyntaxKind.MarkupElementSyntax or
                AkburaSyntaxKind.MarkupElementContentSyntax or
                AkburaSyntaxKind.MarkupInlineExpressionSyntax or
                AkburaSyntaxKind.MarkupTextLiteralSyntax =>
                SemanticModel.CreateBoundMarkupSyntax(syntax),
            AkburaSyntaxKind.MarkupPlainAttributeSyntax or
                AkburaSyntaxKind.MarkupPrefixedAttributeSyntax or
                AkburaSyntaxKind.TailwindFlagAttributeSyntax or
                AkburaSyntaxKind.TailwindFullAttributeSyntax =>
                BindOperationSyntax(syntax),
            _ => base.BindSemanticSyntax(syntax),
        };
    }
}
