using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using RoslynFieldSymbol = Microsoft.CodeAnalysis.IFieldSymbol;

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

    internal ImmutableArray<MarkupAttachedPropertyLookupCandidate>
        LookupMarkupAttachedPropertiesForCompletion(
            string componentName,
            CancellationToken cancellationToken = default)
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

    private ImmutableArray<MarkupAttachedPropertyLookupCandidate>
        ComputeMarkupAttachedPropertiesForCompletion(
            string componentName,
            CancellationToken cancellationToken)
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

            var attachedNames = new HashSet<string>(
                StringComparer.Ordinal);
            AddMarkupAttachedPropertyNames(ownerType, attachedNames);

            foreach (var propertyName in attachedNames.OrderBy(
                         static name => name,
                         StringComparer.Ordinal))
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

    private void AddMarkupAttachedPropertyNames(
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

            var name = field.Name.EndsWith(
                    propertySuffix,
                    StringComparison.Ordinal) &&
                field.Name.Length > propertySuffix.Length
                    ? field.Name[..^propertySuffix.Length]
                    : field.Name;
            names.Add(name);
        }
    }
}
