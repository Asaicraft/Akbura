using System;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Threading;

namespace Akbura.Language.CodeGeneration;

// Keep the instrumentation ABI available in every configuration. Roslyn can load
// Release and ReleaseStats analyzer graphs in the same long-lived compiler process;
// the call sites remain guarded by STATS, so production generation pays no runtime cost.
internal enum GenerationStatisticCounter : byte
{
    ReadSourceText,
    FullParse,
    IncrementalParse,
    CompilationCreated,
    CompilationReused,
    SemanticModelCreated,
    ComponentGenerated,
    ComponentReused,
    AkcssGenerated,
    AkcssReused,
    GeneratedSourceTextCreated,
    DiagnosticBatchCreated,
    DiagnosticDocumentEvaluated,
    DiagnosticDocumentReused,
    DiagnosticSemanticModelCreated,
    GeneratedDiagnosticCreated,
    RoslynDiagnosticCreated,
    DiagnosticDescriptorCreated,
    DiagnosticDeduplicated,
    DiagnosticWorkspaceCollision,
    DiagnosticPublished,
    AkcssLookupRequest,
    AkcssLookupReused,
    Count,
}

internal enum GenerationStatisticStage : byte
{
    Parse,
    Compilation,
    Catalog,
    ComponentGeneration,
    AkcssGeneration,
    SourceTextCreation,
    SemanticBinding,
    ComponentPlanning,
    AkcssPlanning,
    CSharpEmission,
    ComponentBatch,
    AkcssBatch,
    CSharpProbeCompilation,
    DocumentBatch,
    DiagnosticBatch,
    DiagnosticSemantic,
    DiagnosticPublish,
    CSharpProbeBinding,
    CSharpProbeSemanticModel,
    CSharpProbeDiagnostics,
    CSharpTypeResolution,
    CSharpUtilityParameters,
    Count,
}

/// <summary>
/// Opt-in instrumentation. The ambient session flows with ExecutionContext into
/// Parallel.For workers; independent and nested measurements do not share totals.
/// </summary>
internal static partial class GenerationStatistics
{
    private static readonly AsyncLocal<GenerationStatisticsSession?> s_current = new();

    internal static GenerationStatisticsSession? Current => s_current.Value;

    public static GenerationStatisticsSession BeginMeasurement(Func<long>? getAllocatedBytes = null, bool trackOperations = false)
    {
        var session = new GenerationStatisticsSession(s_current.Value, getAllocatedBytes, trackOperations);
        s_current.Value = session;
        return session;
    }

    public static void Increment(GenerationStatisticCounter counter)
    {
        s_current.Value?.Increment(counter);
    }

    public static GenerationStageMeasurement Measure(GenerationStatisticStage stage)
    {
        return s_current.Value is { IsActive: true } session
            ? new GenerationStageMeasurement(session, stage)
            : GenerationStageMeasurement.Empty;
    }

    internal static void Restore(GenerationStatisticsSession session, GenerationStatisticsSession? previous)
    {
        if (ReferenceEquals(s_current.Value, session))
        {
            s_current.Value = previous;
        }
    }
}

internal sealed class GenerationStatisticsSession : IDisposable
{
    private readonly GenerationStatisticsSession? _previous;
    private readonly long[] _counters = new long[(int)GenerationStatisticCounter.Count];
    private readonly long[] _elapsed = new long[(int)GenerationStatisticStage.Count];
    private readonly long[] _allocatedBytes = new long[(int)GenerationStatisticStage.Count];
    private readonly long[] _invocationCounts = new long[(int)GenerationStatisticStage.Count];
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _endTimestamp;
    private long _invalidAllocationMeasurementCount;

    internal GenerationStatisticsSession(GenerationStatisticsSession? previous, Func<long>? getAllocatedBytes, bool trackOperations)
    {
        _previous = previous;
        GetAllocatedBytes = getAllocatedBytes;
        Operations = trackOperations ? new GenerationOperationTracker() : null;
    }

