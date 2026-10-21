using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Akbura.Language.Syntax.Green;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Akbura.Language.Binder;

/// <summary>
/// Compilation-provider-owned, latest-only prepared probes. This store never enters
/// detached artifacts. Projection reuse does not imply reuse of a semantic result.
/// </summary>
internal sealed class CompilationBoundProbeStore(CSharpCompilation owner)
{
    internal const int MaxEntriesPerDocument = 128;
    internal const int MaxEntriesPerProject = 1024;
    internal const int MaxProjectionCharacters = 32768;
    internal const int MaxProjectProjectionCharacters = 2 * 1024 * 1024;
    private Generation? _latest;

    internal CSharpCompilation Owner => owner;
    internal Generation? Latest => Volatile.Read(ref _latest);

    internal Session? Begin(CSharpCompilation compilation, long version, ImmutableArray<DocumentSyntaxVersion> documents,
        string rootNamespace, string projectDirectory, CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(owner, compilation) || documents.Any(static document => !document.CanReuse))
        {
            return null;
        }
        cancellationToken.ThrowIfCancellationRequested();
        return new Session(this, version, documents, rootNamespace, projectDirectory, Latest, cancellationToken);
    }

    // The owning project state's existing publication lock serializes this call.
    internal void Publish(Generation generation)
    {
#if STATS
        using var publication = GenerationStatistics.Measure(GenerationStatisticStage.CompilationBoundProbePublication);
#endif
        if (ReferenceEquals(generation.Owner, owner) && (Latest == null || generation.Version > Latest.Version))
        {
#if STATS
            if (Latest != null)
            {
                GenerationStatistics.Increment(GenerationStatisticCounter.CompilationBoundProbeGenerationReplaced);
            }
            GenerationStatistics.Increment(GenerationStatisticCounter.CompilationBoundProbeEntriesPublished, generation.EntryCount);
            GenerationStatistics.Increment(GenerationStatisticCounter.CompilationBoundProjectionCharactersPublished,
                generation.Documents.Values.Sum(static document => document.Entries.Values.Sum(static entry => entry.Characters)));
#endif
            Volatile.Write(ref _latest, generation);
        }
    }

    internal sealed class Generation(CSharpCompilation owner, long version, string rootNamespace, string projectDirectory,
        ImmutableArray<DeclarationInput> declarations, ImmutableDictionary<string, DocumentGeneration> documents)
    {
        internal CSharpCompilation Owner { get; } = owner;
        internal long Version { get; } = version;
        internal string RootNamespace { get; } = rootNamespace;
        internal string ProjectDirectory { get; } = projectDirectory;
        internal ImmutableArray<DeclarationInput> Declarations { get; } = declarations;
        internal ImmutableDictionary<string, DocumentGeneration> Documents { get; } = documents;
        internal int EntryCount => Documents.Values.Sum(static document => document.Entries.Count);
    }

    internal sealed record DeclarationInput(string Path, SyntaxTreeKind Kind, string LogicalName, string ComponentName, ImmutableArray<string> Surface)
    {
        internal bool Matches(DeclarationInput other) => Path == other.Path && Kind == other.Kind && LogicalName == other.LogicalName &&
            ComponentName == other.ComponentName && Surface.SequenceEqual(other.Surface, StringComparer.Ordinal);
    }

    internal sealed record DocumentGeneration(string Source, ProbeDocumentContract Contract, ImmutableDictionary<RegionKey, Entry> Entries);
    internal sealed record Entry(CSharp.CompilationUnitSyntax Projection, int Characters, WeakReference<GreenNode> SourceGreen);

    internal readonly record struct RegionKey(int Position, ushort Kind, CSharp.ExpressionSyntax Expression, string Text, ITypeSymbol? Target)
    {
        public bool Equals(RegionKey other) => Position == other.Position && Kind == other.Kind && ReferenceEquals(Target, other.Target) &&
            Text == other.Text && Microsoft.CodeAnalysis.CSharp.SyntaxFactory.AreEquivalent(Expression, other.Expression);
        public override int GetHashCode() => unchecked((Position * 397) ^ Kind ^ StringComparer.Ordinal.GetHashCode(Text) ^
            (Target == null ? 0 : RuntimeHelpers.GetHashCode(Target)));
    }

    /// <summary>
    /// One private generation attempt. Published entries have no reverse edge to this
    /// session, a markup compilation, a semantic model, a binder or an old generation.
    /// </summary>
    internal sealed class Session
    {
        private readonly CompilationBoundProbeStore _store;
        private readonly long _version;
        private readonly string _rootNamespace;
        private readonly string _projectDirectory;
        private readonly ImmutableArray<DeclarationInput> _declarations;
        private Generation? _previous;
        private readonly ConcurrentDictionary<string, DocumentSession> _current = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _sources;
        private readonly CancellationToken _cancellationToken;
        private volatile bool _sealed;

        internal Session(CompilationBoundProbeStore store, long version, ImmutableArray<DocumentSyntaxVersion> documents,
            string rootNamespace, string projectDirectory, Generation? previous, CancellationToken cancellationToken)
        {
            _store = store;
            _version = version;
            _rootNamespace = rootNamespace;
            _projectDirectory = projectDirectory;
            _cancellationToken = cancellationToken;
            _declarations = documents.Select(static document => new DeclarationInput(document.FilePath, document.Kind,
                document.LogicalName, document.SyntaxTree.ComponentName ?? string.Empty, document.SurfaceShape)).ToImmutableArray();
            _sources = documents.ToDictionary(static document => document.FilePath, static document => document.GenerationShape, StringComparer.Ordinal);
            if (previous != null && previous.RootNamespace == rootNamespace && previous.ProjectDirectory == projectDirectory &&
                previous.Declarations.Length == _declarations.Length &&
                previous.Declarations.Zip(_declarations, static (left, right) => left.Matches(right)).All(static equal => equal))
            {
                _previous = previous;
            }
        }

        internal DocumentSession? GetDocument(AkburaSyntaxTree tree)
        {
            if (_sealed || !_sources.ContainsKey(tree.FilePath))
            {
                return null;
            }
            _cancellationToken.ThrowIfCancellationRequested();
            if (_current.TryGetValue(tree.FilePath, out var current))
            {
                return current;
            }
#if STATS
            using var matching = GenerationStatistics.Measure(GenerationStatisticStage.CrossRevisionRegionMatching);
#endif
            var contract = ProbeDocumentContract.Create(tree, _cancellationToken);
            if (contract == null)
            {
                return null;
            }
            var previous = _previous != null && _previous.Documents.TryGetValue(tree.FilePath, out var candidate) && contract.Matches(candidate.Contract)
                ? candidate : null;
#if STATS
            if (_previous != null && previous == null)
            {
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionRegionContextRejected);
            }
#endif
            return _current.GetOrAdd(tree.FilePath, new DocumentSession(contract, previous, _cancellationToken));
        }

        internal Generation Seal()
        {
            _cancellationToken.ThrowIfCancellationRequested();
            var result = ImmutableDictionary.CreateBuilder<string, DocumentGeneration>(StringComparer.Ordinal);
            var count = 0;
            var characters = 0;
            foreach (var pair in _sources.OrderBy(static pair => pair.Key, StringComparer.Ordinal))
            {
                _cancellationToken.ThrowIfCancellationRequested();
                DocumentGeneration? document = null;
                if (_current.TryGetValue(pair.Key, out var current))
                {
                    document = new DocumentGeneration(pair.Value, current.Contract, current.Entries.ToImmutableDictionary());
                }
                else if (_previous != null && _previous.Documents.TryGetValue(pair.Key, out var previous) && previous.Source == pair.Value)
                {
                    document = previous;
                }
                if (document == null)
                {
                    continue;
                }
                var entries = ImmutableDictionary.CreateBuilder<RegionKey, Entry>();
                foreach (var entry in document.Entries.OrderBy(static pair => pair.Key.Position).ThenBy(static pair => pair.Key.Text, StringComparer.Ordinal))
                {
                    if (entries.Count >= MaxEntriesPerDocument || count >= MaxEntriesPerProject ||
                        characters + entry.Value.Characters > MaxProjectProjectionCharacters)
                    {
                        continue;
                    }
                    entries.Add(entry.Key, entry.Value);
                    count++;
                    characters += entry.Value.Characters;
                }
                result.Add(pair.Key, document with { Entries = entries.ToImmutable() });
            }
            Discard();
            return new Generation(_store.Owner, _version, _rootNamespace, _projectDirectory, _declarations, result.ToImmutable());
        }

        internal void Discard()
        {
            _sealed = true;
            _previous = null;
            foreach (var document in _current.Values)
            {
                document.Close();
            }
            _current.Clear();
        }
    }

    internal sealed class DocumentSession(ProbeDocumentContract contract, DocumentGeneration? previous, CancellationToken cancellationToken)
    {
        private DocumentGeneration? _previous = previous;
        private volatile bool _closed;
        internal ProbeDocumentContract Contract => contract;
        internal ConcurrentDictionary<RegionKey, Entry> Entries { get; } = new();

        internal bool TryGetProjection(AkburaSyntax scope, CSharp.ExpressionSyntax expression, ITypeSymbol? target, out CSharp.CompilationUnitSyntax projection)
        {
            projection = null!;
            cancellationToken.ThrowIfCancellationRequested();
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionRegionCandidate);
            using var lookup = GenerationStatistics.Measure(GenerationStatisticStage.CrossRevisionProjectionLookup);
#endif
            if (_closed || !TryKey(scope, expression, target, out var key) || _previous == null || !_previous.Entries.TryGetValue(key, out var entry))
            {
#if STATS
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionProjectionMiss);
#endif
                return false;
            }
            Entries.TryAdd(key, entry);
            projection = entry.Projection;
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionRegionContextMatch);
            if (entry.SourceGreen.TryGetTarget(out var green) && ReferenceEquals(green, scope.Green))
            {
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionRegionGreenIdentityMatch);
            }
            GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionProjectionHit);
            GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionProjectionConstructionSkipped);
