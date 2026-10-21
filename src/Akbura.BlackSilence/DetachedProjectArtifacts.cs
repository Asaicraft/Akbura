using Akbura.Diagnostics;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;

namespace Akbura.BlackSilence;

internal enum ArtifactReuseDecision : byte
{
    ValidatedEquivalent,
    RequiresRebinding,
    UnsupportedDependency,
    MissingArtifact,
    ValidateDocumentDependencies,
    AffectedByChangedSymbol,
    AffectedByLookupCandidateChange,
    IncompleteSemanticDependencies,
}

/// <summary>
/// A complete current artifact generation. Every field is detached, including
/// nested values. There is deliberately no previous snapshot or semantic owner.
/// </summary>
internal sealed record DetachedProjectArtifacts(
    long Version,
    int Schema,
    GeneratorProjectOptions Options,
    DetachedCSharpInputs CSharpInputs,
    ImmutableArray<DetachedDocumentInput> Documents,
    ImmutableDictionary<string, DetachedGeneratedArtifact> Entries,
    ImmutableDictionary<string, DetachedDiagnosticArtifact> Diagnostics,
    bool DiagnosticsComputed)
{
    internal const int CurrentSchema = 3;

    internal static DetachedProjectArtifacts Create(BlackSilenceGenerationRequest request,
        ImmutableDictionary<string, GeneratedDocumentEntry> generatedEntries, ImmutableDictionary<string, DiagnosticDocumentEntry> diagnosticEntries)
    {
        var previous = request.PreviousSnapshot;
        var latest = request.State.Artifacts.Latest;
        var entries = ImmutableDictionary.CreateBuilder<string, DetachedGeneratedArtifact>(StringComparer.Ordinal);
        foreach (var pair in generatedEntries)
        {
            var source = pair.Value.Source;
            var dependencies = pair.Value.Version.Dependencies;
            // These characters were already selected by current generation. This
            // memo avoids detachment copies; it never decides semantic reuse.
            if (previous?.DetachedArtifacts != null && previous.Entries.TryGetValue(pair.Key, out var previousEntry) &&
                ReferenceEquals(previousEntry.Source.SourceText, source.SourceText) &&
                previous.DetachedArtifacts.Entries.TryGetValue(pair.Key, out var previousArtifact) && previousArtifact.HintName == source.HintName &&
                DetachedDependencyInput.Matches(previousArtifact.Dependencies, dependencies))
            {
                entries.Add(pair.Key, ReferenceEquals(previousArtifact.SemanticDependencies, pair.Value.SemanticDependencies) ? previousArtifact :
                    previousArtifact with { SemanticDependencies = pair.Value.SemanticDependencies, DocumentPath = pair.Value.Version.Document.FilePath });
                continue;
            }
            if (latest != null && latest.Entries.TryGetValue(pair.Key, out var currentArtifact) &&
                ReferenceEquals(currentArtifact.Source, source.SourceText) && currentArtifact.HintName == source.HintName &&
                DetachedDependencyInput.Matches(currentArtifact.Dependencies, dependencies))
            {
                entries.Add(pair.Key, ReferenceEquals(currentArtifact.SemanticDependencies, pair.Value.SemanticDependencies) ? currentArtifact :
                    currentArtifact with { SemanticDependencies = pair.Value.SemanticDependencies, DocumentPath = pair.Value.Version.Document.FilePath });
                continue;
            }
            // Never accept arbitrary SourceText implementations into the cache.
            // From(string) owns characters, not the writer, reader or compilation.
            entries.Add(pair.Key, new DetachedGeneratedArtifact(source.HintName,
                SourceText.From(source.SourceText.ToString(), Encoding.UTF8, source.SourceText.ChecksumAlgorithm), DetachedDependencyInput.Create(dependencies),
                pair.Value.SemanticDependencies, pair.Value.Version.Document.FilePath));
#if STATS
            GenerationStatistics.Increment(GenerationStatisticCounter.DetachedSourceTextCreated);
#endif
        }
        var diagnostics = ImmutableDictionary.CreateBuilder<string, DetachedDiagnosticArtifact>(StringComparer.Ordinal);
        foreach (var pair in diagnosticEntries)
        {
            diagnostics.Add(pair.Key, new DetachedDiagnosticArtifact(pair.Value.Diagnostics, DetachedDependencyInput.Create(pair.Value.Version.Dependencies),
                pair.Value.SemanticDependencies));
        }
        return new DetachedProjectArtifacts(request.Version, CurrentSchema, request.Options, request.ArtifactInputs!,
            request.DetachedDocuments, entries.ToImmutable(), diagnostics.ToImmutable(), request.ComputeDiagnostics);
    }
}