    internal bool IsActive => Volatile.Read(ref _endTimestamp) == 0;
    internal Func<long>? GetAllocatedBytes { get; }
    internal GenerationOperationTracker? Operations { get; }

    public GenerationStatisticsSnapshot GetSnapshot()
    {
        var counters = new long[_counters.Length];
        var elapsed = new long[_elapsed.Length];
        var allocatedBytes = new long[_allocatedBytes.Length];
        var invocationCounts = new long[_invocationCounts.Length];

        for (var i = 0; i < counters.Length; i++)
        {
            counters[i] = Interlocked.Read(ref _counters[i]);
        }

        for (var i = 0; i < elapsed.Length; i++)
        {
            elapsed[i] = Interlocked.Read(ref _elapsed[i]);
            allocatedBytes[i] = Interlocked.Read(ref _allocatedBytes[i]);
            invocationCounts[i] = Interlocked.Read(ref _invocationCounts[i]);
        }

        var endTimestamp = Volatile.Read(ref _endTimestamp);
        if (endTimestamp == 0)
        {
            endTimestamp = Stopwatch.GetTimestamp();
        }

        return new GenerationStatisticsSnapshot(
            counters,
            elapsed,
            allocatedBytes,
            invocationCounts,
            endTimestamp - _startTimestamp,
            GetAllocatedBytes != null,
            Interlocked.Read(ref _invalidAllocationMeasurementCount),
            Operations != null,
            Operations?.GetSnapshot() ?? ImmutableArray<GenerationOperationStatistics>.Empty);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _endTimestamp, Stopwatch.GetTimestamp(), 0) == 0)
        {
            Operations?.Complete();
            GenerationStatistics.Restore(this, _previous);
        }
    }

    internal void Increment(GenerationStatisticCounter counter)
    {
        if (IsActive)
        {
            Interlocked.Increment(ref _counters[(int)counter]);
        }
    }

    internal void AddMeasurement(GenerationStatisticStage stage, long elapsed, long allocatedBytes, bool invalidAllocation)
    {
        if (IsActive)
        {
            Interlocked.Add(ref _elapsed[(int)stage], elapsed);
            Interlocked.Increment(ref _invocationCounts[(int)stage]);
            if (invalidAllocation)
            {
                Interlocked.Increment(ref _invalidAllocationMeasurementCount);
            }
            else if (GetAllocatedBytes != null)
            {
                Interlocked.Add(ref _allocatedBytes[(int)stage], allocatedBytes);
            }
        }
    }
}

internal sealed class GenerationStageMeasurement : IDisposable
{
    internal static readonly GenerationStageMeasurement Empty = new();

    private readonly GenerationStatisticsSession? _session;
    private readonly GenerationStatisticStage _stage;
    private readonly long _startTimestamp;
    private readonly long _startAllocatedBytes;
    private readonly int _startThreadId;
    private int _disposed;

    private GenerationStageMeasurement()
    {
    }

    internal GenerationStageMeasurement(GenerationStatisticsSession session, GenerationStatisticStage stage)
    {
        _session = session;
        _stage = stage;
        if (session.GetAllocatedBytes is { } getAllocatedBytes)
        {
            _startThreadId = Thread.CurrentThread.ManagedThreadId;
            _startAllocatedBytes = getAllocatedBytes();
        }

        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_session != null && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            if (!_session.IsActive)
            {
                return;
            }

            var elapsed = Stopwatch.GetTimestamp() - _startTimestamp;
            long allocatedBytes = 0;
            var invalidAllocation = false;
            if (_session.GetAllocatedBytes is { } getAllocatedBytes)
            {
                if (_startThreadId == Thread.CurrentThread.ManagedThreadId)
                {
                    allocatedBytes = getAllocatedBytes() - _startAllocatedBytes;
                    invalidAllocation = allocatedBytes < 0;
                }
                else
                {
                    invalidAllocation = true;
                }
            }

            _session.AddMeasurement(_stage, elapsed, allocatedBytes, invalidAllocation);
        }
    }
}

