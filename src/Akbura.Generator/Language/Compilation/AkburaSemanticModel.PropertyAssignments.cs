using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Operations;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;
using AkburaPropertySymbol = Akbura.Language.Symbols.IPropertySymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language;

internal abstract partial class AkburaSemanticModel
{
    private readonly MarkupPropertyMetadataReader _markupPropertyMetadata = new();
    private readonly ConcurrentDictionary<string, CSharpBindingResult> _markupReferenceOwners =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<MarkupElementSyntax, MarkupStyleTargetContext> _markupStyleTargets = new();

    internal MarkupPropertyMetadataReader MarkupPropertyMetadata => _markupPropertyMetadata;

    internal MarkupPropertyAssignmentContract GetMarkupPropertyAssignmentContract(
        AkburaPropertySymbol? property, MarkupElementSyntax? element)
    {
        var member = property?.ClrPropertyDefinition.Symbol ?? property?.WriteDefinition.Symbol;
        var declaredType = (member as RoslynPropertySymbol)?.Type ?? property?.Type.Symbol as ITypeSymbol;
        var metadata = member == null ? default : _markupPropertyMetadata.GetMetadata(member);
        var reference = default(ResolvedMarkupPropertyReference);
        var unknown = false;
        var ambiguous = false;

        if (declaredType?.SpecialType == SpecialType.System_Object && element != null &&
            !metadata.Dependencies.IsDefaultOrEmpty)
        {
            foreach (var dependency in metadata.Dependencies)
            {
                if (dependency is not RoslynPropertySymbol dependencyProperty ||
                    !IsAvaloniaPropertyType(dependencyProperty.Type))
                {
                    continue;
                }

                var dependencyReference = ResolveAssignedPropertyReference(element, dependencyProperty.Name);
                if (dependencyReference == null)
                {
                    // An omitted dependency may be initialized by the holder's constructor.
                    unknown |= HasMarkupAssignment(element, dependencyProperty.Name);
                    continue;
                }

                if (reference == null)
                {
                    reference = dependencyReference;
                }
                else if (!SymbolEqualityComparer.Default.Equals(reference.ValueType, dependencyReference.ValueType))
                {
                    ambiguous = true;
                }
            }
        }

        return new MarkupPropertyAssignmentContract(declaredType,
            !ambiguous && !unknown && reference != null ? reference.ValueType : declaredType,
            metadata, reference, unknown, ambiguous);
    }

    internal MarkupPropertyAssignmentContract GetMarkupPropertyAssignmentContract(AkburaSyntax assignment)
    {
        var element = assignment as MarkupElementSyntax ?? FindContainingElement(assignment);
        var assignmentProperty = GetSymbolInfo(assignment).Symbol as AkburaPropertySymbol;
        var property = assignmentProperty;
        if (property == null && element != null &&
            GetSymbolInfo(element).Symbol is IMarkupComponentSymbol component)
        {
            property = CreateMarkupContentPropertySymbol(component);
        }

        if (assignment is MarkupElementSyntax propertyElement && assignmentProperty != null)
        {
            element = GetParentMarkupElement(propertyElement) ?? element;
        }

        return GetMarkupPropertyAssignmentContract(property, element);
    }

    internal ResolvedMarkupPropertyReference? GetMarkupAvaloniaPropertyReference(
        MarkupLiteralAttributeValueSyntax literal)
    {
        var attribute = literal.Parent as MarkupAttributeSyntax;
        var element = FindContainingElement(literal);
        if (attribute == null || element == null ||
            GetSymbolInfo(attribute).Symbol is not AkburaPropertySymbol property ||
            property.Type.Symbol is not ITypeSymbol type || !IsAvaloniaPropertyType(type))
        {
            return null;
        }

        return ResolveMarkupAvaloniaPropertyReference(GetMarkupLiteralAttributeValueText(literal),
            element, GetMarkupLiteralTextSpan(literal));
    }

