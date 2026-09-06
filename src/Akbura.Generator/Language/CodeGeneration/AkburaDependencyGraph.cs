using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;

namespace Akbura.Language.CodeGeneration;

internal readonly struct AkburaDocumentDependency(
    string identity,
    DocumentSyntaxVersion document,
    bool includesBody)
{
    public string Identity { get; } = identity;
    public DocumentSyntaxVersion Document { get; } = document;
    public bool IncludesBody { get; } = includesBody;
}

/// <summary>
/// Conservative, syntax-only document dependencies. A candidate is never discarded
/// because another candidate looks more likely to bind. Declaration-set changes
/// must separately invalidate the environment, including previously unresolved names.
/// </summary>
internal sealed class AkburaDependencyGraph
{
    private readonly Dictionary<DocumentSyntaxVersion, ImmutableArray<AkburaDocumentDependency>> _dependencies;

    private AkburaDependencyGraph(
        Dictionary<DocumentSyntaxVersion, ImmutableArray<AkburaDocumentDependency>> dependencies)
    {
        _dependencies = dependencies;
    }

    public static AkburaDependencyGraph Create(
        AkburaProjectIndex index,
        CancellationToken cancellationToken = default)
    {
        if (index == null)
        {
            throw new ArgumentNullException(nameof(index));
        }

        return new Builder(index, cancellationToken).Build();
    }

    public ImmutableArray<AkburaDocumentDependency> GetDependencies(DocumentSyntaxVersion document)
    {
        if (!_dependencies.TryGetValue(document, out var dependencies))
        {
            throw new ArgumentException("The document is not part of this project snapshot.", nameof(document));
        }

        return dependencies;
    }

    public ImmutableArray<AkburaDocumentDependency> GetDependencies(AkcssDocumentDescriptor module)
    {
        var dependencies = GetDependencies(module.DocumentVersion);
        if (!module.IsInline)
        {
            return dependencies;
        }

        // Inline modules share the containing source document's exact body version.
        // The owner's request already includes inline bodies; make the reverse edge
        // explicit when callers request dependencies for an individual inline module.
        var result = dependencies.Add(new AkburaDocumentDependency(
            GetDocumentIdentity(module.DocumentVersion),
            module.DocumentVersion,
            includesBody: true)).ToArray();
        Array.Sort(result, CompareDependencies);
        return ImmutableArray.CreateRange(result);
    }

    public static string GetDocumentIdentity(DocumentSyntaxVersion document)
    {
        // Length-delimited fields cannot collide even when paths contain separators.
        return ((int)document.Kind).ToString(CultureInfo.InvariantCulture) + ":" +
            document.FilePath.Length.ToString(CultureInfo.InvariantCulture) + ":" + document.FilePath +
            document.LogicalName.Length.ToString(CultureInfo.InvariantCulture) + ":" + document.LogicalName;
    }