/// <summary>
/// Inclusive elapsed durations, not additive CPU times. Document, planning,
/// emission and source-text durations accumulate across parallel workers and may
/// exceed Elapsed. Batch timings measure the enclosing Parallel.For wall time.
/// SemanticBindingElapsed covers semantic input and module-symbol resolution;
/// further lazy binding remains included in planning. CSharpProbeCompilationElapsed
/// overlaps either phase.
/// Optional allocation totals are likewise inclusive and not additive. The provider
/// must return a monotonic current-thread byte count and support concurrent callers.
/// Cross-thread or decreasing allocation samples are excluded and counted as invalid;
/// their elapsed duration and completed invocation still count. Invocation counts
/// include the first disposal only. Finish all scopes before taking the final snapshot.
/// Operation IDs are session-local. First invocations are reserved atomically at
/// scope creation; repeated inclusive costs are measurements, not predicted savings.
/// Reused counters count explicit pipeline reuse, not Roslyn's skipped callbacks.
/// </summary>
internal sealed class GenerationStatisticsSnapshot
{
    private readonly long[] _counters;
    private readonly long[] _elapsed;
    private readonly long[] _allocatedBytes;
    private readonly long[] _invocationCounts;

    internal GenerationStatisticsSnapshot(
        long[] counters,
        long[] elapsed,
        long[] allocatedBytes,
        long[] invocationCounts,
        long elapsedTimestampTicks,
        bool hasAllocationMeasurements,
        long invalidAllocationMeasurementCount,
        bool operationTrackingEnabled,
        ImmutableArray<GenerationOperationStatistics> operationMeasurements)
    {
        _counters = counters;
        _elapsed = elapsed;
        _allocatedBytes = allocatedBytes;
        _invocationCounts = invocationCounts;
        Elapsed = ToTimeSpan(elapsedTimestampTicks);
        HasAllocationMeasurements = hasAllocationMeasurements;
        InvalidAllocationMeasurementCount = invalidAllocationMeasurementCount;
        OperationTrackingEnabled = operationTrackingEnabled;
        OperationMeasurements = operationMeasurements;
    }

    public long ReadSourceTextCount => _counters[(int)GenerationStatisticCounter.ReadSourceText];
    public long FullParseCount => _counters[(int)GenerationStatisticCounter.FullParse];
    public long IncrementalParseCount => _counters[(int)GenerationStatisticCounter.IncrementalParse];
    public long CompilationCreatedCount => _counters[(int)GenerationStatisticCounter.CompilationCreated];
    public long CompilationReusedCount => _counters[(int)GenerationStatisticCounter.CompilationReused];
    public long SemanticModelCreatedCount => _counters[(int)GenerationStatisticCounter.SemanticModelCreated];
    public long ComponentGeneratedCount => _counters[(int)GenerationStatisticCounter.ComponentGenerated];
    public long ComponentReusedCount => _counters[(int)GenerationStatisticCounter.ComponentReused];
    public long AkcssGeneratedCount => _counters[(int)GenerationStatisticCounter.AkcssGenerated];
    public long AkcssReusedCount => _counters[(int)GenerationStatisticCounter.AkcssReused];
    public long GeneratedSourceTextCreatedCount => _counters[(int)GenerationStatisticCounter.GeneratedSourceTextCreated];
    public long DiagnosticBatchCreatedCount => _counters[(int)GenerationStatisticCounter.DiagnosticBatchCreated];
    public long DiagnosticDocumentEvaluatedCount => _counters[(int)GenerationStatisticCounter.DiagnosticDocumentEvaluated];
    public long DiagnosticDocumentReusedCount => _counters[(int)GenerationStatisticCounter.DiagnosticDocumentReused];
    public long DiagnosticSemanticModelCreatedCount => _counters[(int)GenerationStatisticCounter.DiagnosticSemanticModelCreated];
    public long GeneratedDiagnosticCreatedCount => _counters[(int)GenerationStatisticCounter.GeneratedDiagnosticCreated];
    public long RoslynDiagnosticCreatedCount => _counters[(int)GenerationStatisticCounter.RoslynDiagnosticCreated];
    public long DiagnosticDescriptorCreatedCount => _counters[(int)GenerationStatisticCounter.DiagnosticDescriptorCreated];
    public long DiagnosticDeduplicatedCount => _counters[(int)GenerationStatisticCounter.DiagnosticDeduplicated];
    public long DiagnosticWorkspaceCollisionCount => _counters[(int)GenerationStatisticCounter.DiagnosticWorkspaceCollision];
    public long DiagnosticPublishedCount => _counters[(int)GenerationStatisticCounter.DiagnosticPublished];
    public long AkcssLookupRequestCount => _counters[(int)GenerationStatisticCounter.AkcssLookupRequest];
    public long AkcssLookupReusedCount => _counters[(int)GenerationStatisticCounter.AkcssLookupReused];