internal sealed record DetachedGeneratedArtifact(string HintName, SourceText Source, ImmutableArray<DetachedDependencyInput> Dependencies,
    DetachedCSharpDependencySet? SemanticDependencies = null, string DocumentPath = "");
internal sealed record DetachedDiagnosticArtifact(ImmutableArray<AkburaDiagnosticRecord> Records, ImmutableArray<DetachedDependencyInput> Dependencies,
    DetachedCSharpDependencySet? SemanticDependencies = null);

internal sealed record DetachedDependencyInput(string Identity, bool IncludesBody, ImmutableArray<string> Shape)
{
    internal static ImmutableArray<DetachedDependencyInput> Create(ImmutableArray<DependencyGenerationVersion> dependencies) =>
        dependencies.Select(static dependency => new DetachedDependencyInput(dependency.Identity, dependency.IncludesBody,
            dependency.IncludesBody ? [dependency.Document.GenerationShape] : dependency.Document.SurfaceShape)).ToImmutableArray();

    internal static bool Matches(ImmutableArray<DetachedDependencyInput> inputs, ImmutableArray<DependencyGenerationVersion> dependencies) =>
        inputs.Length == dependencies.Length && !inputs.Where((input, index) =>
            input.Identity != dependencies[index].Identity || input.IncludesBody != dependencies[index].IncludesBody ||
            (input.IncludesBody ? input.Shape.Length != 1 || input.Shape[0] != dependencies[index].Document.GenerationShape :
                !input.Shape.SequenceEqual(dependencies[index].Document.SurfaceShape, StringComparer.Ordinal))).Any();
}

internal sealed record DetachedDocumentInput(
    string Path,
    string LogicalName,
    int Kind,
    string Shape,
    bool Supported,
    ImmutableArray<string> LookupNames,
    ImmutableArray<string> SurfaceShape,
    bool HasGlobalUsings)
{
    internal static ImmutableArray<DetachedDocumentInput> Create(BlackSilenceGenerationRequest request)
    {
        // Global directives can originate in another ordinary markup document.
        // Their complete conservative lookup space belongs to every consumer.
        var globalDocuments = request.Documents.Where(static document => document.HasGlobalUsings).ToArray();
        var previous = request.PreviousSnapshot;
        var previousGlobals = previous?.Documents.Where(static document => document.HasGlobalUsings).ToArray();
        var reuseFacts = previous?.DetachedArtifacts != null && previous.Options == request.Options &&
            ReferenceEquals(previous.Index.Compilation.CSharpCompilation, request.State.CSharpCompilation) &&
            previous.Documents.Length == request.Documents.Length && previous.DetachedArtifacts.Documents.Length == request.Documents.Length &&
            previousGlobals!.Length == globalDocuments.Length &&
            !previousGlobals.Where((document, index) => !ReferenceEquals(document, globalDocuments[index])).Any();
        var globalNames = globalDocuments.SelectMany(static document => document.DependencyNames).ToImmutableArray();
        var globalSupported = globalDocuments.All(static document => document.CanReuse && !document.RequiresConservativeDependencies);
        return request.Documents.Select((document, index) => reuseFacts && ReferenceEquals(document, previous!.Documents[index])
            ? previous.DetachedArtifacts!.Documents[index]
            : new DetachedDocumentInput(document.FilePath, document.LogicalName,
            (int)document.Kind, document.GenerationShape, document.CanReuse && !document.RequiresConservativeDependencies && globalSupported,
            document.DependencyNames.Concat(globalNames).SelectMany(static name => new[] { name.Name, name.Alias })
                .SelectMany(static name => Microsoft.CodeAnalysis.CSharp.SyntaxFactory.ParseTokens(name)
                    .Where(static token => token.RawKind == (int)Microsoft.CodeAnalysis.CSharp.SyntaxKind.IdentifierToken)
                    .Select(static token => token.ValueText).Prepend(name))
                .Distinct(StringComparer.Ordinal).ToImmutableArray(), document.SurfaceShape, document.HasGlobalUsings)).ToImmutableArray();
    }

    internal bool Matches(DetachedDocumentInput other) => Path == other.Path && LogicalName == other.LogicalName &&
        Kind == other.Kind && Shape == other.Shape && Supported == other.Supported &&
        LookupNames.SequenceEqual(other.LookupNames, StringComparer.Ordinal);

    internal bool HasSameDeclarations(DetachedDocumentInput other) => Path == other.Path && LogicalName == other.LogicalName &&
        Kind == other.Kind && HasGlobalUsings == other.HasGlobalUsings && (Shape == other.Shape || Supported && other.Supported) &&
        SurfaceShape.SequenceEqual(other.SurfaceShape, StringComparer.Ordinal);

    internal bool Excludes(ImmutableArray<string> names) => Supported &&
        !LookupNames.Any(lookup => names.Any(name => lookup.IndexOf(name, StringComparison.Ordinal) >= 0));
}

