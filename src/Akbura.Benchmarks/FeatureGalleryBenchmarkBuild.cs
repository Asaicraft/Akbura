using BenchmarkDotNet.Jobs;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryBenchmarkBuild
{
    public static Job Configure(Job job) =>
        job.WithMsBuildArguments(
            "/p:EnableAkburaStats=false",
            "/p:EnableQuickScanBenchmark=false",
            "/p:AkburaEmbedSourceFiles=false",
            "/p:BuildInParallel=false");
}
