#if STATS
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Akbura.Language.CodeGeneration;

internal enum GenerationStatisticOperation : byte
{
    AkcssLookupSymbols,
    UtilityParameterBinding,
}

internal readonly record struct GenerationOperationStatistics(
    GenerationStatisticOperation Operation,
    int OwnerId,
    int SourceId,
    string SourcePath,
    int Start,
    int Length,
    string Name,
    int ItemCount,
    long InvocationCount,
    TimeSpan Elapsed,
    long AllocatedBytes,
    long InvalidAllocationMeasurementCount,
    long FirstInvocationAllocatedBytes,
    TimeSpan FirstInvocationElapsed,
    long RepeatedAllocatedBytes,
    TimeSpan RepeatedElapsed);

internal static partial class GenerationStatistics
{
    public static bool OperationTrackingEnabled => Current is { IsActive: true, Operations: not null };

    public static GenerationOperationMeasurement MeasureOperation(
        GenerationStatisticOperation operation,
        object owner,
        object sourceIdentity,
        string sourcePath,
        int start,
        int length,
        string name,
        int itemCount)
    {
        return Current is { IsActive: true, Operations: { } operations } session
            ? operations.Begin(session, operation, owner, sourceIdentity, sourcePath, start, length, name, itemCount)
            : GenerationOperationMeasurement.Empty;
    }
}

internal sealed class GenerationOperationTracker
{
    private readonly object _gate = new();
    private readonly Dictionary<OperationKey, GenerationOperationAccumulator> _operations = new();
    private readonly Dictionary<object, int> _owners = new(ReferenceComparer.Instance);
    private readonly Dictionary<object, int> _sources = new(ReferenceComparer.Instance);
    private readonly List<GenerationOperationAccumulator> _rows = new();
    private bool _completed;

    internal GenerationOperationMeasurement Begin(
        GenerationStatisticsSession session,
        GenerationStatisticOperation operation,
        object owner,
        object sourceIdentity,
        string sourcePath,
        int start,
        int length,
        string name,
        int itemCount)
    {
        if (owner == null)
        {
            throw new ArgumentNullException(nameof(owner));
        }

        if (sourceIdentity == null)
        {
            throw new ArgumentNullException(nameof(sourceIdentity));
        }

        GenerationOperationAccumulator accumulator;
        bool firstInvocation;
        lock (_gate)
        {
            if (_completed)
            {
                return GenerationOperationMeasurement.Empty;
            }

            var key = new OperationKey(operation, owner, sourceIdentity, start, length, itemCount);
            if (!_operations.TryGetValue(key, out accumulator!))
            {
                var definition = new GenerationOperationStatistics(
                    operation, GetId(_owners, owner), GetId(_sources, sourceIdentity), sourcePath,
                    start, length, name, itemCount, 0, default, 0, 0, 0, default, 0, default);
                accumulator = new GenerationOperationAccumulator(definition);
                _operations.Add(key, accumulator);
                _rows.Add(accumulator);
            }

            // Reserve the first invocation at BEGIN, not whichever worker finishes first.
            firstInvocation = ++accumulator.BegunInvocations == 1;
        }

        return new GenerationOperationMeasurement(this, session, accumulator, firstInvocation);
    }

    internal void AddMeasurement(
        GenerationOperationAccumulator accumulator,
        bool firstInvocation,
        long elapsedTicks,
        long allocatedBytes,
        bool invalidAllocation)
    {
        lock (_gate)
        {
            if (_completed)
            {
                return;
            }

            accumulator.InvocationCount++;
            accumulator.ElapsedTicks += elapsedTicks;
            if (invalidAllocation)
            {
                accumulator.InvalidAllocationMeasurementCount++;
                allocatedBytes = 0;
            }

            accumulator.AllocatedBytes += allocatedBytes;
            if (firstInvocation)
            {
                accumulator.FirstInvocationElapsedTicks += elapsedTicks;
                accumulator.FirstInvocationAllocatedBytes += allocatedBytes;
            }
            else
            {
                accumulator.RepeatedElapsedTicks += elapsedTicks;
                accumulator.RepeatedAllocatedBytes += allocatedBytes;
            }
        }
    }

