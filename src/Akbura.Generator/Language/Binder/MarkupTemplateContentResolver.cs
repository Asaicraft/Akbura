using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language.Binder;

internal sealed class MarkupTemplateContentResolver
{
    private const string TemplateContentAttributeName =
        "global::Avalonia.Metadata.TemplateContentAttribute";
    private const string DataTypeAttributeName =
        "global::Avalonia.Metadata.DataTypeAttribute";
    private const string DataTemplateTypeName =
        "Avalonia.Controls.Templates.IDataTemplate";

    private readonly AkburaSemanticModel _semanticModel;

    public MarkupTemplateContentResolver(
        AkburaSemanticModel semanticModel)
    {
        _semanticModel = semanticModel ??
            throw new ArgumentNullException(
                nameof(semanticModel));
    }

    /// <summary>
    /// Returns true when the children of this markup element
    /// belong to deferred template content.
    /// </summary>
    internal bool IsDeferredContent(
        MarkupElementSyntax element,
        MarkupContentModel contentModel)
    {
        // Example:
        //
        // <DataTemplate>
        //     <Button />
        // </DataTemplate>
        //
        // DataTemplate.Content itself has [TemplateContent].
        if (contentModel.ContentProperty.Symbol
                is RoslynPropertySymbol contentProperty &&
            IsDeferredContentProperty(contentProperty))
        {
            return true;
        }

        // Example:
        //
        // <DataTemplate>
        //     <Button>
        //         <TextBlock />
        //     </Button>
        // </DataTemplate>
        //
        // Button.Content does not have [TemplateContent],
        // but Button is already inside a deferred section.
        return IsInsideDeferredContent(element);
    }

    /// <summary>
    /// Checks whether an element is nested somewhere inside
    /// a property marked with TemplateContentAttribute.
    /// </summary>
    public bool IsInsideDeferredContent(
        MarkupElementSyntax element)
    {
        for (var ancestor = element.Parent;
             ancestor != null;
             ancestor = ancestor.Parent)
        {
            if (ancestor is not MarkupElementSyntax ancestorElement)
            {
                continue;
            }

            var symbol =
                _semanticModel.GetSymbolInfo(ancestorElement).Symbol;

            // Explicit property element:
            //
            // <DataTemplate.Content>
            //     <Button />
            // </DataTemplate.Content>
            if (symbol is AkburaPropertySymbol propertySymbol)
            {
                var clrProperty =
                    GetClrProperty(propertySymbol);

                if (clrProperty != null &&
                    IsDeferredContentProperty(clrProperty))
                {
                    return true;
                }

                continue;
            }

            // Implicit content property:
            //
            // <DataTemplate>
            //     <Button />
            // </DataTemplate>
            if (symbol is IMarkupComponentSymbol component &&
                component.ContentModel.ContentProperty.Symbol
                    is RoslynPropertySymbol componentContentProperty &&
                IsDeferredContentProperty(componentContentProperty))
            {
                return true;
            }
        }

        return false;
    }

    internal MarkupElementSyntax? GetLocalNameScopeOwner(
        AkburaSyntax syntax)
    {
        MarkupElementSyntax? element = null;
        for (var current = syntax;
             current != null;
             current = current.Parent)
        {
            if (current is MarkupElementSyntax markupElement)
            {
                element = markupElement;
                break;
            }
        }

        return element == null
            ? null
            : GetLocalNameScopeOwner(element);
    }

    internal MarkupElementSyntax? GetLocalNameScopeOwner(
        MarkupElementSyntax element)
    {
        var directChild = element;
        for (var ancestor = element.Parent;
             ancestor != null;
             ancestor = ancestor.Parent)
        {
            if (ancestor is not MarkupElementSyntax ancestorElement)
            {
                continue;
            }

            if (DefinesLocalNameScope(
                    ancestorElement,
                    directChild))
            {
                return ancestorElement;
            }

            directChild = ancestorElement;
        }

        return null;
    }

    private bool DefinesLocalNameScope(
        MarkupElementSyntax element,
        MarkupElementSyntax directChild)
    {
        var symbol = _semanticModel.GetSymbolInfo(element).Symbol;
        if (symbol is AkburaPropertySymbol property)
        {
            var clrProperty = GetClrProperty(property);
            if (clrProperty == null)
            {
                return false;
            }

            if (IsDeferredContentProperty(clrProperty))
            {
                return true;
            }

            return IsDataTemplateProperty(clrProperty) &&
                   !IsDataTemplateElement(directChild);
        }

        return symbol is IMarkupComponentSymbol component &&
               _semanticModel.GetSymbolInfo(directChild).Symbol
                   is not AkburaPropertySymbol &&
               component.ContentModel.ContentProperty.Symbol
                   is RoslynPropertySymbol contentProperty &&
               IsDeferredContentProperty(contentProperty);
    }

