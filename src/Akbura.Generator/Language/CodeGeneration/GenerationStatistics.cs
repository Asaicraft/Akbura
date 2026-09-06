#if STATS
using System;
using System.Diagnostics;
using System.Threading;

namespace Akbura.Language.CodeGeneration;

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
    Count,
}

/// <summary>
/// Opt-in instrumentation. The ambient session flows with ExecutionContext into
/// Parallel.For workers; independent and nested measurements do not share totals.
/// </summary>
internal static class GenerationStatistics
{
    private static readonly AsyncLocal<GenerationStatisticsSession?> s_current = new();

    internal static GenerationStatisticsSession? Current => s_current.Value;

    public static GenerationStatisticsSession BeginMeasurement()
    {
        var session = new GenerationStatisticsSession(s_current.Value);
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
    private readonly long _startTimestamp = Stopwatch.GetTimestamp();
    private long _endTimestamp;

    internal GenerationStatisticsSession(GenerationStatisticsSession? previous)
    {
        _previous = previous;
    }

    internal bool IsActive => Volatile.Read(ref _endTimestamp) == 0;

    public GenerationStatisticsSnapshot GetSnapshot()
    {
        var counters = new long[_counters.Length];
        var elapsed = new long[_elapsed.Length];

        for (var i = 0; i < counters.Length; i++)
        {
            counters[i] = Interlocked.Read(ref _counters[i]);
        }

        for (var i = 0; i < elapsed.Length; i++)
        {
            elapsed[i] = Interlocked.Read(ref _elapsed[i]);
        }

        var endTimestamp = Volatile.Read(ref _endTimestamp);
        if (endTimestamp == 0)
        {
            endTimestamp = Stopwatch.GetTimestamp();
        }

        return new GenerationStatisticsSnapshot(counters, elapsed, endTimestamp - _startTimestamp);
    }

    public void Dispose()
    {
        if (Interlocked.CompareExchange(ref _endTimestamp, Stopwatch.GetTimestamp(), 0) == 0)
        {
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

    internal void AddElapsed(GenerationStatisticStage stage, long elapsed)
    {
        if (IsActive)
        {
            Interlocked.Add(ref _elapsed[(int)stage], elapsed);
        }
    }
}

internal sealed class GenerationStageMeasurement : IDisposable
{
    internal static readonly GenerationStageMeasurement Empty = new();

    private readonly GenerationStatisticsSession? _session;
    private readonly GenerationStatisticStage _stage;
    private readonly long _startTimestamp;
    private int _disposed;

    private GenerationStageMeasurement()
    {
    }

    internal GenerationStageMeasurement(GenerationStatisticsSession session, GenerationStatisticStage stage)
    {
        _session = session;
        _stage = stage;
        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_session != null && Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            _session.AddElapsed(_stage, Stopwatch.GetTimestamp() - _startTimestamp);
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
/// Reused counters count explicit pipeline reuse, not Roslyn's skipped callbacks.
/// </summary>
internal sealed class GenerationStatisticsSnapshot
{
    private readonly long[] _counters;
    private readonly long[] _elapsed;

    internal GenerationStatisticsSnapshot(long[] counters, long[] elapsed, long elapsedTimestampTicks)
    {
        _counters = counters;
        _elapsed = elapsed;
        Elapsed = ToTimeSpan(elapsedTimestampTicks);
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

    public TimeSpan Elapsed { get; }
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

    private TimeSpan GetElapsed(GenerationStatisticStage stage) => ToTimeSpan(_elapsed[(int)stage]);

    private static TimeSpan ToTimeSpan(long timestampTicks)
    {
        return TimeSpan.FromTicks((long)(timestampTicks * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
    }
}
#endif
