using Microsoft.CodeAnalysis.CSharp;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Binder;
using System.Threading;

namespace Akbura.BlackSilence;

/// <summary>
/// A best-effort cache owned by one incremental compilation-provider value.
/// Losing the cache, concurrent branches and cancellation cannot change output.
/// </summary>
internal sealed class BlackSilenceProjectState
{
    private readonly object _gate = new();
    private BlackSilenceProjectSnapshot? _snapshot;

    public BlackSilenceProjectState(CSharpCompilation compilation, BoundedArtifactStore? artifacts = null)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.ProjectStateConstruction);
#endif
        CSharpCompilation = compilation;
        Artifacts = artifacts ?? new BoundedArtifactStore();
#if AKBURA_PROJECTION_REUSE || AKBURA_SEMANTIC_REUSE
#if AKBURA_PROJECTION_REUSE
        const bool reuseProjection = true;
#else
        const bool reuseProjection = false;
#endif
#if AKBURA_SEMANTIC_REUSE
        const bool reuseBinding = true;
#else
        const bool reuseBinding = false;
#endif
        ProbeStore = new CompilationBoundProbeStore(compilation, reuseProjection, reuseBinding);
#endif
#if STATS
        if (GenerationStatistics.IsInvalidationTrackingEnabled)
        {
            BlackSilenceInvalidationTrace.Record("ProjectState", string.Empty, "Created", [],
                ("State", BlackSilenceInvalidationTrace.Id(this)), ("CSharpCompilation", BlackSilenceInvalidationTrace.Id(compilation)));
        }
#endif
    }

    public CSharpCompilation CSharpCompilation { get; }
    public BoundedArtifactStore Artifacts { get; }
    internal CompilationBoundProbeStore? ProbeStore { get; }

    public BlackSilenceProjectSnapshot? TryGetSnapshot(GeneratorProjectOptions options)
    {
#if STATS
        using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.PreviousSnapshotLookup);
#endif
        lock (_gate)
        {
#if STATS
            if (GenerationStatistics.IsInvalidationTrackingEnabled)
            {
                BlackSilenceInvalidationTrace.Record("PreviousSnapshot", string.Empty,
                    _snapshot == null ? "Absent" : _snapshot.Options == options ? "Available" : "OptionsMismatch", [],
                    ("State", BlackSilenceInvalidationTrace.Id(this)), ("Snapshot", BlackSilenceInvalidationTrace.Id(_snapshot)));
            }
#endif
            return _snapshot?.Options == options ? _snapshot : null;
        }
    }

    public long GetNextVersion() => Artifacts.GetNextVersion();

    public void Publish(BlackSilenceProjectSnapshot snapshot, CancellationToken cancellationToken = default)
    {
#if STATS
        using var publication = GenerationStatistics.Measure(GenerationStatisticStage.CompilationBoundProbePublication);
#endif
        lock (_gate)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_snapshot == null || snapshot.Version > _snapshot.Version)
            {
                if (snapshot.ProbeGeneration != null)
                {
                    ProbeStore?.Publish(snapshot.ProbeGeneration);
                }
                _snapshot = snapshot;
#if STATS
                if (GenerationStatistics.IsInvalidationTrackingEnabled)
                {
                    BlackSilenceInvalidationTrace.Record("SnapshotPublication", string.Empty, "Published", [],
                        ("State", BlackSilenceInvalidationTrace.Id(this)), ("Version", snapshot.Version));
                }
#endif
            }
        }
    }
}
