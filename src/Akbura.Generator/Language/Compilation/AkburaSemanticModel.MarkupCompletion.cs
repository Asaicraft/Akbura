using Akbura.Language.Binder;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynSymbol = Microsoft.CodeAnalysis.ISymbol;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private ImmutableDictionary<
        string,
        ImmutableArray<MarkupAttachedPropertyLookupCandidate>>
        _completionMarkupAttachedProperties =
            ImmutableDictionary.Create<
                string,
                ImmutableArray<MarkupAttachedPropertyLookupCandidate>>(
                    StringComparer.Ordinal);

    internal ImmutableArray<MarkupAttachedPropertyLookupCandidate> LookupMarkupAttachedPropertiesForCompletion(string componentName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return [];
        }

        componentName = componentName.Trim();
        var snapshot = Volatile.Read(
            ref _completionMarkupAttachedProperties);
        if (snapshot.TryGetValue(componentName, out var cached))
        {
            return cached;
        }

        var computed = ComputeMarkupAttachedPropertiesForCompletion(
            componentName,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        return ImmutableInterlocked.GetOrAdd(
            ref _completionMarkupAttachedProperties,
            componentName,
            computed);
    }

    private ImmutableArray<MarkupAttachedPropertyLookupCandidate> ComputeMarkupAttachedPropertiesForCompletion(string componentName, CancellationToken cancellationToken)
    {
        if (!TryResolveMarkupComponentForCompletion(
                componentName,
                out var target))
        {
            return [];
        }

        var compilation = Compilation.CSharpProbeCompilation;
        var visibleOwners = new Dictionary<
            string,
            INamedTypeSymbol?>(StringComparer.Ordinal);

        foreach (var visibleNamespace in GetVisibleMarkupNamespaces())
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceSymbol = GetNamespaceSymbol(
                compilation.GlobalNamespace,
                visibleNamespace.Name);
            if (namespaceSymbol == null)
            {
                continue;
            }

            foreach (var ownerType in namespaceSymbol.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (ownerType.Arity != 0 ||
                    !compilation.IsSymbolAccessibleWithin(
                        ownerType,
                        compilation.Assembly))
                {
                    continue;
                }

                var attachedNames = new HashSet<string>(
                    StringComparer.Ordinal);
                AddMarkupAttachedPropertyNames(
                    ownerType,
                    attachedNames);
                if (attachedNames.Count == 0)
                {
                    continue;
                }

                var ownerReference = visibleNamespace.Alias == null
                    ? ownerType.Name
                    : visibleNamespace.Alias + "::" + ownerType.Name;
                if (!visibleOwners.TryGetValue(
                        ownerReference,
                        out var existingOwner))
                {
                    visibleOwners.Add(ownerReference, ownerType);
                }
                else if (existingOwner != null &&
                    !SymbolEqualityComparer.Default.Equals(
                        existingOwner,
                        ownerType))
                {
                    visibleOwners[ownerReference] = null;
                }
            }
        }

        using var candidates =
            ImmutableArrayBuilder<
                MarkupAttachedPropertyLookupCandidate>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var pair in visibleOwners.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownerType = pair.Value;
            if (ownerType == null)
            {
                continue;
            }

            var attachedNames = new HashSet<string>(
                StringComparer.Ordinal);
            AddMarkupAttachedPropertyNames(ownerType, attachedNames);

            foreach (var propertyName in attachedNames.OrderBy(static name => name, StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryCreateAttachedPropertySymbol(
                        ownerType,
                        propertyName,
                        target.ComponentType,
                        SymbolLanguage.Markup,
                        target,
                        out var property) ||
                    !property.CanWrite)
                {
                    continue;
                }

                var displayName = pair.Key + "." + propertyName;
                if (!seen.Add(displayName))
                {
                    continue;
                }

                var definitionOwner = property.WriteDefinition.Symbol
                        ?.ContainingType ??
                    property.ReadDefinition.Symbol?.ContainingType ??
                    ownerType;
                candidates.Add(
                    new MarkupAttachedPropertyLookupCandidate(
                        displayName,
                        definitionOwner.ToDisplayString(
                            SymbolDisplayFormat
                                .MinimallyQualifiedFormat),
                        property.Type.ToDisplayString(
                            SymbolDisplayFormat
                                .MinimallyQualifiedFormat),
                        property));
            }
        }

        return candidates.ToImmutable();
    }

    private void AddMarkupAttachedPropertyNames(INamedTypeSymbol ownerType, HashSet<string> names)
    {
        const string propertySuffix = "Property";

        foreach (var field in ownerType.GetMembers().OfType<RoslynFieldSymbol>())
        {
            if (!field.IsStatic ||
                field.DeclaredAccessibility != Accessibility.Public ||
                !IsAttachedPropertyType(field.Type))
            {
                continue;
            }

            var name = field.Name.EndsWith(
                    propertySuffix,
                    StringComparison.Ordinal) &&
                field.Name.Length > propertySuffix.Length
                    ? field.Name[..^propertySuffix.Length]
                    : field.Name;
            names.Add(name);
        }
    }

    internal ImmutableArray<MarkupBindingPathCompletionCandidate> LookupMarkupBindingPathMembersForCompletion(MarkupAttributeSyntax attribute, MarkupExtensionSyntax extension, string completedPath, out bool receiverResolved, CancellationToken cancellationToken = default)
    {
        ValidateMarkupCompletionSyntax(attribute, extension);
        receiverResolved = TryGetMarkupBindingPathCompletionType(
                attribute,
                extension,
                completedPath,
                out var receiverType);
        if (!receiverResolved)
        {
            return [];
        }

        using var items =
            ImmutableArrayBuilder<MarkupBindingPathCompletionCandidate>
                .Rent();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var type in EnumerateBindingMemberTypes(receiverType))
        {
            foreach (var member in type.GetMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                switch (member)
                {
                    case Microsoft.CodeAnalysis.IPropertySymbol property
                        when !property.IsStatic &&
                             !property.IsIndexer &&
                             property.DeclaredAccessibility ==
                                 Accessibility.Public &&
                             property.GetMethod?.DeclaredAccessibility ==
                                 Accessibility.Public &&
                             names.Add(property.Name):
                        items.Add(
                            new MarkupBindingPathCompletionCandidate(
                                property.Name,
                                property,
                                property.Type));
                        break;

                    case IFieldSymbol field
                        when !field.IsStatic &&
                             field.DeclaredAccessibility ==
                                 Accessibility.Public &&
                             names.Add(field.Name):
                        items.Add(
                            new MarkupBindingPathCompletionCandidate(
                                field.Name,
                                field,
                                field.Type));
                        break;
                }
            }
        }

        return items.ToImmutable();
    }

    internal ImmutableArray<MarkupBindingPathRootCompletionCandidate> LookupMarkupBindingPathRootsForCompletion(MarkupAttributeSyntax attribute, CancellationToken cancellationToken = default)
    {
        if (attribute == null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        ValidateSyntaxTreeOwnership(attribute);
        using var items =
            ImmutableArrayBuilder<MarkupBindingPathRootCompletionCandidate>
                .Rent();
        items.Add(new MarkupBindingPathRootCompletionCandidate(
            "$self",
            GetContainingMarkupComponentSymbol(attribute)?.ComponentType));
        items.Add(new MarkupBindingPathRootCompletionCandidate(
            "$parent",
            type: null));
        items.Add(new MarkupBindingPathRootCompletionCandidate(
            "$templatedParent",
            type: null));

        var root = FindMarkupRoot(attribute);
        if (root == null)
        {
            return items.ToImmutable();
        }

        var scope = BindingSession.GetMarkupNameScope(root);
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in scope.Declarations)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!declaration.IsValid ||
                !names.Add(declaration.Name) ||
                !scope.TryGetVisibleDeclaredSymbol(
                    this,
                    attribute,
                    declaration.Name,
                    out var symbol))
            {
                continue;
            }

            items.Add(new MarkupBindingPathRootCompletionCandidate(
                "#" + symbol.Name,
                symbol.Type.Symbol as ITypeSymbol));
        }

        return items.ToImmutable();
    }

    internal ImmutableArray<MarkupExtensionArgumentCompletionCandidate> LookupMarkupExtensionArgumentsForCompletion(MarkupExtensionSyntax extension, CancellationToken cancellationToken = default)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        ValidateSyntaxTreeOwnership(extension);
        if (!TryGetMarkupExtensionCompletionType(
                extension,
                out var extensionType,
                out var bindingKind))
        {
            return [];
        }

        var existing = new HashSet<string>(StringComparer.Ordinal);
        foreach (var argument in extension.Arguments)
        {
            if (argument is MarkupExtensionPropertyArgumentSyntax property)
            {
                existing.Add(property.Name.Identifier.ValueText);
            }
        }

        using var items =
            ImmutableArrayBuilder<MarkupExtensionArgumentCompletionCandidate>
                .Rent();
        var names = new HashSet<string>(StringComparer.Ordinal);
        for (var current = extensionType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers().OfType<Microsoft.CodeAnalysis.IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (property.IsStatic ||
                    property.IsIndexer ||
                    property.DeclaredAccessibility !=
                        Accessibility.Public ||
                    property.SetMethod?.DeclaredAccessibility !=
                        Accessibility.Public ||
                    existing.Contains(property.Name) ||
                    !names.Add(property.Name))
                {
                    continue;
                }

                items.Add(
                    new MarkupExtensionArgumentCompletionCandidate(
                        property.Name,
                        property.Type,
                        property));
            }
        }

        if (bindingKind != null)
        {
            var stringType = Compilation.CSharpCompilation
                .GetSpecialType(SpecialType.System_String);
            if (!existing.Contains("Path") && names.Add("Path"))
            {
                items.Add(
                    new MarkupExtensionArgumentCompletionCandidate(
                        "Path",
                        stringType,
                        symbol: null));
            }

            if (bindingKind == MarkupBindingKind.Compiled)
            {
                if (!existing.Contains("ElementName") &&
                    names.Add("ElementName"))
                {
                    items.Add(
                        new MarkupExtensionArgumentCompletionCandidate(
                            "ElementName",
                            stringType,
                            symbol: null));
                }
            }
        }

        return items.ToImmutable();
    }

    internal ITypeSymbol? GetMarkupExtensionArgumentValueTypeForCompletion(MarkupExtensionSyntax extension, string argumentName, CancellationToken cancellationToken = default)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        ValidateSyntaxTreeOwnership(extension);
        if (!TryGetMarkupExtensionCompletionType(
                extension,
                out var extensionType,
                out var bindingKind))
        {
            return null;
        }

        if (bindingKind != null &&
            (string.Equals(
                 argumentName,
                 "Path",
                 StringComparison.Ordinal) ||
             bindingKind == MarkupBindingKind.Compiled &&
             string.Equals(
                 argumentName,
                 "ElementName",
                 StringComparison.Ordinal)))
        {
            return Compilation.CSharpCompilation.GetSpecialType(
                SpecialType.System_String);
        }

        for (var current = extensionType; current != null; current = current.BaseType)
        {
            foreach (var property in current.GetMembers(argumentName).OfType<Microsoft.CodeAnalysis.IPropertySymbol>())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!property.IsStatic &&
                    !property.IsIndexer &&
                    property.DeclaredAccessibility ==
                        Accessibility.Public &&
                    property.SetMethod?.DeclaredAccessibility ==
                        Accessibility.Public)
                {
                    return property.Type;
                }
            }
        }

        return null;
    }

    private bool TryGetMarkupBindingPathCompletionType(MarkupAttributeSyntax attribute, MarkupExtensionSyntax extension, string completedPath, out ITypeSymbol receiverType)
    {
        receiverType = null!;
        var extensionName = GetMarkupExtensionTypeName(extension.Type);
        var bindingName = GetUnqualifiedMarkupExtensionName(
            extensionName);
        if (!IsAvaloniaBindingExtensionName(bindingName))
        {
            return false;
        }

        var hasDataType = BindingSession.MarkupDataTypes
            .TryGetDataType(attribute, out var dataType);
        var kind = GetMarkupBindingKind(bindingName, hasDataType);
        ITypeSymbol? sourceType = hasDataType
            ? dataType
            : null;

        if (TryGetMarkupExtensionPropertyArgument(
                extension,
                "Source",
                out _))
        {
            sourceType = TryGetExplicitBindingSourceType(
                attribute,
                extension,
                out var explicitSourceType)
                    ? explicitSourceType
                    : null;
        }
        else if (TryGetMarkupExtensionPropertyArgument(
                extension,
                "RelativeSource",
                out var relativeSource))
        {
            sourceType = TryGetRelativeBindingSourceType(
                attribute,
                relativeSource,
                out var relativeSourceType)
                    ? relativeSourceType
                    : null;
        }
        else if (TryGetBindingElementName(
                extension,
                out var elementName) &&
            TryGetMarkupBindingElementNameType(
                attribute,
                elementName,
                out var elementType))
        {
            sourceType = elementType;
        }

        var receiverPath = completedPath.TrimEnd();
        while (receiverPath.EndsWith(
                   "!",
                   StringComparison.Ordinal))
        {
            receiverPath = receiverPath[..^1].TrimEnd();
        }

        if (receiverPath.EndsWith(
                "?.",
                StringComparison.Ordinal))
        {
            receiverPath = receiverPath[..^2].TrimEnd();
        }
        else if (receiverPath.EndsWith(
                     ".",
                     StringComparison.Ordinal))
        {
            receiverPath = receiverPath[..^1].TrimEnd();
        }

        if (TryGetUntypedParentBindingPathCompletionType(
                attribute,
                receiverPath,
                out var parentType,
                out var parentPath))
        {
            if (parentPath.Length == 0)
            {
                receiverType = parentType;
                return true;
            }

            using var parentDiagnostics =
                ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
            BindMarkupBindingPath(
                attribute,
                parentPath,
                kind,
                parentType,
                parentDiagnostics,
                out var parentResultType);
            receiverType = parentResultType!;
            return receiverType != null &&
                receiverType.TypeKind != TypeKind.Error;
        }

        if (receiverPath.Length == 0)
        {
            receiverType = sourceType!;
            return receiverType != null &&
                receiverType.TypeKind != TypeKind.Error;
        }

        using var diagnostics =
            ImmutableArrayBuilder<AkburaSemanticDiagnostic>.Rent();
        BindMarkupBindingPath(
            attribute,
            receiverPath,
            kind,
            sourceType as INamedTypeSymbol,
            diagnostics,
            out var resultType);
        receiverType = resultType!;
        return receiverType != null &&
            receiverType.TypeKind != TypeKind.Error;
    }

    private bool TryGetUntypedParentBindingPathCompletionType(MarkupAttributeSyntax attribute, string path, out INamedTypeSymbol parentType, out string remainingPath)
    {
        const string parentRoot = "$parent";
        if (!string.Equals(
                path,
                parentRoot,
                StringComparison.Ordinal) &&
            !path.StartsWith(
                parentRoot + ".",
                StringComparison.Ordinal))
        {
            parentType = null!;
            remainingPath = string.Empty;
            return false;
        }

        remainingPath = path.Length == parentRoot.Length
            ? string.Empty
            : path[(parentRoot.Length + 1)..];
        var foundCurrentElement = false;
        for (var current = attribute.Parent; current != null; current = current.Parent)
        {
            if (current is not MarkupElementSyntax element)
            {
                continue;
            }

            if (!foundCurrentElement)
            {
                foundCurrentElement = true;
                continue;
            }

            if (GetSymbolInfo(element).Symbol is
                IMarkupComponentSymbol
                {
                    ComponentType: { } componentType,
                })
            {
                parentType = componentType;
                return true;
            }
        }

        parentType = null!;
        remainingPath = string.Empty;
        return false;
    }

    private bool TryGetExplicitBindingSourceType(MarkupAttributeSyntax attribute, MarkupExtensionSyntax extension, out ITypeSymbol sourceType)
    {
        var property = GetSymbolInfo(attribute).Symbol
            as Symbols.IPropertySymbol;
        var binding = BindMarkupExtensionAttributeValue(
            attribute,
            extension,
            property);
        foreach (var candidate in binding.Value?.Properties ?? [])
        {
            if (!string.Equals(
                    candidate.Name,
                    "Source",
                    StringComparison.Ordinal))
            {
                continue;
            }

            var type = candidate.Conversion.SourceType ??
                candidate.Type.Symbol as ITypeSymbol;
            if (type == null ||
                type.TypeKind == TypeKind.Error ||
                type.SpecialType == SpecialType.System_Object)
            {
                continue;
            }

            sourceType = type;
            return true;
        }

        sourceType = null!;
        return false;
    }

    private bool TryGetRelativeBindingSourceType(MarkupAttributeSyntax attribute, MarkupExtensionPropertyArgumentSyntax relativeSource, out ITypeSymbol sourceType)
    {
        if (!TryGetRelativeSourceExtension(
                relativeSource.Value,
                out var extension,
                out var mode))
        {
            sourceType = null!;
            return false;
        }

        if (string.Equals(
                mode,
                "Self",
                StringComparison.Ordinal))
        {
            for (var current = attribute.Parent; current != null; current = current.Parent)
            {
                if (current is MarkupElementSyntax element &&
                    GetSymbolInfo(element).Symbol is
                        IMarkupComponentSymbol
                    {
                        ComponentType: { } elementType,
                    })
                {
                    sourceType = elementType;
                    return true;
                }
            }
        }
        else if (string.Equals(
                     mode,
                     "FindAncestor",
                     StringComparison.Ordinal) &&
                 extension != null &&
                 TryGetMarkupExtensionPropertyArgument(
                     extension,
                     "AncestorType",
                     out var ancestorTypeArgument) &&
                 ancestorTypeArgument.Value is
                     MarkupExtensionLiteralValueSyntax ancestorTypeLiteral &&
                 TryBindMarkupDataType(
                     GetMarkupExtensionLiteralText(
                         ancestorTypeLiteral),
                     out var ancestorType))
        {
            sourceType = ancestorType;
            return true;
        }

        sourceType = null!;
        return false;
    }

    private static bool TryGetRelativeSourceExtension(MarkupExtensionValueSyntax value, out MarkupExtensionSyntax? extension, out string mode)
    {
        extension = null;
        if (value is MarkupExtensionLiteralValueSyntax literal)
        {
            mode = GetMarkupExtensionLiteralText(literal);
            return mode.Length > 0;
        }

        if (value is not MarkupExtensionNestedValueSyntax nested ||
            !string.Equals(
                GetUnqualifiedMarkupExtensionName(
                    GetMarkupExtensionTypeName(nested.Extension.Type)),
                "RelativeSource",
                StringComparison.Ordinal))
        {
            mode = string.Empty;
            return false;
        }

        extension = nested.Extension;
        foreach (var argument in extension.Arguments)
        {
            if (argument is
                    MarkupExtensionPositionalArgumentSyntax positional &&
                positional.Value is
                    MarkupExtensionLiteralValueSyntax modeLiteral)
            {
                mode = GetMarkupExtensionLiteralText(modeLiteral);
                return mode.Length > 0;
            }
        }

        mode = string.Empty;
        return false;
    }

    internal bool IsAvaloniaResourceMarkupExtensionForCompletion(MarkupExtensionSyntax extension)
    {
        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        ValidateSyntaxTreeOwnership(extension);
        var extensionName = GetMarkupExtensionTypeName(extension.Type);
        if (!TryResolveMarkupExtensionType(
                extensionName,
                out var extensionType,
                out _))
        {
            return false;
        }

        return IsAvaloniaResourceMarkupExtensionTypeForCompletion(
            extensionType);
    }

    internal bool IsAvaloniaResourceMarkupExtensionTypeForCompletion(INamedTypeSymbol extensionType)
    {
        if (extensionType == null)
        {
            throw new ArgumentNullException(nameof(extensionType));
        }

        return IsCanonicalResourceExtension(
                "Avalonia.Markup.Xaml.MarkupExtensions." +
                "StaticResourceExtension") ||
            IsCanonicalResourceExtension(
                "Avalonia.Markup.Xaml.MarkupExtensions." +
                "DynamicResourceExtension");

        bool IsCanonicalResourceExtension(string metadataName)
        {
            var canonicalType = Compilation.CSharpCompilation
                .GetTypeByMetadataName(metadataName);
            if (canonicalType == null ||
                !string.Equals(
                    canonicalType.ContainingAssembly.Identity.Name,
                    "Avalonia.Markup.Xaml",
                    StringComparison.Ordinal))
            {
                return false;
            }

            if (SymbolEqualityComparer.Default.Equals(
                    extensionType,
                    canonicalType))
            {
                return true;
            }

            // Each markup probe adds a transient syntax tree to the probe
            // compilation. Roslyn does not consider a source symbol from that
            // transient compilation equal to the owning compilation's symbol,
            // so compare the actual source declarations as the fallback.
            return extensionType.ContainingType == null &&
                extensionType.ContainingAssembly.Identity.Equals(
                    canonicalType.ContainingAssembly.Identity) &&
                string.Equals(
                    GetTopLevelMetadataName(extensionType),
                    metadataName,
                    StringComparison.Ordinal) &&
                HasSameSourceDeclarations(
                    extensionType,
                    canonicalType);
        }

        static string GetTopLevelMetadataName(INamedTypeSymbol type)
        {
            var namespaceName = type.ContainingNamespace.ToDisplayString();
            return namespaceName.Length == 0
                ? type.MetadataName
                : namespaceName + "." + type.MetadataName;
        }

        static bool HasSameSourceDeclarations(INamedTypeSymbol type, INamedTypeSymbol canonicalType)
        {
            var declarations = type.DeclaringSyntaxReferences;
            var canonicalDeclarations =
                canonicalType.DeclaringSyntaxReferences;
            if (declarations.IsDefaultOrEmpty ||
                declarations.Length != canonicalDeclarations.Length)
            {
                return false;
            }

            foreach (var declaration in declarations)
            {
                var matches = false;
                foreach (var canonicalDeclaration in canonicalDeclarations)
                {
                    if (ReferenceEquals(
                            declaration.SyntaxTree,
                            canonicalDeclaration.SyntaxTree) &&
                        declaration.Span == canonicalDeclaration.Span)
                    {
                        matches = true;
                        break;
                    }
                }

                if (!matches)
                {
                    return false;
                }
            }

            return true;
        }
    }

    private bool TryGetMarkupExtensionCompletionType(MarkupExtensionSyntax extension, out INamedTypeSymbol extensionType, out MarkupBindingKind? bindingKind)
    {
        var extensionName = GetMarkupExtensionTypeName(extension.Type);
        var bindingName = GetUnqualifiedMarkupExtensionName(
            extensionName);
        if (IsAvaloniaBindingExtensionName(bindingName))
        {
            var hasDataType = false;
            for (var current = extension.Parent; current != null; current = current.Parent)
            {
                if (current is MarkupAttributeSyntax attribute)
                {
                    hasDataType = BindingSession.MarkupDataTypes
                        .TryGetDataType(attribute, out _);
                    break;
                }
            }

            bindingKind = GetMarkupBindingKind(
                bindingName,
                hasDataType);
            var metadataName = bindingKind ==
                    MarkupBindingKind.Compiled
                ? "Avalonia.Data.CompiledBinding"
                : bindingName is
                    "ReflectionBinding" or
                    "ReflectionBindingExtension"
                    ? "Avalonia.Data.ReflectionBinding"
                    : "Avalonia.Data.Binding";
            extensionType = Compilation.CSharpCompilation
                .GetTypeByMetadataName(metadataName)!;
            return extensionType != null;
        }

        bindingKind = null;
        return TryResolveMarkupExtensionType(
            extensionName,
            out extensionType,
            out _);
    }

    private static bool TryGetBindingElementName(MarkupExtensionSyntax extension, out string elementName)
    {
        if (TryGetMarkupExtensionPropertyArgument(
                extension,
                "ElementName",
                out var property) &&
            property.Value is
                MarkupExtensionLiteralValueSyntax literal)
        {
            elementName = GetMarkupExtensionLiteralText(literal);
            return elementName.Length > 0;
        }

        elementName = string.Empty;
        return false;
    }

    private static bool TryGetMarkupExtensionPropertyArgument(MarkupExtensionSyntax extension, string name, out MarkupExtensionPropertyArgumentSyntax property)
    {
        foreach (var argument in extension.Arguments)
        {
            if (argument is
                    MarkupExtensionPropertyArgumentSyntax candidate &&
                string.Equals(
                    candidate.Name.Identifier.ValueText,
                    name,
                    StringComparison.Ordinal))
            {
                property = candidate;
                return true;
            }
        }

        property = null!;
        return false;
    }

    private static MarkupRootSyntax? FindMarkupRoot(AkburaSyntax syntax)
    {
        for (var current = syntax.Parent; current != null; current = current.Parent)
        {
            if (current is MarkupRootSyntax root)
            {
                return root;
            }
        }

        return null;
    }

    private void ValidateMarkupCompletionSyntax(MarkupAttributeSyntax attribute, MarkupExtensionSyntax extension)
    {
        if (attribute == null)
        {
            throw new ArgumentNullException(nameof(attribute));
        }

        if (extension == null)
        {
            throw new ArgumentNullException(nameof(extension));
        }

        ValidateSyntaxTreeOwnership(attribute);
        ValidateSyntaxTreeOwnership(extension);
    }
}

internal readonly struct MarkupBindingPathCompletionCandidate
{
    internal MarkupBindingPathCompletionCandidate(string name, RoslynSymbol symbol, ITypeSymbol type)
    {
        Name = name;
        Symbol = symbol;
        Type = type;
    }

    internal string Name { get; }

    internal RoslynSymbol Symbol { get; }

    internal ITypeSymbol Type { get; }
}

internal readonly struct MarkupBindingPathRootCompletionCandidate
{
    internal MarkupBindingPathRootCompletionCandidate(string name, ITypeSymbol? type)
    {
        Name = name;
        Type = type;
    }

    internal string Name { get; }

    internal ITypeSymbol? Type { get; }
}

internal readonly struct MarkupExtensionArgumentCompletionCandidate
{
    internal MarkupExtensionArgumentCompletionCandidate(string name, ITypeSymbol type, RoslynSymbol? symbol)
    {
        Name = name;
        Type = type;
        Symbol = symbol;
    }

    internal string Name { get; }

    internal ITypeSymbol Type { get; }

    internal RoslynSymbol? Symbol { get; }
}
