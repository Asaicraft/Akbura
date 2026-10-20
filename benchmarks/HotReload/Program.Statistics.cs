using Akbura.Language.CodeGeneration;

internal static partial class Program
{
    // Count is an enum constant embedded in the benchmark at compile time. Its
    // numeric value can differ in a frozen analyzer; select runtime names instead.
    private static IEnumerable<T> StatisticValues<T>() where T : struct, Enum =>
        Enum.GetNames<T>().Where(static name => name != "Count").Select(static name => Enum.Parse<T>(name));

    private static Dictionary<string, long> ReadCounters(GenerationStatisticsSnapshot snapshot) =>
        StatisticValues<GenerationStatisticCounter>().ToDictionary(static counter => counter.ToString(), snapshot.GetCounterCount);

    private static void CheckStatisticsSnapshot()
    {
        using var measurement = GenerationStatistics.BeginMeasurement();
        var snapshot = measurement.GetSnapshot();
        var counters = ReadCounters(snapshot);
        var stages = StatisticValues<GenerationStatisticStage>().ToDictionary(static stage => stage.ToString(), snapshot.GetStageInvocationCount);
        var counterLimit = Convert.ToInt32(Enum.Parse<GenerationStatisticCounter>("Count"));
        var stageLimit = Convert.ToInt32(Enum.Parse<GenerationStatisticStage>("Count"));
        Assert.Equal(counterLimit, counters.Count);
        Assert.Equal(stageLimit, stages.Count);
        Assert.DoesNotContain("Count", counters.Keys);
        Assert.DoesNotContain("Count", stages.Keys);
        Assert.All(counters.Values, static value => Assert.Equal(0, value));
        Assert.All(stages.Values, static value => Assert.Equal(0, value));
        Write(new { Kind = "statistics-compatibility", BinaryFlavor = InvalidationBinaryFlavor,
            CounterLimit = counterLimit, StageLimit = stageLimit, Counters = counters, Stages = stages });
    }
}