    public TimeSpan Elapsed { get; }
    public bool HasAllocationMeasurements { get; }
    public long InvalidAllocationMeasurementCount { get; }
    public bool OperationTrackingEnabled { get; }
    public ImmutableArray<GenerationOperationStatistics> OperationMeasurements { get; }
    public TimeSpan ParseElapsed => GetElapsed(GenerationStatisticStage.Parse);
    public TimeSpan CompilationElapsed => GetElapsed(GenerationStatisticStage.Compilation);
    public TimeSpan CatalogElapsed => GetElapsed(GenerationStatisticStage.Catalog);
    public TimeSpan ComponentGenerationElapsed => GetElapsed(GenerationStatisticStage.ComponentGeneration);
    public TimeSpan AkcssGenerationElapsed => GetElapsed(GenerationStatisticStage.AkcssGeneration);
    public TimeSpan SourceTextCreationElapsed => GetElapsed(GenerationStatisticStage.SourceTextCreation);
    public TimeSpan SemanticBindingElapsed => GetElapsed(GenerationStatisticStage.SemanticBinding);
    public TimeSpan ComponentPlanningElapsed => GetElapsed(GenerationStatisticStage.ComponentPlanning);
    public TimeSpan AkcssPlanningElapsed => GetElapsed(GenerationStatisticStage.AkcssPlanning);
    public TimeSpan CSharpEmissionElapsed => GetElapsed(GenerationStatisticStage.CSharpEmission);
    public TimeSpan ComponentBatchElapsed => GetElapsed(GenerationStatisticStage.ComponentBatch);
    public TimeSpan AkcssBatchElapsed => GetElapsed(GenerationStatisticStage.AkcssBatch);
    public TimeSpan CSharpProbeCompilationElapsed => GetElapsed(GenerationStatisticStage.CSharpProbeCompilation);
    public TimeSpan DocumentBatchElapsed => GetElapsed(GenerationStatisticStage.DocumentBatch);
    public TimeSpan DiagnosticBatchElapsed => GetElapsed(GenerationStatisticStage.DiagnosticBatch);
    public TimeSpan DiagnosticSemanticElapsed => GetElapsed(GenerationStatisticStage.DiagnosticSemantic);
    public TimeSpan DiagnosticPublishElapsed => GetElapsed(GenerationStatisticStage.DiagnosticPublish);

    public TimeSpan GetStageElapsed(GenerationStatisticStage stage) => ToTimeSpan(_elapsed[GetStageIndex(stage)]);

    public long GetStageAllocatedBytes(GenerationStatisticStage stage) => _allocatedBytes[GetStageIndex(stage)];

    public long GetStageInvocationCount(GenerationStatisticStage stage) => _invocationCounts[GetStageIndex(stage)];

    private TimeSpan GetElapsed(GenerationStatisticStage stage) => GetStageElapsed(stage);

    private static int GetStageIndex(GenerationStatisticStage stage)
    {
        if (stage >= GenerationStatisticStage.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(stage));
        }

        return (int)stage;
    }

    private static TimeSpan ToTimeSpan(long timestampTicks)
    {
        return TimeSpan.FromTicks((long)(timestampTicks * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
    }
}
