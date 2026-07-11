using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;

namespace Akbura.Language.Binder;

internal sealed class MarkupDataTypeResolver
{
    private const string InheritDataTypeFromItemsAttributeName =
        "global::Avalonia.Metadata.InheritDataTypeFromItemsAttribute";

    private readonly AkburaSemanticModel _semanticModel;

    public MarkupDataTypeResolver(AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ?? throw new ArgumentNullException(nameof(semanticModel));
    }

    public bool TryGetDataType(
        MarkupAttributeSyntax anchor,
        out INamedTypeSymbol dataType)
    {
        for (var node = anchor.Parent; node != null; node = node.Parent)
        {
            if (node.Kind != AkburaSyntaxKind.MarkupElementSyntax)
            {
                continue;
            }

            var markupElement = Unsafe.As<MarkupElementSyntax>(node);
            if (TryGetExplicitDataType(markupElement, out dataType) ||
                TryGetInheritedDataType(markupElement, out dataType, out var isTemplateBoundary))
            {
                return true;
            }

            if (isTemplateBoundary)
            {
                break;
            }
        }

        dataType = null!;
        return false;
    }

    public bool TryCreateItemSymbol(
        MarkupElementSyntax scope,
        out IMarkupItemSymbol? itemSymbol)
    {
        itemSymbol = null;
        if (!TryGetItemName(scope, out var declarationSyntax, out var itemName))
        {
            return false;
        }

        if (!TryGetExplicitDataType(scope, out var itemType) &&
            !TryGetInheritedDataType(scope, out itemType, out _))
        {
            return false;
        }

        var containingElement = GetParentElement(scope);
        var containingSymbol = containingElement == null
            ? null
            : _semanticModel.GetSymbolInfo(containingElement).Symbol;
        itemSymbol = new MarkupItemSymbol(
            itemName,
            new CSharpSymbolDefinition(itemType),
            declarationSyntax,
            containingSymbol);
        return true;
    }

    private bool TryGetExplicitDataType(
        MarkupElementSyntax markupElement,
        out INamedTypeSymbol dataType)
    {
        if (markupElement.StartTag != null)
        {
            foreach (var attribute in markupElement.StartTag.Attributes)
            {
                if (AkburaSemanticModel.IsMarkupDataTypeDirective(attribute) &&
                    AkburaSemanticModel.TryGetMarkupDataTypeText(attribute, out var typeText) &&
                    _semanticModel.TryBindMarkupDataTypeDirective(typeText, out dataType))
                {
                    return true;
                }
            }
        }

        dataType = null!;
        return false;
    }

    private static bool TryGetItemName(
        MarkupElementSyntax scope,
        out MarkupAttachedPropertyAttributeSyntax declarationSyntax,
        out string itemName)
    {
        declarationSyntax = null!;
        itemName = string.Empty;
        if (scope.StartTag == null)
        {
            return false;
        }

        foreach (var attribute in scope.StartTag.Attributes)
        {
            if (!AkburaSemanticModel.IsMarkupItemNameDirective(attribute) ||
                !AkburaSemanticModel.TryGetMarkupDataTypeText(attribute, out itemName) ||
                !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsValidIdentifier(itemName))
            {
                continue;
            }

            declarationSyntax = Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(attribute);
            return true;
        }

        itemName = string.Empty;
        return false;
    }

    private bool TryGetInheritedDataType(
        MarkupElementSyntax propertyElement,
        out INamedTypeSymbol dataType,
        out bool isTemplateBoundary)
    {
        dataType = null!;
        isTemplateBoundary = false;
        if (!TryGetPropertyElement(
                propertyElement,
                out var property,
                out var containingElement) ||
            !TryGetItemsDataTypeMetadata(
                property,
                out var itemsPropertyName,
                out var ancestorType))
        {
            return false;
        }

        isTemplateBoundary = true;
        var itemsOwner = FindAncestor(containingElement, ancestorType);
        if (itemsOwner == null ||
            !TryFindAttribute(itemsOwner, itemsPropertyName, out var itemsAttribute) ||
            !TryGetAttributeSourceType(itemsAttribute, out var itemsSourceType) ||
            !TryGetEnumerableElementType(itemsSourceType, out var itemType) ||
            itemType is not INamedTypeSymbol namedItemType ||
            namedItemType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        dataType = namedItemType;
        return true;
    }

    private bool TryGetPropertyElement(
        MarkupElementSyntax propertyElement,
        out Microsoft.CodeAnalysis.IPropertySymbol property,
        out MarkupElementSyntax containingElement)
    {
        property = null!;
        containingElement = null!;
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

        var parentElement = GetParentElement(propertyElement);
        if (parentElement == null ||
            _semanticModel.GetSymbolInfo(parentElement).Symbol is not IMarkupComponentSymbol { ComponentType: { } componentType })
        {
            return false;
        }

        var propertyName = nameText[(separator + 1)..].Trim();
        var resolvedProperty = AkburaSemanticModel.FindPublicClrProperty(componentType, propertyName);
        if (resolvedProperty == null)
        {
            return false;
        }

        property = resolvedProperty;
        containingElement = parentElement;
        return true;
    }

    private static bool TryGetItemsDataTypeMetadata(
        Microsoft.CodeAnalysis.IPropertySymbol property,
        out string itemsPropertyName,
        out INamedTypeSymbol ancestorType)
    {
        itemsPropertyName = string.Empty;
        ancestorType = null!;
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) !=
                InheritDataTypeFromItemsAttributeName ||
                attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string propertyName ||
                string.IsNullOrWhiteSpace(propertyName))
            {
                continue;
            }