    internal ResolvedMarkupPropertyReference? ResolveMarkupAvaloniaPropertyReference(
        string text, MarkupElementSyntax element, TextSpan span = default)
    {
        var trimmed = text.Trim();
        var offset = text.IndexOf(trimmed, StringComparison.Ordinal);
        var separator = trimmed.LastIndexOf('.');
        var propertyName = separator >= 0 ? trimmed[(separator + 1)..] : trimmed;
        if (propertyName.EndsWith("Property", StringComparison.Ordinal))
        {
            propertyName = propertyName[..^8];
        }

        if (string.IsNullOrWhiteSpace(propertyName))
        {
            return null;
        }

        var ownerSpan = separator < 0 ? default : new TextSpan(span.Start + offset, separator);
        var propertySpan = new TextSpan(span.Start + offset + separator + 1,
            trimmed.Length - separator - 1);
        if (separator >= 0)
        {
            var owner = ResolveMarkupReferenceOwner(trimmed[..separator]);
            return owner == null ? null : ResolvePropertyReference(owner, propertyName, ownerSpan, propertySpan);
        }

        var target = GetMarkupStyleTargetContext(element);
        if (target.IsUnknown || target.Types.IsDefaultOrEmpty)
        {
            return null;
        }

        ResolvedMarkupPropertyReference? selected = null;
        foreach (var owner in target.Types)
        {
            var candidate = ResolvePropertyReference(owner, propertyName, ownerSpan, propertySpan);
            if (candidate == null || selected != null &&
                !SymbolEqualityComparer.Default.Equals(selected.Field, candidate.Field))
            {
                return null;
            }

            selected = candidate;
        }

        return selected;
    }

    private ResolvedMarkupPropertyReference? ResolvePropertyReference(INamedTypeSymbol owner,
        string propertyName, TextSpan ownerSpan, TextSpan propertySpan)
    {
        var field = FindAvaloniaPropertyField(owner, propertyName);
        return field != null && TryGetAvaloniaPropertyValueType(field.Type, out var valueType)
            ? new ResolvedMarkupPropertyReference(owner, field, valueType, ownerSpan, propertySpan)
            : null;
    }

    internal INamedTypeSymbol? ResolveMarkupReferenceOwner(string name)
    {
        name = name.Replace('|', '.');
        var binding = _markupReferenceOwners.GetOrAdd(name,
            owner => BindCSharpType(Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTypeName(owner)));
        return binding.Symbol as INamedTypeSymbol ?? binding.TypeSymbol as INamedTypeSymbol;
    }

    private ResolvedMarkupPropertyReference? ResolveAssignedPropertyReference(
        MarkupElementSyntax element, string propertyName)
    {
        if (element.StartTag == null)
        {
            return null;
        }
        foreach (var attribute in element.StartTag.Attributes)
        {
            if (GetMarkupAssignmentName(attribute) != propertyName)
            {
                continue;
            }

            var value = GetMarkupAttributeValue(attribute);
            if (value is MarkupLiteralAttributeValueSyntax literal)
            {
                return ResolveMarkupAvaloniaPropertyReference(GetMarkupLiteralAttributeValueText(literal),
                    element, GetMarkupLiteralTextSpan(literal));
            }

            if (value is MarkupDynamicAttributeValueSyntax dynamicValue)
            {
                return ResolveExpressionPropertyReference(attribute,
                    ParseInlineExpression(dynamicValue.Expression));
            }
        }

        foreach (var content in element.Body)
        {
            if (content is not MarkupElementContentSyntax child ||
                !(child.Element.StartTag?.Name.ToString() ?? string.Empty).EndsWith("." + propertyName, StringComparison.Ordinal))
            {
                continue;
            }

            if (TryCreateMarkupContentValueExpression(child.Element, MarkupWhitespaceMode.Default,
                out var expression, out var literalValue, out _, out _, out var diagnosticSyntax))
            {
                if (literalValue != null)
                {
                    var raw = diagnosticSyntax.ToFullString();
                    var offset = raw.IndexOf(literalValue, StringComparison.Ordinal);
                    return ResolveMarkupAvaloniaPropertyReference(literalValue, element,
                        new TextSpan(diagnosticSyntax.FullSpan.Start + Math.Max(0, offset), literalValue.Length));
                }

                return ResolveExpressionPropertyReference(diagnosticSyntax, expression);
            }
        }

        return null;
    }

    private ResolvedMarkupPropertyReference? ResolveExpressionPropertyReference(
        AkburaSyntax syntax, Microsoft.CodeAnalysis.CSharp.Syntax.ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return null;
        }

