using Akbura.Language;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;

namespace Akbura.Workspaces.Completion;

internal sealed partial class AkburaCompletionService
{
    private static MarkupElementSyntax? FindCompletionElement(
        AkburaSemanticModel semanticModel,
        int position)
    {
        return semanticModel.SyntaxTree.GetRootSyntax().DescendantNodes()
            .OfType<MarkupElementSyntax>()
            .Where(element => element.StartTag != null &&
                element.StartTag.FullSpan.Start <= position &&
                element.StartTag.FullSpan.End >= position)
            .OrderBy(element => element.StartTag!.FullSpan.Length)
            .FirstOrDefault();
    }

    private static IEnumerable<AkburaCompletionItem> GetDictionaryKeyItems(
        AkburaSemanticModel semanticModel,
        AkburaSyntacticCompletionContext context,
        int position)
    {
        if (!MatchesPrefix("x.key", context.Prefix) ||
            FindCompletionElement(semanticModel, position) is not { } element ||
            element.StartTag is not { } startTag ||
            !semanticModel.TryGetMarkupDictionaryContext(element, out var contentModel) ||
            contentModel.DictionaryShape.ContractType == null ||
            contentModel.DictionaryShape.IsAmbiguous || contentModel.DictionaryShape.IsReadOnlyOnly ||
            startTag.Attributes.Any(static attribute =>
                AkburaSemanticModel.IsMarkupDictionaryKeyDirective(attribute) &&
                attribute is MarkupAttachedPropertyAttributeSyntax { EqualsToken.IsMissing: false }))
        {
            return [];
        }

        return [new AkburaCompletionItem(
            "x.key", "x.key=\"\"", AkburaCompletionKind.Property,
            "Sets the key of this entry in its parent dictionary.",
            descriptionFactory: null, caretOffsetFromEnd: 1)];
    }

    private static ImmutableArray<AkburaCompletionItem> GetAttributeValueItems(
        AkburaSemanticModel semanticModel,
        AkburaSyntacticCompletionContext context,
        int position,
        CancellationToken cancellationToken)
    {
        var element = FindCompletionElement(semanticModel, position);
        var attribute = element?.StartTag?.Attributes.FirstOrDefault(candidate =>
            candidate.Span.Start <= position && candidate.Span.End >= position);
        if (element == null || attribute == null ||
            semanticModel.GetSymbolInfo(attribute).Symbol is not AkburaPropertySymbol property)
        {
            return [];
        }

        var contract = semanticModel.GetMarkupPropertyAssignmentContract(property, element);
        if (contract.DeclaredType is { } declaredType &&
            semanticModel.IsAvaloniaPropertyType(declaredType))
        {
            return GetPropertyReferenceItems(semanticModel, element, declaredType, context.Prefix,
                cancellationToken);
        }

        if (contract.ContextualValueType is not { } valueType)
        {
            return [];
        }

        var items = new Dictionary<string, AkburaCompletionItem>(StringComparer.Ordinal);
        if (valueType.TypeKind == TypeKind.Enum)
        {
            foreach (var field in valueType.GetMembers().OfType<IFieldSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (field.HasConstantValue)
                {
                    AddLiteralValueItem(items, field.Name, field, context.Prefix);
                }
            }
        }
        else if (valueType.SpecialType == SpecialType.System_Boolean)
        {
            foreach (var value in new[] { "false", "true" })
            {
                if (MatchesPrefix(value, context.Prefix))
                {
                    items[value] = new AkburaCompletionItem(value, value,
                        AkburaCompletionKind.AkcssValue, "bool literal", descriptionFactory: null);
                }
            }
        }

        if (semanticModel.IsAkcssColorPropertyType(valueType) &&
            semanticModel.Compilation.CSharpCompilation.GetTypeByMetadataName(
                "Avalonia.Media.Colors") is { } colorsType)
        {
            foreach (var member in colorsType.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (member.IsStatic && member.DeclaredAccessibility == Accessibility.Public &&
                    member is IFieldSymbol or Microsoft.CodeAnalysis.IPropertySymbol)
                {
                    AddLiteralValueItem(items, member.Name, member, context.Prefix);
                }
            }
        }

        return OrderCompletionItems(items.Values, context.Prefix);
    }

    private static void AddLiteralValueItem(
        Dictionary<string, AkburaCompletionItem> items,
        string name,
        Microsoft.CodeAnalysis.ISymbol symbol,
        string prefix)
    {
        if (MatchesPrefix(name, prefix))
        {
            items[name] = new AkburaCompletionItem(name, name,
                AkburaCompletionKind.AkcssValue, symbol.ToDisplayString(),
                descriptionFactory: null);
        }
    }

    private static ImmutableArray<AkburaCompletionItem> GetPropertyReferenceItems(
        AkburaSemanticModel semanticModel,
        MarkupElementSyntax element,
        ITypeSymbol declaredType,
        string prefix,
        CancellationToken cancellationToken)
    {
        var items = new Dictionary<string, AkburaCompletionItem>(StringComparer.Ordinal);
        var separator = prefix.LastIndexOf('.');
        ImmutableArray<INamedTypeSymbol> owners;
        var ownerPrefix = string.Empty;
        if (separator >= 0)
        {
            var ownerName = prefix.Substring(0, separator);
            if (!semanticModel.TryResolveMarkupComponentForCompletion(ownerName, out var owner) ||
                owner.ComponentType == null)
            {
                return [];
            }

            owners = [owner.ComponentType];
            ownerPrefix = ownerName + ".";
        }
        else
        {
            owners = semanticModel.GetMarkupStyleTargetContext(element).Types;
            foreach (var candidate in semanticModel.LookupMarkupComponents(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (candidate.ComponentType is not { } type ||
                    !MatchesPrefix(candidate.DisplayName, prefix) ||
                    !GetPropertyReferenceFields(semanticModel, type).Any(field =>
                        AkburaSemanticModel.IsAssignableTo(field.Type, declaredType)))
                {
                    continue;
                }

                var name = candidate.DisplayName + ".";
                items[name] = new AkburaCompletionItem(name, name,
                    AkburaCompletionKind.Component, type.ToDisplayString(),
                    descriptionFactory: null, triggerCompletionAfterInsert: true);
            }
        }

        foreach (var owner in owners)
        {
            foreach (var field in GetPropertyReferenceFields(semanticModel, owner))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var name = field.Name.Substring(0, field.Name.Length - "Property".Length);
                var referenceText = ownerPrefix + name;
                if (!MatchesPrefix(referenceText, prefix) ||
                    semanticModel.ResolveMarkupAvaloniaPropertyReference(referenceText, element)
                        is not { } reference ||
                    !AkburaSemanticModel.IsAssignableTo(reference.Field.Type, declaredType))
                {
                    continue;
                }

                items[referenceText] = new AkburaCompletionItem(referenceText, referenceText,
                    AkburaCompletionKind.Property, reference.Field.ToDisplayString(),
                    descriptionFactory: null,
                    suffix: reference.ValueType.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat));
            }
        }

        return OrderCompletionItems(items.Values, prefix);
    }

    private static IEnumerable<IFieldSymbol> GetPropertyReferenceFields(
        AkburaSemanticModel semanticModel,
        INamedTypeSymbol owner)
    {
        for (var type = owner; type != null; type = type.BaseType)
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (field.IsStatic && field.DeclaredAccessibility == Accessibility.Public &&
                    field.Name.EndsWith("Property", StringComparison.Ordinal) &&
                    field.Name.Length > "Property".Length &&
                    semanticModel.IsAvaloniaPropertyType(field.Type))
                {
                    yield return field;
                }
            }
        }
    }
}