            itemsPropertyName = propertyName;
            ancestorType = property.ContainingType;
            foreach (var namedArgument in attribute.NamedArguments)
            {
                if (namedArgument.Key == "AncestorType" &&
                    namedArgument.Value.Value is INamedTypeSymbol configuredAncestorType)
                {
                    ancestorType = configuredAncestorType;
                    break;
                }
            }

            return true;
        }

        return false;
    }

    private MarkupElementSyntax? FindAncestor(
        MarkupElementSyntax element,
        INamedTypeSymbol ancestorType)
    {
        for (var current = element; current != null; current = GetParentElement(current))
        {
            if (_semanticModel.GetSymbolInfo(current).Symbol is IMarkupComponentSymbol { ComponentType: { } componentType } &&
                AkburaSemanticModel.IsAssignableTo(componentType, ancestorType))
            {
                return current;
            }
        }

        return null;
    }

    private static MarkupElementSyntax? GetParentElement(MarkupElementSyntax element)
    {
        for (var node = element.Parent; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.MarkupElementSyntax)
            {
                return Unsafe.As<MarkupElementSyntax>(node);
            }
        }

        return null;
    }

    private static bool TryFindAttribute(
        MarkupElementSyntax element,
        string propertyName,
        out MarkupAttributeSyntax attribute)
    {
        if (element.StartTag != null)
        {
            foreach (var candidate in element.StartTag.Attributes)
            {
                if (string.Equals(
                        AkburaSemanticModel.GetMarkupPropertyName(candidate),
                        propertyName,
                        StringComparison.Ordinal))
                {
                    attribute = candidate;
                    return true;
                }
            }
        }

        attribute = null!;
        return false;
    }

    private bool TryGetAttributeSourceType(
        MarkupAttributeSyntax attribute,
        out ITypeSymbol sourceType)
    {
        sourceType = null!;
        var value = AkburaSemanticModel.GetMarkupAttributeValue(attribute);
        if (value == null)
        {
            return false;
        }

        if (value.Kind == AkburaSyntaxKind.MarkupDynamicAttributeValueSyntax)
        {
            var dynamicValue = Unsafe.As<MarkupDynamicAttributeValueSyntax>(value);
            var expression = AkburaSemanticModel.ParseInlineExpression(dynamicValue.Expression);
            if (expression == null)
            {
                return false;
            }

            var binding = _semanticModel.BindMarkupAttributeExpression(attribute, expression);
            sourceType = binding.Conversion.SourceType ??
                binding.TypeSymbol ??
                binding.OperationDefinition.Type!;
            return sourceType != null && sourceType.TypeKind != TypeKind.Error;
        }

        if (value.Kind == AkburaSyntaxKind.MarkupExtensionAttributeValueSyntax)
        {
            var extension = Unsafe.As<MarkupExtensionAttributeValueSyntax>(value);
            var property = _semanticModel.GetSymbolInfo(attribute).Symbol as Symbols.IPropertySymbol;
            var binding = _semanticModel.BindMarkupExtensionAttributeValue(
                attribute,
                extension.Extension,
                property);
            sourceType = binding.Value?.Binding?.ResultType.Symbol as ITypeSymbol ?? null!;
            return sourceType != null && sourceType.TypeKind != TypeKind.Error;
        }

        return false;
    }

    private static bool TryGetEnumerableElementType(
        ITypeSymbol type,
        out ITypeSymbol elementType)
    {
        if (type is IArrayTypeSymbol arrayType)
        {
            elementType = arrayType.ElementType;
            return true;
        }

        if (type is INamedTypeSymbol namedType &&
            IsGenericEnumerable(namedType))
        {
            elementType = namedType.TypeArguments[0];
            return true;
        }

        foreach (var @interface in type.AllInterfaces)
        {
            if (IsGenericEnumerable(@interface))
            {
                elementType = @interface.TypeArguments[0];
                return true;
            }
        }

        elementType = null!;
        return false;
    }

    private static bool IsGenericEnumerable(INamedTypeSymbol type)
    {
        var definition = type.OriginalDefinition;
        return definition.Name == "IEnumerable" &&
            definition.Arity == 1 &&
            definition.ContainingNamespace.ToDisplayString() == "System.Collections.Generic";
    }
}