    internal ImmutableArray<GenerationOperationStatistics> GetSnapshot()
    {
        lock (_gate)
        {
            return _rows.Select(static row => row.GetSnapshot())
                .OrderBy(static row => row.Operation)
                .ThenBy(static row => row.OwnerId)
                .ThenBy(static row => row.SourceId)
                .ThenBy(static row => row.Start)
                .ThenBy(static row => row.Length)
                .ThenBy(static row => row.ItemCount)
                .ToImmutableArray();
        }
    }

    internal void Complete()
    {
        lock (_gate)
        {
            _completed = true;
            // Completed measurements retain detached labels/totals, not semantic models or trees.
            _operations.Clear();
            _owners.Clear();
            _sources.Clear();
        }
    }

    private static int GetId(Dictionary<object, int> identities, object value)
    {
        if (!identities.TryGetValue(value, out var id))
        {
            id = identities.Count + 1;
            identities.Add(value, id);
        }

        return id;
    }

    private readonly record struct OperationKey(
        GenerationStatisticOperation Operation, object Owner, object SourceIdentity, int Start, int Length, int ItemCount)
    {
        public bool Equals(OperationKey other) =>
            Operation == other.Operation && ReferenceEquals(Owner, other.Owner) &&
            ReferenceEquals(SourceIdentity, other.SourceIdentity) && Start == other.Start &&
            Length == other.Length && ItemCount == other.ItemCount;

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = (int)Operation;
                hash = (hash * 397) ^ RuntimeHelpers.GetHashCode(Owner);
                hash = (hash * 397) ^ RuntimeHelpers.GetHashCode(SourceIdentity);
                hash = (hash * 397) ^ Start;
                hash = (hash * 397) ^ Length;
                return (hash * 397) ^ ItemCount;
            }
        }
    }

    private sealed class ReferenceComparer : IEqualityComparer<object>
    {
        internal static readonly ReferenceComparer Instance = new();
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
    }
}

internal sealed class GenerationOperationAccumulator(GenerationOperationStatistics definition)
{
    public long BegunInvocations;
    public long InvocationCount;
    public long ElapsedTicks;
    public long AllocatedBytes;
    public long InvalidAllocationMeasurementCount;
    public long FirstInvocationElapsedTicks;
    public long FirstInvocationAllocatedBytes;
    public long RepeatedElapsedTicks;
    public long RepeatedAllocatedBytes;

    internal GenerationOperationStatistics GetSnapshot() => definition with
    {
        InvocationCount = InvocationCount,
        Elapsed = ToTimeSpan(ElapsedTicks),
        AllocatedBytes = AllocatedBytes,
        InvalidAllocationMeasurementCount = InvalidAllocationMeasurementCount,
        FirstInvocationElapsed = ToTimeSpan(FirstInvocationElapsedTicks),
        FirstInvocationAllocatedBytes = FirstInvocationAllocatedBytes,
        RepeatedElapsed = ToTimeSpan(RepeatedElapsedTicks),
        RepeatedAllocatedBytes = RepeatedAllocatedBytes,
    };

    private static TimeSpan ToTimeSpan(long ticks) =>
        TimeSpan.FromTicks((long)(ticks * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
}

internal sealed class GenerationOperationMeasurement : IDisposable
{
    internal static readonly GenerationOperationMeasurement Empty = new();
    private readonly GenerationOperationTracker? _tracker;
    private readonly GenerationStatisticsSession? _session;
    private readonly GenerationOperationAccumulator? _accumulator;
    private readonly bool _firstInvocation;
    private readonly long _startTimestamp;
    private readonly long _startAllocatedBytes;
    private readonly int _startThreadId;
    private int _disposed;

    private GenerationOperationMeasurement()
    {
    }

    internal GenerationOperationMeasurement(
        GenerationOperationTracker tracker,
        GenerationStatisticsSession session,
        GenerationOperationAccumulator accumulator,
        bool firstInvocation)
    {
        _tracker = tracker;
        _session = session;
        _accumulator = accumulator;
        _firstInvocation = firstInvocation;
        if (session.GetAllocatedBytes is { } getAllocatedBytes)
        {
            _startThreadId = Thread.CurrentThread.ManagedThreadId;
            _startAllocatedBytes = getAllocatedBytes();
        }

        _startTimestamp = Stopwatch.GetTimestamp();
    }

    public void Dispose()
    {
        if (_session == null || Interlocked.Exchange(ref _disposed, 1) != 0 || !_session.IsActive)
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

        _tracker!.AddMeasurement(_accumulator!, _firstInvocation, elapsed, allocatedBytes, invalidAllocation);
    }
}
#endif
