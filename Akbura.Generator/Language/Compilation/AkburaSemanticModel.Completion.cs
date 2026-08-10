using Akbura.Language.Binder;
using Akbura.Language.Symbols;
using Akbura.Pools;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    internal ImmutableArray<MarkupComponentLookupCandidate>
        LookupMarkupComponents(CancellationToken cancellationToken = default)
    {
        var visibleNamespaces = GetVisibleMarkupNamespaces();
        var akburaComponents = GetAkburaComponentsForLookup(cancellationToken);
        var candidates = new Dictionary<
            string,
            MarkupComponentLookupCandidate>(StringComparer.Ordinal);
        var csharpCompilation = Compilation.CSharpProbeCompilation;

        foreach (var visibleNamespace in visibleNamespaces)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var namespaceSymbol = GetNamespaceSymbol(
                csharpCompilation.GlobalNamespace,
                visibleNamespace.Name);
            if (namespaceSymbol == null)
            {
                continue;
            }

            foreach (var type in namespaceSymbol.GetTypeMembers())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsCompletableMarkupComponent(
                        csharpCompilation,
                        type))
                {
                    continue;
                }

                var metadataName = GetTypeMetadataName(type);
                akburaComponents.TryGetValue(
                    metadataName,
                    out var akburaComponent);
                var displayName = visibleNamespace.Alias == null
                    ? type.Name
                    : visibleNamespace.Alias + "::" + type.Name;
                AddMarkupComponentCandidate(
                    candidates,
                    displayName,
                    type,
                    akburaComponent);
            }
        }

        foreach (var pair in akburaComponents)
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddVisibleAkburaComponentCandidates(
                candidates,
                visibleNamespaces,
                pair.Key,
                pair.Value,
                csharpCompilation);
        }

        return candidates.Values
            .OrderBy(
                static candidate => candidate.DisplayName,
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    internal bool TryResolveMarkupComponentForCompletion(
        string componentName,
        out IMarkupComponentSymbol component)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            component = null!;
            return false;
        }

        var name = componentName.Trim();
        foreach (var metadataName in
                 GetAkburaComponentCandidateMetadataNames(name))
        {
            var akburaComponent = FindLocalAkburaComponent(metadataName) ??
                Compilation
                    .GetReferencedComponentSymbols(metadataName)
                    .FirstOrDefault();
            var componentType = Compilation.CSharpProbeCompilation
                .GetTypeByMetadataName(metadataName);
            if (akburaComponent != null || componentType != null)
            {
                component = CreateMarkupComponentLookupSymbol(
                    name,
                    componentType ?? akburaComponent?.ComponentType,
                    akburaComponent);
                return true;
            }
        }

        CSharpBindingResult binding;
        try
        {
            binding = BindCSharpType(
                SyntaxFactory.ParseTypeName(name));
        }
        catch (InvalidOperationException)
        {
            component = null!;
            return false;
        }

        if (TryGetMarkupComponentType(binding, out var boundType))
        {
            var metadataName = GetTypeMetadataName(boundType);
            var akburaComponent = FindLocalAkburaComponent(metadataName) ??
                Compilation
                    .GetReferencedComponentSymbols(metadataName)
                    .FirstOrDefault();
            component = CreateMarkupComponentLookupSymbol(
                name,
                boundType,
                akburaComponent);
            return true;
        }

        component = null!;
        return false;
    }

    private Dictionary<string, IAkburaComponentSymbol>
        GetAkburaComponentsForLookup(
            CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, IAkburaComponentSymbol>(
            StringComparer.Ordinal);
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ReferenceEquals(syntaxTree, SyntaxTree))
            {
                continue;
            }

            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            if (semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is IAkburaComponentSymbol component)
            {
                AddAkburaComponent(result, component);
            }
        }

        foreach (var component in
                 Compilation.GetReferencedComponentSymbols())
        {
            cancellationToken.ThrowIfCancellationRequested();
            AddAkburaComponent(result, component);
        }

        return result;
    }

    private IAkburaComponentSymbol? FindLocalAkburaComponent(
        string metadataName)
    {
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            if (ReferenceEquals(syntaxTree, SyntaxTree) ||
                !string.Equals(
                    GetAkburaComponentMetadataName(syntaxTree),
                    metadataName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            if (semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is IAkburaComponentSymbol component)
            {
                return component;
            }
        }

        return null;
    }

    private IEnumerable<string>
        GetAkburaComponentCandidateMetadataNames(string componentName)
    {
        var name = componentName.Trim();
        if (name.Length == 0)
        {
            yield break;
        }

        if (name.StartsWith("global::", StringComparison.Ordinal))
        {
            yield return name["global::".Length..];
            yield break;
        }

        var usingDirectives = GetCSharpUsingDirectives();
        var aliasSeparator = name.IndexOf("::", StringComparison.Ordinal);
        if (aliasSeparator > 0)
        {
            var alias = name[..aliasSeparator];
            var remainder = name[(aliasSeparator + 2)..];
            foreach (var directive in usingDirectives)
            {
                if (directive.Alias != null &&
                    directive.Name != null &&
                    string.Equals(
                        directive.Alias.Name.Identifier.ValueText,
                        alias,
                        StringComparison.Ordinal))
                {
                    yield return NormalizeGlobalName(
                        directive.Name + "." + remainder);
                }
            }

            yield break;
        }

        if (name.IndexOf(".", StringComparison.Ordinal) >= 0)
        {
            yield return name;
            yield break;
        }

        foreach (var directive in usingDirectives)
        {
            if (directive.Alias != null &&
                directive.Name != null &&
                string.Equals(
                    directive.Alias.Name.Identifier.ValueText,
                    name,
                    StringComparison.Ordinal))
            {
                yield return NormalizeGlobalName(
                    directive.Name.ToString());
            }
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var @namespace in GetAkburaUsingNamespaces())
        {
            var metadataName = @namespace + "." + name;
            if (seen.Add(metadataName))
            {
                yield return metadataName;
            }
        }

        var currentNamespace = GetAkburaNamespaceText(
            SyntaxTree.GetRoot(),
            SyntaxTree);
        if (currentNamespace.Length > 0)
        {
            var metadataName = currentNamespace + "." + name;
            if (seen.Add(metadataName))
            {
                yield return metadataName;
            }
        }

        if (seen.Add(name))
        {
            yield return name;
        }
    }

    private ImmutableArray<VisibleMarkupNamespace>
        GetVisibleMarkupNamespaces()
    {
        using var builder =
            ImmutableArrayBuilder<VisibleMarkupNamespace>.Rent();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        AddVisibleMarkupNamespace(
            builder,
            seen,
            string.Empty,
            alias: null);
        AddVisibleMarkupNamespace(
            builder,
            seen,
            GetAkburaNamespaceText(
                SyntaxTree.GetRoot(),
                SyntaxTree),
            alias: null);

        foreach (var directive in GetCSharpUsingDirectives())
        {
            if (directive.StaticKeyword.RawKind != 0 ||
                directive.Name == null)
            {
                continue;
            }

            var name = NormalizeGlobalName(
                directive.Name.ToString());
            if (name.EndsWith(".akcss", StringComparison.Ordinal))
            {
                continue;
            }

            AddVisibleMarkupNamespace(
                builder,
                seen,
                name,
                directive.Alias?.Name.Identifier.ValueText);
        }

        return builder.ToImmutable();
    }

    private static void AddVisibleMarkupNamespace(
        ImmutableArrayBuilder<VisibleMarkupNamespace> builder,
        HashSet<string> seen,
        string name,
        string? alias)
    {
        var normalizedName = NormalizeGlobalName(name);
        var key = (alias ?? string.Empty) + "\0" + normalizedName;
        if (seen.Add(key))
        {
            builder.Add(new VisibleMarkupNamespace(
                normalizedName,
                alias));
        }
    }

    private static void AddAkburaComponent(
        Dictionary<string, IAkburaComponentSymbol> components,
        IAkburaComponentSymbol component)
    {
        if (!components.ContainsKey(component.MetadataName))
        {
            components.Add(component.MetadataName, component);
        }
    }

    private static void AddVisibleAkburaComponentCandidates(
        Dictionary<string, MarkupComponentLookupCandidate> candidates,
        ImmutableArray<VisibleMarkupNamespace> visibleNamespaces,
        string metadataName,
        IAkburaComponentSymbol component,
        CSharpCompilation csharpCompilation)
    {
        var separator = metadataName.LastIndexOf('.');
        var namespaceName = separator < 0
            ? string.Empty
            : metadataName[..separator];
        var simpleName = separator < 0
            ? metadataName
            : metadataName[(separator + 1)..];
        var componentType = component.ComponentType ??
            csharpCompilation.GetTypeByMetadataName(metadataName);

        foreach (var visibleNamespace in visibleNamespaces)
        {
            if (!string.Equals(
                    visibleNamespace.Name,
                    namespaceName,
                    StringComparison.Ordinal))
            {
                continue;
            }

            var displayName = visibleNamespace.Alias == null
                ? simpleName
                : visibleNamespace.Alias + "::" + simpleName;
            AddMarkupComponentCandidate(
                candidates,
                displayName,
                componentType,
                component);
        }
    }

    private static void AddMarkupComponentCandidate(
        Dictionary<string, MarkupComponentLookupCandidate> candidates,
        string displayName,
        INamedTypeSymbol? componentType,
        IAkburaComponentSymbol? akburaComponent)
    {
        if (candidates.TryGetValue(displayName, out var existing) &&
            (existing.Symbol.AkburaComponent != null ||
             akburaComponent == null))
        {
            return;
        }

        candidates[displayName] = new MarkupComponentLookupCandidate(
            displayName,
            CreateMarkupComponentLookupSymbol(
                displayName,
                componentType,
                akburaComponent));
    }

    private static IMarkupComponentSymbol
        CreateMarkupComponentLookupSymbol(
            string displayName,
            INamedTypeSymbol? componentType,
            IAkburaComponentSymbol? akburaComponent)
    {
        var csharpDefinition = componentType != null
            ? new CSharpSymbolDefinition(componentType)
            : akburaComponent?.CSharpDefinition ?? default;
        return new MarkupComponentSymbol(
            displayName,
            csharpDefinition,
            akburaComponent?.ContentModel ?? default,
            akburaComponent: akburaComponent);
    }

    private static INamespaceSymbol? GetNamespaceSymbol(
        INamespaceSymbol globalNamespace,
        string namespaceName)
    {
        var current = globalNamespace;
        if (namespaceName.Length == 0)
        {
            return current;
        }

        foreach (var segment in namespaceName.Split('.'))
        {
            current = current.GetNamespaceMembers()
                .FirstOrDefault(candidate => string.Equals(
                    candidate.Name,
                    segment,
                    StringComparison.Ordinal));
            if (current == null)
            {
                return null;
            }
        }

        return current;
    }

    private static bool IsCompletableMarkupComponent(
        Compilation compilation,
        INamedTypeSymbol type)
    {
        return !type.IsStatic &&
            !type.IsAbstract &&
            type.Arity == 0 &&
            type.TypeKind is TypeKind.Class or TypeKind.Struct &&
            compilation.IsSymbolAccessibleWithin(
                type,
                compilation.Assembly);
    }

    private static string GetTypeMetadataName(INamedTypeSymbol type)
    {
        return NormalizeGlobalName(type.ToDisplayString(
            SymbolDisplayFormat.FullyQualifiedFormat));
    }

    private static string NormalizeGlobalName(string name)
    {
        return name.StartsWith("global::", StringComparison.Ordinal)
            ? name["global::".Length..]
            : name;
    }

    private readonly struct VisibleMarkupNamespace
    {
        public VisibleMarkupNamespace(string name, string? alias)
        {
            Name = name;
            Alias = alias;
        }

        public string Name { get; }

        public string? Alias { get; }
    }
}
