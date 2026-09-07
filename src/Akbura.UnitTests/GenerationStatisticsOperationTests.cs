#if STATS
using Akbura.Language.CodeGeneration;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed class GenerationStatisticsOperationTests
{
    [Fact]
    public void DisabledTracking_DoesNotCollectRowsOrSampleAllocations()
    {
        var samples = 0;
        using var measurement = GenerationStatistics.BeginMeasurement(() => ++samples);
        Assert.False(GenerationStatistics.OperationTrackingEnabled);
        using (Measure(new object(), new object()))
        {
        }

        var snapshot = measurement.GetSnapshot();
        Assert.False(snapshot.OperationTrackingEnabled);
        Assert.Empty(snapshot.OperationMeasurements);
        Assert.Equal(0, samples);
    }

    [Fact]
    public void EnabledTrackingWithoutAllocationProvider_StillCountsOperations()
    {
        using var measurement = GenerationStatistics.BeginMeasurement(trackOperations: true);
        Assert.True(GenerationStatistics.OperationTrackingEnabled);
        using (Measure(new object(), new object()))
        {
        }

        var snapshot = measurement.GetSnapshot();
        var row = Assert.Single(snapshot.OperationMeasurements);
        Assert.True(snapshot.OperationTrackingEnabled);
        Assert.False(snapshot.HasAllocationMeasurements);
        Assert.Equal(1, row.InvocationCount);
        Assert.Equal(0, row.AllocatedBytes);
        Assert.Equal(0, row.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void OperationIdentity_UsesReferenceOwnersSourcesAndShapeRatherThanLabels()
    {
        var owner = new EqualIdentity();
        var source = new EqualIdentity();
        using var measurement = GenerationStatistics.BeginMeasurement(trackOperations: true);
        Measure(owner, source, path: "first.akcss", name: "first").Dispose();
        Measure(owner, source, path: "changed-label.akcss", name: "changed-label").Dispose();
        Measure(new EqualIdentity(), source).Dispose();
        Measure(owner, new EqualIdentity()).Dispose();
        Measure(owner, source, start: 4).Dispose();
        Measure(owner, source, length: 8).Dispose();
        Measure(owner, source, count: 3).Dispose();
        Measure(owner, source, operation: GenerationStatisticOperation.UtilityParameterBinding).Dispose();

        var rows = measurement.GetSnapshot().OperationMeasurements;
        var repeated = Assert.Single(rows.Where(static row => row.SourcePath == "first.akcss"));
        Assert.Equal(7, rows.Length);
        Assert.Equal(8, rows.Sum(static row => row.InvocationCount));
        Assert.Equal(2, rows.Select(static row => row.OwnerId).Distinct().Count());
        Assert.Equal(2, rows.Select(static row => row.SourceId).Distinct().Count());
        Assert.Equal(2, repeated.InvocationCount);
        Assert.Equal("first", repeated.Name);
        Assert.Equal(1, repeated.OwnerId);
        Assert.Equal(1, repeated.SourceId);
    }

    [Fact]
    public void FirstInvocation_IsReservedAtBeginEvenWhenRepeatedScopeFinishesFirst()
    {
        long allocatedBytes = 100;
        var samples = 0;
        var owner = new object();
        var source = new object();
        using var measurement = GenerationStatistics.BeginMeasurement(() =>
        {
            samples++;
            return allocatedBytes;
        }, trackOperations: true);
        var first = Measure(owner, source);
        allocatedBytes += 3;
        var repeated = Measure(owner, source);
        allocatedBytes += 7;
        repeated.Dispose();
        var intermediate = Assert.Single(measurement.GetSnapshot().OperationMeasurements);
        allocatedBytes += 5;
        first.Dispose();
        first.Dispose();
        repeated.Dispose();
        measurement.Dispose();
        var completed = Assert.Single(measurement.GetSnapshot().OperationMeasurements);

        Assert.Equal(4, samples);
        Assert.Equal(1, intermediate.InvocationCount);
        Assert.Equal(0, intermediate.FirstInvocationAllocatedBytes);
        Assert.Equal(7, intermediate.RepeatedAllocatedBytes);
        Assert.Equal(2, completed.InvocationCount);
        Assert.Equal(15, completed.FirstInvocationAllocatedBytes);
        Assert.Equal(7, completed.RepeatedAllocatedBytes);
        Assert.Equal(22, completed.AllocatedBytes);
        Assert.True(completed.FirstInvocationElapsed >= completed.RepeatedElapsed);
        Assert.Equal(0, completed.InvalidAllocationMeasurementCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedSessions_IsolateOperationRowsAndRestoreParent(bool trackInner)
    {
        long outerBytes = 0;
        long innerBytes = 0;
        var owner = new object();
        var source = new object();
        using var outer = GenerationStatistics.BeginMeasurement(() => outerBytes, trackOperations: true);
        using (Measure(owner, source))
        {
            outerBytes += 10;
        }

        GenerationStatisticsSnapshot innerSnapshot;
        using (var inner = GenerationStatistics.BeginMeasurement(() => innerBytes, trackOperations: trackInner))
        {
            Assert.Equal(trackInner, GenerationStatistics.OperationTrackingEnabled);
            using (Measure(owner, source))
            {
                innerBytes += 20;
            }

            innerSnapshot = inner.GetSnapshot();
        }

        Assert.True(GenerationStatistics.OperationTrackingEnabled);
        using (Measure(owner, source))
        {
            outerBytes += 5;
        }

        var outerRow = Assert.Single(outer.GetSnapshot().OperationMeasurements);
        Assert.Equal(2, outerRow.InvocationCount);
        Assert.Equal(10, outerRow.FirstInvocationAllocatedBytes);
        Assert.Equal(5, outerRow.RepeatedAllocatedBytes);
        Assert.Equal(trackInner, innerSnapshot.OperationTrackingEnabled);
        if (trackInner)
        {
            var innerRow = Assert.Single(innerSnapshot.OperationMeasurements);
            Assert.Equal(1, innerRow.InvocationCount);
            Assert.Equal(20, innerRow.FirstInvocationAllocatedBytes);
            Assert.Equal(0, innerRow.RepeatedAllocatedBytes);
        }
        else
        {
            Assert.Empty(innerSnapshot.OperationMeasurements);
        }
    }

    [Fact]
    public void ParallelOperations_AggregateOneFirstAndAllRepeatedCurrentThreadSamples()
    {
        var owner = new object();
        var source = new object();
        using var allocatedBytes = new ThreadLocal<long>(static () => 0);
        using var measurement = GenerationStatistics.BeginMeasurement(() => allocatedBytes.Value, trackOperations: true);
        Parallel.For(0, 31, _ =>
        {
            using var operation = Measure(owner, source);
            allocatedBytes.Value += 7;
        });

        var row = Assert.Single(measurement.GetSnapshot().OperationMeasurements);
        Assert.Equal(31, row.InvocationCount);
        Assert.Equal(217, row.AllocatedBytes);
        Assert.Equal(7, row.FirstInvocationAllocatedBytes);
        Assert.Equal(210, row.RepeatedAllocatedBytes);
        Assert.Equal(0, row.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void CrossThreadDisposal_ExcludesInvalidAllocationAndCountsInvocationOnce()
    {
        var samples = 0;
        using var measurement = GenerationStatistics.BeginMeasurement(() =>
        {
            Interlocked.Increment(ref samples);
            return 100;
        }, trackOperations: true);
        var operation = Measure(new object(), new object());
        var thread = new Thread(operation.Dispose);
        thread.Start();
        thread.Join();
        operation.Dispose();

        var row = Assert.Single(measurement.GetSnapshot().OperationMeasurements);
        Assert.Equal(1, samples);
        Assert.Equal(1, row.InvocationCount);
        Assert.Equal(0, row.AllocatedBytes);
        Assert.Equal(0, row.FirstInvocationAllocatedBytes);
        Assert.Equal(1, row.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void DecreasingAllocationCounter_ExcludesOperationDelta()
    {
        long bytes = 100;
        using var measurement = GenerationStatistics.BeginMeasurement(() => bytes, trackOperations: true);
        using (Measure(new object(), new object()))
        {
            bytes = 90;
        }

        var row = Assert.Single(measurement.GetSnapshot().OperationMeasurements);
        Assert.Equal(1, row.InvocationCount);
        Assert.Equal(0, row.AllocatedBytes);
        Assert.Equal(1, row.InvalidAllocationMeasurementCount);
    }

    private static GenerationOperationMeasurement Measure(
        object owner,
        object source,
        string path = "page.akcss",
        int start = 3,
        int length = 7,
        string name = "button",
        int count = 2,
        GenerationStatisticOperation operation = GenerationStatisticOperation.AkcssLookupSymbols)
    {
        return GenerationStatistics.MeasureOperation(operation, owner, source, path, start, length, name, count);
    }

    private sealed class EqualIdentity
    {
        public override bool Equals(object? obj) => obj is EqualIdentity;
        public override int GetHashCode() => 1;
    }
}
#endif
