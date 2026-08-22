using Akbura.Pools;
using Microsoft.CodeAnalysis;
using System.Collections.Immutable;
using System.Diagnostics;

namespace Akbura.Language;

internal partial class AkburaSemanticModel
{
    private CompletionComponentImportCatalog?
        _completionComponentImportCatalog;

    internal ImmutableArray<MarkupComponentImportCandidate>
        LookupMarkupComponentCompletionImports(
            CancellationToken cancellationToken = default)
    {
        var catalog = Volatile.Read(
            ref _completionComponentImportCatalog);
        if (catalog != null)
        {
            return catalog.Candidates;
        }

        var candidates = ComputeMarkupComponentCompletionImports(
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        var created = new CompletionComponentImportCatalog(candidates);
        catalog = Interlocked.CompareExchange(
            ref _completionComponentImportCatalog,
            created,
            comparand: null);

        return (catalog ?? created).Candidates;
    }

    private ImmutableArray<MarkupComponentImportCandidate>
        ComputeMarkupComponentCompletionImports(
            CancellationToken cancellationToken)
    {
        var timer = Stopwatch.StartNew();
        var compilation = Compilation.CSharpProbeCompilation;
        var akburaControlType = compilation.GetTypeByMetadataName(
            "Akbura.AkburaControl");
        var avaloniaObjectType = compilation.GetTypeByMetadataName(
            "Avalonia.AvaloniaObject");
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

        VisitMarkupComponentCompletionNamespace(
            compilation.GlobalNamespace,
            compilation,
            akburaControlType,
            avaloniaObjectType,
            visibleNamespaces,
            seenTypes,
            seenCandidates,
            candidates,
            cancellationToken);

        // Component metadata can precede the generated CLR type.
        foreach (var syntaxTree in Compilation.SyntaxTrees)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataName = GetAkburaComponentMetadataName(syntaxTree);
            if (metadataName.Length == 0)
            {
                continue;
            }

            AddAkburaComponentImportCandidate(
                metadataName,
                compilation.AssemblyName ?? compilation.Assembly.Name,
                compilation.GetTypeByMetadataName(metadataName),
                priority: 0,
                visibleNamespaces,
                seenCandidates,
                candidates);
        }

        foreach (var reference in Compilation.CompilationReferences)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var referenceCompilation =
                reference.Compilation.CSharpProbeCompilation;

            foreach (var metadataName in
                     reference.GetComponentMetadataNames(cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                AddAkburaComponentImportCandidate(
                    metadataName,
                    referenceCompilation.AssemblyName ?? string.Empty,
                    referenceCompilation.GetTypeByMetadataName(metadataName),
                    priority: 10,
                    visibleNamespaces,
                    seenCandidates,
                    candidates);
            }
        }

        var result = candidates.AsEnumerable()
            .GroupBy(
                static candidate => candidate.NamespaceName +
                    "\0" +
                    GetImportComponentName(candidate),
                StringComparer.Ordinal)
            .Where(static group => group.Count() == 1)
            .Select(static group => group.First())
            .OrderBy(static candidate => candidate.Priority)
            .ThenBy(
                static candidate => GetImportComponentName(candidate),
                StringComparer.Ordinal)
            .ThenBy(
                static candidate => candidate.NamespaceName,
                StringComparer.Ordinal)
            .ToImmutableArray();

        AkburaWorkspaceDiagnostics.WriteCompletionElapsed(
            "Component auto-import catalog",
            timer.Elapsed);
        return result;
    }

    private static void VisitMarkupComponentCompletionNamespace(
        INamespaceSymbol namespaceSymbol,
        Compilation compilation,
        INamedTypeSymbol? akburaControlType,
        INamedTypeSymbol? avaloniaObjectType,
        HashSet<string> visibleNamespaces,
        HashSet<INamedTypeSymbol> seenTypes,
        HashSet<string> seenCandidates,
        ImmutableArrayBuilder<MarkupComponentImportCandidate> candidates,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        foreach (var type in namespaceSymbol.GetTypeMembers())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (type.ContainingType != null ||
                type.ContainingNamespace.IsGlobalNamespace ||
                !seenTypes.Add(type))
            {
                continue;
            }

            var namespaceName =
                type.ContainingNamespace.ToDisplayString();
            if (visibleNamespaces.Contains(namespaceName))
            {
                continue;
            }

            var isAkburaComponent = IsDerivedFromOrEqual(
                type,
                akburaControlType);
            var isAvaloniaNamespace = namespaceName.Equals(
                    "Avalonia",
                    StringComparison.Ordinal) ||
                namespaceName.StartsWith(
                    "Avalonia.",
                    StringComparison.Ordinal);
            var isAvaloniaObject = IsDerivedFromOrEqual(
                type,
                avaloniaObjectType);

            if (!isAkburaComponent &&
                !isAvaloniaNamespace &&
                !isAvaloniaObject)
            {
                continue;
            }

            if (!IsCompletableMarkupComponent(
                    compilation,
                    type,
                    isAkburaComponent))
            {
                continue;
            }

            var metadataName = GetTypeMetadataName(type);
            if (!seenCandidates.Add(CreateCandidateKey(
                    type.ContainingAssembly.Name,
                    metadataName)))
            {
                continue;
            }

            var isCurrentAssembly = SymbolEqualityComparer.Default.Equals(
                type.ContainingAssembly,
                compilation.Assembly);
            var priority = isCurrentAssembly
                ? 0
                : isAkburaComponent
                    ? 10
                    : isAvaloniaNamespace
                        ? 20
                        : 30;

            candidates.Add(new MarkupComponentImportCandidate(
                namespaceName,
                type.Name,
                type.ContainingAssembly.Name,
                type,
                priority));
        }

        foreach (var childNamespace in
                 namespaceSymbol.GetNamespaceMembers())
        {
            VisitMarkupComponentCompletionNamespace(
                childNamespace,
                compilation,
                akburaControlType,
                avaloniaObjectType,
                visibleNamespaces,
                seenTypes,
                seenCandidates,
                candidates,
                cancellationToken);
        }
    }

    private static string GetImportComponentName(
        MarkupComponentImportCandidate candidate)
    {
        if (candidate.Type != null)
        {
            return candidate.Type.Name;
        }

        var separator = candidate.TypeDisplay.LastIndexOf('.');
        return separator < 0
            ? candidate.TypeDisplay
            : candidate.TypeDisplay[(separator + 1)..];
    }

    private sealed class CompletionComponentImportCatalog
    {
        public CompletionComponentImportCatalog(
            ImmutableArray<MarkupComponentImportCandidate> candidates)
        {
            Candidates = candidates;
        }

        public ImmutableArray<MarkupComponentImportCandidate> Candidates
        {
            get;
        }
    }
}
