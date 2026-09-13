using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.Workspaces.Documents;

internal static class AkburaMarkupSemanticFacts
{
    internal static ResolvedMarkupPropertyReference? GetPropertyReference(
        AkburaSemanticModel semanticModel,
        int position)
    {
        var root = semanticModel.SyntaxTree.GetRootSyntax();
        var literal = root.DescendantNodes().OfType<MarkupLiteralAttributeValueSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(position));
        return literal == null ? null : semanticModel.GetMarkupAvaloniaPropertyReference(literal);
    }

    internal static MarkupSelectorNode? GetSelectorTypeReference(
        AkburaSemanticModel semanticModel,
        int position)
    {
        var literal = semanticModel.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupLiteralAttributeValueSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(position));
        if (literal == null || semanticModel.GetMarkupSelectorLiteral(literal) is not { } selector)
        {
            return null;
        }

        foreach (var branch in selector.Branches)
        {
            foreach (var node in branch)
            {
                if (node.Type != null && node.Span.Contains(position))
                {
                    return node;
                }
            }
        }

        return null;
    }

    internal static bool TryGetAssignment(
        AkburaSemanticModel semanticModel,
        int position,
        out Akbura.Language.Symbols.IPropertySymbol property,
        out MarkupPropertyAssignmentContract contract,
        out TextSpan nameSpan)
    {
        var root = semanticModel.SyntaxTree.GetRootSyntax();
        var attribute = root.DescendantNodes().OfType<MarkupAttributeSyntax>()
            .FirstOrDefault(candidate => candidate.Span.Contains(position));
        nameSpan = attribute switch
        {
            MarkupPlainAttributeSyntax plain => plain.Name.Span,
            MarkupAttachedPropertyAttributeSyntax attached => attached.Name.Span,
            _ => default,
        };
        if (attribute != null && nameSpan.Contains(position) &&
            semanticModel.GetSymbolInfo(attribute).Symbol is Akbura.Language.Symbols.IPropertySymbol resolved)
        {
            var element = attribute.Parent?.Parent as MarkupElementSyntax;
            property = resolved;
            contract = semanticModel.GetMarkupPropertyAssignmentContract(resolved, element);
            return true;
        }

        property = null!;
        contract = default;
        return false;
    }
}