    private static int CompareDependencies(AkburaDocumentDependency left, AkburaDocumentDependency right)
    {
        var comparison = left.Document.Kind.CompareTo(right.Document.Kind);
        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.Document.FilePath, right.Document.FilePath);
        }

        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.Document.LogicalName, right.Document.LogicalName);
        }

        if (comparison == 0)
        {
            comparison = StringComparer.Ordinal.Compare(left.Document.GenerationShape, right.Document.GenerationShape);
        }

        return comparison;
    }

    private sealed class Builder
    {
        private readonly AkburaProjectIndex _index;
        private readonly CancellationToken _cancellationToken;
        private readonly ImmutableArray<DocumentSyntaxVersion> _documents;
        private readonly Dictionary<DocumentSyntaxVersion, int> _documentIndices = [];
        private readonly Dictionary<string, List<int>> _componentsByName = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<int>> _modulesByName = new(StringComparer.Ordinal);
        private readonly Dictionary<int, bool>[] _edges;

        public Builder(AkburaProjectIndex index, CancellationToken cancellationToken)
        {
            _index = index;
            _cancellationToken = cancellationToken;
            _documents = index.Documents;
            _edges = new Dictionary<int, bool>[_documents.Length];

            for (var i = 0; i < _documents.Length; i++)
            {
                _documentIndices[_documents[i]] = i;
                _edges[i] = [];
            }
        }

        public AkburaDependencyGraph Build()
        {
            CollectDeclarations();

            for (var i = 0; i < _documents.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                CollectDependencies(i);
            }

            var dependencies = new Dictionary<DocumentSyntaxVersion, ImmutableArray<AkburaDocumentDependency>>(
                _documents.Length);

            for (var i = 0; i < _documents.Length; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                dependencies[_documents[i]] = GetTransitiveDependencies(i);
            }

            return new AkburaDependencyGraph(dependencies);
        }

        private void CollectDeclarations()
        {
            foreach (var component in _index.ComponentDescriptors)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var index = _documentIndices[component.DocumentVersion];
                AddName(_componentsByName, NormalizeName(component.ComponentMetadataName), index);
                AddName(_componentsByName, GetSimpleName(component.ComponentMetadataName), index);
            }

            foreach (var module in _index.ExternalAkcssDescriptors)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var index = _documentIndices[module.DocumentVersion];
                AddModuleName(module.DocumentVersion.LogicalName, index);
                AddModuleName(AkcssGeneratedModuleNames.GetMetadataName(_index.RootNamespace, module.ModuleIdentity), index);
                AddModuleName(module.ModuleIdentity.Replace('/', '.').Replace('\\', '.'), index);
            }
        }

        private void AddModuleName(string name, int index)
        {
            if (name.Length == 0)
            {
                return;
            }

            name = NormalizeName(name);
            AddName(_modulesByName, name, index);
            var stem = name.EndsWith(".akcss", StringComparison.Ordinal) ? name[..^6] : name;
            var simpleName = GetSimpleName(stem);
            AddName(_modulesByName, simpleName, index);
            AddName(_modulesByName, simpleName + ".akcss", index);
        }

        private void CollectDependencies(int sourceIndex)
        {
            var document = _documents[sourceIndex];

            for (var i = 0; i < _documents.Length; i++)
            {
                if (document.RequiresConservativeDependencies || _documents[i].HasGlobalUsings)
                {
                    AddEdge(sourceIndex, i, includesBody: true);
                }
            }

            foreach (var dependency in document.DependencyNames)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                switch (dependency.Kind)
                {
                    case DocumentDependencyKind.Component:
                    case DocumentDependencyKind.Identifier:
                    case DocumentDependencyKind.Alias:
                    case DocumentDependencyKind.StaticUsing:
                        AddMatches(_componentsByName, NormalizeName(dependency.Name), sourceIndex, includesBody: false);
                        AddMatches(_componentsByName, GetSimpleName(dependency.Name), sourceIndex, includesBody: false);
                        break;
                }

                if (dependency.Kind is DocumentDependencyKind.AkcssModule or DocumentDependencyKind.Identifier)
                {
                    AddMatches(_modulesByName, NormalizeName(dependency.Name), sourceIndex, includesBody: true);
                }

                // @apply candidates are intentionally retained in DocumentSyntaxVersion.
                // Imported modules (including imports in inline blocks/global usings)
                // are whole-body edges, so their operations and source mappings are covered.
            }
        }

        private void AddMatches(
            Dictionary<string, List<int>> declarations,
            string name,
            int sourceIndex,
            bool includesBody)
        {
            if (!declarations.TryGetValue(name, out var candidates))
            {
                return;
            }

            for (var i = 0; i < candidates.Count; i++)
            {
                AddEdge(sourceIndex, candidates[i], includesBody);
            }
        }

        private void AddEdge(int sourceIndex, int targetIndex, bool includesBody)
        {
            if (sourceIndex == targetIndex || ReferenceEquals(_documents[sourceIndex], _documents[targetIndex]))
            {
                return;
            }

            var edges = _edges[sourceIndex];
            if (!edges.TryGetValue(targetIndex, out var previous) || includesBody && !previous)
            {
                edges[targetIndex] = includesBody;
            }
        }

        private ImmutableArray<AkburaDocumentDependency> GetTransitiveDependencies(int sourceIndex)
        {
            var visited = new Dictionary<int, bool>();
            var queue = new Queue<int>();
            queue.Enqueue(sourceIndex);

            while (queue.Count != 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
                var current = queue.Dequeue();

                foreach (var edge in _edges[current])
                {
                    if (edge.Key == sourceIndex || ReferenceEquals(_documents[edge.Key], _documents[sourceIndex]))
                    {
                        continue;
                    }

                    if (!visited.TryGetValue(edge.Key, out var includesBody))
                    {
                        visited.Add(edge.Key, edge.Value);
                        queue.Enqueue(edge.Key);
                    }
                    else if (edge.Value && !includesBody)
                    {
                        visited[edge.Key] = true;
                    }
                }
            }

            var result = new AkburaDocumentDependency[visited.Count];
            var index = 0;

            foreach (var dependency in visited)
            {
                var document = _documents[dependency.Key];
                result[index++] = new AkburaDocumentDependency(GetDocumentIdentity(document), document, dependency.Value);
            }

            Array.Sort(result, CompareDependencies);
            return ImmutableArray.CreateRange(result);
        }

        private static void AddName(Dictionary<string, List<int>> declarations, string name, int index)
        {
            if (name.Length == 0)
            {
                return;
            }

            if (!declarations.TryGetValue(name, out var indices))
            {
                declarations.Add(name, indices = []);
            }

            if (!indices.Contains(index))
            {
                indices.Add(index);
            }
        }

        private static string NormalizeName(string name)
        {
            name = name.Trim();
            return name.StartsWith("global::", StringComparison.Ordinal) ? name[8..] : name;
        }

        private static string GetSimpleName(string name)
        {
            name = NormalizeName(name);
            var genericStart = name.IndexOfAny(['<', '{', '`']);
            if (genericStart >= 0)
            {
                name = name[..genericStart];
            }

            var separator = Math.Max(name.LastIndexOf('.'), Math.Max(name.LastIndexOf(':'), name.LastIndexOf('+')));
            return name[(separator + 1)..].TrimStart('@');
        }
    }
}
