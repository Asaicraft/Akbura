#if STATS
namespace Akbura.UnitTests;

// Exact parse-reuse counters require the process-wide bounded parse cache not
// to be evicted by unrelated tests between the cold and incremental runs.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BlackSilenceStatisticsCollection
{
    public const string Name = "BlackSilence generation statistics";
}
#endif
