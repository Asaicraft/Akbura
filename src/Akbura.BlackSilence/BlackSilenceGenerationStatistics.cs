#if STATS
using Akbura.Language.CodeGeneration;

namespace Akbura.BlackSilence;

/// <summary>
/// Starts one opt-in measurement around a GeneratorDriver call or backend workload.
/// Finish all worker tasks before disposing the session or reading its snapshot.
/// </summary>
internal static class BlackSilenceGenerationStatistics
{
    public static GenerationStatisticsSession BeginMeasurement() => GenerationStatistics.BeginMeasurement();
}
#endif
