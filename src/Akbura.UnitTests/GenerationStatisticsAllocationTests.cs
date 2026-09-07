#if STATS
using Akbura.Language.CodeGeneration;
using System.Threading;
using System.Threading.Tasks;

namespace Akbura.UnitTests;

public sealed class GenerationStatisticsAllocationTests
{
    [Fact]
    public void WithoutAllocationProvider_CountsCompletedStagesWithoutAllocationSamples()
    {
        using var measurement = GenerationStatistics.BeginMeasurement();
        using (GenerationStatistics.Measure(GenerationStatisticStage.Parse))
        {
        }

        var snapshot = measurement.GetSnapshot();

        Assert.False(snapshot.HasAllocationMeasurements);
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.Parse));
        Assert.Equal(0, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.Parse));
        Assert.Equal(0, snapshot.InvalidAllocationMeasurementCount);
        Assert.Equal(snapshot.ParseElapsed, snapshot.GetStageElapsed(GenerationStatisticStage.Parse));
    }

    [Fact]
    public void DeterministicProvider_AccumulatesCompletedStagesAndDetachesSnapshots()
    {
        long allocatedBytes = 100;
        using var measurement = GenerationStatistics.BeginMeasurement(() => allocatedBytes);
        var firstStage = GenerationStatistics.Measure(GenerationStatisticStage.CSharpProbeBinding);
        allocatedBytes += 37;
        Assert.Equal(0, measurement.GetSnapshot().GetStageInvocationCount(GenerationStatisticStage.CSharpProbeBinding));
        firstStage.Dispose();
        var first = measurement.GetSnapshot();

        using (GenerationStatistics.Measure(GenerationStatisticStage.CSharpProbeBinding))
        {
            allocatedBytes += 11;
        }

        measurement.Dispose();
        var completed = measurement.GetSnapshot();

        Assert.True(completed.HasAllocationMeasurements);
        Assert.Equal(37, first.GetStageAllocatedBytes(GenerationStatisticStage.CSharpProbeBinding));
        Assert.Equal(1, first.GetStageInvocationCount(GenerationStatisticStage.CSharpProbeBinding));
        Assert.Equal(48, completed.GetStageAllocatedBytes(GenerationStatisticStage.CSharpProbeBinding));
        Assert.Equal(2, completed.GetStageInvocationCount(GenerationStatisticStage.CSharpProbeBinding));
        Assert.Equal(0, completed.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void NestedStages_RecordInclusiveRatherThanAdditiveAllocations()
    {
        long allocatedBytes = 100;
        using var measurement = GenerationStatistics.BeginMeasurement(() => allocatedBytes);
        using (GenerationStatistics.Measure(GenerationStatisticStage.ComponentGeneration))
        {
            allocatedBytes += 10;
            using (GenerationStatistics.Measure(GenerationStatisticStage.ComponentPlanning))
            {
                allocatedBytes += 30;
            }

            allocatedBytes += 7;
        }

        var snapshot = measurement.GetSnapshot();

        Assert.Equal(47, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.ComponentGeneration));
        Assert.Equal(30, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.ComponentPlanning));
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.ComponentGeneration));
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.ComponentPlanning));
        Assert.True(snapshot.ComponentGenerationElapsed >= snapshot.ComponentPlanningElapsed);
        Assert.Equal(0, snapshot.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void NestedSessions_IsolateProvidersAndRestoreParentMeasurements()
    {
        long outerBytes = 100;
        long innerBytes = 500;
        using var outer = GenerationStatistics.BeginMeasurement(() => outerBytes);
        using (GenerationStatistics.Measure(GenerationStatisticStage.Parse))
        {
            outerBytes += 10;
        }

        GenerationStatisticsSnapshot innerSnapshot;
        using (var inner = GenerationStatistics.BeginMeasurement(() => innerBytes))
        {
            using (GenerationStatistics.Measure(GenerationStatisticStage.AkcssGeneration))
            {
                innerBytes += 25;
            }

            innerSnapshot = inner.GetSnapshot();
        }

        using (GenerationStatistics.Measure(GenerationStatisticStage.Parse))
        {
            outerBytes += 7;
        }

        var outerSnapshot = outer.GetSnapshot();

        Assert.Equal(17, outerSnapshot.GetStageAllocatedBytes(GenerationStatisticStage.Parse));
        Assert.Equal(2, outerSnapshot.GetStageInvocationCount(GenerationStatisticStage.Parse));
        Assert.Equal(0, outerSnapshot.GetStageInvocationCount(GenerationStatisticStage.AkcssGeneration));
        Assert.Equal(25, innerSnapshot.GetStageAllocatedBytes(GenerationStatisticStage.AkcssGeneration));
        Assert.Equal(1, innerSnapshot.GetStageInvocationCount(GenerationStatisticStage.AkcssGeneration));
        Assert.Equal(0, innerSnapshot.GetStageInvocationCount(GenerationStatisticStage.Parse));
    }

    [Fact]
    public void ParallelStages_AccumulateIndependentCurrentThreadSamples()
    {
        using var allocatedBytes = new ThreadLocal<long>(static () => 0);
        using var measurement = GenerationStatistics.BeginMeasurement(() => allocatedBytes.Value);
        Parallel.For(0, 31, _ =>
        {
            using var stage = GenerationStatistics.Measure(GenerationStatisticStage.SemanticBinding);
            allocatedBytes.Value += 7;
        });

        var snapshot = measurement.GetSnapshot();

        Assert.Equal(31, snapshot.GetStageInvocationCount(GenerationStatisticStage.SemanticBinding));
        Assert.Equal(217, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.SemanticBinding));
        Assert.Equal(0, snapshot.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void DuplicateDispose_DoesNotCountOrSampleTheSameStageAgain()
    {
        long allocatedBytes = 100;
        var samples = 0;
        using var measurement = GenerationStatistics.BeginMeasurement(() =>
        {
            samples++;
            return allocatedBytes;
        });
        var stage = GenerationStatistics.Measure(GenerationStatisticStage.CSharpEmission);
        allocatedBytes = 140;
        stage.Dispose();
        allocatedBytes = 999;
        stage.Dispose();

        var snapshot = measurement.GetSnapshot();

        Assert.Equal(2, samples);
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.CSharpEmission));
        Assert.Equal(40, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.CSharpEmission));
        Assert.Equal(0, snapshot.InvalidAllocationMeasurementCount);
    }

    [Fact]
    public void CrossThreadDispose_ExcludesAllocationButKeepsCompletedInvocation()
    {
        var samples = 0;
        using var measurement = GenerationStatistics.BeginMeasurement(() =>
        {
            Interlocked.Increment(ref samples);
            return 100;
        });
        var stage = GenerationStatistics.Measure(GenerationStatisticStage.CSharpEmission);
        var thread = new Thread(stage.Dispose);
        thread.Start();
        thread.Join();
        stage.Dispose();

        var snapshot = measurement.GetSnapshot();

        Assert.True(snapshot.HasAllocationMeasurements);
        Assert.Equal(1, samples);
        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.CSharpEmission));
        Assert.Equal(0, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.CSharpEmission));
        Assert.Equal(1, snapshot.InvalidAllocationMeasurementCount);
        Assert.Equal(snapshot.CSharpEmissionElapsed, snapshot.GetStageElapsed(GenerationStatisticStage.CSharpEmission));
    }

    [Fact]
    public void DecreasingAllocationCounter_ExcludesInvalidDelta()
    {
        long allocatedBytes = 100;
        using var measurement = GenerationStatistics.BeginMeasurement(() => allocatedBytes);
        using (GenerationStatistics.Measure(GenerationStatisticStage.Parse))
        {
            allocatedBytes = 90;
        }

        var snapshot = measurement.GetSnapshot();

        Assert.Equal(1, snapshot.GetStageInvocationCount(GenerationStatisticStage.Parse));
        Assert.Equal(0, snapshot.GetStageAllocatedBytes(GenerationStatisticStage.Parse));
        Assert.Equal(1, snapshot.InvalidAllocationMeasurementCount);
    }
}
#endif
