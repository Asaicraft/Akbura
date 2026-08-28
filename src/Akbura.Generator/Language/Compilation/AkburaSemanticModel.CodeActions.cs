using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;

namespace Akbura.Language;

internal readonly struct MarkupComponentImportCandidate
{
    public MarkupComponentImportCandidate(
        string namespaceName,
        string typeDisplay,
        string assemblyName,
        INamedTypeSymbol? type,
        int priority)
    {
        NamespaceName = namespaceName;
        TypeDisplay = typeDisplay;
        AssemblyName = assemblyName;
        Type = type;
        Priority = priority;
    }

    public string NamespaceName { get; }

    public string TypeDisplay { get; }

    public string AssemblyName { get; }

    public INamedTypeSymbol? Type { get; }

    public int Priority { get; }
}

internal abstract partial class AkburaSemanticModel
{
    private ImmutableDictionary<string, ImmutableArray<MarkupComponentImportCandidate>> _componentImports =
            ImmutableDictionary.Create<string,ImmutableArray<MarkupComponentImportCandidate>>(StringComparer.Ordinal);

    internal ImmutableArray<MarkupComponentImportCandidate> LookupMarkupComponentImports(
        string componentName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(componentName))
        {
            return [];
        }

        componentName = componentName.Trim();

        var snapshot = Volatile.Read(ref _componentImports);

        if (snapshot.TryGetValue(componentName,out var cached))
        {
            return cached;
        }

        var computed =
            ComputeMarkupComponentImports(
                componentName,
                cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        return ImmutableInterlocked.GetOrAdd(
            ref _componentImports,
            componentName,
            computed);
    }

    private ImmutableArray<MarkupComponentImportCandidate> ComputeMarkupComponentImports(
        string componentName,
        CancellationToken cancellationToken)
    {
        var compilation = Compilation.CSharpProbeCompilation;
        var akburaControlType = compilation.GetTypeByMetadataName(
            "Akbura.AkburaControl");
        var visibleNamespaces = new HashSet<string>(
            StringComparer.Ordinal);
        foreach (var visibleNamespace in GetVisibleMarkupNamespaces())
        {
            if (visibleNamespace.Alias == null)
            {
                visibleNamespaces.Add(visibleNamespace.Name);
            }
        }
        using var candidates =
            ImmutableArrayBuilder<MarkupComponentImportCandidate>.Rent();
        var seenTypes = new HashSet<INamedTypeSymbol>(
            SymbolEqualityComparer.Default);
        var seenCandidates = new HashSet<string>(StringComparer.Ordinal);

        VisitMarkupComponentImportNamespace(
            compilation.GlobalNamespace,
            componentName,
            compilation,
            akburaControlType,
            visibleNamespaces,
            seenTypes,
            seenCandidates,
            candidates,
            cancellationToken);

        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var semanticModel = Compilation.GetSemanticModel(syntaxTree);
            var metadataName = semanticModel
                .GetAkburaComponentMetadataName(syntaxTree);
            if (!HasSimpleMetadataName(metadataName, componentName) ||
                semanticModel.GetDeclaredSymbol(
                    syntaxTree.GetRoot()) is not
                    Symbols.IAkburaComponentSymbol component)
            {
                continue;
            }

            AddAkburaComponentImportCandidate(
                metadataName,
                compilation.AssemblyName ?? compilation.Assembly.Name,
                component.ComponentType,
                priority: 0,
                visibleNamespaces,
                seenCandidates,
                candidates);
        }

        foreach (var reference in Compilation.CompilationReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var metadataName in
                     reference.GetComponentMetadataNames(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!HasSimpleMetadataName(metadataName, componentName) ||
                    !reference.TryGetComponentSymbol(
                        metadataName,
                        out var component))
                {
                    continue;
                }

                AddAkburaComponentImportCandidate(
                    metadataName,
                    reference.Compilation.CSharpProbeCompilation
                        .AssemblyName ?? string.Empty,
                    component.ComponentType,
                    priority: 10,
                    visibleNamespaces,
                    seenCandidates,
                    candidates);
            }
        }

        return candidates.AsEnumerable()
            .GroupBy(
                static candidate => candidate.NamespaceName,
                StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(
                static candidate => candidate.NamespaceName,
                StringComparer.Ordinal)
            .ToImmutableArray();
    }

    private static void VisitMarkupComponentImportNamespace(
        INamespaceSymbol namespaceSymbol,
        string componentName,
        Compilation compilation,
        INamedTypeSymbol? akburaControlType,
        HashSet<string> visibleNamespaces,
        HashSet<INamedTypeSymbol> seenTypes,
        HashSet<string> seenCandidates,
        ImmutableArrayBuilder<MarkupComponentImportCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var type in namespaceSymbol.GetTypeMembers(componentName))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.ContainingType != null ||
                type.ContainingNamespace.IsGlobalNamespace ||
                !seenTypes.Add(type))
            {
                continue;
            }

            var isAkburaComponent = IsDerivedFromOrEqual(
                type,
                akburaControlType);
            if (!IsCompletableMarkupComponent(
                    compilation,
                    type,
                    isAkburaComponent))
            {
                continue;
            }

            var namespaceName = type.ContainingNamespace.ToDisplayString();
            if (visibleNamespaces.Contains(namespaceName))
            {
                continue;
            }

            var metadataName = GetTypeMetadataName(type);
            if (!seenCandidates.Add(
                    CreateCandidateKey(
                        type.ContainingAssembly.Name,
                        metadataName)))
            {
                continue;
            }

            var isCurrentAssembly =
                SymbolEqualityComparer.Default.Equals(
                    type.ContainingAssembly,
                    compilation.Assembly);
            var priority = isCurrentAssembly
                ? 0
                : isAkburaComponent
                    ? 10
                    : 20;

            candidates.Add(new MarkupComponentImportCandidate(
                namespaceName,
                type.ToDisplayString(
                    SymbolDisplayFormat.MinimallyQualifiedFormat),
                type.ContainingAssembly.Name,
                type,
                priority));
        }

        foreach (var childNamespace in namespaceSymbol.GetNamespaceMembers())
        {
            VisitMarkupComponentImportNamespace(
                childNamespace,
                componentName,
                compilation,
                akburaControlType,
                visibleNamespaces,
                seenTypes,
                seenCandidates,
                candidates,
                cancellationToken);
        }
    }

    private static void AddAkburaComponentImportCandidate(
        string metadataName,
        string assemblyName,
        INamedTypeSymbol? componentType,
        int priority,
        HashSet<string> visibleNamespaces,
        HashSet<string> seenCandidates,
        ImmutableArrayBuilder<MarkupComponentImportCandidate> candidates)
    {
        var separator = metadataName.LastIndexOf('.');
        var namespaceName = separator < 0
            ? string.Empty
            : metadataName[..separator];
        if (namespaceName.Length == 0 ||
            visibleNamespaces.Contains(namespaceName) ||
            !seenCandidates.Add(
                CreateCandidateKey(assemblyName, metadataName)))
        {
            return;
        }

        var simpleName = metadataName[(separator + 1)..];
        candidates.Add(new MarkupComponentImportCandidate(
            namespaceName,
            simpleName,
            assemblyName,
            componentType,
            priority));
    }

    private static bool HasSimpleMetadataName(
        string metadataName,
        string expectedName)
    {
        var separator = metadataName.LastIndexOf('.');
        return string.Equals(
            metadataName[(separator + 1)..],
            expectedName,
            StringComparison.Ordinal);
    }

    private static string CreateCandidateKey(
        string assemblyName,
        string metadataName)
    {
        return assemblyName + "\0" + metadataName;
    }
}
