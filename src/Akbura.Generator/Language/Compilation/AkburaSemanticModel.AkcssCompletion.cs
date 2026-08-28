using Akbura.Language.Symbols;
using Akbura.Language.Syntax;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;
using RoslynPropertySymbol = Microsoft.CodeAnalysis.IPropertySymbol;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private ImmutableDictionary<AkcssPropertyCompletionKey, ImmutableArray<AkcssPropertyLookupCandidate>> _completionAkcssProperties =
        ImmutableDictionary<AkcssPropertyCompletionKey, ImmutableArray<AkcssPropertyLookupCandidate>>.Empty;

    private ImmutableDictionary<TextSpan, ImmutableArray<AkcssApplyLookupCandidate>> _completionAkcssApplyItems =
        ImmutableDictionary<TextSpan, ImmutableArray<AkcssApplyLookupCandidate>>.Empty;

    internal ImmutableArray<AkcssPropertyLookupCandidate>
        LookupAkcssPropertiesForCompletion(
            TextSpan containingDeclarationSpan,
            string qualifier,
            CancellationToken cancellationToken = default)
    {
        return LookupAkcssPropertiesForCompletion(
            containingDeclarationSpan,
            qualifier,
            requireReadable: false,
            cancellationToken);
    }

    internal ImmutableArray<AkcssPropertyLookupCandidate>
        LookupReadableAkcssPropertiesForCompletion(
            TextSpan containingDeclarationSpan,
            string qualifier,
            CancellationToken cancellationToken = default)
    {
        return LookupAkcssPropertiesForCompletion(
            containingDeclarationSpan,
            qualifier,
            requireReadable: true,
            cancellationToken);
    }

    private ImmutableArray<AkcssPropertyLookupCandidate> LookupAkcssPropertiesForCompletion(
        TextSpan containingDeclarationSpan,
        string qualifier,
        bool requireReadable,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var declaration = FindAkcssCompletionDeclaration(
            containingDeclarationSpan);

        if (declaration == null)
        {
            return [];
        }

        var key = new AkcssPropertyCompletionKey(
            declaration.FullSpan,
            qualifier,
            requireReadable);

        var snapshot = Volatile.Read(ref _completionAkcssProperties);

        if (snapshot.TryGetValue(
                key,
                out var cached))
        {
            return cached;
        }

        var candidates =
            ComputeAkcssPropertiesForCompletion(
                declaration,
                qualifier,
                requireReadable,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return ImmutableInterlocked.GetOrAdd(
            ref _completionAkcssProperties,
            key,
            candidates);
    }

    internal bool TryGetAkcssValueCompletionInfo(
        TextSpan containingDeclarationSpan,
        string propertyReference,
        out AkcssValueCompletionInfo info)
    {
        info = default;
        var declaration = FindAkcssCompletionDeclaration(
            containingDeclarationSpan);
        if (declaration == null ||
            string.IsNullOrWhiteSpace(propertyReference) ||
            !TryGetAkcssCompletionContainingSymbol(
                declaration,
                out var containingSymbol))
        {
            return false;
        }

        propertyReference = propertyReference.Trim();
        var separator = propertyReference.LastIndexOf('.');
        var qualifier = separator > 0
            ? propertyReference[..separator]
            : string.Empty;
        var propertyName = separator >= 0
            ? propertyReference[(separator + 1)..]
            : propertyReference;
        if (propertyName.Length == 0 ||
            !TryGetAkcssCompletionOwnerType(
                declaration,
                containingSymbol,
                qualifier,
                out var ownerType))
        {
            return false;
        }

        var appliedTargetType =
            containingSymbol.TargetType.Symbol as ITypeSymbol;
        if (appliedTargetType == null &&
            TryGetDefaultAkcssStyleTargetType(
                out var defaultTargetType))
        {
            appliedTargetType = defaultTargetType;
        }

        if (!TryCreateAkcssCompletionProperty(
                ownerType,
                propertyName,
                appliedTargetType,
                containingSymbol,
                preferAttached: qualifier.Length > 0,
                out var property) ||
            !property.CanWrite)
        {
            return false;
        }

        info = new AkcssValueCompletionInfo(
            containingSymbol,
            property);
        return true;
    }

    internal ImmutableArray<AkcssExpectedValueLookupCandidate>
        LookupAkcssExpectedValuesForCompletion(
            AkcssValueCompletionInfo info,
            CancellationToken cancellationToken = default)
    {
        if (info.ExpectedType is not { } expectedType ||
            !TryGetStaticMemberOwnerType(
                expectedType,
                out var ownerType))
        {
            return ImmutableArray<AkcssExpectedValueLookupCandidate>.Empty;
        }

        using var names = ImmutableArrayBuilder<string>.Rent();
        AddAkcssExpectedValueMemberNames(ownerType, names);
        if (TryGetCompanionStaticMemberOwnerType(
                ownerType,
                out var companionOwnerType))
        {
            AddAkcssExpectedValueMemberNames(
                companionOwnerType,
                names);
        }

        using var candidates =
            ImmutableArrayBuilder<AkcssExpectedValueLookupCandidate>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in names.WrittenSpan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!seen.Add(name))
            {
                continue;
            }

            var expression = Microsoft.CodeAnalysis.CSharp.SyntaxFactory
                .IdentifierName(name);
            if (!TryBindExpectedTypeStaticMember(
                    expression,
                    expectedType,
                    info.ContainingSymbol,
                    out var binding) ||
                binding.Symbol == null)
            {
                continue;
            }

            candidates.Add(new AkcssExpectedValueLookupCandidate(
                name,
                name,
                expectedType.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat),
                binding.Symbol));
        }

        return candidates.ToImmutable();
    }

    internal ImmutableArray<AkcssExpectedValueLookupCandidate>
        LookupAkcssNamedColorsForCompletion(
            IAkcssSymbol containingSymbol,
            CancellationToken cancellationToken = default)
    {
        var colorsType = Compilation.CSharpCompilation
            .GetTypeByMetadataName("Avalonia.Media.Colors");
        if (colorsType == null)
        {
            return ImmutableArray<AkcssExpectedValueLookupCandidate>.Empty;
        }

        using var candidates =
            ImmutableArrayBuilder<AkcssExpectedValueLookupCandidate>.Rent();
        foreach (var member in colorsType.GetMembers()
                     .Where(static member => member.IsStatic &&
                         member.DeclaredAccessibility == Accessibility.Public)
                     .OrderBy(static member => member.Name,
                         StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (member is not (Microsoft.CodeAnalysis.IFieldSymbol or
                    Microsoft.CodeAnalysis.IPropertySymbol) ||
                !TryBindAvaloniaNamedColor(
                    member.Name,
                    containingSymbol,
                    out var binding) ||
                binding.Symbol == null)
            {
                continue;
            }

            candidates.Add(new AkcssExpectedValueLookupCandidate(
                member.Name,
                member.Name,
                "Color",
                binding.Symbol));
        }

        return candidates.ToImmutable();
    }

    internal bool IsAvaloniaCornerRadiusType(ITypeSymbol type)
    {
        var cornerRadiusType = Compilation.CSharpCompilation
            .GetTypeByMetadataName("Avalonia.CornerRadius");
        return cornerRadiusType != null &&
            IsSameType(type, cornerRadiusType);
    }

    private static void AddAkcssExpectedValueMemberNames(
        INamedTypeSymbol ownerType,
        ImmutableArrayBuilder<string> names)
    {
        foreach (var member in ownerType.GetMembers())
        {
            if (!member.IsStatic ||
                member.DeclaredAccessibility != Accessibility.Public)
            {
                continue;
            }

            switch (member)
            {
                case Microsoft.CodeAnalysis.IFieldSymbol:
                    names.Add(member.Name);
                    break;
                case Microsoft.CodeAnalysis.IPropertySymbol
                {
                    GetMethod: not null
                }:
                    names.Add(member.Name);
                    break;
            }
        }
    }

    private ImmutableArray<AkcssPropertyLookupCandidate>
        ComputeAkcssPropertiesForCompletion(
            AkburaSyntax declaration,
            string qualifier,
            bool requireReadable,
            CancellationToken cancellationToken)
    {
        if (!TryGetAkcssCompletionContainingSymbol(
                declaration,
                out var containingSymbol) ||
            !TryGetAkcssCompletionOwnerType(
                declaration,
                containingSymbol,
                qualifier,
                out var ownerType))
        {
            return ImmutableArray<AkcssPropertyLookupCandidate>.Empty;
        }

        var appliedTargetType =
            containingSymbol.TargetType.Symbol as ITypeSymbol;
        if (appliedTargetType == null &&
            TryGetDefaultAkcssStyleTargetType(
                out var defaultTargetType))
        {
            appliedTargetType = defaultTargetType;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        AddAkcssCompletionPropertyNames(ownerType, names);

        using var candidates =
            ImmutableArrayBuilder<AkcssPropertyLookupCandidate>.Rent();
        foreach (var name in names.OrderBy(
                     static name => name,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!TryCreateAkcssCompletionProperty(
                    ownerType,
                    name,
                    appliedTargetType,
                    containingSymbol,
                    qualifier.Length > 0,
                    out var property) ||
                !(requireReadable
                    ? property.CanRead
                    : property.CanWrite))
            {
                continue;
            }

            var definitionOwner = property.WriteDefinition.Symbol
                    ?.ContainingType ??
                property.ReadDefinition.Symbol?.ContainingType ??
                ownerType;
            var ownerDisplay = definitionOwner.ToDisplayString(
                SymbolDisplayFormat.MinimallyQualifiedFormat);
            var displayName = qualifier.Length == 0
                ? name
                : qualifier + "." + name;
            candidates.Add(new AkcssPropertyLookupCandidate(
                displayName,
                name,
                ownerDisplay,
                property.Type.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat),
                property,
                property.IsAttachedProperty));
        }

        if (qualifier.Length == 0)
        {
            AddVisibleAttachedPropertiesForCompletion(
                candidates,
                declaration,
                containingSymbol,
                appliedTargetType,
                requireReadable,
                cancellationToken);
        }

        return candidates.ToImmutable();
    }

    private void AddVisibleAttachedPropertiesForCompletion(
        ImmutableArrayBuilder<AkcssPropertyLookupCandidate> candidates,
        AkburaSyntax declaration,
        IAkcssSymbol containingSymbol,
        ITypeSymbol? appliedTargetType,
        bool requireReadable,
        CancellationToken cancellationToken)
    {
        var compilation = Compilation.CSharpCompilation;
        var usingDirectives = GetAkcssCSharpUsingDirectives(declaration);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var visibleOwners = new Dictionary<
            string,
            INamedTypeSymbol?>(StringComparer.Ordinal);

        foreach (var usingDirective in usingDirectives)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (usingDirective.StaticKeyword.RawKind != 0 ||
                usingDirective.Name == null)
            {
                continue;
            }

            var namespaceName = NormalizeGlobalName(
                usingDirective.Name.ToString());
            var namespaceSymbol = GetNamespaceSymbol(
                compilation.GlobalNamespace,
                namespaceName);
            if (namespaceSymbol == null)
            {
                continue;
            }

            var alias = usingDirective.Alias?.Name.Identifier.ValueText;
            foreach (var ownerType in namespaceSymbol.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!compilation.IsSymbolAccessibleWithin(
                        ownerType,
                        compilation.Assembly))
                {
                    continue;
                }

                var ownerReference = alias == null
                    ? ownerType.Name
                    : alias + "::" + ownerType.Name;
                var attachedNames = new HashSet<string>(
                    StringComparer.Ordinal);
                AddAkcssAttachedPropertyNames(
                    ownerType,
                    attachedNames);
                if (attachedNames.Count == 0)
                {
                    continue;
                }

                if (!visibleOwners.TryGetValue(
                        ownerReference,
                        out var existingOwner))
                {
                    visibleOwners.Add(ownerReference, ownerType);
                }
                else if (existingOwner != null &&
                    !IsSameType(existingOwner, ownerType))
                {
                    visibleOwners[ownerReference] = null;
                }
            }
        }

        foreach (var pair in visibleOwners.OrderBy(
                     static pair => pair.Key,
                     StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownerType = pair.Value;
            if (ownerType == null)
            {
                continue;
            }

            var names = new HashSet<string>(StringComparer.Ordinal);
            AddAkcssAttachedPropertyNames(ownerType, names);
            foreach (var name in names.OrderBy(
                         static name => name,
                         StringComparer.Ordinal))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!TryCreateAkcssCompletionProperty(
                        ownerType,
                        name,
                        appliedTargetType,
                        containingSymbol,
                        preferAttached: true,
                        out var property) ||
                    !(requireReadable
                        ? property.CanRead
                        : property.CanWrite))
                {
                    continue;
                }

                var fullName = pair.Key + "." + name;
                if (!seen.Add(fullName))
                {
                    continue;
                }

                var definitionOwner = property.WriteDefinition.Symbol
                        ?.ContainingType ??
                    property.ReadDefinition.Symbol?.ContainingType ??
                    ownerType;
                candidates.Add(new AkcssPropertyLookupCandidate(
                    fullName,
                    fullName,
                    definitionOwner.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat),
                    property.Type.ToDisplayString(
                        SymbolDisplayFormat.MinimallyQualifiedFormat),
                    property,
                    isAttached: true));
            }
        }
    }

    private void AddAkcssAttachedPropertyNames(
        INamedTypeSymbol ownerType,
        HashSet<string> names)
    {
        const string propertySuffix = "Property";
        foreach (var field in ownerType.GetMembers()
                     .OfType<RoslynFieldSymbol>())
        {
            if (!field.IsStatic ||
                field.DeclaredAccessibility != Accessibility.Public ||
                !IsAttachedPropertyType(field.Type))
            {
                continue;
            }

            names.Add(field.Name.EndsWith(
                    propertySuffix,
                    StringComparison.Ordinal) &&
                field.Name.Length > propertySuffix.Length
                    ? field.Name[..^propertySuffix.Length]
                    : field.Name);
        }
    }

    internal ImmutableArray<AkcssApplyLookupCandidate> LookupAkcssApplyItemsForCompletion(
        TextSpan containingDeclarationSpan,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var declaration = FindAkcssCompletionDeclaration(
            containingDeclarationSpan);

        if (declaration == null)
        {
            return [];
        }

        var key = declaration.FullSpan;

        var snapshot = Volatile.Read(ref _completionAkcssApplyItems);

        if (snapshot.TryGetValue(
                key,
                out var cached))
        {
            return cached;
        }

        var candidates =
            ComputeAkcssApplyItemsForCompletion(
                declaration,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return ImmutableInterlocked.GetOrAdd(
            ref _completionAkcssApplyItems,
            key,
            candidates);
    }

    private ImmutableArray<AkcssApplyLookupCandidate>
        ComputeAkcssApplyItemsForCompletion(
            AkburaSyntax declaration,
            CancellationToken cancellationToken)
    {
        if (!TryGetAkcssCompletionContainingSymbol(
                declaration,
                out var containingSymbol))
        {
            return ImmutableArray<AkcssApplyLookupCandidate>.Empty;
        }

        using var candidates =
            ImmutableArrayBuilder<AkcssApplyLookupCandidate>.Rent();
        AddAkcssApplyCompletionLayer(
            candidates,
            GetContainingAkcssLayer(declaration),
            containingSymbol,
            sourceModule: SyntaxTree is AkcssSyntaxTree akcssTree
                ? !string.IsNullOrWhiteSpace(akcssTree.LogicalName)
                    ? akcssTree.LogicalName
                    : akcssTree.FilePath
                : string.Empty,
            priority: 0,
            cancellationToken);

        var importIndex = 0;
        foreach (var importName in GetAkcssImportNames(declaration))
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var layer = ImmutableArrayBuilder<IAkcssSymbol>.Rent();
            AddAkcssImportCompletionSymbols(
                layer,
                importName);
            AddAkcssApplyCompletionLayer(
                candidates,
                layer.ToImmutable(),
                containingSymbol,
                importName,
                priority: 10 + importIndex * 10,
                cancellationToken);
            importIndex++;
        }

        return candidates.ToImmutable();
    }

    private readonly record struct AkcssPropertyCompletionKey(
        TextSpan DeclarationSpan,
        string Qualifier,
        bool RequireReadable);

    private AkburaSyntax? FindAkcssCompletionDeclaration(
        TextSpan declarationSpan)
    {
        var declarations = SyntaxTree.GetRootSyntax()
            .DescendantNodes()
            .Where(static node => node is
                AkcssStyleRuleSyntax or
                AkcssUtilityDeclarationSyntax)
            .ToImmutableArray();

        return declarations.FirstOrDefault(node =>
                   node.FullSpan == declarationSpan) ??
            declarations
                .Where(node =>
                    node.FullSpan.Start <= declarationSpan.Start &&
                    declarationSpan.Start <= node.FullSpan.End)
                .OrderByDescending(static node => node.FullSpan.Start)
                .FirstOrDefault();
    }

    private bool TryGetAkcssCompletionContainingSymbol(
        AkburaSyntax declaration,
        out IAkcssSymbol containingSymbol)
    {
        containingSymbol = GetContainingAkcssLayer(declaration)
            .FirstOrDefault(symbol =>
                ReferenceEquals(
                    symbol.DeclarationSyntax,
                    declaration) ||
                symbol.DeclarationSyntax?.FullSpan ==
                    declaration.FullSpan)!;
        return containingSymbol != null;
    }

    private bool TryGetAkcssCompletionOwnerType(
        AkburaSyntax declaration,
        IAkcssSymbol containingSymbol,
        string qualifier,
        out INamedTypeSymbol ownerType)
    {
        if (string.IsNullOrWhiteSpace(qualifier))
        {
            return TryGetAkcssPropertyOwner(
                containingSymbol,
                out ownerType);
        }

        ownerType = null!;
        try
        {
            var binding = BindCSharpType(
                Microsoft.CodeAnalysis.CSharp.SyntaxFactory
                    .ParseTypeName(qualifier),
                GetAkcssCSharpUsingDirectives(declaration));
            if (binding.TypeSymbol is INamedTypeSymbol namedType)
            {
                ownerType = namedType;
                return true;
            }
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        return false;
    }

    private static void AddAkcssCompletionPropertyNames(
        INamedTypeSymbol ownerType,
        HashSet<string> names)
    {
        for (var current = ownerType;
             current != null;
             current = current.BaseType)
        {
            foreach (var property in current.GetMembers()
                         .OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility ==
                        Accessibility.Public &&
                    property.Parameters.Length == 0)
                {
                    names.Add(property.Name);
                }
            }

            foreach (var field in current.GetMembers()
                         .OfType<RoslynFieldSymbol>())
            {
                AddAkcssPropertyFieldName(field, names);
            }
        }

        foreach (var @interface in ownerType.AllInterfaces)
        {
            foreach (var property in @interface.GetMembers()
                         .OfType<RoslynPropertySymbol>())
            {
                if (!property.IsStatic &&
                    property.DeclaredAccessibility ==
                        Accessibility.Public &&
                    property.Parameters.Length == 0)
                {
                    names.Add(property.Name);
                }
            }
        }
    }

    private static void AddAkcssPropertyFieldName(
        RoslynFieldSymbol field,
        HashSet<string> names)
    {
        const string propertySuffix = "Property";
        if (field.IsStatic &&
            field.DeclaredAccessibility == Accessibility.Public &&
            field.Name.EndsWith(
                propertySuffix,
                StringComparison.Ordinal) &&
            field.Name.Length > propertySuffix.Length)
        {
            names.Add(field.Name[..^propertySuffix.Length]);
        }
    }

    private bool TryCreateAkcssCompletionProperty(
        INamedTypeSymbol ownerType,
        string propertyName,
        ITypeSymbol? appliedTargetType,
        IAkcssSymbol containingSymbol,
        bool preferAttached,
        out PropertySymbol property)
    {
        if (preferAttached)
        {
            return TryCreateAttachedPropertySymbol(
                ownerType,
                propertyName,
                appliedTargetType,
                SymbolLanguage.Akcss,
                containingSymbol,
                out property);
        }

        var clrProperty = FindPublicClrProperty(
            ownerType,
            propertyName);
        var avaloniaProperty = FindAvaloniaPropertyField(
            ownerType,
            propertyName);
        if (clrProperty == null && avaloniaProperty == null)
        {
            property = null!;
            return false;
        }

        property = new PropertySymbol(
            propertyName,
            GetMarkupPropertyType(
                parameter: null,
                command: null,
                clrProperty,
                avaloniaProperty,
                attachedProperty: null),
            avaloniaPropertyDefinition: avaloniaProperty == null
                ? default
                : new CSharpSymbolDefinition(avaloniaProperty),
            clrPropertyDefinition: clrProperty == null
                ? default
                : new CSharpSymbolDefinition(clrProperty),
            language: SymbolLanguage.Akcss,
            containingSymbol: containingSymbol);
        return true;
    }

    private void AddAkcssImportCompletionSymbols(
        ImmutableArrayBuilder<IAkcssSymbol> symbols,
        string importName)
    {
        var matches = Compilation
            .GetLocalAkcssSyntaxTreesByLogicalName(importName);
        if (matches.Length == 0)
        {
            var modules = Compilation
                .GetAkcssModuleSymbolsByLogicalName(importName);
            if (modules.Length > 0)
            {
                foreach (var module in modules)
                {
                    symbols.AddRange(module.AkcssSymbols);
                }

                return;
            }

            matches = Compilation
                .GetAkcssSyntaxTreesByLogicalName(importName);
        }

        foreach (var tree in matches)
        {
            symbols.AddRange(CreateAkcssLookupSymbols(
                tree.GetRoot().Members));
        }
    }

    private static void AddAkcssApplyCompletionLayer(
        ImmutableArrayBuilder<AkcssApplyLookupCandidate> candidates,
        ImmutableArray<IAkcssSymbol> layer,
        IAkcssSymbol containingSymbol,
        string sourceModule,
        int priority,
        CancellationToken cancellationToken)
    {
        foreach (var symbol in layer)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(symbol, containingSymbol) ||
                ReferenceEquals(
                    symbol.DeclarationSyntax,
                    containingSymbol.DeclarationSyntax) ||
                !IsAkcssApplyTargetCompatible(
                    symbol,
                    containingSymbol))
            {
                continue;
            }

            if (symbol is ITailwindUtilitySymbol utility)
            {
                var parameters = string.Join(
                    ", ",
                    utility.Parameters.Select(static parameter =>
                        parameter.Type.ToDisplayString(
                            SymbolDisplayFormat.MinimallyQualifiedFormat) +
                        " " + parameter.Name));
                candidates.Add(new AkcssApplyLookupCandidate(
                    utility.Parameters.Length == 0
                        ? utility.Name
                        : utility.Name + "-(" + parameters + ")",
                    utility.Parameters.Length == 0
                        ? utility.Name
                        : utility.Name + "-",
                    sourceModule,
                    priority == 0 ? 1 : priority,
                    symbol));
                continue;
            }

            if (!string.IsNullOrWhiteSpace(symbol.ClassName))
            {
                candidates.Add(new AkcssApplyLookupCandidate(
                    symbol.ClassName!,
                    symbol.ClassName!,
                    sourceModule,
                    priority,
                    symbol));
            }
        }
    }
}
