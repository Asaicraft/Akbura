using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using RoslynTypeSymbol = Microsoft.CodeAnalysis.ITypeSymbol;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    internal static bool IsMarkupDictionaryKeyDirective(MarkupAttributeSyntax attribute) =>
        IsMarkupDirective(attribute, "key") || IsMarkupDirective(attribute, "Key");

    internal bool TryGetMarkupDictionaryContext(
        MarkupElementSyntax child,
        out MarkupContentModel contentModel)
    {
        contentModel = default;
        var parent = GetParentMarkupElement(child);
        if (parent == null)
        {
            return false;
        }

        // Component resolution publishes the content contract before binding descendants.
        // Never request a full parent component while its children are being constructed.
        if (TryGetCachedSymbolInfo(parent, out var cached))
        {
            if (cached.Symbol is IMarkupComponentSymbol component)
            {
                contentModel = component.ContentModel;
                return contentModel.IsDictionary;
            }

            if (cached.Symbol is Symbols.IPropertySymbol property)
            {
                contentModel = CreateMarkupPropertyElementContentModel(property);
                return contentModel.IsDictionary;
            }
        }

        var parentName = parent.StartTag?.Name.ToFullString().Trim();
        if (string.IsNullOrEmpty(parentName))
        {
            return false;
        }

        var owner = GetParentMarkupElement(parent);
        var separator = parentName!.LastIndexOf('.');
        if (owner != null && separator > 0 &&
            TryGetCachedSymbolInfo(owner, out var ownerInfo) &&
            ownerInfo.Symbol is IMarkupComponentSymbol ownerComponent &&
            ownerComponent.AkburaComponent is { } akburaComponent &&
            IsMarkupPropertyElementOwner(akburaComponent, parentName[..separator]))
        {
            var parameter = FindComponentParameter(ownerComponent, parentName[(separator + 1)..]);
            if (parameter?.Type.Symbol is RoslynTypeSymbol parameterType &&
                TryCreateMarkupDictionaryContentModel(parameterType, default, parameter, out contentModel))
            {
                return true;
            }
        }

        if (owner != null && separator > 0 &&
            TryGetMarkupElementTypeWithoutChildren(owner, out var ownerType) &&
            IsMarkupPropertyElementOwner(ownerType, parentName[..separator]))
        {
            var clrProperty = FindPublicClrProperty(ownerType, parentName[(separator + 1)..]);
            if (clrProperty != null &&
                TryCreateMarkupDictionaryContentModel(clrProperty.Type,
                    new CSharpSymbolDefinition(clrProperty), null, out contentModel))
            {
                return true;
            }

            return false;
        }

        if (!TryGetMarkupElementTypeWithoutChildren(parent, out var parentType))
        {
            return false;
        }

        contentModel = CreateMarkupContentModel(parentType, parent);
        return contentModel.IsDictionary;
    }

    private bool TryGetMarkupElementTypeWithoutChildren(
        MarkupElementSyntax element,
        out INamedTypeSymbol type)
    {
        type = null!;
        if (TryGetCachedSymbolInfo(element, out var cached) &&
            cached.Symbol is IMarkupComponentSymbol component &&
            component.ComponentType is { } cachedType)
        {
            type = cachedType;
            return true;
        }

        try
        {
            return element.StartTag != null &&
                TryGetMarkupComponentType(BindCSharpType(element.StartTag.Name.ToCSharp()), out type);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private bool TryCreateMarkupDictionaryContentModel(
        RoslynTypeSymbol type,
        CSharpSymbolDefinition contentProperty,
        IParamSymbol? parameter,
        out MarkupContentModel contentModel)
    {
        var shape = MarkupDictionaryShape.Create(type, Compilation.CSharpCompilation);
        contentModel = shape.IsDictionary
            ? new MarkupContentModel(contentProperty,
                shape.ValueType == null ? default : new CSharpSymbolDefinition(shape.ValueType),
                isCollection: false, allowsText: false, contentParameter: parameter,
                dictionaryShape: shape)
            : default;
        return shape.IsDictionary;
    }

    private void AddMarkupDictionaryContentDiagnostics(
        MarkupElementSyntax element,
        MarkupContentModel model,
        ImmutableArrayBuilder<MarkupChildContent> children,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnostics)
    {
        if (!model.IsDictionary)
        {
            return;
        }

        var shape = model.DictionaryShape;
        if (shape.IsAmbiguous || shape.IsReadOnlyOnly)
        {
            diagnostics.Add(new AkburaSemanticDiagnostic(element,
                shape.IsAmbiguous ? ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryContractAmbiguous
                    : ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryReadOnlyTarget, []));
        }

        var constants = new HashSet<object>();
        foreach (var child in children.WrittenSpan)
        {
            if (child.Kind == MarkupChildKind.Conditional)
            {
                continue;
            }

            if (child.Syntax is not MarkupElementContentSyntax childElement)
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(child.Syntax,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyRequired, []));
                continue;
            }

            IMarkupDictionaryKeyOperation? key = null;
            foreach (var attribute in child.ComponentSymbol?.AttributeOperations ??
                System.Collections.Immutable.ImmutableArray<IMarkupAttributeOperation>.Empty)
            {
                if (attribute is IMarkupDictionaryKeyOperation dictionaryKey)
                {
                    key = dictionaryKey;
                    break;
                }
            }

            var hasKeyDirective = false;
            if (childElement.Element.StartTag is { } startTag)
            {
                foreach (var attribute in startTag.Attributes)
                {
                    if (IsMarkupDictionaryKeyDirective(attribute))
                    {
                        hasKeyDirective = true;
                        break;
                    }
                }
            }

            if (key == null && !hasKeyDirective)
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(childElement.Element.StartTag ??
                    (AkburaSyntax)childElement.Element,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryKeyRequired, []));
            }
            else if (key != null && !key.HasErrors && key.HasConstantValue && key.ConstantValue is { } constant &&
                !(shape.KeyType?.IsReferenceType == true && constant.GetType().IsValueType) &&
                shape.KeyType?.SpecialType is not (SpecialType.System_Single or SpecialType.System_Double or
                    SpecialType.System_Decimal) &&
                !constants.Add(constant))
            {
                diagnostics.Add(new AkburaSemanticDiagnostic(key.Syntax,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDictionaryDuplicateConstantKey,
                    [constant.ToString() ?? string.Empty]));
            }
        }
    }
}
