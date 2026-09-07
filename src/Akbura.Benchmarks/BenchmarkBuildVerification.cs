using BenchmarkDotNet.Characteristics;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Loggers;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using System.Reflection;

namespace Akbura.Benchmarks;

internal static class BenchmarkBuildVerification
{
    public static bool TryRun(string[] args)
    {
        if (!args.Contains("--verify-benchmark-build", StringComparer.Ordinal))
        {
            return false;
        }

        if (args.Length != 1)
        {
            throw new ArgumentException("Usage: --verify-benchmark-build");
        }

        var repositoryRoot = typeof(BenchmarkBuildVerification).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == "AkburaRepositoryRoot").Value
            ?? throw new InvalidOperationException("The benchmark repository root is unavailable.");
        var artifactsPath = Path.Combine(repositoryRoot, "artifacts", "benchmark-build");
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(artifactsPath)
            .WithBuildTimeout(TimeSpan.FromMinutes(5));

        // Discovery only reads benchmark metadata and enum parameter values; it does not run setup.
        using var benchmarks = BenchmarkConverter.TypeToBenchmarks(typeof(FeatureGalleryIncrementalBenchmarks), config);
        if (benchmarks.BenchmarksCases.Length == 0)
        {
            throw new InvalidOperationException("No incremental FeatureGallery benchmark cases were discovered.");
        }

        Console.WriteLine($"Build-only verification: {benchmarks.BenchmarksCases.Length} cases; no benchmark execution.");
        foreach (var benchmark in benchmarks.BenchmarksCases)
        {
            Console.WriteLine($"Case: {benchmark.DisplayInfo}");
        }

        var resolver = new CompositeResolver(EnvironmentResolver.Instance, InfrastructureResolver.Instance);
        var partitions = BenchmarkPartitioner.CreateForBuild([benchmarks], resolver);
        var toolchain = CsProjCoreToolchain.NetCoreApp10_0;
        for (var i = 0; i < partitions.Length; i++)
        {
            var partition = partitions[i];
            Console.WriteLine($"Generate partition {i + 1}/{partitions.Length}: {partition.Benchmarks.Length} cases.");
            var generated = toolchain.Generator.GenerateProject(partition, ConsoleLogger.Default, artifactsPath);
            if (!generated.IsGenerateSuccess)
            {
                throw new InvalidOperationException("BenchmarkDotNet harness generation failed.", generated.GenerateException);
            }

            Console.WriteLine($"Project: {generated.ArtifactsPaths.ProjectFilePath}");
            var built = toolchain.Builder.Build(generated, partition, ConsoleLogger.Default);
            if (!built.IsBuildSuccess)
            {
                throw new InvalidOperationException($"BenchmarkDotNet harness build failed: {built.ErrorMessage}");
            }

            Console.WriteLine($"Build succeeded: {built.ArtifactsPaths.ExecutablePath}");
        }

        Console.WriteLine($"Verified {benchmarks.BenchmarksCases.Length} cases in {partitions.Length} build partitions. No benchmarks were executed.");
        return true;
    }
}