        var binding = BindMarkupAttributeExpression(syntax, expression);
        Microsoft.CodeAnalysis.IOperation? operation = binding.OperationDefinition.Operation;
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        var field = (operation as IFieldReferenceOperation)?.Field ?? binding.Symbol as IFieldSymbol;
        return field != null && IsAvaloniaPropertyType(field.Type) &&
            TryGetAvaloniaPropertyValueType(field.Type, out var valueType)
            ? new ResolvedMarkupPropertyReference(field.ContainingType, field, valueType, default, syntax.Span)
            : null;
    }

    private static bool HasMarkupAssignment(MarkupElementSyntax element, string name) =>
        element.StartTag != null && element.StartTag.Attributes.Any(attribute => GetMarkupAssignmentName(attribute) == name) ||
        element.Body.OfType<MarkupElementContentSyntax>().Any(child =>
            (child.Element.StartTag?.Name.ToString() ?? string.Empty).EndsWith("." + name, StringComparison.Ordinal));

    private static string GetMarkupAssignmentName(MarkupAttributeSyntax attribute) => attribute switch
    {
        MarkupPlainAttributeSyntax plain => plain.Name.ToString(),
        MarkupAttachedPropertyAttributeSyntax attached => attached.OwnerType.ToString() + "." + attached.Name.ToString(),
        _ => string.Empty,
    };

    private static MarkupElementSyntax? FindContainingElement(AkburaSyntax syntax)
    {
        for (var current = syntax; current != null; current = current.Parent)
        {
            if (current is MarkupElementSyntax element)
            {
                return element;
            }
        }

        return null;
    }

    internal static TextSpan GetMarkupLiteralTextSpan(MarkupLiteralAttributeValueSyntax literal) =>
        new((literal.Value?.Span.Start ?? literal.Span.Start) + 1,
            Math.Max(0, (literal.Value?.Span.Length ?? literal.Span.Length) - 2));

    internal MarkupStyleTargetContext GetMarkupStyleTargetContext(MarkupElementSyntax element)
        => _markupStyleTargets.GetOrAdd(element, ResolveMarkupStyleTargetContext);

    private MarkupStyleTargetContext ResolveMarkupStyleTargetContext(MarkupElementSyntax element)
    {
        for (var current = element; current != null; current = GetParentMarkupElement(current))
        {
            if (current.StartTag == null)
            {
                continue;
            }
            var type = ResolveMarkupReferenceOwner(current.StartTag.Name.ToString());
            if (IsMarkupTypeOrBase(type, "Avalonia.Styling.ControlTheme"))
            {
                foreach (var attribute in current.StartTag.Attributes)
                {
                    if (GetMarkupAssignmentName(attribute) == "TargetType" &&
                        GetMarkupAttributeValue(attribute) is MarkupLiteralAttributeValueSyntax literalTarget)
                    {
                        var target = ResolveMarkupReferenceOwner(GetMarkupLiteralAttributeValueText(literalTarget));
                        return new MarkupStyleTargetContext(target == null ? default : ImmutableArray.Create(target));
                    }

                    if (GetMarkupAssignmentName(attribute) == "TargetType" &&
                        GetMarkupAttributeValue(attribute) is MarkupDynamicAttributeValueSyntax value &&
                        ParseInlineExpression(value.Expression) is Microsoft.CodeAnalysis.CSharp.Syntax.TypeOfExpressionSyntax typeOf)
                    {
                        var target = ResolveMarkupReferenceOwner(typeOf.Type.ToString());
                        return new MarkupStyleTargetContext(target == null ? default : ImmutableArray.Create(target));
                    }
                }

                return new MarkupStyleTargetContext(default, isUnknown: true);
            }

            if (!IsMarkupTypeOrBase(type, "Avalonia.Styling.Style"))
            {
                continue;
            }

            foreach (var attribute in current.StartTag.Attributes)
            {
                if (GetMarkupAssignmentName(attribute) != "Selector")
                {
                    continue;
                }

                if (GetMarkupAttributeValue(attribute) is not MarkupLiteralAttributeValueSyntax literal)
                {
                    return new MarkupStyleTargetContext(default, isUnknown: true);
                }

                var parent = GetParentMarkupElement(current);
                var inherited = parent == null ? new MarkupStyleTargetContext(default, true)
                    : GetMarkupStyleTargetContext(parent);
                var selector = ResolveMarkupSelectorLiteral(GetMarkupLiteralAttributeValueText(literal),
                    GetMarkupLiteralTextSpan(literal));
                return selector == null ? new MarkupStyleTargetContext(default, true)
                    : GetSelectorTargetContext(selector, inherited);
            }

            return new MarkupStyleTargetContext(default, true);
        }

        return new MarkupStyleTargetContext(default, true);
    }

    private static bool IsMarkupTypeOrBase(INamedTypeSymbol? type, string metadataName)
    {
        for (var current = type; current != null; current = current.BaseType)
        {
            if (current.ToDisplayString() == metadataName)
            {
                return true;
            }
        }

        return false;
    }
}
