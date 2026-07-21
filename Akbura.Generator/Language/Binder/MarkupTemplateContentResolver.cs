using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;

namespace Akbura.Language.Binder;

internal sealed class MarkupTemplateContentResolver
{
    private const string TemplateContentAttributeName =
        "global::Avalonia.Metadata.TemplateContentAttribute";

    private readonly AkburaSemanticModel _semanticModel;

    public MarkupTemplateContentResolver(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public bool IsInsideTemplateContent(MarkupElementSyntax element)
    {
        for (var ancestor = element.Parent; ancestor != null; ancestor = ancestor.Parent)
        {
            if (ancestor is MarkupElementSyntax propertyElement &&
                IsTemplateContentPropertyElement(propertyElement))
            {
                return true;
            }
        }

        return false;
    }

    private bool IsTemplateContentPropertyElement(MarkupElementSyntax propertyElement)
    {
        var nameText = propertyElement.StartTag?.Name.ToFullString().Trim();
        if (string.IsNullOrWhiteSpace(nameText))
        {
            return false;
        }

        var separator = nameText!.LastIndexOf('.');
        if (separator <= 0 || separator == nameText.Length - 1)
        {
            return false;
        }

        var containingElement = AkburaSemanticModel.GetParentMarkupElement(propertyElement);
        if (containingElement == null ||
            !_semanticModel.TryGetMarkupElementReferenceType(containingElement, out var typeDefinition) ||
            typeDefinition.Symbol is not INamedTypeSymbol containingType ||
            !AkburaSemanticModel.IsMarkupPropertyElementOwner(
                containingType,
                nameText[..separator]))
        {
            return false;
        }

        var propertyName = nameText[(separator + 1)..].Trim();
        var property = AkburaSemanticModel.FindPublicClrProperty(containingType, propertyName);
        return property != null && HasTemplateContentAttribute(property);
    }

    private static bool HasTemplateContentAttribute(IPropertySymbol property)
    {
        for (var current = property; current != null; current = current.OverriddenProperty)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat) ==
                    TemplateContentAttributeName)
                {
                    return true;
                }
            }
        }

        return false;
    }
}