    private bool IsDataTemplateElement(
        MarkupElementSyntax element)
    {
        if (_semanticModel.TryGetMarkupElementReferenceType(
                element,
                out var referenceType) &&
            referenceType.Symbol is ITypeSymbol resolvedType)
        {
            return IsDataTemplateType(resolvedType);
        }

        return _semanticModel.GetSymbolInfo(element).Symbol
                   is IMarkupComponentSymbol component &&
               (component.ComponentType ??
                component.AkburaComponent?.ComponentType) is { } componentType &&
               IsDataTemplateType(componentType);
    }

    /// <summary>
    /// True only for properties with TemplateContentAttribute.
    /// </summary>
    internal bool IsDeferredContentProperty(
        RoslynPropertySymbol property)
    {
        return FindTemplateContentAttribute(property) != null;
    }

    /// <summary>
    /// True when the property's type implements IDataTemplate.
    /// This is separate from TemplateContentAttribute.
    /// </summary>
    internal bool IsDataTemplateProperty(
        RoslynPropertySymbol property)
    {
        return IsDataTemplateType(property.Type);
    }

    internal bool IsDataTemplateType(ITypeSymbol type)
    {
        var dataTemplateType =
            _semanticModel.Compilation.CSharpCompilation
                .GetTypeByMetadataName(DataTemplateTypeName);

        return dataTemplateType != null &&
               AkburaSemanticModel.IsAssignableTo(
                   type,
                   dataTemplateType);
    }

    internal RoslynPropertySymbol? FindDataTypeProperty(
        INamedTypeSymbol type)
    {
        for (var current = type;
             current != null;
             current = current.BaseType)
        {
            foreach (var property in current.GetMembers()
                         .OfType<RoslynPropertySymbol>())
            {
                if (property.IsStatic ||
                    property.DeclaredAccessibility !=
                        Accessibility.Public ||
                    property.SetMethod?.DeclaredAccessibility !=
                        Accessibility.Public)
                {
                    continue;
                }

                for (var candidate = property;
                     candidate != null;
                     candidate = candidate.OverriddenProperty)
                {
                    foreach (var attribute in
                             candidate.GetAttributes())
                    {
                        if (attribute.AttributeClass?.ToDisplayString(
                                SymbolDisplayFormat
                                    .FullyQualifiedFormat) ==
                            DataTypeAttributeName)
                        {
                            return property;
                        }
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves TemplateContentAttribute.TemplateResultType.
    /// Avalonia uses Control when TemplateResultType is omitted.
    /// </summary>
    internal ITypeSymbol GetDeferredResultType(
        RoslynPropertySymbol property)
    {
        var attribute =
            FindTemplateContentAttribute(property);

        if (attribute != null)
        {
            foreach (var argument in attribute.NamedArguments)
            {
                if (argument.Key == "TemplateResultType" &&
                    argument.Value.Kind == TypedConstantKind.Type &&
                    argument.Value.Value is ITypeSymbol resultType)
                {
                    return resultType;
                }
            }
        }

        return _semanticModel.Compilation.CSharpCompilation
                   .GetTypeByMetadataName(
                       "Avalonia.Controls.Control")
               ?? _semanticModel.Compilation.CSharpCompilation
                   .GetSpecialType(
                       SpecialType.System_Object);
    }

    private static AttributeData?
        FindTemplateContentAttribute(
            RoslynPropertySymbol property)
    {
        for (var current = property;
             current != null;
             current = current.OverriddenProperty)
        {
            foreach (var attribute in current.GetAttributes())
            {
                if (attribute.AttributeClass?.ToDisplayString(
                        SymbolDisplayFormat.FullyQualifiedFormat) ==
                    TemplateContentAttributeName)
                {
                    return attribute;
                }
            }
        }

        return null;
    }

    private static RoslynPropertySymbol?
        GetClrProperty(
            AkburaPropertySymbol property)
    {
        return property.ClrPropertyDefinition.Symbol
                   as RoslynPropertySymbol
               ?? property.WriteDefinition.Symbol
                   as RoslynPropertySymbol
               ?? property.ReadDefinition.Symbol
                   as RoslynPropertySymbol;
    }
}
