using Akbura.Language.Binder;
using Akbura.Language.BoundTree;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Runtime.CompilerServices;
using AkburaSyntaxKind = Akbura.Language.Syntax.SyntaxKind;
using AkburaCandidateReason = Akbura.Language.Symbols.CandidateReason;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using CSharpSyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    internal IMarkupComponentSymbol? GetContainingMarkupComponentSymbol(MarkupAttributeSyntax markupAttribute)
    {
        var markupElement = GetContainingMarkupElement(markupAttribute);
        return markupElement == null
            ? null
            : GetSymbolInfo(markupElement).Symbol as IMarkupComponentSymbol;
    }

    internal static MarkupAttributeValueSyntax? GetMarkupAttributeValue(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind switch
        {
            AkburaSyntaxKind.MarkupPlainAttributeSyntax => Unsafe.As<MarkupPlainAttributeSyntax>(markupAttribute).Value,
            AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax => Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute).Value,
            AkburaSyntaxKind.MarkupPrefixedAttributeSyntax => Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Value,
            _ => null,
        };
    }

    internal static MarkupAttributeBindingKind GetMarkupAttributeBindingKind(MarkupAttributeSyntax markupAttribute)
    {
        return markupAttribute.Kind == AkburaSyntaxKind.MarkupPrefixedAttributeSyntax
            ? Unsafe.As<MarkupPrefixedAttributeSyntax>(markupAttribute).Prefix.Kind switch
            {
                Akbura.Language.Syntax.SyntaxKind.BindToken => MarkupAttributeBindingKind.Bind,
                Akbura.Language.Syntax.SyntaxKind.OutToken => MarkupAttributeBindingKind.Out,
                _ => MarkupAttributeBindingKind.None,
            }
            : MarkupAttributeBindingKind.None;
    }

    internal static string GetMarkupLiteralAttributeValueText(MarkupLiteralAttributeValueSyntax literalValue)
    {
        var text = (literalValue.Value?.ToFullString() ?? string.Empty).Trim();
        if (text.Length >= 2 &&
            ((text[0] == '"' && text[^1] == '"') ||
             (text[0] == '\'' && text[^1] == '\'')))
        {
            return text[1..^1];
        }

        return text;
    }

    internal MarkupExtensionBindingResult BindMarkupExtensionAttributeValue(
        MarkupAttributeSyntax markupAttribute,
        MarkupExtensionSyntax extensionSyntax,
        Symbols.IPropertySymbol? property)
    {
        using var diagnosticsBuilder = ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();

        var result = BindMarkupExtensionSyntax(
            markupAttribute,
            extensionSyntax,
            property?.Type.Symbol as ITypeSymbol,
            diagnosticsBuilder);

        if (property?.Type.Symbol is ITypeSymbol targetType &&
            result.Value?.ProvideValueMethod.Symbol is IMethodSymbol provideValueMethod &&
            result.Value.Binding == null &&
            !CanMarkupExtensionResultConvertToTarget(provideValueMethod.ReturnType, targetType))
        {
            AddMarkupAttributeCannotConvertDiagnostic(
                markupAttribute,
                property,
                provideValueMethod.ReturnType,
                targetType,
                diagnosticsBuilder);
        }

        return new MarkupExtensionBindingResult(
            result.Value,
            result.ResultType,
            diagnosticsBuilder.ToImmutable(),
            result.Conversion);
    }

    private MarkupExtensionBindingResult BindMarkupExtensionSyntax(
        MarkupAttributeSyntax markupAttribute,
        MarkupExtensionSyntax extensionSyntax,
        ITypeSymbol? targetType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var rawText = extensionSyntax.ToFullString();
        var extensionName = GetMarkupExtensionTypeName(extensionSyntax.Type);

        if (TryBindAvaloniaBindingExtension(
                markupAttribute,
                extensionSyntax,
                extensionName,
                targetType,
                diagnosticsBuilder,
                out var bindingResult))
        {
            return bindingResult;
        }

        if (!TryResolveMarkupExtensionType(
                extensionName,
                out var extensionType,
                out var ambiguousExtensionTypes))
        {
            var message = ambiguousExtensionTypes.IsDefaultOrEmpty
                ? $"Markup extension type '{extensionName}' was not found."
                : $"Markup extension type '{extensionName}' is ambiguous between: {string.Join(", ", ambiguousExtensionTypes.Select(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))}.";
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                extensionSyntax.Type,
                extensionName,
                message));
            return new MarkupExtensionBindingResult(null, default, diagnosticsBuilder.ToImmutable());
        }

        var provideValueMethod = FindMarkupExtensionProvideValueMethod(extensionType);
        if (provideValueMethod == null)
        {
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                extensionSyntax.Type,
                extensionName,
                $"Markup extension type '{extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' does not contain a public ProvideValue method."));
            return new MarkupExtensionBindingResult(
                null,
                new CSharpSymbolDefinition(extensionType),
                diagnosticsBuilder.ToImmutable());
        }

        var constructorResolution = ResolveMarkupExtensionConstructor(
            markupAttribute,
            extensionType,
            extensionSyntax);
        var selectedConstructor = constructorResolution.SelectedMethod;
        if (selectedConstructor == null)
        {
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                extensionSyntax,
                rawText,
                constructorResolution.CandidateReason == AkburaCandidateReason.Ambiguous
                    ? $"Markup extension constructor for '{extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is ambiguous."
                    : $"Markup extension type '{extensionType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' does not contain a public constructor for the supplied positional arguments."));
        }

        using var argumentsBuilder = ImmutableArrayBuilder<MarkupExtensionArgumentValue>.Rent();
        using var propertiesBuilder = ImmutableArrayBuilder<MarkupExtensionPropertyValue>.Rent();

        var positionalIndex = 0;
        foreach (var argument in extensionSyntax.Arguments)
        {
            switch (argument.Kind)
            {
                case AkburaSyntaxKind.MarkupExtensionPositionalArgumentSyntax:
                    {
                        var positionalArgument = Unsafe.As<MarkupExtensionPositionalArgumentSyntax>(argument);
                        var parameter = selectedConstructor != null && positionalIndex < selectedConstructor.Parameters.Length
                            ? selectedConstructor.Parameters[positionalIndex]
                            : null;
                        var boundValue = BindMarkupExtensionValue(
                            markupAttribute,
                            positionalArgument.Value,
                            parameter?.Type,
                            diagnosticsBuilder);

                        argumentsBuilder.Add(new MarkupExtensionArgumentValue(
                            boundValue.Text,
                            parameter == null ? default : new CSharpSymbolDefinition(parameter),
                            boundValue.Type,
                            boundValue.Operation,
                            boundValue.Conversion,
                            boundValue.ConvertedValue,
                            boundValue.NestedValue));
                        positionalIndex++;
                        break;
                    }

                case AkburaSyntaxKind.MarkupExtensionPropertyArgumentSyntax:
                    {
                        var propertyArgument = Unsafe.As<MarkupExtensionPropertyArgumentSyntax>(argument);
                        var propertyName = propertyArgument.Name.Identifier.ValueText;
                        var extensionProperty = FindMarkupExtensionSettableProperty(
                            extensionType,
                            propertyName,
                            out var inaccessibleProperty);

                        if (extensionProperty == null)
                        {
                            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                                propertyArgument.Name,
                                propertyName,
                                inaccessibleProperty
                                    ? $"Markup extension property '{propertyName}' is inaccessible."
                                    : $"Markup extension property '{propertyName}' was not found."));
                        }

                        var boundValue = BindMarkupExtensionValue(
                            markupAttribute,
                            propertyArgument.Value,
                            extensionProperty?.Type,
                            diagnosticsBuilder);

                        propertiesBuilder.Add(new MarkupExtensionPropertyValue(
                            propertyName,
                            boundValue.Text,
                            extensionProperty == null ? default : new CSharpSymbolDefinition(extensionProperty),
                            boundValue.Type,
                            boundValue.Operation,
                            boundValue.Conversion,
                            boundValue.ConvertedValue,
                            boundValue.NestedValue));
                        break;
                    }
            }
        }

        var resultType = new CSharpSymbolDefinition(provideValueMethod.ReturnType);
        var value = new MarkupExtensionValue(
            rawText,
            extensionName,
            new CSharpSymbolDefinition(extensionType),
            selectedConstructor == null ? default : new CSharpSymbolDefinition(selectedConstructor),
            new CSharpSymbolDefinition(provideValueMethod),
            resultType,
            argumentsBuilder.ToImmutable(),
            propertiesBuilder.ToImmutable());

        if (targetType != null &&
            !CanMarkupExtensionResultConvertToTarget(provideValueMethod.ReturnType, targetType))
        {
            AddMarkupExtensionValueConversionDiagnostic(
                extensionSyntax,
                rawText,
                provideValueMethod.ReturnType,
                targetType,
                diagnosticsBuilder);
        }

        var conversion = targetType == null
            ? default
            : GetBinder(markupAttribute).Conversions.ClassifyConversion(
                provideValueMethod.ReturnType,
                targetType);
        return new MarkupExtensionBindingResult(
            value,
            resultType,
            diagnosticsBuilder.ToImmutable(),
            conversion);
    }

    private bool TryResolveMarkupExtensionType(
        string name,
        out INamedTypeSymbol extensionType,
        out ImmutableArray<INamedTypeSymbol> ambiguousTypes)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in GetMarkupExtensionTypeCandidates(name))
        {
            if (!seen.Add(candidate))
            {
                continue;
            }

            if (TryBindMarkupExtensionTypeCandidate(
                    candidate,
                    out extensionType,
                    out ambiguousTypes))
            {
                return true;
            }

            if (!ambiguousTypes.IsDefaultOrEmpty)
            {
                extensionType = null!;
                return false;
            }
        }

        extensionType = null!;
        ambiguousTypes = ImmutableArray<INamedTypeSymbol>.Empty;
        return false;
    }

    private static IEnumerable<string> GetMarkupExtensionTypeCandidates(string name)
    {
        var normalizedName = NormalizeMarkupExtensionTypeName(name);
        yield return normalizedName;

        if (!HasMarkupExtensionSuffix(normalizedName))
        {
            yield return AddMarkupExtensionSuffix(normalizedName);
        }

        if (IsQualifiedMarkupExtensionName(normalizedName))
        {
            yield break;
        }

        foreach (var namespaceName in GetDefaultMarkupExtensionNamespaces())
        {
            yield return namespaceName + "." + normalizedName;
            if (!HasMarkupExtensionSuffix(normalizedName))
            {
                yield return namespaceName + "." + AddMarkupExtensionSuffix(normalizedName);
            }
        }
    }

    private bool TryBindMarkupExtensionTypeCandidate(
        string candidate,
        out INamedTypeSymbol extensionType,
        out ImmutableArray<INamedTypeSymbol> ambiguousTypes)
    {
        ambiguousTypes = ImmutableArray<INamedTypeSymbol>.Empty;
        try
        {
            var typeSyntax = CSharpSyntaxFactory.ParseTypeName(candidate);
            var binding = BindCSharpType(typeSyntax, GetCSharpUsingDirectives());
            if (binding.TypeSymbol is INamedTypeSymbol { TypeKind: not TypeKind.Error } boundType)
            {
                extensionType = boundType;
                return true;
            }

            if (binding.CandidateReason == AkburaCandidateReason.Ambiguous)
            {
                ambiguousTypes = binding.CandidateSymbols
                    .OfType<INamedTypeSymbol>()
                    .ToImmutableArray();
            }
        }
        catch (ArgumentException)
        {
        }

        var metadataName = StripGlobalAlias(candidate);
        if (metadataName.Contains('.') &&
            Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName) is { } metadataType)
        {
            extensionType = metadataType;
            return true;
        }

        extensionType = null!;
        return false;
    }

    private OverloadResolutionResult ResolveMarkupExtensionConstructor(
        MarkupAttributeSyntax markupAttribute,
        INamedTypeSymbol extensionType,
        MarkupExtensionSyntax extensionSyntax)
    {
        using var candidates = ImmutableArrayBuilder<IMethodSymbol>.Rent();
        var positionalArgumentCount = extensionSyntax.Arguments
            .Count(static argument => argument.Kind == AkburaSyntaxKind.MarkupExtensionPositionalArgumentSyntax);
        foreach (var constructor in extensionType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.Parameters.Length != positionalArgumentCount)
            {
                continue;
            }

            candidates.Add(constructor);
        }

        var candidateArray = candidates.ToImmutable();
        if (candidateArray.Length == 0)
        {
            return OverloadResolutionResult.NotFound(candidateArray);
        }

        if (candidateArray.Length == 1)
        {
            return OverloadResolutionResult.Success(candidateArray[0]);
        }

        using var argumentTypes = ImmutableArrayBuilder<ITypeSymbol?>.Rent(positionalArgumentCount);
        foreach (var argument in extensionSyntax.Arguments)
        {
            if (argument.Kind == AkburaSyntaxKind.MarkupExtensionPositionalArgumentSyntax)
            {
                argumentTypes.Add(GetMarkupExtensionArgumentType(
                    markupAttribute,
                    Unsafe.As<MarkupExtensionPositionalArgumentSyntax>(argument).Value));
            }
        }

        return GetBinder(markupAttribute).OverloadResolution.ResolveMethodGroup(
            candidateArray,
            argumentTypes.ToImmutable());
    }

    private ITypeSymbol? GetMarkupExtensionArgumentType(
        MarkupAttributeSyntax markupAttribute,
        MarkupExtensionValueSyntax valueSyntax)
    {
        switch (valueSyntax.Kind)
        {
            case AkburaSyntaxKind.MarkupExtensionExpressionValueSyntax:
                var expressionValue = Unsafe.As<MarkupExtensionExpressionValueSyntax>(valueSyntax);
                var expression = ParseInlineExpression(expressionValue.Expression);
                return expression == null
                    ? null
                    : BindMarkupAttributeExpression(expressionValue.Expression, expression).TypeSymbol;

            case AkburaSyntaxKind.MarkupExtensionNestedValueSyntax:
                var nestedValue = Unsafe.As<MarkupExtensionNestedValueSyntax>(valueSyntax);
                var nestedName = GetMarkupExtensionTypeName(nestedValue.Extension.Type);
                return TryResolveMarkupExtensionType(nestedName, out var nestedType, out _) &&
                    FindMarkupExtensionProvideValueMethod(nestedType) is { } provideValue
                        ? provideValue.ReturnType
                        : null;

            default:
                var literalValue = Unsafe.As<MarkupExtensionLiteralValueSyntax>(valueSyntax);
                var text = GetMarkupExtensionLiteralText(literalValue);
                var literalExpression = CreateMarkupExtensionLiteralExpression(text, expectedType: null);
                return BindMarkupAttributeExpression(
                    markupAttribute,
                    literalExpression).TypeSymbol;
        }
    }

    private Microsoft.CodeAnalysis.IPropertySymbol? FindMarkupExtensionSettableProperty(
        INamedTypeSymbol extensionType,
        string name,
        out bool inaccessible)
    {
        inaccessible = false;

        for (var current = extensionType; current != null; current = current.BaseType)
        {
            var hasMemberWithName = false;
            foreach (var property in current.GetMembers(name).OfType<Microsoft.CodeAnalysis.IPropertySymbol>())
            {
                hasMemberWithName = true;
                if (!property.IsStatic &&
                    property.DeclaredAccessibility == Accessibility.Public &&
                    property.SetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    return property;
                }
            }

            inaccessible |= hasMemberWithName;
        }

        return null;
    }

    private IMethodSymbol? FindMarkupExtensionProvideValueMethod(INamedTypeSymbol extensionType)
    {
        var serviceProviderType = Compilation.CSharpCompilation.GetTypeByMetadataName("System.IServiceProvider");
        for (var current = extensionType; current != null; current = current.BaseType)
        {
            foreach (var method in current.GetMembers("ProvideValue").OfType<IMethodSymbol>())
            {
                if (IsMarkupExtensionProvideValueMethod(method, serviceProviderType))
                {
                    return method;
                }
            }
        }

        return null;
    }

    private static bool IsMarkupExtensionProvideValueMethod(
        IMethodSymbol method,
        INamedTypeSymbol? serviceProviderType)
    {
        if (method.IsStatic ||
            method.DeclaredAccessibility != Accessibility.Public ||
            method.MethodKind != MethodKind.Ordinary ||
            method.Arity != 0)
        {
            return false;
        }

        if (method.Parameters.Length == 0)
        {
            return true;
        }

        return method.Parameters.Length == 1 &&
            IsMarkupExtensionServiceProviderParameter(method.Parameters[0], serviceProviderType);
    }

    private static bool IsMarkupExtensionServiceProviderParameter(
        IParameterSymbol parameter,
        INamedTypeSymbol? serviceProviderType)
    {
        var parameterType = parameter.Type;
        return (serviceProviderType != null &&
                SymbolEqualityComparer.Default.Equals(parameterType, serviceProviderType)) ||
            parameterType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).TrimEnd('?') ==
            "global::System.IServiceProvider";
    }

    private bool CanMarkupExtensionResultConvertToTarget(
        ITypeSymbol sourceType,
        ITypeSymbol targetType)
    {
        if (sourceType.TypeKind == TypeKind.Error ||
            targetType.TypeKind == TypeKind.Error ||
            sourceType.SpecialType == SpecialType.System_Object ||
            targetType.SpecialType == SpecialType.System_Object)
        {
            return true;
        }

        return Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit ||
            IsAssignableTo(sourceType, targetType);
    }

    private static string NormalizeMarkupExtensionTypeName(string name)
    {
        return name.Trim();
    }

    private static bool HasMarkupExtensionSuffix(string name)
    {
        var genericStart = name.IndexOf('<');
        var stem = genericStart < 0
            ? name
            : name[..genericStart];

        return stem.EndsWith("Extension", StringComparison.Ordinal);
    }

    private static string AddMarkupExtensionSuffix(string name)
    {
        var genericStart = name.IndexOf('<');
        return genericStart < 0
            ? name + "Extension"
            : name.Insert(genericStart, "Extension");
    }

    private static bool IsQualifiedMarkupExtensionName(string name)
    {
        return name.Contains('.') || name.Contains("::", StringComparison.Ordinal);
    }

    private static string StripGlobalAlias(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }

    private static ImmutableArray<string> GetDefaultMarkupExtensionNamespaces()
    {
        return ImmutableArray.Create(
            "Avalonia.Markup.Xaml.MarkupExtensions",
            "Avalonia.Data");
    }

    private bool TryBindAvaloniaBindingExtension(
        MarkupAttributeSyntax markupAttribute,
        MarkupExtensionSyntax extensionSyntax,
        string extensionName,
        ITypeSymbol? targetType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out MarkupExtensionBindingResult result)
    {
        result = default;
        var bindingName = GetUnqualifiedMarkupExtensionName(extensionName);
        if (!IsAvaloniaBindingExtensionName(bindingName))
        {
            return false;
        }

        var hasDataType = BindingSession.MarkupDataTypes.TryGetDataType(
            markupAttribute,
            out var dataType,
            out var dataTypeFailure);
        if (dataTypeFailure != null)
        {
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                markupAttribute,
                extensionSyntax.ToFullString(),
                dataTypeFailure));
        }
        var kind = GetMarkupBindingKind(bindingName, hasDataType);
        var bindingTypeName = kind == MarkupBindingKind.Compiled
            ? "Avalonia.Data.CompiledBinding"
            : bindingName is "ReflectionBinding" or "ReflectionBindingExtension"
                ? "Avalonia.Data.ReflectionBinding"
                : "Avalonia.Data.Binding";
        var bindingType = Compilation.CSharpCompilation.GetTypeByMetadataName(bindingTypeName);
        if (bindingType == null)
        {
            return false;
        }

        var resultType = Compilation.CSharpCompilation.GetTypeByMetadataName("Avalonia.Data.BindingBase") ??
            bindingType;
        var selectedConstructor = SelectMarkupBindingConstructor(bindingType, kind);
        using var argumentsBuilder = ImmutableArrayBuilder<MarkupExtensionArgumentValue>.Rent();
        using var propertiesBuilder = ImmutableArrayBuilder<MarkupExtensionPropertyValue>.Rent();

        string? path = null;
        var positionalIndex = 0;
        foreach (var argument in extensionSyntax.Arguments)
        {
            switch (argument.Kind)
            {
                case AkburaSyntaxKind.MarkupExtensionPositionalArgumentSyntax:
                    {
                        var positionalArgument = Unsafe.As<MarkupExtensionPositionalArgumentSyntax>(argument);
                        var isPathArgument = positionalIndex == 0;
                        var parameter = selectedConstructor != null && positionalIndex < selectedConstructor.Parameters.Length
                            ? selectedConstructor.Parameters[positionalIndex]
                            : null;
                        var boundValue = isPathArgument
                            ? BindMarkupBindingPathValue(positionalArgument.Value)
                            : BindMarkupExtensionValue(
                                markupAttribute,
                                positionalArgument.Value,
                                parameter?.Type,
                                diagnosticsBuilder);

                        if (isPathArgument)
                        {
                            path = boundValue.Text;
                        }

                        argumentsBuilder.Add(new MarkupExtensionArgumentValue(
                            boundValue.Text,
                            parameter == null ? default : new CSharpSymbolDefinition(parameter),
                            boundValue.Type,
                            boundValue.Operation,
                            boundValue.Conversion,
                            boundValue.ConvertedValue,
                            boundValue.NestedValue));
                        positionalIndex++;
                        break;
                    }

                case AkburaSyntaxKind.MarkupExtensionPropertyArgumentSyntax:
                    {
                        var propertyArgument = Unsafe.As<MarkupExtensionPropertyArgumentSyntax>(argument);
                        var propertyName = propertyArgument.Name.Identifier.ValueText;
                        var extensionProperty = FindMarkupExtensionSettableProperty(
                            bindingType,
                            propertyName,
                            out var inaccessibleProperty);

                        if (extensionProperty == null)
                        {
                            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                                propertyArgument.Name,
                                propertyName,
                                inaccessibleProperty
                                    ? $"Binding property '{propertyName}' is inaccessible."
                                    : $"Binding property '{propertyName}' was not found."));
                        }

                        var isPathProperty = string.Equals(propertyName, "Path", StringComparison.Ordinal);
                        var boundValue = isPathProperty
                            ? BindMarkupBindingPathValue(propertyArgument.Value)
                            : BindMarkupExtensionValue(
                                markupAttribute,
                                propertyArgument.Value,
                                extensionProperty?.Type,
                                diagnosticsBuilder);

                        if (isPathProperty)
                        {
                            path = boundValue.Text;
                        }

                        propertiesBuilder.Add(new MarkupExtensionPropertyValue(
                            propertyName,
                            boundValue.Text,
                            extensionProperty == null ? default : new CSharpSymbolDefinition(extensionProperty),
                            boundValue.Type,
                            boundValue.Operation,
                            boundValue.Conversion,
                            boundValue.ConvertedValue,
                            boundValue.NestedValue));
                        break;
                    }
            }
        }

        path ??= string.Empty;
        var pathElements = BindMarkupBindingPath(
            markupAttribute,
            path,
            kind,
            hasDataType ? dataType : null,
            diagnosticsBuilder,
            out var bindingResultType);
        var bindingValue = new MarkupBindingValue(
            kind,
            path,
            new CSharpSymbolDefinition(bindingType),
            hasDataType ? new CSharpSymbolDefinition(dataType) : default,
            bindingResultType == null ? default : new CSharpSymbolDefinition(bindingResultType),
            pathElements);
        var value = new MarkupExtensionValue(
            extensionSyntax.ToFullString(),
            extensionName,
            new CSharpSymbolDefinition(bindingType),
            selectedConstructor == null ? default : new CSharpSymbolDefinition(selectedConstructor),
            provideValueMethod: default,
            new CSharpSymbolDefinition(resultType),
            argumentsBuilder.ToImmutable(),
            propertiesBuilder.ToImmutable(),
            bindingValue);

        result = new MarkupExtensionBindingResult(
            value,
            new CSharpSymbolDefinition(resultType),
            diagnosticsBuilder.ToImmutable());
        return true;
    }

    private static bool IsAvaloniaBindingExtensionName(string name)
    {
        return name is
            "Binding" or
            "BindingExtension" or
            "ReflectionBinding" or
            "ReflectionBindingExtension" or
            "CompiledBinding" or
            "CompiledBindingExtension";
    }

    private static MarkupBindingKind GetMarkupBindingKind(string name, bool hasDataType)
    {
        if (name is "CompiledBinding" or "CompiledBindingExtension")
        {
            return MarkupBindingKind.Compiled;
        }

        if (name is "ReflectionBinding" or "ReflectionBindingExtension")
        {
            return MarkupBindingKind.Reflection;
        }

        return hasDataType
            ? MarkupBindingKind.Compiled
            : MarkupBindingKind.Reflection;
    }

    private static string GetUnqualifiedMarkupExtensionName(string name)
    {
        var normalizedName = StripGlobalAlias(NormalizeMarkupExtensionTypeName(name));
        var genericStart = normalizedName.IndexOf('<');
        if (genericStart >= 0)
        {
            normalizedName = normalizedName[..genericStart];
        }

        var lastDot = normalizedName.LastIndexOf('.');
        return lastDot < 0
            ? normalizedName
            : normalizedName[(lastDot + 1)..];
    }

    private static IMethodSymbol? SelectMarkupBindingConstructor(
        INamedTypeSymbol bindingType,
        MarkupBindingKind kind)
    {
        var expectedParameterTypeName = kind == MarkupBindingKind.Compiled
            ? "Avalonia.Data.CompiledBindingPath"
            : "System.String";

        foreach (var constructor in bindingType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility != Accessibility.Public ||
                constructor.Parameters.Length != 1)
            {
                continue;
            }

            if (IsTypeNamed(constructor.Parameters[0].Type, expectedParameterTypeName))
            {
                return constructor;
            }
        }

        foreach (var constructor in bindingType.InstanceConstructors)
        {
            if (constructor.DeclaredAccessibility == Accessibility.Public &&
                constructor.Parameters.Length == 0)
            {
                return constructor;
            }
        }

        return null;
    }

    private static bool IsTypeNamed(ITypeSymbol type, string metadataName)
    {
        var fullyQualifiedName = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (fullyQualifiedName.StartsWith("global::", StringComparison.Ordinal))
        {
            fullyQualifiedName = fullyQualifiedName["global::".Length..];
        }

        return fullyQualifiedName == metadataName ||
            type.ToDisplayString() == metadataName;
    }

    private MarkupExtensionBoundValue BindMarkupBindingPathValue(MarkupExtensionValueSyntax valueSyntax)
    {
        if (valueSyntax.Kind == AkburaSyntaxKind.MarkupExtensionNestedValueSyntax)
        {
            var nestedValue = Unsafe.As<MarkupExtensionNestedValueSyntax>(valueSyntax);
            return new MarkupExtensionBoundValue(
                nestedValue.Extension.ToFullString(),
                default,
                default,
                default,
                nestedValue.Extension.ToFullString(),
                null);
        }

        if (valueSyntax.Kind == AkburaSyntaxKind.MarkupExtensionExpressionValueSyntax)
        {
            var expressionValue = Unsafe.As<MarkupExtensionExpressionValueSyntax>(valueSyntax);
            return new MarkupExtensionBoundValue(
                expressionValue.Expression.Expression.ToFullString().Trim(),
                default,
                default,
                default,
                expressionValue.Expression.Expression.ToFullString().Trim(),
                null);
        }

        return new MarkupExtensionBoundValue(
            GetMarkupExtensionLiteralText(Unsafe.As<MarkupExtensionLiteralValueSyntax>(valueSyntax)),
            default,
            default,
            default,
            GetMarkupExtensionLiteralText(Unsafe.As<MarkupExtensionLiteralValueSyntax>(valueSyntax)),
            null);
    }

    private ImmutableArray<MarkupBindingPathElement> BindMarkupBindingPath(
        MarkupAttributeSyntax markupAttribute,
        string path,
        MarkupBindingKind kind,
        INamedTypeSymbol? dataType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out ITypeSymbol? resultType)
    {
        using var builder = ImmutableArrayBuilder<MarkupBindingPathElement>.Rent();
        resultType = dataType;
        var currentType = dataType as ITypeSymbol;
        var index = 0;
        var trimmedPath = path.Trim();

        if (!TryValidateMarkupBindingPath(trimmedPath, out var pathError))
        {
            builder.Add(new MarkupBindingPathElement(
                MarkupBindingPathElementKind.Unknown,
                trimmedPath));
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                markupAttribute,
                path,
                pathError));
            resultType = null;
            return builder.ToImmutable();
        }

        while (index < trimmedPath.Length && trimmedPath[index] == '!')
        {
            builder.Add(new MarkupBindingPathElement(MarkupBindingPathElementKind.Not, "!"));
            index++;
        }

        if (TryReadBindingPathRoot(
                markupAttribute,
                trimmedPath,
                ref index,
                builder,
                out var rootType,
                out var isRooted))
        {
            currentType = rootType;
            resultType = rootType;
        }

        while (index < trimmedPath.Length)
        {
            if (trimmedPath[index] == '.')
            {
                index++;
                continue;
            }

            if (trimmedPath[index] == '[')
            {
                var indexerText = ReadBracketedBindingText(trimmedPath, ref index);
                var indexerArguments = GetBindingPathArguments(indexerText, '[', ']');
                BindMarkupBindingIndexer(
                    markupAttribute,
                    currentType,
                    indexerText,
                    indexerArguments,
                    diagnosticsBuilder,
                    out var indexer,
                    out var indexerResultType,
                    out var boundIndexerArguments);
                builder.Add(new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.Indexer,
                    indexerText,
                    indexer == null ? default : new CSharpSymbolDefinition(indexer),
                    indexerResultType == null ? default : new CSharpSymbolDefinition(indexerResultType),
                    indexerArguments,
                    boundIndexerArguments));
                currentType = indexerResultType;
                resultType = currentType ?? resultType;
                continue;
            }

            if (trimmedPath[index] == '(')
            {
                var typeCastText = ReadParenthesizedBindingText(trimmedPath, ref index);
                if (TryBindMarkupDataType(typeCastText.Trim('(', ')'), out var castType))
                {
                    currentType = castType;
                    resultType = castType;
                }

                builder.Add(new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.TypeCast,
                    typeCastText,
                    type: currentType == null ? default : new CSharpSymbolDefinition(currentType)));

                continue;
            }

            var memberName = ReadBindingMemberName(trimmedPath, ref index);
            if (memberName.Length == 0)
            {
                index++;
                continue;
            }

            if (TryBindMarkupBindingPathMember(
                    currentType,
                    memberName,
                    out var member,
                    out var memberType,
                    out var elementKind,
                    out var inaccessibleMember))
            {
                currentType = memberType;
                resultType = memberType;
                builder.Add(new MarkupBindingPathElement(
                    elementKind,
                    memberName,
                    new CSharpSymbolDefinition(member),
                    memberType == null ? default : new CSharpSymbolDefinition(memberType)));
                continue;
            }

            builder.Add(new MarkupBindingPathElement(MarkupBindingPathElementKind.Unknown, memberName));
            if (kind == MarkupBindingKind.Compiled &&
                currentType != null &&
                !isRooted)
            {
                diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                    markupAttribute,
                    memberName,
                    inaccessibleMember
                        ? $"Compiled binding path member '{memberName}' on '{currentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is inaccessible."
                        : $"Compiled binding path member '{memberName}' was not found on '{currentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'."));
            }

            currentType = null;
            resultType = null;
        }

        return builder.ToImmutable();
    }

    private bool TryReadBindingPathRoot(
        MarkupAttributeSyntax markupAttribute,
        string path,
        ref int index,
        ImmutableArrayBuilder<MarkupBindingPathElement> builder,
        out ITypeSymbol? rootType,
        out bool isRooted)
    {
        rootType = null;
        isRooted = false;

        if (index >= path.Length)
        {
            return false;
        }

        if (path[index] == '#')
        {
            var start = index;
            index++;
            var nameStart = index;
            while (index < path.Length && IsBindingIdentifierPart(path[index]))
            {
                index++;
            }

            var text = path[start..index];
            builder.Add(new MarkupBindingPathElement(MarkupBindingPathElementKind.ElementName, text));
            isRooted = true;
            return true;
        }

        if (!path[index..].StartsWith("$", StringComparison.Ordinal))
        {
            return false;
        }

        if (path[index..].StartsWith("$self", StringComparison.Ordinal))
        {
            index += "$self".Length;
            rootType = GetContainingMarkupComponentSymbol(markupAttribute)?.ComponentType;
            builder.Add(new MarkupBindingPathElement(
                MarkupBindingPathElementKind.Self,
                "$self",
                type: rootType == null ? default : new CSharpSymbolDefinition(rootType)));
            isRooted = true;
            return true;
        }

        if (path[index..].StartsWith("$templatedParent", StringComparison.Ordinal))
        {
            index += "$templatedParent".Length;
            builder.Add(new MarkupBindingPathElement(MarkupBindingPathElementKind.TemplatedParent, "$templatedParent"));
            isRooted = true;
            return true;
        }

        if (path[index..].StartsWith("$parent", StringComparison.Ordinal))
        {
            var start = index;
            index += "$parent".Length;
            if (index < path.Length && path[index] == '[')
            {
                var bracketText = ReadBracketedBindingText(path, ref index);
                var arguments = GetBindingPathArguments(bracketText, '[', ']');
                var ancestorTypeText = arguments.IsEmpty ? string.Empty : arguments[0];
                var level = arguments.Length > 1 && int.TryParse(arguments[1], out var parsedLevel)
                    ? parsedLevel
                    : (int?)null;
                if (ancestorTypeText.Length > 0 &&
                    TryBindMarkupDataType(ancestorTypeText, out var ancestorType))
                {
                    rootType = ancestorType;
                }

                builder.Add(new MarkupBindingPathElement(
                    MarkupBindingPathElementKind.Ancestor,
                    path[start..index],
                    type: rootType == null ? default : new CSharpSymbolDefinition(rootType),
                    arguments: arguments,
                    level: level));
            }
            else
            {
                builder.Add(new MarkupBindingPathElement(MarkupBindingPathElementKind.Ancestor, "$parent"));
            }

            isRooted = true;
            return true;
        }

        return false;
    }

    private static bool TryBindMarkupBindingPathMember(
        ITypeSymbol? currentType,
        string memberName,
        out Microsoft.CodeAnalysis.ISymbol member,
        out ITypeSymbol? memberType,
        out MarkupBindingPathElementKind elementKind,
        out bool inaccessible)
    {
        member = null!;
        memberType = null;
        elementKind = MarkupBindingPathElementKind.Unknown;
        inaccessible = false;

        if (currentType == null ||
            currentType.TypeKind == TypeKind.Error)
        {
            return false;
        }

        foreach (var type in EnumerateBindingMemberTypes(currentType))
        {
            foreach (var property in type.GetMembers(memberName).OfType<Microsoft.CodeAnalysis.IPropertySymbol>())
            {
                if (property.DeclaredAccessibility == Accessibility.Public &&
                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public)
                {
                    member = property;
                    memberType = property.Type;
                    elementKind = MarkupBindingPathElementKind.Property;
                    return true;
                }

                inaccessible = true;
            }

            foreach (var field in type.GetMembers(memberName).OfType<IFieldSymbol>())
            {
                if (field.DeclaredAccessibility == Accessibility.Public)
                {
                    member = field;
                    memberType = field.Type;
                    elementKind = MarkupBindingPathElementKind.Field;
                    return true;
                }

                inaccessible = true;
            }
        }

        return false;
    }

    private void BindMarkupBindingIndexer(
        MarkupAttributeSyntax markupAttribute,
        ITypeSymbol? currentType,
        string indexerText,
        ImmutableArray<string> argumentTexts,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder,
        out Microsoft.CodeAnalysis.IPropertySymbol? indexer,
        out ITypeSymbol? resultType,
        out ImmutableArray<MarkupBindingPathArgument> boundArguments)
    {
        indexer = null;
        resultType = null;
        boundArguments = ImmutableArray<MarkupBindingPathArgument>.Empty;
        if (currentType is IArrayTypeSymbol arrayType)
        {
            resultType = arrayType.ElementType;
            var indexType = Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_Int32);
            if (argumentTexts.Length != arrayType.Rank)
            {
                diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                    markupAttribute,
                    indexerText,
                    $"Array binding indexer requires {arrayType.Rank} argument(s), but {argumentTexts.Length} were supplied."));
                return;
            }

            boundArguments = BindMarkupBindingPathArguments(
                markupAttribute,
                argumentTexts,
                parameterSource: null,
                indexType,
                diagnosticsBuilder);
            return;
        }

        if (currentType == null)
        {
            return;
        }

        using var candidates = ImmutableArrayBuilder<Microsoft.CodeAnalysis.IPropertySymbol>.Rent();
        foreach (var type in EnumerateBindingMemberTypes(currentType))
        {
            foreach (var property in type.GetMembers().OfType<Microsoft.CodeAnalysis.IPropertySymbol>())
            {
                if (property.IsIndexer &&
                    property.DeclaredAccessibility == Accessibility.Public &&
                    property.GetMethod?.DeclaredAccessibility == Accessibility.Public &&
                    property.Parameters.Length == argumentTexts.Length &&
                    property.Parameters.All(static parameter => parameter.RefKind == RefKind.None))
                {
                    candidates.Add(property);
                }
            }
        }

        indexer = SelectMarkupBindingIndexer(markupAttribute, candidates.ToImmutable(), argumentTexts, out var ambiguous);
        if (indexer == null)
        {
            diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                markupAttribute,
                indexerText,
                ambiguous
                    ? $"Binding indexer on '{currentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' is ambiguous."
                    : $"A public binding indexer accepting {argumentTexts.Length} argument(s) was not found on '{currentType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'."));
            return;
        }

        resultType = indexer.Type;
        boundArguments = BindMarkupBindingPathArguments(
            markupAttribute,
            argumentTexts,
            indexer.Parameters,
            fallbackType: null,
            diagnosticsBuilder);
    }

    private Microsoft.CodeAnalysis.IPropertySymbol? SelectMarkupBindingIndexer(
        MarkupAttributeSyntax markupAttribute,
        ImmutableArray<Microsoft.CodeAnalysis.IPropertySymbol> candidates,
        ImmutableArray<string> argumentTexts,
        out bool ambiguous)
    {
        ambiguous = false;
        if (candidates.Length <= 1)
        {
            return candidates.IsEmpty ? null : candidates[0];
        }

        using var argumentTypes = ImmutableArrayBuilder<ITypeSymbol?>.Rent(argumentTexts.Length);
        foreach (var text in argumentTexts)
        {
            var expression = CreateMarkupExtensionLiteralExpression(text, expectedType: null);
            argumentTypes.Add(BindMarkupAttributeExpression(markupAttribute, expression).TypeSymbol);
        }

        var bestScore = int.MaxValue;
        Microsoft.CodeAnalysis.IPropertySymbol? best = null;
        var inferredArgumentTypes = argumentTypes.WrittenSpan;
        foreach (var candidate in candidates)
        {
            var score = 0;
            var applicable = true;
            for (var index = 0; index < inferredArgumentTypes.Length; index++)
            {
                var conversion = GetBinder(markupAttribute).Conversions.ClassifyConversion(
                    inferredArgumentTypes[index],
                    candidate.Parameters[index].Type);
                if (!conversion.IsImplicit)
                {
                    applicable = false;
                    break;
                }

                score += conversion.Kind == AkburaConversionKind.Identity ? 0 : 1;
            }

            if (!applicable || score > bestScore)
            {
                continue;
            }

            if (score == bestScore)
            {
                ambiguous = true;
                continue;
            }

            bestScore = score;
            best = candidate;
            ambiguous = false;
        }

        return ambiguous ? null : best;
    }

    private ImmutableArray<MarkupBindingPathArgument> BindMarkupBindingPathArguments(
        MarkupAttributeSyntax markupAttribute,
        ImmutableArray<string> argumentTexts,
        ImmutableArray<IParameterSymbol>? parameterSource,
        ITypeSymbol? fallbackType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        using var builder = ImmutableArrayBuilder<MarkupBindingPathArgument>.Rent(argumentTexts.Length);
        for (var index = 0; index < argumentTexts.Length; index++)
        {
            var parameter = parameterSource.HasValue ? parameterSource.Value[index] : null;
            var expectedType = parameter?.Type ?? fallbackType;
            var text = argumentTexts[index];
            var expression = CreateMarkupExtensionLiteralExpression(text, expectedType);
            var binding = BindMarkupAttributeExpression(markupAttribute, expression, expectedType);
            AddMarkupExpressionDiagnostics(
                markupAttribute,
                text,
                binding,
                diagnosticsBuilder);
            AddMarkupExtensionValueConversionDiagnostics(
                markupAttribute,
                text,
                binding,
                expectedType,
                diagnosticsBuilder);

            builder.Add(new MarkupBindingPathArgument(
                text,
                parameter == null ? default : new CSharpSymbolDefinition(parameter),
                expectedType == null ? default : new CSharpSymbolDefinition(expectedType),
                binding.OperationDefinition,
                binding.Conversion,
                binding.OperationDefinition.ConstantValue.HasValue
                    ? binding.OperationDefinition.ConstantValue.Value
                    : null));
        }

        return builder.ToImmutable();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateBindingMemberTypes(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            yield break;
        }

        for (var current = namedType; current != null; current = current.BaseType)
        {
            yield return current;
        }

        foreach (var @interface in namedType.AllInterfaces)
        {
            yield return @interface;
        }
    }

    private static ImmutableArray<string> GetBindingPathArguments(
        string text,
        char open,
        char close)
    {
        if (text.Length < 2 || text[0] != open || text[^1] != close)
        {
            return ImmutableArray<string>.Empty;
        }

        using var builder = ImmutableArrayBuilder<string>.Rent();
        var start = 1;
        var depth = 0;
        var quote = '\0';
        for (var index = 1; index < text.Length - 1; index++)
        {
            var character = text[index];
            if (quote != '\0')
            {
                if (character == quote && text[index - 1] != '\\')
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            if (character is '[' or '(')
            {
                depth++;
                continue;
            }

            if (character is ']' or ')')
            {
                depth--;
                continue;
            }

            if (character != ',' || depth != 0)
            {
                continue;
            }

            builder.Add(text[start..index].Trim());
            start = index + 1;
        }

        var finalArgument = text[start..^1].Trim();
        if (finalArgument.Length > 0 || builder.Count > 0)
        {
            builder.Add(finalArgument);
        }

        return builder.ToImmutable();
    }

    private static bool TryValidateMarkupBindingPath(
        string path,
        out string error)
    {
        error = string.Empty;
        var bracketDepth = 0;
        var parenthesisDepth = 0;
        var quote = '\0';
        for (var index = 0; index < path.Length; index++)
        {
            var character = path[index];
            if (quote != '\0')
            {
                if (character == quote && (index == 0 || path[index - 1] != '\\'))
                {
                    quote = '\0';
                }

                continue;
            }

            if (character is '\'' or '"')
            {
                quote = character;
                continue;
            }

            switch (character)
            {
                case '[':
                    bracketDepth++;
                    break;
                case ']':
                    if (--bracketDepth < 0)
                    {
                        error = "Binding path contains an unmatched ']'.";
                        return false;
                    }

                    break;
                case '(':
                    parenthesisDepth++;
                    break;
                case ')':
                    if (--parenthesisDepth < 0)
                    {
                        error = "Binding path contains an unmatched ')'.";
                        return false;
                    }

                    break;
            }
        }

        if (quote != '\0')
        {
            error = "Binding path contains an unterminated quoted value.";
            return false;
        }

        if (bracketDepth != 0 || parenthesisDepth != 0)
        {
            error = "Binding path contains an unterminated indexer or type cast.";
            return false;
        }

        if (path.EndsWith(".", StringComparison.Ordinal) ||
            path.Contains("..", StringComparison.Ordinal))
        {
            error = "Binding path contains an empty member segment.";
            return false;
        }

        if (path.StartsWith("$", StringComparison.Ordinal) &&
            !path.StartsWith("$self", StringComparison.Ordinal) &&
            !path.StartsWith("$parent", StringComparison.Ordinal) &&
            !path.StartsWith("$templatedParent", StringComparison.Ordinal))
        {
            error = "Binding path contains an unknown rooted source.";
            return false;
        }

        return true;
    }

    private static string ReadBindingMemberName(string text, ref int index)
    {
        var start = index;
        while (index < text.Length &&
               (IsBindingIdentifierPart(text[index]) || text[index] == ':'))
        {
            index++;
        }

        return text[start..index];
    }

    private static string ReadBracketedBindingText(string text, ref int index)
    {
        return ReadDelimitedBindingText(text, ref index, '[', ']');
    }

    private static string ReadParenthesizedBindingText(string text, ref int index)
    {
        return ReadDelimitedBindingText(text, ref index, '(', ')');
    }

    private static string ReadDelimitedBindingText(
        string text,
        ref int index,
        char open,
        char close)
    {
        var start = index;
        var depth = 0;
        while (index < text.Length)
        {
            if (text[index] == open)
            {
                depth++;
            }
            else if (text[index] == close)
            {
                depth--;
                index++;
                if (depth == 0)
                {
                    break;
                }

                continue;
            }

            index++;
        }

        return text[start..Math.Min(index, text.Length)];
    }

    private static bool IsBindingIdentifierPart(char ch)
    {
        return char.IsLetterOrDigit(ch) ||
            ch == '_' ||
            ch == '-';
    }

    internal static bool IsMarkupDataTypeDirective(MarkupAttributeSyntax markupAttribute)
    {
        return IsMarkupDirective(markupAttribute, "DataType");
    }

    internal static bool IsMarkupItemNameDirective(MarkupAttributeSyntax markupAttribute)
    {
        return IsMarkupDirective(markupAttribute, "ItemName");
    }

    private static bool IsMarkupDirective(
        MarkupAttributeSyntax markupAttribute,
        string name)
    {
        if (markupAttribute.Kind != AkburaSyntaxKind.MarkupAttachedPropertyAttributeSyntax)
        {
            return false;
        }

        var attachedProperty = Unsafe.As<MarkupAttachedPropertyAttributeSyntax>(markupAttribute);
        return attachedProperty.OwnerType.ToFullString().Trim() == "x" &&
            attachedProperty.Name.Identifier.ValueText == name;
    }

    internal static bool TryGetMarkupDataTypeText(
        MarkupAttributeSyntax attribute,
        out string typeText)
    {
        typeText = string.Empty;
        var value = GetMarkupAttributeValue(attribute);
        if (value == null)
        {
            return false;
        }

        switch (value.Kind)
        {
            case AkburaSyntaxKind.MarkupLiteralAttributeValueSyntax:
                typeText = GetMarkupLiteralAttributeValueText(Unsafe.As<MarkupLiteralAttributeValueSyntax>(value));
                return typeText.Length > 0;

            case AkburaSyntaxKind.MarkupDynamicAttributeValueSyntax:
                typeText = Unsafe.As<MarkupDynamicAttributeValueSyntax>(value).Expression.ToFullString().Trim();
                return typeText.Length > 0;

            case AkburaSyntaxKind.MarkupExtensionAttributeValueSyntax:
                typeText = Unsafe.As<MarkupExtensionAttributeValueSyntax>(value).Extension.ToFullString().Trim();
                return typeText.Length > 0;

            default:
                return false;
        }
    }

    private bool TryBindMarkupDataType(
        string typeText,
        out INamedTypeSymbol dataType)
    {
        dataType = null!;
        if (string.IsNullOrWhiteSpace(typeText))
        {
            return false;
        }

        try
        {
            var binding = BindCSharpType(
                CSharpSyntaxFactory.ParseTypeName(typeText.Trim()),
                GetCSharpUsingDirectives());
            if (binding.TypeSymbol is INamedTypeSymbol { TypeKind: not TypeKind.Error } namedType)
            {
                dataType = namedType;
                return true;
            }
        }
        catch (ArgumentException)
        {
        }

        var metadataName = StripGlobalAlias(typeText.Trim());
        if (Compilation.CSharpCompilation.GetTypeByMetadataName(metadataName) is { } metadataType)
        {
            dataType = metadataType;
            return true;
        }

        return false;
    }

    internal bool TryBindMarkupDataTypeDirective(
        string typeText,
        out INamedTypeSymbol dataType)
    {
        return TryBindMarkupDataType(typeText, out dataType);
    }

    private MarkupExtensionBoundValue BindMarkupExtensionValue(
        MarkupAttributeSyntax markupAttribute,
        MarkupExtensionValueSyntax valueSyntax,
        ITypeSymbol? expectedType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        switch (valueSyntax.Kind)
        {
            case AkburaSyntaxKind.MarkupExtensionExpressionValueSyntax:
                {
                    var expressionValue = Unsafe.As<MarkupExtensionExpressionValueSyntax>(valueSyntax);
                    var expression = ParseInlineExpression(expressionValue.Expression);
                    if (expression == null)
                    {
                        diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
                            expressionValue,
                            expressionValue.ToFullString(),
                            "Markup extension C# expression is malformed."));
                        return new MarkupExtensionBoundValue(
                            expressionValue.ToFullString(),
                            default,
                            default,
                            default,
                            null,
                            null);
                    }

                    var binding = BindMarkupAttributeExpression(expressionValue.Expression, expression, expectedType);
                    AddMarkupExpressionDiagnostics(
                        expressionValue.Expression,
                        expression.ToString(),
                        binding,
                        diagnosticsBuilder);
                    AddMarkupExtensionValueConversionDiagnostics(
                        expressionValue,
                        expression.ToString(),
                        binding,
                        expectedType,
                        diagnosticsBuilder);

                    return new MarkupExtensionBoundValue(
                        expressionValue.Expression.Expression.ToFullString().Trim(),
                        binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
                        binding.OperationDefinition,
                        binding.Conversion,
                        binding.OperationDefinition.ConstantValue.HasValue
                            ? binding.OperationDefinition.ConstantValue.Value
                            : null,
                        null);
                }

            case AkburaSyntaxKind.MarkupExtensionNestedValueSyntax:
                {
                    var nestedValue = Unsafe.As<MarkupExtensionNestedValueSyntax>(valueSyntax);
                    var nestedResult = BindMarkupExtensionSyntax(
                        markupAttribute,
                        nestedValue.Extension,
                        expectedType,
                        diagnosticsBuilder);

                    return new MarkupExtensionBoundValue(
                        nestedValue.Extension.ToFullString(),
                        nestedResult.ResultType,
                        default,
                        nestedResult.Conversion,
                        nestedResult.Value,
                        nestedResult.Value);
                }

            default:
                {
                    var literalValue = Unsafe.As<MarkupExtensionLiteralValueSyntax>(valueSyntax);
                    var text = GetMarkupExtensionLiteralText(literalValue);
                    var expression = CreateMarkupExtensionLiteralExpression(text, expectedType);
                    var binding = BindMarkupAttributeExpression(literalValue, expression, expectedType);
                    AddMarkupExtensionValueConversionDiagnostics(
                        literalValue,
                        text,
                        binding,
                        expectedType,
                        diagnosticsBuilder);

                    return new MarkupExtensionBoundValue(
                        text,
                        binding.TypeSymbol == null ? default : new CSharpSymbolDefinition(binding.TypeSymbol),
                        binding.OperationDefinition,
                        binding.Conversion,
                        binding.OperationDefinition.ConstantValue.HasValue
                            ? binding.OperationDefinition.ConstantValue.Value
                            : text,
                        null);
                }
        }
    }

    private static string GetMarkupExtensionLiteralText(MarkupExtensionLiteralValueSyntax literalValue)
    {
        return literalValue.Value.ToFullString().Trim();
    }

    private static CSharp.ExpressionSyntax CreateMarkupExtensionLiteralExpression(
        string text,
        ITypeSymbol? expectedType)
    {
        if (expectedType?.SpecialType == SpecialType.System_String)
        {
            return CreateStringLiteralExpression(text);
        }

        if (expectedType != null &&
            IsSimpleMarkupExtensionIdentifier(text))
        {
            foreach (var field in expectedType.GetMembers(text).OfType<IFieldSymbol>())
            {
                if (field.IsStatic &&
                    field.HasConstantValue)
                {
                    return CSharpSyntaxFactory.ParseExpression(
                        expectedType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + text);
                }
            }
        }

        if (LooksLikeCSharpLiteral(text))
        {
            try
            {
                var expression = CSharpSyntaxFactory.ParseExpression(text);
                if (!expression.GetDiagnostics().Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
                {
                    return expression;
                }
            }
            catch (ArgumentException)
            {
            }
        }

        return CreateStringLiteralExpression(text);
    }

    private static bool IsSimpleMarkupExtensionIdentifier(string text)
    {
        if (text.Length == 0 ||
            !Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(text[0]))
        {
            return false;
        }

        for (var index = 1; index < text.Length; index++)
        {
            if (!Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(text[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static CSharp.ExpressionSyntax CreateStringLiteralExpression(string text)
    {
        return CSharpSyntaxFactory.LiteralExpression(
            Microsoft.CodeAnalysis.CSharp.SyntaxKind.StringLiteralExpression,
            CSharpSyntaxFactory.Literal(text));
    }

    private static bool LooksLikeCSharpLiteral(string text)
    {
        if (text.Length == 0)
        {
            return false;
        }

        return text is "true" or "false" or "null" ||
               text[0] == '"' ||
               text[0] == '\'' ||
               char.IsDigit(text[0]) ||
               (text[0] is '+' or '-' && text.Length > 1 && char.IsDigit(text[1]));
    }

    private void AddMarkupExtensionValueConversionDiagnostics(
        AkburaSyntax syntax,
        string valueText,
        CSharpBindingResult binding,
        ITypeSymbol? expectedType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (expectedType == null)
        {
            return;
        }

        var conversion = binding.Conversion;
        if (conversion.TargetType != null)
        {
            if (conversion.SourceType == null ||
                IsSameType(conversion.SourceType, expectedType) ||
                conversion.IsImplicit)
            {
                return;
            }

            AddMarkupExtensionValueConversionDiagnostic(
                syntax,
                valueText,
                conversion.SourceType,
                expectedType,
                diagnosticsBuilder);
            return;
        }

        if (binding.TypeSymbol == null ||
            IsSameType(binding.TypeSymbol, expectedType) ||
            Compilation.CSharpCompilation.ClassifyConversion(binding.TypeSymbol, expectedType).IsImplicit)
        {
            return;
        }

        AddMarkupExtensionValueConversionDiagnostic(
            syntax,
            valueText,
            binding.TypeSymbol,
            expectedType,
            diagnosticsBuilder);
    }

    private static void AddMarkupExtensionValueConversionDiagnostic(
        AkburaSyntax syntax,
        string valueText,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        diagnosticsBuilder.Add(CreateMarkupExtensionErrorDiagnostic(
            syntax,
            valueText,
            $"Cannot convert value from '{sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}' to '{targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}'."));
    }

    private static string GetMarkupExtensionTypeName(MarkupExtensionTypeSyntax typeSyntax)
    {
        var builder = new StringBuilder();
        if (typeSyntax.AliasQualifier != null)
        {
            builder.Append(typeSyntax.AliasQualifier.Alias.Identifier.ValueText);
            builder.Append("::");
        }

        for (var index = 0; index < typeSyntax.Name.Segments.Count; index++)
        {
            if (index > 0)
            {
                builder.Append('.');
            }

            AppendMarkupExtensionNameSegment(builder, typeSyntax.Name.Segments[index]);
        }

        return builder.ToString();
    }

    private static void AppendMarkupExtensionNameSegment(
        StringBuilder builder,
        MarkupNameSegmentSyntax segment)
    {
        switch (segment.Kind)
        {
            case AkburaSyntaxKind.MarkupIdentifierNameSegmentSyntax:
                builder.Append(Unsafe.As<MarkupIdentifierNameSegmentSyntax>(segment).Name.Identifier.ValueText);
                break;

            case AkburaSyntaxKind.MarkupGenericNameSegmentSyntax:
                var genericSegment = Unsafe.As<MarkupGenericNameSegmentSyntax>(segment);
                builder.Append(genericSegment.Name.Identifier.ValueText);
                builder.Append('<');
                for (var index = 0; index < genericSegment.GenericArgs.Arguments.Count; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(", ");
                    }

                    builder.Append(genericSegment.GenericArgs.Arguments[index].ToFullString().Trim());
                }

                builder.Append('>');
                break;
        }
    }

    private readonly struct MarkupExtensionBoundValue
    {
        public MarkupExtensionBoundValue(
            string text,
            CSharpSymbolDefinition type,
            CSharpOperationDefinition operation,
            AkburaConversion conversion,
            object? convertedValue,
            MarkupExtensionValue? nestedValue)
        {
            Text = text;
            Type = type;
            Operation = operation;
            Conversion = conversion;
            ConvertedValue = convertedValue;
            NestedValue = nestedValue;
        }

        public string Text { get; }

        public CSharpSymbolDefinition Type { get; }

        public CSharpOperationDefinition Operation { get; }

        public AkburaConversion Conversion { get; }

        public object? ConvertedValue { get; }

        public MarkupExtensionValue? NestedValue { get; }
    }

    internal static CSharp.ExpressionSyntax? ParseInlineExpression(InlineExpressionSyntax inlineExpression)
    {
        return inlineExpression.GetRawCSharpExpression();
    }

    internal static void AddMarkupAttributeBindingDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property.Command != null)
        {
            if (bindingKind != MarkupAttributeBindingKind.None)
            {
                diagnosticsBuilder.Add(CreateMarkupCommandBindingNotAllowedDiagnostic(
                    markupAttribute,
                    property.Command,
                    bindingKind));
            }

            return;
        }

        if (property.Parameter == null)
        {
            if (IsMarkupDataTypeDirective(markupAttribute) ||
                IsMarkupItemNameDirective(markupAttribute) ||
                string.Equals(GetMarkupPropertyName(markupAttribute), "class", StringComparison.Ordinal))
            {
                return;
            }

            var hasRequiredAccess = bindingKind switch
            {
                MarkupAttributeBindingKind.Bind => property.CanRead && property.CanWrite,
                MarkupAttributeBindingKind.Out => property.CanRead,
                _ => property.CanWrite,
            };
            if (!hasRequiredAccess)
            {
                var requiredAccess = bindingKind switch
                {
                    MarkupAttributeBindingKind.Bind => "public getter and setter",
                    MarkupAttributeBindingKind.Out => "public getter",
                    _ => "public setter",
                };
                diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                    markupAttribute,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupPropertyAccessNotSupported,
                    [property.Name, requiredAccess, GetMarkupAttributeBindingText(bindingKind)]));
            }

            return;
        }

        var parameter = property.Parameter;
        var isAllowed = bindingKind switch
        {
            MarkupAttributeBindingKind.None => parameter.ReceivesValueFromParent,
            MarkupAttributeBindingKind.Bind => parameter.IsTwoWayBinding,
            MarkupAttributeBindingKind.Out => parameter.SendsValueToParent,
            _ => false,
        };

        if (isAllowed)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateMarkupAttributeBindingNotAllowedDiagnostic(
            markupAttribute,
            property,
            bindingKind));
    }

    private static AkburaSemanticDiagnostic CreateMarkupAttributeBindingNotAllowedDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind)
    {
        return new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeBindingNotAllowed,
            [GetMarkupAttributeBindingText(bindingKind), property.Name, GetParamBindingText(property.Parameter!.BindingKind)]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupCommandBindingNotAllowedDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        MarkupAttributeBindingKind bindingKind)
    {
        return new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupCommandBindingNotAllowed,
            [command.Name, GetMarkupAttributeBindingText(bindingKind)]);
    }

    private static string GetMarkupAttributeBindingText(MarkupAttributeBindingKind bindingKind)
    {
        return bindingKind switch
        {
            MarkupAttributeBindingKind.Bind => "bind",
            MarkupAttributeBindingKind.Out => "out",
            _ => "set",
        };
    }

    private static string GetParamBindingText(ParamBindingKind bindingKind)
    {
        return bindingKind switch
        {
            ParamBindingKind.Bind => "param bind",
            ParamBindingKind.Out => "param out",
            _ => "param",
        };
    }

    internal static void AddDuplicateMarkupPropertySetterDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (!IsMarkupPropertySetter(bindingKind) ||
            GetContainingMarkupStartTag(markupAttribute) is not { } startTag)
        {
            return;
        }

        var propertyName = property.Name;
        foreach (var attribute in startTag.Attributes)
        {
            if (attribute.Position >= markupAttribute.Position)
            {
                break;
            }

            if (GetMarkupPropertyName(attribute) == propertyName &&
                IsMarkupPropertySetter(GetMarkupAttributeBindingKind(attribute)))
            {
                diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
                    markupAttribute,
                    ErrorCodes.AKBURA_SEMANTIC_MarkupDuplicatePropertySetter,
                    [propertyName]));
                return;
            }
        }
    }

    private static bool IsMarkupPropertySetter(MarkupAttributeBindingKind bindingKind)
    {
        return bindingKind is MarkupAttributeBindingKind.None or MarkupAttributeBindingKind.Bind;
    }

    internal static void AddMarkupEventBindingDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        MarkupAttributeBindingKind bindingKind,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (bindingKind == MarkupAttributeBindingKind.None)
        {
            return;
        }

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupEventBindingNotAllowed,
            [routedEvent.Name, GetMarkupAttributeBindingText(bindingKind)]));
    }

    internal void AddMarkupEventHandlerSignatureDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax? expression,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (expression == null ||
            !TryGetLambdaParameterTypes(expression, out var parameterTypes))
        {
            return;
        }

        var expectedParameterTypes = GetEventHandlerParameterTypes(routedEvent);
        if (expectedParameterTypes.IsDefaultOrEmpty ||
            HasCompatibleEventHandlerParameters(expectedParameterTypes, parameterTypes))
        {
            return;
        }

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupEventHandlerSignatureMismatch,
            [
                routedEvent.Name,
                FormatEventHandlerSignature(expectedParameterTypes),
                FormatEventHandlerSignature(parameterTypes)
            ]));
    }

    private static bool TryGetLambdaParameterTypes(
        CSharp.ExpressionSyntax expression,
        out ImmutableArray<CSharp.TypeSyntax?> parameterTypes)
    {
        using var builder = ImmutableArrayBuilder<CSharp.TypeSyntax?>.Rent();
        switch (expression)
        {
            case CSharp.ParenthesizedLambdaExpressionSyntax lambda:
                foreach (var parameter in lambda.ParameterList.Parameters)
                {
                    builder.Add(parameter.Type);
                }

                parameterTypes = builder.ToImmutable();
                return true;

            case CSharp.SimpleLambdaExpressionSyntax lambda:
                builder.Add(lambda.Parameter.Type);
                parameterTypes = builder.ToImmutable();
                return true;

            case CSharp.AnonymousMethodExpressionSyntax { ParameterList: { } parameterList }:
                foreach (var parameter in parameterList.Parameters)
                {
                    builder.Add(parameter.Type);
                }

                parameterTypes = builder.ToImmutable();
                return true;
        }

        parameterTypes = default;
        return false;
    }

    private static ImmutableArray<ITypeSymbol> GetEventHandlerParameterTypes(
        IRoutedEventSymbol routedEvent)
    {
        if (routedEvent.HandlerType.Symbol is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
        {
            return ImmutableArray<ITypeSymbol>.Empty;
        }

        using var builder = ImmutableArrayBuilder<ITypeSymbol>.Rent();
        foreach (var parameter in invokeMethod.Parameters)
        {
            builder.Add(parameter.Type);
        }

        return builder.ToImmutable();
    }

    private bool HasCompatibleEventHandlerParameters(
        ImmutableArray<ITypeSymbol> expectedParameterTypes,
        ImmutableArray<CSharp.TypeSyntax?> actualParameterTypes)
    {
        if (expectedParameterTypes.Length != actualParameterTypes.Length)
        {
            return false;
        }

        for (var index = 0; index < actualParameterTypes.Length; index++)
        {
            var actualParameterType = actualParameterTypes[index];
            if (actualParameterType == null)
            {
                continue;
            }

            var binding = BindCSharpType(actualParameterType);
            if (binding.TypeSymbol == null ||
                !IsSameType(expectedParameterTypes[index], binding.TypeSymbol))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatEventHandlerSignature(
        ImmutableArray<ITypeSymbol> parameterTypes)
    {
        return "(" +
            string.Join(
                ", ",
                parameterTypes.Select(static type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) +
            ")";
    }

    private static string FormatEventHandlerSignature(
        ImmutableArray<CSharp.TypeSyntax?> parameterTypes)
    {
        return "(" +
            string.Join(
                ", ",
                parameterTypes.Select(static type => type?.ToString() ?? "<inferred>")) +
            ")";
    }

    private static MarkupStartTagSyntax? GetContainingMarkupStartTag(MarkupAttributeSyntax markupAttribute)
    {
        for (var node = markupAttribute.Parent; node != null; node = node.Parent)
        {
            if (node.Kind == AkburaSyntaxKind.MarkupStartTagSyntax)
            {
                return Unsafe.As<MarkupStartTagSyntax>(node);
            }
        }

        return null;
    }

    internal static bool IsAvaloniaGridDefinitionListType(ITypeSymbol? type)
    {
        if (type is not INamedTypeSymbol namedType)
        {
            return false;
        }

        var originalDefinition = namedType.OriginalDefinition;
        return originalDefinition.ContainingNamespace.ToDisplayString() == "Avalonia.Controls" &&
               originalDefinition.Name is "ColumnDefinitions" or "RowDefinitions";
    }

    internal void AddMarkupDefinitionListLiteralDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        string literalValue,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property.Type.Symbol is not ITypeSymbol targetType ||
            !IsAvaloniaGridDefinitionListType(targetType) ||
            GridDefinitionLiteralParser.TryParse(literalValue))
        {
            return;
        }

        AddMarkupAttributeCannotConvertDiagnostic(
            markupAttribute,
            property,
            Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String),
            targetType,
            diagnosticsBuilder);
    }

    internal void AddMarkupLiteralValueDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        MarkupLiteralConversionStatus conversionStatus,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (conversionStatus != MarkupLiteralConversionStatus.Invalid ||
            property.Type.Symbol is not ITypeSymbol targetType)
        {
            return;
        }

        AddMarkupAttributeCannotConvertDiagnostic(
            markupAttribute,
            property,
            Compilation.CSharpCompilation.GetSpecialType(SpecialType.System_String),
            targetType,
            diagnosticsBuilder);
    }

    internal void AddMarkupAttributeValueDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (property.Type.Symbol is not ITypeSymbol targetType)
        {
            return;
        }

        var conversion = binding.Conversion;
        if (conversion.TargetType != null)
        {
            if (conversion.SourceType != null &&
                IsSameType(conversion.SourceType, targetType))
            {
                return;
            }

            if (conversion.IsImplicit)
            {
                return;
            }

            if (conversion.SourceType != null)
            {
                AddMarkupAttributeCannotConvertDiagnostic(
                    markupAttribute,
                    property,
                    conversion.SourceType,
                    targetType,
                    diagnosticsBuilder);
            }

            return;
        }

        if (binding.TypeSymbol is not ITypeSymbol sourceType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        AddMarkupAttributeCannotConvertDiagnostic(
            markupAttribute,
            property,
            sourceType,
            targetType,
            diagnosticsBuilder);
    }

    private static void AddMarkupAttributeCannotConvertDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        Symbols.IPropertySymbol property,
        ITypeSymbol sourceType,
        ITypeSymbol targetType,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        var sourceTypeText = sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        var targetTypeText = targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

        diagnosticsBuilder.Add(new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupAttributeValueCannotConvert,
            [property.Name, sourceTypeText, targetTypeText]));
    }

    internal void AddMarkupExpressionDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            markupAttribute,
            GetMarkupExpressionDiagnosticText(markupAttribute),
            binding,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            markupAttribute,
            GetMarkupExpressionDiagnosticText(markupAttribute),
            diagnostics,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        CSharpBindingResult binding,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        AddMarkupExpressionDiagnostics(
            syntax,
            expressionText,
            binding.Diagnostics,
            diagnosticsBuilder);
    }

    internal void AddMarkupExpressionDiagnostics(
        AkburaSyntax syntax,
        string expressionText,
        ImmutableArray<Diagnostic> diagnostics,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (diagnostics.IsDefaultOrEmpty)
        {
            return;
        }

        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                diagnosticsBuilder.Add(CreateMarkupExpressionErrorDiagnostic(
                    syntax,
                    expressionText,
                    diagnostic));
            }
        }
    }

    internal void AddMarkupCommandHandlerSignatureDiagnostics(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        CSharp.ExpressionSyntax? expression,
        MarkupCommandHandlerAnalysis handler,
        ImmutableArrayBuilder<AkburaSemanticDiagnostic> diagnosticsBuilder)
    {
        if (handler.Kind != MarkupCommandHandlerKind.Lambda ||
            expression == null)
        {
            return;
        }

        if (handler.ParameterCount != 0 &&
            handler.ParameterCount != command.Parameters.Length)
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                FormatCommandHandlerParameterExpectation(command),
                handler.ParameterCount + " parameter(s)"));
            return;
        }

        if (handler.ParameterCount != 0 &&
            TryGetLambdaParameterTypes(expression, out var actualParameterTypes) &&
            !HasCompatibleCommandHandlerParameters(command, actualParameterTypes))
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                FormatCommandHandlerParameterTypes(command),
                FormatEventHandlerSignature(actualParameterTypes)));
            return;
        }

        if (handler.ResultMode != MarkupCommandResultMode.ReturnsResult)
        {
            return;
        }

        if (!command.HasResult)
        {
            diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
                markupAttribute,
                command,
                "no result",
                FormatCommandHandlerResultType(handler.ResultType)));
            return;
        }

        if (handler.ResultType.Symbol is not ITypeSymbol sourceType ||
            command.ResultType.Symbol is not ITypeSymbol targetType ||
            IsSameType(sourceType, targetType) ||
            Compilation.CSharpCompilation.ClassifyConversion(sourceType, targetType).IsImplicit)
        {
            return;
        }

        diagnosticsBuilder.Add(CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
            markupAttribute,
            command,
            targetType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)));
    }

    private bool HasCompatibleCommandHandlerParameters(
        ICommandSymbol command,
        ImmutableArray<CSharp.TypeSyntax?> actualParameterTypes)
    {
        if (actualParameterTypes.Length != command.Parameters.Length)
        {
            return false;
        }

        for (var index = 0; index < actualParameterTypes.Length; index++)
        {
            var actualParameterType = actualParameterTypes[index];
            if (actualParameterType == null)
            {
                continue;
            }

            if (command.Parameters[index].Type.Symbol is not ITypeSymbol expectedType)
            {
                return false;
            }

            var binding = BindCSharpType(actualParameterType);
            if (binding.TypeSymbol == null ||
                !IsSameType(expectedType, binding.TypeSymbol))
            {
                return false;
            }
        }

        return true;
    }

    private static string FormatCommandHandlerParameterExpectation(ICommandSymbol command)
    {
        return command.Parameters.Length == 0
            ? "0 parameter(s)"
            : "0 or " + command.Parameters.Length + " parameter(s)";
    }

    private static string FormatCommandHandlerParameterTypes(ICommandSymbol command)
    {
        if (command.Parameters.Length == 0)
        {
            return "()";
        }

        return "(" +
            string.Join(
                ", ",
                command.Parameters.Select(static parameter => parameter.Type.IsDefault
                    ? "object"
                    : parameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) +
            ")";
    }

    private static string FormatCommandHandlerResultType(CSharpSymbolDefinition resultType)
    {
        return resultType.IsDefault
            ? "<unknown>"
            : resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
    }

    internal MarkupCommandHandlerAnalysis AnalyzeMarkupCommandHandler(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        CSharp.ExpressionSyntax? expression,
        CSharpSymbolDefinition handlerType,
        CSharpOperationDefinition handlerOperation)
    {
        if (expression == null)
        {
            return MarkupCommandHandlerAnalysis.Error;
        }

        return expression switch
        {
            CSharp.ParenthesizedLambdaExpressionSyntax lambda => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                lambda.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray(),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.SimpleLambdaExpressionSyntax lambda => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                ImmutableArray.Create(lambda.Parameter.Identifier.ValueText),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.AnonymousMethodExpressionSyntax anonymousMethod => AnalyzeMarkupCommandLambda(
                markupAttribute,
                command,
                anonymousMethod.ParameterList?.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray() ??
                    ImmutableArray<string>.Empty,
                anonymousMethod.AsyncKeyword.RawKind != 0,
                anonymousMethod.Body),
            _ => new MarkupCommandHandlerAnalysis(
                MarkupCommandHandlerKind.DirectReference,
                MarkupCommandArgumentMode.None,
                MarkupCommandResultMode.Unknown,
                parameterCount: 0,
                isAsync: ContainsAwaitExpression(expression),
                containsAwait: ContainsAwaitExpression(expression),
                handlerType,
                resultType: default,
                handlerOperation),
        };
    }

    internal MarkupEventHandlerAnalysis AnalyzeMarkupEventHandler(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax? expression)
    {
        if (expression == null)
        {
            return MarkupEventHandlerAnalysis.Error;
        }

        return expression switch
        {
            CSharp.ParenthesizedLambdaExpressionSyntax lambda => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                lambda.ParameterList.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray(),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.SimpleLambdaExpressionSyntax lambda => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                ImmutableArray.Create(lambda.Parameter.Identifier.ValueText),
                lambda.AsyncKeyword.RawKind != 0,
                lambda.Body),
            CSharp.AnonymousMethodExpressionSyntax anonymousMethod => AnalyzeMarkupEventLambda(
                markupAttribute,
                routedEvent,
                anonymousMethod.ParameterList?.Parameters.Select(static parameter => parameter.Identifier.ValueText).ToImmutableArray() ??
                    ImmutableArray<string>.Empty,
                anonymousMethod.AsyncKeyword.RawKind != 0,
                anonymousMethod.Body),
            CSharp.IdentifierNameSyntax or CSharp.MemberAccessExpressionSyntax =>
                CreateDirectMarkupEventHandlerAnalysis(markupAttribute, expression),
            _ => CreateExpressionMarkupEventHandlerAnalysis(markupAttribute, routedEvent, expression),
        };
    }

    private MarkupEventHandlerAnalysis CreateDirectMarkupEventHandlerAnalysis(
        MarkupAttributeSyntax markupAttribute,
        CSharp.ExpressionSyntax expression)
    {
        var binding = BindMarkupAttributeExpression(markupAttribute, expression);
        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.DirectReference,
            MarkupCommandArgumentMode.None,
            parameterCount: 0,
            isAsync: ContainsAwaitExpression(expression),
            containsAwait: ContainsAwaitExpression(expression),
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupEventHandlerAnalysis CreateExpressionMarkupEventHandlerAnalysis(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        CSharp.ExpressionSyntax expression)
    {
        var binding = BindMarkupEventHandlerStatementExpression(
            markupAttribute,
            routedEvent,
            ImmutableArray<string>.Empty,
            expression,
            isAsync: false);

        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.Expression,
            MarkupCommandArgumentMode.IgnoresCommandArgument,
            parameterCount: 0,
            isAsync: ContainsAwaitExpression(expression),
            containsAwait: ContainsAwaitExpression(expression),
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupEventHandlerAnalysis AnalyzeMarkupEventLambda(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        bool isAsync,
        SyntaxNode body)
    {
        var containsAwait = ContainsAwaitExpression(body);
        var binding = body switch
        {
            CSharp.ExpressionSyntax expression => BindMarkupEventHandlerStatementExpression(
                markupAttribute,
                routedEvent,
                parameterNames,
                expression,
                isAsync || containsAwait),
            CSharp.BlockSyntax block => BindMarkupEventHandlerBlock(
                markupAttribute,
                routedEvent,
                parameterNames,
                block,
                isAsync || containsAwait),
            _ => CSharpBindingResult.Empty,
        };

        return new MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind.Lambda,
            parameterNames.Length == 0
                ? MarkupCommandArgumentMode.IgnoresCommandArgument
                : MarkupCommandArgumentMode.ReceivesCommandArgument,
            parameterNames.Length,
            isAsync || containsAwait,
            containsAwait,
            binding.OperationDefinition,
            binding.Diagnostics);
    }

    private MarkupCommandHandlerAnalysis AnalyzeMarkupCommandLambda(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        bool isAsync,
        SyntaxNode body)
    {
        var containsAwait = ContainsAwaitExpression(body);
        var argumentMode = parameterNames.Length == 0
            ? MarkupCommandArgumentMode.IgnoresCommandArgument
            : MarkupCommandArgumentMode.ReceivesCommandArgument;
        var resultMode = MarkupCommandResultMode.NoResult;
        var resultType = default(CSharpSymbolDefinition);
        var operation = default(CSharpOperationDefinition);
        var diagnostics = ImmutableArray<Diagnostic>.Empty;

        if (body is CSharp.ExpressionSyntax expressionBody)
        {
            var resultBinding = BindCommandHandlerResultExpression(markupAttribute, command, parameterNames, expressionBody);
            operation = resultBinding.OperationDefinition;
            diagnostics = resultBinding.Diagnostics;
            if (TryGetAwaitedLocalCommandExecuteResultType(expressionBody, out var awaitedCommandResultType))
            {
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = awaitedCommandResultType;
            }
            else if (resultBinding.TypeSymbol is { SpecialType: SpecialType.System_Void } ||
                resultBinding.Symbol is IMethodSymbol { ReturnsVoid: true })
            {
                resultMode = MarkupCommandResultMode.NoResult;
            }
            else if (resultBinding.TypeSymbol != null)
            {
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = new CSharpSymbolDefinition(resultBinding.TypeSymbol);
            }
            else if (expressionBody is CSharp.InvocationExpressionSyntax)
            {
                var statementBinding = BindCommandHandlerStatementExpression(markupAttribute, command, parameterNames, expressionBody);
                operation = statementBinding.OperationDefinition;
                diagnostics = statementBinding.Diagnostics;
                if (statementBinding.Symbol is IMethodSymbol { ReturnsVoid: true })
                {
                    resultMode = MarkupCommandResultMode.NoResult;
                }
                else if (statementBinding.TypeSymbol != null)
                {
                    resultMode = MarkupCommandResultMode.ReturnsResult;
                    resultType = new CSharpSymbolDefinition(statementBinding.TypeSymbol);
                }
                else
                {
                    resultMode = MarkupCommandResultMode.Unknown;
                }
            }
            else
            {
                resultMode = MarkupCommandResultMode.Unknown;
            }
        }
        else if (body is CSharp.BlockSyntax block)
        {
            var returnExpression = block
                .DescendantNodes()
                .OfType<CSharp.ReturnStatementSyntax>()
                .Select(static returnStatement => returnStatement.Expression)
                .FirstOrDefault(expression => expression != null);
            if (returnExpression != null)
            {
                var resultBinding = BindCommandHandlerResultExpression(markupAttribute, command, parameterNames, returnExpression);
                operation = resultBinding.OperationDefinition;
                diagnostics = resultBinding.Diagnostics;
                resultMode = MarkupCommandResultMode.ReturnsResult;
                resultType = resultBinding.TypeSymbol == null
                    ? default
                    : new CSharpSymbolDefinition(resultBinding.TypeSymbol);
            }
        }

        return new MarkupCommandHandlerAnalysis(
            MarkupCommandHandlerKind.Lambda,
            argumentMode,
            resultMode,
            parameterNames.Length,
            isAsync || containsAwait,
            containsAwait,
            type: default,
            resultType,
            operation,
            diagnostics);
    }

    private bool TryGetAwaitedLocalCommandExecuteResultType(
        CSharp.ExpressionSyntax expression,
        out CSharpSymbolDefinition resultType)
    {
        resultType = default;

        if (expression is not CSharp.AwaitExpressionSyntax awaitExpression ||
            awaitExpression.Expression is not CSharp.InvocationExpressionSyntax invocation ||
            invocation.Expression is not CSharp.MemberAccessExpressionSyntax memberAccess ||
            memberAccess.Name.Identifier.ValueText != "Execute" ||
            memberAccess.Expression is not CSharp.IdentifierNameSyntax receiver)
        {
            return false;
        }

        var commandName = receiver.Identifier.ValueText;
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            if (member.Kind != AkburaSyntaxKind.CommandDeclarationSyntax)
            {
                continue;
            }

            var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
            if (commandDeclaration.Name.Identifier.ValueText != commandName ||
                GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command ||
                command.ResultType.IsDefault)
            {
                continue;
            }

            resultType = command.ResultType;
            return true;
        }

        return false;
    }

    private CSharpBindingResult BindCommandHandlerResultExpression(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                ContainsAwaitExpression(expressionSyntax)
                    ? CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.Task<object>")
                    : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaCommandHandlerProbe")
            .WithParameterList(CreateCommandHandlerProbeParameterList(command, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, returnStatement));

        if (ContainsAwaitExpression(expressionSyntax))
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindReturnExpression(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindCommandHandlerStatementExpression(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var statement = CSharpSyntaxFactory.ExpressionStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaCommandHandlerProbe")
            .WithParameterList(CreateCommandHandlerProbeParameterList(command, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, statement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindExpressionStatement(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindMarkupEventHandlerStatementExpression(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        CSharp.ExpressionSyntax expressionSyntax,
        bool isAsync)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            expressionSyntax,
            parameterNames);
        var statement = CSharpSyntaxFactory.ExpressionStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaEventHandlerProbe")
            .WithParameterList(CreateEventHandlerProbeParameterList(routedEvent, parameterNames))
            .WithBody(CreateMarkupHandlerProbeBlock(probeScope.LocalStatements, statement));

        if (isAsync)
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindExpressionStatement(compilationUnit, isBindingPath: false);
    }

    private CSharpBindingResult BindMarkupEventHandlerBlock(
        MarkupAttributeSyntax markupAttribute,
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames,
        CSharp.BlockSyntax block,
        bool isAsync)
    {
        var probeScope = CreateMarkupHandlerProbeScope(
            markupAttribute,
            block,
            parameterNames);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.VoidKeyword)),
                "__AkburaEventHandlerProbe")
            .WithParameterList(CreateEventHandlerProbeParameterList(routedEvent, parameterNames))
            .WithBody(PrependMarkupHandlerProbeLocals(block, probeScope.LocalStatements));

        if (isAsync)
        {
            method = method.WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.AsyncKeyword)));
        }

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        AddMarkupAttributeProbeMembers(membersBuilder, probeScope);

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(GetMarkupBindingScope(markupAttribute), BinderUsage.Markup)
            .BindMethodBlock(compilationUnit, "__AkburaEventHandlerProbe");
    }

    private CSharpProbeScope CreateMarkupHandlerProbeScope(
        MarkupAttributeSyntax markupAttribute,
        SyntaxNode csharpNode,
        ImmutableArray<string> parameterNames)
    {
        var scope = GetMarkupBindingScope(markupAttribute);
        return BindingSession
            .GetCSharpProbeBinder(scope, BinderUsage.Markup)
            .CreateProbeScope(scope, csharpNode, parameterNames);
    }

    private void AddMarkupAttributeProbeMembers(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> membersBuilder,
        CSharpProbeScope probeScope)
    {
        var addedMemberKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var field in CreateMarkupAttributeProbeFields())
        {
            AddMarkupAttributeProbeMember(membersBuilder, addedMemberKeys, field);
        }

        foreach (var member in probeScope.MemberDeclarations)
        {
            AddMarkupAttributeProbeMember(membersBuilder, addedMemberKeys, member);
        }
    }

    private static void AddMarkupAttributeProbeMember(
        ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax> membersBuilder,
        HashSet<string> addedMemberKeys,
        CSharp.MemberDeclarationSyntax member)
    {
        var key = GetMarkupAttributeProbeMemberKey(member);
        if (key == null ||
            addedMemberKeys.Add(key))
        {
            membersBuilder.Add(member);
        }
    }

    private static string? GetMarkupAttributeProbeMemberKey(CSharp.MemberDeclarationSyntax member)
    {
        return member switch
        {
            CSharp.ClassDeclarationSyntax classDeclaration => "class:" + classDeclaration.Identifier.ValueText,
            CSharp.StructDeclarationSyntax structDeclaration => "struct:" + structDeclaration.Identifier.ValueText,
            CSharp.MethodDeclarationSyntax methodDeclaration => "method:" + methodDeclaration.Identifier.ValueText,
            CSharp.FieldDeclarationSyntax fieldDeclaration when fieldDeclaration.Declaration.Variables.Count == 1 =>
                "field:" + fieldDeclaration.Declaration.Variables[0].Identifier.ValueText,
            _ => null,
        };
    }

    private static CSharp.BlockSyntax CreateMarkupHandlerProbeBlock(
        ImmutableArray<CSharp.StatementSyntax> localStatements,
        CSharp.StatementSyntax statement)
    {
        if (localStatements.IsDefaultOrEmpty)
        {
            return CSharpSyntaxFactory.Block(statement);
        }

        using var statements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent(localStatements.Length + 1);
        statements.AddRange(localStatements.AsSpan());
        statements.Add(statement);
        return CSharpSyntaxFactory.Block(CSharpSyntaxFactory.List(statements.ToImmutable()));
    }

    private static CSharp.BlockSyntax PrependMarkupHandlerProbeLocals(
        CSharp.BlockSyntax block,
        ImmutableArray<CSharp.StatementSyntax> localStatements)
    {
        if (localStatements.IsDefaultOrEmpty)
        {
            return block;
        }

        using var statements = ImmutableArrayBuilder<CSharp.StatementSyntax>.Rent(
            localStatements.Length + block.Statements.Count);
        statements.AddRange(localStatements.AsSpan());
        foreach (var statement in block.Statements)
        {
            statements.Add(statement);
        }

        return block.WithStatements(CSharpSyntaxFactory.List(statements.ToImmutable()));
    }

    private static CSharp.ParameterListSyntax CreateCommandHandlerProbeParameterList(
        ICommandSymbol command,
        ImmutableArray<string> parameterNames)
    {
        using var parameters = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        for (var index = 0; index < parameterNames.Length; index++)
        {
            var name = string.IsNullOrWhiteSpace(parameterNames[index])
                ? "__arg" + index
                : parameterNames[index];
            var type = index < command.Parameters.Length && !command.Parameters[index].Type.IsDefault
                ? CSharpSyntaxFactory.ParseTypeName(command.Parameters[index].Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))
                : CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword));

            parameters.Add(CSharpSyntaxFactory.Parameter(CSharpSyntaxFactory.Identifier(name)).WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(parameters.ToImmutable()));
    }

    private static CSharp.ParameterListSyntax CreateEventHandlerProbeParameterList(
        IRoutedEventSymbol routedEvent,
        ImmutableArray<string> parameterNames)
    {
        if (routedEvent.HandlerType.Symbol is not INamedTypeSymbol { DelegateInvokeMethod: { } invokeMethod })
        {
            return CSharpSyntaxFactory.ParameterList();
        }

        using var parameters = ImmutableArrayBuilder<CSharp.ParameterSyntax>.Rent();
        for (var index = 0; index < invokeMethod.Parameters.Length; index++)
        {
            var delegateParameter = invokeMethod.Parameters[index];
            var name = index < parameterNames.Length && !string.IsNullOrWhiteSpace(parameterNames[index])
                ? parameterNames[index]
                : string.IsNullOrWhiteSpace(delegateParameter.Name)
                    ? "__arg" + index
                    : delegateParameter.Name;
            var type = CSharpSyntaxFactory.ParseTypeName(
                delegateParameter.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));

            parameters.Add(CSharpSyntaxFactory.Parameter(CSharpSyntaxFactory.Identifier(name)).WithType(type));
        }

        return CSharpSyntaxFactory.ParameterList(
            CSharpSyntaxFactory.SeparatedList(parameters.ToImmutable()));
    }

    private static bool ContainsAwaitExpression(SyntaxNode node)
    {
        return node.DescendantNodesAndSelf().OfType<CSharp.AwaitExpressionSyntax>().Any();
    }

    private CSharpBindingResult BindMarkupAttributeExpression(CSharp.ExpressionSyntax expressionSyntax)
    {
        var returnStatement = CSharpSyntaxFactory.ReturnStatement(expressionSyntax);
        var method = CSharpSyntaxFactory.MethodDeclaration(
                CSharpSyntaxFactory.PredefinedType(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.ObjectKeyword)),
                "__AkburaSemanticProbe")
            .WithBody(CSharpSyntaxFactory.Block(returnStatement));

        using var membersBuilder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var field in CreateMarkupAttributeProbeFields())
        {
            membersBuilder.Add(field);
        }

        membersBuilder.Add(method);

        var probeClass = CSharpSyntaxFactory.ClassDeclaration("__AkburaSemanticProbe")
            .WithMembers(CSharpSyntaxFactory.List(membersBuilder.ToImmutable()));

        var compilationUnit = CreateCSharpProbeCompilationUnit(probeClass);
        return BindingSession
            .GetCSharpProbeBinder(SyntaxTree.GetRoot(), BinderUsage.Markup)
            .BindReturnExpression(compilationUnit, isBindingPath: true);
    }

    internal CSharpBindingResult BindMarkupAttributeExpression(
        AkburaSyntax scopeSyntax,
        CSharp.ExpressionSyntax expressionSyntax,
        ITypeSymbol? targetType = null)
    {
        var scope = GetMarkupBindingScope(scopeSyntax);
        var bound = BindingSession
            .GetCSharpProbeBinder(scope, BinderUsage.Markup)
            .BindExpression(scope, expressionSyntax, targetType);

        return GetCSharpBindingResult(bound);
    }

    private static CSharpBindingResult GetCSharpBindingResult(BoundExpression expression)
    {
        return expression.Kind switch
        {
            BoundKind.CSharpExpression => Unsafe.As<BoundCSharpExpression>(expression).BindingResult,
            BoundKind.LiteralExpression => Unsafe.As<BoundLiteralExpression>(expression).BindingResult,
            BoundKind.BinaryExpression => Unsafe.As<BoundBinaryExpression>(expression).BindingResult,
            BoundKind.CallExpression => Unsafe.As<BoundCallExpression>(expression).BindingResult,
            BoundKind.ConversionExpression => GetCSharpConversionBindingResult(Unsafe.As<BoundConversionExpression>(expression)),
            _ => CSharpBindingResult.Empty,
        };
    }

    private static CSharpBindingResult GetCSharpConversionBindingResult(BoundConversionExpression expression)
    {
        return GetCSharpBindingResult(expression.Operand)
            .WithConversion(expression.Conversion);
    }

    private AkburaSyntax GetMarkupBindingScope(AkburaSyntax syntax)
    {
        for (var node = syntax; node != null; node = node.Parent)
        {
            switch (node.Kind)
            {
                case AkburaSyntaxKind.MarkupRootSyntax:
                    return Unsafe.As<MarkupRootSyntax>(node);
                case AkburaSyntaxKind.MarkupElementSyntax:
                    var markupElement = Unsafe.As<MarkupElementSyntax>(node);
                    if (markupElement.Parent?.Kind == AkburaSyntaxKind.MarkupRootSyntax)
                    {
                        return Unsafe.As<MarkupRootSyntax>(markupElement.Parent);
                    }

                    return markupElement;
            }
        }

        return SyntaxTree.GetRoot();
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateMarkupAttributeProbeFields()
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();
        foreach (var member in SyntaxTree.GetRoot().Members)
        {
            switch (member.Kind)
            {
                case AkburaSyntaxKind.StateDeclarationSyntax:
                    var stateDeclaration = Unsafe.As<StateDeclarationSyntax>(member);
                    if (TryCreateStateProbeField(stateDeclaration, out var stateField))
                    {
                        builder.Add(stateField);
                    }

                    break;

                case AkburaSyntaxKind.ParamDeclarationSyntax:
                    var paramDeclaration = Unsafe.As<ParamDeclarationSyntax>(member);
                    if (TryCreateParamProbeField(paramDeclaration, out var paramField))
                    {
                        builder.Add(paramField);
                    }

                    break;

                case AkburaSyntaxKind.InjectDeclarationSyntax:
                    var injectDeclaration = Unsafe.As<InjectDeclarationSyntax>(member);
                    if (TryCreateInjectProbeField(injectDeclaration, out var injectField))
                    {
                        builder.Add(injectField);
                    }

                    break;

                case AkburaSyntaxKind.CommandDeclarationSyntax:
                    var commandDeclaration = Unsafe.As<CommandDeclarationSyntax>(member);
                    foreach (var commandMember in CreateCommandProbeMembers(commandDeclaration))
                    {
                        builder.Add(commandMember);
                    }

                    break;
            }
        }

        return builder.ToImmutable();
    }

    private bool TryCreateParamProbeField(
        ParamDeclarationSyntax paramDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = paramDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        CSharp.TypeSyntax? type = null;
        if (paramDeclaration.Type != null)
        {
            try
            {
                type = paramDeclaration.Type.ToCSharp();
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
        else if (GetSymbolInfo(paramDeclaration).Symbol is IParamSymbol paramSymbol &&
                 paramSymbol.Type.Symbol is ITypeSymbol typeSymbol)
        {
            type = CSharpSyntaxFactory.ParseTypeName(
                typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        }

        if (type == null)
        {
            return false;
        }

        field = CreateProbeField(type, name);
        return true;
    }

    private static bool TryCreateInjectProbeField(
        InjectDeclarationSyntax injectDeclaration,
        out CSharp.FieldDeclarationSyntax field)
    {
        field = null!;

        var name = injectDeclaration.Name.Identifier.ValueText;
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        try
        {
            field = CreateProbeField(injectDeclaration.Type.ToCSharp(), name);
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeMembers(
        CommandDeclarationSyntax commandDeclaration)
    {
        if (GetSymbolInfo(commandDeclaration).Symbol is not ICommandSymbol command)
        {
            return ImmutableArray<CSharp.MemberDeclarationSyntax>.Empty;
        }

        var commandTypeName = "__AkburaCommand_" + ToCSharpIdentifier(command.Name);
        var commandType = CSharpSyntaxFactory.IdentifierName(commandTypeName);
        var commandClass = CSharpSyntaxFactory.ClassDeclaration(commandTypeName)
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword),
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SealedKeyword)))
            .WithMembers(CSharpSyntaxFactory.List(CreateCommandProbeTypeMembers(commandDeclaration, command)));
        var commandField = CreateProbeField(commandType, command.Name);

        return ImmutableArray.Create<CSharp.MemberDeclarationSyntax>(commandClass, commandField);
    }

    private ImmutableArray<CSharp.MemberDeclarationSyntax> CreateCommandProbeTypeMembers(
        CommandDeclarationSyntax commandDeclaration,
        ICommandSymbol command)
    {
        using var builder = ImmutableArrayBuilder<CSharp.MemberDeclarationSyntax>.Rent();

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "IsExecuting")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken)));

        builder.Add(CSharpSyntaxFactory.PropertyDeclaration(
                CSharpSyntaxFactory.ParseTypeName("global::System.IObservable<bool>"),
                "CanExecute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithExpressionBody(CSharpSyntaxFactory.ArrowExpressionClause(
                CSharpSyntaxFactory.LiteralExpression(Microsoft.CodeAnalysis.CSharp.SyntaxKind.DefaultLiteralExpression)))
            .WithSemicolonToken(CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.SemicolonToken)));

        var parameterList = GetCSharpParameterList(commandDeclaration.Parameters) ??
            CSharpSyntaxFactory.ParameterList();
        var execute = CSharpSyntaxFactory.MethodDeclaration(
                GetCommandExecuteReturnTypeSyntax(command),
                "Execute")
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PublicKeyword)))
            .WithParameterList(parameterList)
            .WithBody(CSharpSyntaxFactory.Block(CSharpSyntaxFactory.ThrowStatement(
                CSharpSyntaxFactory.ObjectCreationExpression(
                        CSharpSyntaxFactory.ParseTypeName("global::System.NotImplementedException"))
                    .WithArgumentList(CSharpSyntaxFactory.ArgumentList()))));

        builder.Add(execute);
        return builder.ToImmutable();
    }

    private static CSharp.TypeSyntax GetCommandExecuteReturnTypeSyntax(ICommandSymbol command)
    {
        if (command.HasResult &&
            !command.ResultType.IsDefault)
        {
            return CSharpSyntaxFactory.ParseTypeName(
                "global::System.Threading.Tasks.ValueTask<" +
                command.ResultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) +
                ">");
        }

        return CSharpSyntaxFactory.ParseTypeName("global::System.Threading.Tasks.ValueTask");
    }

    private static string ToCSharpIdentifier(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "_";
        }

        var builder = new StringBuilder(value.Length);
        for (var index = 0; index < value.Length; index++)
        {
            var ch = value[index];
            builder.Append(index == 0
                ? Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierStartCharacter(ch) ? ch : '_'
                : Microsoft.CodeAnalysis.CSharp.SyntaxFacts.IsIdentifierPartCharacter(ch) ? ch : '_');
        }

        return builder.ToString();
    }

    private static CSharp.FieldDeclarationSyntax CreateProbeField(
        CSharp.TypeSyntax type,
        string name)
    {
        return CSharpSyntaxFactory.FieldDeclaration(
                CSharpSyntaxFactory.VariableDeclaration(type)
                    .WithVariables(CSharpSyntaxFactory.SingletonSeparatedList(
                        CSharpSyntaxFactory.VariableDeclarator(
                            CSharpSyntaxFactory.Identifier(name)))))
            .WithModifiers(CSharpSyntaxFactory.TokenList(
                CSharpSyntaxFactory.Token(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PrivateKeyword)));
    }

    internal static string GetTailwindUtilityName(TailwindAttributeSyntax attribute)
    {
        return attribute.Kind switch
        {
            AkburaSyntaxKind.TailwindFlagAttributeSyntax => Unsafe.As<TailwindFlagAttributeSyntax>(attribute).Name.Identifier.ValueText,
            AkburaSyntaxKind.TailwindFullAttributeSyntax => Unsafe.As<TailwindFullAttributeSyntax>(attribute).Name.Identifier.ValueText,
            _ => string.Empty,
        };
    }

    internal AkburaSemanticDiagnostic CreateTailwindUtilityNotFoundDiagnostic(
        TailwindAttributeSyntax syntax,
        string utilityName,
        IMarkupComponentSymbol? componentSymbol)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityNotFound,
            [utilityName, componentSymbol?.Name ?? "<unknown>"]);
    }

    internal AkburaSemanticDiagnostic CreateTailwindUtilityAmbiguousDiagnostic(
        TailwindAttributeSyntax syntax,
        string utilityName,
        IMarkupComponentSymbol? componentSymbol)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityAmbiguous,
            [utilityName, componentSymbol?.Name ?? "<unknown>"]);
    }

    internal AkburaSemanticDiagnostic CreateTailwindUtilityArgumentMismatchDiagnostic(
        TailwindAttributeSyntax syntax,
        ITailwindUtilitySymbol utility,
        int actualCount)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_TailwindUtilityArgumentMismatch,
            [utility.Name, utility.Parameters.Length, actualCount]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupExpressionErrorDiagnostic(
        MarkupAttributeSyntax syntax,
        Diagnostic diagnostic)
    {
        return CreateMarkupExpressionErrorDiagnostic(
            syntax,
            GetMarkupExpressionDiagnosticText(syntax),
            diagnostic);
    }

    private static string GetMarkupExpressionDiagnosticText(MarkupAttributeSyntax syntax)
    {
        var valueSyntax = GetMarkupAttributeValue(syntax);
        return valueSyntax == null
            ? syntax.ToFullString().Trim()
            : valueSyntax.ToFullString().Trim();
    }

    private static AkburaSemanticDiagnostic CreateMarkupExpressionErrorDiagnostic(
        AkburaSyntax syntax,
        string expressionText,
        Diagnostic diagnostic)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError,
            [expressionText, diagnostic.GetMessage()]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupExtensionErrorDiagnostic(
        AkburaSyntax syntax,
        string expressionText,
        string message)
    {
        return new AkburaSemanticDiagnostic(
            syntax,
            ErrorCodes.AKBURA_SEMANTIC_MarkupExpressionError,
            ["${" + expressionText + "}", message]);
    }

    private static AkburaSemanticDiagnostic CreateMarkupCommandHandlerSignatureMismatchDiagnostic(
        MarkupAttributeSyntax markupAttribute,
        ICommandSymbol command,
        string expected,
        string actual)
    {
        return new AkburaSemanticDiagnostic(
            markupAttribute,
            ErrorCodes.AKBURA_SEMANTIC_MarkupCommandHandlerSignatureMismatch,
            [command.Name, expected, actual]);
    }

    internal AkburaSemanticDiagnostic CreateAkcssImportNotFoundDiagnostic(string importName)
    {
        return new AkburaSemanticDiagnostic(
            SyntaxTree.GetRoot(),
            ErrorCodes.AKBURA_SEMANTIC_AkcssImportNotFound,
            [importName]);
    }

    internal readonly struct MarkupCommandHandlerAnalysis
    {
        public static MarkupCommandHandlerAnalysis Error { get; } = new(
            MarkupCommandHandlerKind.Error,
            MarkupCommandArgumentMode.None,
            MarkupCommandResultMode.Unknown,
            parameterCount: 0,
            isAsync: false,
            containsAwait: false,
            type: default,
            resultType: default,
            operation: default,
            diagnostics: ImmutableArray<Diagnostic>.Empty);

        public MarkupCommandHandlerAnalysis(
            MarkupCommandHandlerKind kind,
            MarkupCommandArgumentMode argumentMode,
            MarkupCommandResultMode resultMode,
            int parameterCount,
            bool isAsync,
            bool containsAwait,
            CSharpSymbolDefinition type,
            CSharpSymbolDefinition resultType,
            CSharpOperationDefinition operation,
            ImmutableArray<Diagnostic> diagnostics = default)
        {
            Kind = kind;
            ArgumentMode = argumentMode;
            ResultMode = resultMode;
            ParameterCount = parameterCount;
            IsAsync = isAsync;
            ContainsAwait = containsAwait;
            Type = type;
            ResultType = resultType;
            Operation = operation;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<Diagnostic>.Empty
                : diagnostics;
        }

        public MarkupCommandHandlerKind Kind { get; }

        public MarkupCommandArgumentMode ArgumentMode { get; }

        public MarkupCommandResultMode ResultMode { get; }

        public int ParameterCount { get; }

        public bool IsAsync { get; }

        public bool ContainsAwait { get; }

        public CSharpSymbolDefinition Type { get; }

        public CSharpSymbolDefinition ResultType { get; }

        public CSharpOperationDefinition Operation { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }

    internal readonly struct MarkupEventHandlerAnalysis
    {
        public static MarkupEventHandlerAnalysis Error { get; } = new(
            MarkupCommandHandlerKind.Error,
            MarkupCommandArgumentMode.None,
            parameterCount: 0,
            isAsync: false,
            containsAwait: false,
            operation: default,
            diagnostics: ImmutableArray<Diagnostic>.Empty);

        public MarkupEventHandlerAnalysis(
            MarkupCommandHandlerKind kind,
            MarkupCommandArgumentMode argumentMode,
            int parameterCount,
            bool isAsync,
            bool containsAwait,
            CSharpOperationDefinition operation,
            ImmutableArray<Diagnostic> diagnostics)
        {
            Kind = kind;
            ArgumentMode = argumentMode;
            ParameterCount = parameterCount;
            IsAsync = isAsync;
            ContainsAwait = containsAwait;
            Operation = operation;
            Diagnostics = diagnostics.IsDefault
                ? ImmutableArray<Diagnostic>.Empty
                : diagnostics;
        }

        public MarkupCommandHandlerKind Kind { get; }

        public MarkupCommandArgumentMode ArgumentMode { get; }

        public int ParameterCount { get; }

        public bool IsAsync { get; }

        public bool ContainsAwait { get; }

        public CSharpOperationDefinition Operation { get; }

        public ImmutableArray<Diagnostic> Diagnostics { get; }
    }
}
