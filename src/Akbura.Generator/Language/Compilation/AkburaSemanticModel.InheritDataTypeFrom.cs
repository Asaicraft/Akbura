using Akbura.Language.Operations;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System;
using System.Collections.Immutable;
using System.Linq;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private const string InheritDataTypeFromAttributeMetadataName =
        "Avalonia.Metadata.InheritDataTypeFromAttribute";

    internal bool TryGetInheritedDataTypeFromScopeKind(
        ISymbol targetMember,
        out string scopeKind)
    {
        foreach (var attribute in targetMember.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() !=
                InheritDataTypeFromAttributeMetadataName ||
                attribute.ConstructorArguments.Length != 1)
            {
                continue;
            }

            var argument = attribute.ConstructorArguments[0];
            if (argument.Type is not INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType ||
                argument.Value == null)
            {
                continue;
            }

            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue &&
                    Equals(member.ConstantValue, argument.Value))
                {
                    scopeKind = member.Name;
                    return scopeKind is "Style" or "ControlTemplate";
                }
            }
        }

        scopeKind = string.Empty;
        return false;
    }

    internal bool TryGetInheritedDataTypeFromScopeTargetType(
        MarkupAttributeSyntax anchor,
        string scopeKind,
        out INamedTypeSymbol targetType)
    {
        switch (scopeKind)
        {
            case "ControlTemplate":
                return TryGetControlTemplateTargetType(anchor, out targetType);

            case "Style":
                var element = GetContainingMarkupElement(anchor);
                if (element != null)
                {
                    var target = GetMarkupStyleTargetContext(element);
                    if (!target.IsUnknown &&
                        !target.IsAmbiguous &&
                        target.Types.Length == 1)
                    {
                        targetType = target.Types[0];
                        return true;
                    }
                }

                break;
        }

        targetType = null!;
        return false;
    }

    internal ImmutableArray<MarkupInheritedAvaloniaPropertyCompletionCandidate>
        LookupInheritedAvaloniaPropertiesForMarkupExtensionCompletion(
            MarkupAttributeSyntax attribute,
            MarkupExtensionSyntax extension,
            int argumentIndex,
            string? argumentName,
            CancellationToken cancellationToken = default)
    {
        ValidateMarkupCompletionSyntax(attribute, extension);
        if (!TryGetMarkupExtensionArgumentTargetMember(
                attribute,
                extension,
                argumentIndex,
                argumentName,
                out var targetMember) ||
            !IsAvaloniaPropertyType(GetMarkupExtensionTargetMemberType(targetMember)) ||
            !TryGetInheritedDataTypeFromScopeKind(targetMember, out var scopeKind) ||
            !TryGetInheritedDataTypeFromScopeTargetType(
                attribute,
                scopeKind,
                out var targetType))
        {
            return [];
        }

        using var items =
            ImmutableArrayBuilder<MarkupInheritedAvaloniaPropertyCompletionCandidate>
                .Rent();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var current = targetType; current != null; current = current.BaseType)
        {
            foreach (var field in current.GetMembers().OfType<IFieldSymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!field.IsStatic ||
                    field.DeclaredAccessibility != Accessibility.Public ||
                    !field.Name.EndsWith("Property", StringComparison.Ordinal) ||
                    field.Name.Length == "Property".Length ||
                    !IsAvaloniaPropertyType(field.Type))
                {
                    continue;
                }

                var name = field.Name[..^"Property".Length];
                if (!names.Add(name))
                {
                    continue;
                }

                if (!TryGetAvaloniaPropertyValueType(field.Type, out var valueType))
                {
                    continue;
                }

                items.Add(new MarkupInheritedAvaloniaPropertyCompletionCandidate(
                    name,
                    field,
                    valueType));
            }
        }

        return items.ToImmutable();
    }

    internal bool TryGetMarkupExtensionArgumentTargetMember(
        MarkupAttributeSyntax attribute,
        MarkupExtensionSyntax extension,
        int argumentIndex,
        string? argumentName,
        out ISymbol targetMember)
    {
        if (!TryGetMarkupExtensionCompletionType(
                extension,
                out var extensionType,
                out _))
        {
            targetMember = null!;
            return false;
        }

        if (argumentName != null)
        {
            targetMember = FindMarkupExtensionSettableProperty(
                extensionType,
                argumentName,
                out _) ?? null!;
            return targetMember != null;
        }

        var inheritedParameters = extensionType.InstanceConstructors
            .Where(constructor =>
                constructor.DeclaredAccessibility == Accessibility.Public &&
                argumentIndex >= 0 &&
                argumentIndex < constructor.Parameters.Length)
            .Select(constructor => constructor.Parameters[argumentIndex])
            .Where(parameter =>
                IsAvaloniaPropertyType(parameter.Type) &&
                TryGetInheritedDataTypeFromScopeKind(parameter, out _))
            .ToArray();
        if (inheritedParameters.Length == 1)
        {
            targetMember = inheritedParameters[0];
            return true;
        }

        var resolution = ResolveMarkupExtensionConstructor(
            attribute,
            extensionType,
            extension);
        if (resolution.SelectedMethod is not IMethodSymbol constructor ||
            argumentIndex < 0 ||
            argumentIndex >= constructor.Parameters.Length)
        {
            targetMember = null!;
            return false;
        }

        targetMember = constructor.Parameters[argumentIndex];
        return true;
    }

    private static ITypeSymbol GetMarkupExtensionTargetMemberType(ISymbol targetMember)
    {
        return targetMember switch
        {
            IParameterSymbol parameter => parameter.Type,
            IPropertySymbol property => property.Type,
            _ => null!,
        };
    }

    private bool TryGetControlTemplateTargetType(
        MarkupAttributeSyntax anchor,
        out INamedTypeSymbol targetType)
    {
        if (TryGetMarkupControlThemeTargetType(anchor, out var themeTarget) &&
            themeTarget != null)
        {
            targetType = themeTarget;
            return true;
        }

        for (var current = anchor.Parent; current != null; current = current.Parent)
        {
            if (current is not MarkupElementSyntax element ||
                element.StartTag == null ||
                !IsMarkupTypeOrBase(
                    ResolveMarkupReferenceOwner(element.StartTag.Name.ToString()),
                    "Avalonia.Markup.Xaml.Templates.ControlTemplate"))
            {
                continue;
            }

            for (var parent = GetParentMarkupElement(element);
                 parent != null;
                 parent = GetParentMarkupElement(parent))
            {
                var parentName = parent.StartTag?.Name.ToString();
                if (parentName == null)
                {
                    continue;
                }

                var separator = parentName.LastIndexOf('.');
                if (separator <= 0 ||
                    !parentName[(separator + 1)..].Equals(
                        "Template",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var ownerType = ResolveMarkupReferenceOwner(
                    parentName[..separator]);
                if (ownerType != null)
                {
                    targetType = ownerType;
                    return true;
                }
            }

            var outerElement = GetParentMarkupElement(element);
            if (outerElement != null)
            {
                var styleTarget = GetMarkupStyleTargetContext(outerElement);
                if (!styleTarget.IsUnknown &&
                    !styleTarget.IsAmbiguous &&
                    styleTarget.Types.Length == 1)
                {
                    targetType = styleTarget.Types[0];
                    return true;
                }
            }
        }

        targetType = null!;
        return false;
    }
}

internal readonly struct MarkupInheritedAvaloniaPropertyCompletionCandidate
{
    internal MarkupInheritedAvaloniaPropertyCompletionCandidate(
        string name,
        IFieldSymbol field,
        ITypeSymbol valueType)
    {
        Name = name;
        Field = field;
        ValueType = valueType;
    }

    internal string Name { get; }
    internal IFieldSymbol Field { get; }
    internal ITypeSymbol ValueType { get; }
}