#endif
            return true;
        }

        internal void Record(AkburaSyntax scope, CSharp.ExpressionSyntax expression, ITypeSymbol? target, CSharp.CompilationUnitSyntax projection)
        {
            if (_closed || Entries.Count >= MaxEntriesPerDocument || projection.ContainsDiagnostics || projection.ContainsDirectives ||
                projection.ContainsSkippedText || !TryKey(scope, expression, target, out var key))
            {
                return;
            }
            cancellationToken.ThrowIfCancellationRequested();
            var characters = projection.ToFullString().Length;
            if (characters <= MaxProjectionCharacters)
            {
                Entries.TryAdd(key, new Entry(projection, characters, new WeakReference<GreenNode>(scope.Green)));
            }
#if STATS
            else
            {
                GenerationStatistics.Increment(GenerationStatisticCounter.CrossRevisionProjectionAdmissionRejectedSize);
            }
#endif
        }

        private bool TryKey(AkburaSyntax scope, CSharp.ExpressionSyntax expression, ITypeSymbol? target, out RegionKey key)
        {
            key = default;
            if (expression.ContainsDiagnostics || expression.ContainsDirectives || expression.ContainsSkippedText || expression.ContainsAnnotations ||
                !contract.TryGetRegionPosition(scope, out var position))
            {
                return false;
            }
            key = new RegionKey(position, scope.RawKind, expression, expression.ToFullString(), target);
            return true;
        }

        internal void Close()
        {
            _closed = true;
            _previous = null;
            Entries.Clear();
        }
    }
}
