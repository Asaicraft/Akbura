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
    private readonly object _completionAkcssPropertiesGate = new();
    private readonly Dictionary<
        AkcssPropertyCompletionKey,
        ImmutableArray<AkcssPropertyLookupCandidate>>
        _completionAkcssProperties = new();
    private readonly object _completionAkcssApplyGate = new();
    private readonly Dictionary<
        TextSpan,
        ImmutableArray<AkcssApplyLookupCandidate>>
        _completionAkcssApplyItems = new();

    internal ImmutableArray<AkcssPropertyLookupCandidate>
        LookupAkcssPropertiesForCompletion(
            TextSpan containingDeclarationSpan,
            string qualifier,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var declaration = FindAkcssCompletionDeclaration(
            containingDeclarationSpan);
        if (declaration == null)
        {
            return ImmutableArray<AkcssPropertyLookupCandidate>.Empty;
        }

        var key = new AkcssPropertyCompletionKey(
            declaration.FullSpan,
            qualifier);
        lock (_completionAkcssPropertiesGate)
        {
            if (_completionAkcssProperties.TryGetValue(
                    key,
                    out var cached))
            {
                return cached;
            }
        }

        var candidates = ComputeAkcssPropertiesForCompletion(
            declaration,
            qualifier,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_completionAkcssPropertiesGate)
        {
            if (_completionAkcssProperties.TryGetValue(
                    key,
                    out var cached))
            {
                return cached;
            }

            _completionAkcssProperties.Add(key, candidates);
            return candidates;
        }
    }

    private ImmutableArray<AkcssPropertyLookupCandidate>
        ComputeAkcssPropertiesForCompletion(
            AkburaSyntax declaration,
            string qualifier,
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
                !property.CanWrite)
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

        return candidates.ToImmutable();
    }

    internal ImmutableArray<AkcssApplyLookupCandidate>
        LookupAkcssApplyItemsForCompletion(
            TextSpan containingDeclarationSpan,
            CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var declaration = FindAkcssCompletionDeclaration(
            containingDeclarationSpan);
        if (declaration == null)
        {
            return ImmutableArray<AkcssApplyLookupCandidate>.Empty;
        }

        lock (_completionAkcssApplyGate)
        {
            if (_completionAkcssApplyItems.TryGetValue(
                    declaration.FullSpan,
                    out var cached))
            {
                return cached;
            }
        }

        var candidates = ComputeAkcssApplyItemsForCompletion(
            declaration,
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_completionAkcssApplyGate)
        {
            if (_completionAkcssApplyItems.TryGetValue(
                    declaration.FullSpan,
                    out var cached))
            {
                return cached;
            }

            _completionAkcssApplyItems.Add(
                declaration.FullSpan,
                candidates);
            return candidates;
        }
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
        string Qualifier);

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
        if (preferAttached &&
            TryCreateAttachedPropertySymbol(
                ownerType,
                propertyName,
                appliedTargetType,
                SymbolLanguage.Akcss,
                containingSymbol,
                out property))
        {
            return true;
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