/// <summary>
/// One project/options node owns one latest detached generation. Its bounded
/// weak handles validate exact reference/option identity without rooting graphs.
/// Publication replaces both artifacts and handles; there is no identity history.
/// </summary>
internal sealed class BoundedArtifactStore
{
    private readonly object _gate = new();
    private ArtifactPublication? _latest;
    private long _nextIdentity;
    private long _nextVersion;

    internal long GetNextIdentity() => Interlocked.Increment(ref _nextIdentity);
    internal long GetNextVersion() => Interlocked.Increment(ref _nextVersion);
    internal DetachedProjectArtifacts? Latest => Read()?.Artifacts;
    internal ArtifactPublication? Read() => Volatile.Read(ref _latest);

    internal void Publish(DetachedProjectArtifacts artifacts, BlackSilenceProjectState state,
        BlackSilenceProjectSnapshot snapshot, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            state.Publish(snapshot, cancellationToken);
            if (_latest == null || artifacts.Version > _latest.Artifacts.Version)
            {
                Volatile.Write(ref _latest, new ArtifactPublication(artifacts, snapshot.Identities));
            }
        }
    }
}

internal sealed record ArtifactPublication(DetachedProjectArtifacts Artifacts, ImmutableArray<ArtifactIdentity> Identities);
internal sealed record ArtifactIdentity(long Id, WeakReference<object> Target);

/// <summary>
/// A short-lived validator, never a pipeline output or persistent root. Only keys
/// used by this revision are exported, as weak handles, alongside its publication.
/// </summary>
internal sealed class ArtifactIdentitySession(BoundedArtifactStore owner, ArtifactPublication? previous)
{
    private readonly System.Collections.Generic.Dictionary<object, long> _current = new(ReferenceIdentityComparer.Instance);

    internal long GetIdentity(object? value)
    {
        if (value == null)
        {
            return 0;
        }
        if (_current.TryGetValue(value, out var existing))
        {
            return existing;
        }
        if (previous != null)
        {
            foreach (var identity in previous.Identities)
            {
                if (identity.Target.TryGetTarget(out var target) && ReferenceEquals(value, target))
                {
                    _current.Add(value, identity.Id);
                    return identity.Id;
                }
            }
        }
        var created = owner.GetNextIdentity();
        _current.Add(value, created);
        return created;
    }

    internal ImmutableArray<ArtifactIdentity> Detach() => _current.Select(static pair =>
        new ArtifactIdentity(pair.Value, new WeakReference<object>(pair.Key))).ToImmutableArray();

    private sealed class ReferenceIdentityComparer : System.Collections.Generic.IEqualityComparer<object>
    {
        internal static readonly ReferenceIdentityComparer Instance = new();
        public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
        public int GetHashCode(object value) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(value);
    }
}
