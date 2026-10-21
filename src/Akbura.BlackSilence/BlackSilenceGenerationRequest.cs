using Akbura.Language.CodeGeneration;
using Akbura.Language.Binder;
using System.Collections.Immutable;
using System;

namespace Akbura.BlackSilence;

internal sealed class BlackSilenceGenerationRequest
{
#if STATS
    private readonly System.Collections.Generic.Dictionary<string, ArtifactReuseDecision> _semanticDependencyDecisions = new(StringComparer.Ordinal);

    internal void ObserveSemanticDocument(string path, ArtifactReuseDecision decision)
    {
        if (!_semanticDependencyDecisions.TryGetValue(path, out var previous) || previous == ArtifactReuseDecision.ValidatedEquivalent)
        {
            _semanticDependencyDecisions[path] = decision;
        }
    }
#endif
    private readonly WeakReference<BlackSilenceProjectSnapshot>? _previousSnapshot;
    public BlackSilenceGenerationRequest(
        BlackSilenceProjectState state,
        long version,
        GeneratorProjectOptions options,
        AkburaProjectIndex index,
        ImmutableArray<DocumentSyntaxVersion> documents,
        object declarationEnvironment,
        ImmutableArray<ComponentGenerationRequest> components,
        ImmutableArray<AkcssGenerationRequest> externalAkcss,
        ImmutableArray<AkcssGenerationRequest> inlineAkcss,
        BlackSilenceProjectSnapshot? previousSnapshot,
        ImmutableArray<DocumentDiagnosticRequest> diagnostics = default,
        bool computeDiagnostics = true,
        CompilationBoundProbeStore.Session? probeSession = null)
    {
        State = state;
        Version = version;
        Options = options;
        Index = index;
        Documents = documents;
        DeclarationEnvironment = declarationEnvironment;
        Components = components;
        ExternalAkcss = externalAkcss;
        InlineAkcss = inlineAkcss;
        _previousSnapshot = previousSnapshot == null ? null : new WeakReference<BlackSilenceProjectSnapshot>(previousSnapshot);
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
        ComputeDiagnostics = computeDiagnostics;
        ProbeSession = probeSession;
    }

    public BlackSilenceProjectState State { get; }
    public long Version { get; }
    public GeneratorProjectOptions Options { get; }
    public AkburaProjectIndex Index { get; }
    public ImmutableArray<DocumentSyntaxVersion> Documents { get; }
    public object DeclarationEnvironment { get; }
    public ImmutableArray<ComponentGenerationRequest> Components { get; private set; }
    public ImmutableArray<AkcssGenerationRequest> ExternalAkcss { get; private set; }
    public ImmutableArray<AkcssGenerationRequest> InlineAkcss { get; private set; }
    public BlackSilenceProjectSnapshot? PreviousSnapshot => _previousSnapshot != null && _previousSnapshot.TryGetTarget(out var snapshot) ? snapshot : null;
    public ImmutableArray<DocumentDiagnosticRequest> Diagnostics { get; private set; }
    public bool ComputeDiagnostics { get; }
    internal CompilationBoundProbeStore.Session? ProbeSession { get; }

    // Initialized before publication into the incremental graph, immutable thereafter.
    internal DetachedCSharpInputs? ArtifactInputs { get; private set; }
    internal ImmutableArray<DetachedDocumentInput> DetachedDocuments { get; private set; }
    internal ArtifactValidation ArtifactValidation { get; private set; }
    internal bool HasArtifactCandidate { get; private set; }
    internal ImmutableArray<ArtifactIdentity> ArtifactIdentities { get; private set; }

    internal void InitializeArtifacts(System.Threading.CancellationToken cancellationToken)
    {
        if (!Options.CrossCompilationReuse)
        {
            return;
        }
        Index.Compilation.CollectCSharpSemanticDependencies = true;
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.ArtifactValidation);
#endif
        DetachedDocuments = DetachedDocumentInput.Create(this);
        var publication = State.Artifacts.Read();
        var context = PreviousSnapshot;
        if (context?.ArtifactInputs != null && ReferenceEquals(context.Index.Compilation.CSharpCompilation, State.CSharpCompilation))
        {
            // Same-compilation markup edits do not rescan unrelated C# files.
            // Only detached facts and weak identity handles are shared here.
            ArtifactInputs = context.ArtifactInputs;
            ArtifactIdentities = context.Identities;
        }
        else
        {
            var identities = new ArtifactIdentitySession(State.Artifacts, publication);
            ArtifactInputs = DetachedCSharpInputs.Create(State.CSharpCompilation, identities, cancellationToken);
            ArtifactIdentities = identities.Detach();
        }
        var candidate = publication?.Artifacts;
        HasArtifactCandidate = candidate != null;
        ArtifactValidation = DetachedArtifactReuse.Validate(this, candidate, cancellationToken);
        if (PreviousSnapshot == null)
        {
#if STATS
            using var selectionMeasurement = GenerationStatistics.Measure(GenerationStatisticStage.ArtifactSelection);
#endif
            cancellationToken.ThrowIfCancellationRequested();
            Components = DetachedArtifactReuse.Components(this, candidate, cancellationToken);
            ExternalAkcss = DetachedArtifactReuse.Akcss(this, ExternalAkcss, candidate, cancellationToken);
            InlineAkcss = DetachedArtifactReuse.Akcss(this, InlineAkcss, candidate, cancellationToken);
            Diagnostics = DetachedArtifactReuse.Diagnostics(this, candidate, cancellationToken);
#if STATS
            foreach (var decision in _semanticDependencyDecisions.Values)
            {
                GenerationStatistics.Increment(GenerationStatisticCounter.SemanticDependencyDocumentsExamined);
                GenerationStatistics.Increment(decision == ArtifactReuseDecision.ValidatedEquivalent
                    ? GenerationStatisticCounter.SemanticDependencyDocumentsIndependent
                    : decision == ArtifactReuseDecision.IncompleteSemanticDependencies ? GenerationStatisticCounter.SemanticDependencyDocumentsIncomplete
                    : GenerationStatisticCounter.SemanticDependencyDocumentsAffected);
            }
#endif
        }
        cancellationToken.ThrowIfCancellationRequested();
    }
}
