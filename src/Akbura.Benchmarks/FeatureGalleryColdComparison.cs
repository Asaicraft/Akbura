using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryColdComparison
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--black-silence-cold-compare")
        {
            return false;
        }

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("--black-silence-cold-compare [--warmups 1] [--pairs 3] [--output artifacts/black-silence-cold-compare]");
            Console.WriteLine("Requires -p:EnableAkburaStats=true. Compares cold backends in one warmed process, not full driver/MSBuild latency.");
            return true;
        }

#if STATS
        Run(Options.Parse(args));
        return true;
#else
        throw new InvalidOperationException("Cold comparison requires STATS. Build with -p:EnableAkburaStats=true.");
#endif
    }

#if STATS
    private static void Run(Options options)
    {
        Console.WriteLine("Loading the real FeatureGallery compilation for paired cold-backend measurements...");
        var project = FeatureGalleryBenchmarkProject.Load();
        Console.WriteLine("Parsing, version extraction, forced GC, validation and parity checks are outside timing.");
        Console.WriteLine("Both arms use fresh syntax objects and project state; a pair shares exact file identities and C# compilation.");

        for (var i = 0; i < options.Warmups; i++)
        {
            RunPair(project, i + 1, warmup: true);
        }

        var pairs = new List<PairSample>(options.Pairs);
        for (var i = 0; i < options.Pairs; i++)
        {
            pairs.Add(RunPair(project, i + 1, warmup: false));
        }

        WriteReports(project, options, pairs);
    }

    private static PairSample RunPair(FeatureGalleryBenchmarkProject project, int pair, bool warmup)
    {
        var snapshot = project.CreateSnapshot();
        var currentFirst = pair % 2 == 0;
        Console.WriteLine($"{(warmup ? "Warmup" : "Measured")} pair {pair}: {(currentFirst ? "Current -> Eager" : "Eager -> Current")}");

        var first = Measure(project, Prepare(project, snapshot), currentFirst);
        var second = Measure(project, Prepare(project, snapshot), !currentFirst);
        var eager = currentFirst ? second : first;
        var current = currentFirst ? first : second;

        Validate(project, eager.Sources);
        Validate(project, current.Sources);
        ValidateParity(eager.Sources, current.Sources);

        var ratio = current.Sample.ElapsedMilliseconds / eager.Sample.ElapsedMilliseconds;
        Console.WriteLine(FormattableString.Invariant($"  Compilation valid; exact hint/text parity; Current/Eager = {ratio:F4}x."));
        return new PairSample(pair, currentFirst ? "Current -> Eager" : "Eager -> Current", eager.Sample, current.Sample, ratio);
    }

    private static Inputs Prepare(FeatureGalleryBenchmarkProject project, FeatureGalleryBenchmarkSnapshot snapshot)
    {
        var trees = ImmutableArray.CreateBuilder<AkburaSyntaxTree>(snapshot.AdditionalTexts.Length);
        var versions = ImmutableArray.CreateBuilder<DocumentSyntaxVersion>(snapshot.AdditionalTexts.Length);

        foreach (var file in snapshot.AdditionalTexts)
        {
            var text = file.GetText() ?? throw new InvalidOperationException("An AdditionalFile did not provide source text.");
            AkburaSyntaxTree tree;
            if (Path.GetExtension(file.Path).Equals(".akcss", StringComparison.OrdinalIgnoreCase))
            {
                var sourcePath = Path.GetRelativePath(snapshot.ProjectDirectory, file.Path).Replace('\\', '/');
                tree = AkcssSyntaxTree.ParseText(text, file.Path,
                    AkcssGeneratedModuleNames.GetMetadataName(project.RootNamespace, sourcePath));
            }
            else
            {
                tree = ComponentSyntaxTree.ParseText(text, file.Path);
            }

            trees.Add(tree);
            versions.Add(DocumentSyntaxVersion.Create(tree));
        }

        return new Inputs(snapshot.ProjectDirectory, trees.MoveToImmutable(), versions.MoveToImmutable());
    }

    private static MeasuredResult Measure(FeatureGalleryBenchmarkProject project, Inputs inputs, bool current)
    {
        // Prevent a preceding arm's unreachable semantic world from determining the
        // next sample's initial GC pressure. No forced collection is inside timing.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

        ImmutableArray<GeneratedSource> sources;
        Sample sample;
        using (var measurement = BlackSilenceGenerationStatistics.BeginMeasurement())
        {
            var gen0 = GC.CollectionCount(0);
            var gen1 = GC.CollectionCount(1);
            var gen2 = GC.CollectionCount(2);
            var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
            var started = Stopwatch.GetTimestamp();

            sources = current ? GenerateCurrent(project, inputs) : GenerateEager(project, inputs);

            var elapsed = Stopwatch.GetElapsedTime(started);
            var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
            sample = new Sample(
                current ? "Current" : "Eager",
                elapsed.TotalMilliseconds,
                allocatedBytes,
                GC.CollectionCount(0) - gen0,
                GC.CollectionCount(1) - gen1,
                GC.CollectionCount(2) - gen2,
                sources.Length,
                measurement.GetSnapshot());
        }

        var mib = sample.AllocatedBytes / (1024d * 1024d);
        var semanticMilliseconds = sample.Statistics.SemanticBindingElapsed.TotalMilliseconds;
        Console.WriteLine(FormattableString.Invariant(
            $"  {sample.Backend}: {sample.ElapsedMilliseconds:F3} ms, {mib:F3} MiB, semantic {semanticMilliseconds:F3} ms, {sources.Length} documents."));
        return new MeasuredResult(sample, sources);
    }

    private static ImmutableArray<GeneratedSource> GenerateCurrent(FeatureGalleryBenchmarkProject project, Inputs inputs)
    {
        var request = BlackSilenceGenerationRequestBuilder.Create(
            inputs.Versions,
            new BlackSilenceProjectState(project.Compilation),
            new GeneratorProjectOptions(project.RootNamespace, inputs.ProjectDirectory),
            CancellationToken.None) ?? throw new InvalidOperationException("Current backend did not create a generation request.");
        var batch = BlackSilenceDocumentBatch.Generate(request, CancellationToken.None);
        return batch.Components.AddRange(batch.ExternalAkcss).AddRange(batch.InlineAkcss);
    }

    private static ImmutableArray<GeneratedSource> GenerateEager(FeatureGalleryBenchmarkProject project, Inputs inputs)
    {
        var catalog = AkburaGenerationCatalogBuilder.Create(
            project.Compilation, inputs.Trees, project.RootNamespace, inputs.ProjectDirectory);
        var componentCount = catalog.Components.Length;
        var externalCount = catalog.ExternalAkcssModules.Length;
        var sources = new GeneratedSource[componentCount + externalCount + catalog.InlineAkcssModules.Length];

        using (GenerationStatistics.Measure(GenerationStatisticStage.ComponentBatch))
        {
            Parallel.For(0, componentCount, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, ProcessorCountHelper.GetProcessorCount() / 4),
            }, i =>
            {
                var input = catalog.Components[i];
                var text = ComponentDocumentWriter.Generate(
                    input.Component, input.SemanticModel, input.SourcePath, catalog.AkcssModuleTypeNames);
                sources[i] = new GeneratedSource(ComponentDocumentWriter.GetHintName(input.Component, input.SourcePath), text);
            });
        }

        GenerateAkcss(catalog.ExternalAkcssModules, componentCount);
        GenerateAkcss(catalog.InlineAkcssModules, componentCount + externalCount);
        return sources.ToImmutableArrayUnsafe();

        void GenerateAkcss(ImmutableArray<AkcssGenerationInput> modules, int offset)
        {
            using var measurement = GenerationStatistics.Measure(GenerationStatisticStage.AkcssBatch);
            Parallel.For(0, modules.Length, new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, ProcessorCountHelper.GetProcessorCount() / 2),
            }, i =>
            {
                var input = modules[i];
                var text = AkcssDocumentWriter.Generate(input, catalog.AkcssSourceMap, catalog.RootNamespace);
                sources[offset + i] = new GeneratedSource(AkcssDocumentWriter.GetHintName(input), text);
            });
        }
    }

    private static void Validate(FeatureGalleryBenchmarkProject project, ImmutableArray<GeneratedSource> sources)
    {
        if (sources.IsEmpty)
        {
            throw new InvalidOperationException("Cold backend produced no FeatureGallery documents.");
        }

        FeatureGalleryStageMeasurements.ValidateCompilation(project.Compilation.AddSyntaxTrees(sources.Select(source =>
            CSharpSyntaxTree.ParseText(source.SourceText, project.ParseOptions, source.HintName))));
    }

    private static void ValidateParity(ImmutableArray<GeneratedSource> eager, ImmutableArray<GeneratedSource> current)
    {
        var expected = eager.ToDictionary(static source => source.HintName, StringComparer.Ordinal);
        var actual = current.ToDictionary(static source => source.HintName, StringComparer.Ordinal);
        if (expected.Count != actual.Count)
        {
            throw new InvalidOperationException($"Cold backend source counts differ: Eager={expected.Count}, Current={actual.Count}.");
        }

        foreach (var pair in expected)
        {
            if (!actual.TryGetValue(pair.Key, out var source) || !pair.Value.SourceText.ContentEquals(source.SourceText))
            {
                throw new InvalidOperationException($"Cold backend generated source parity failed for '{pair.Key}'.");
            }
        }
    }

    private static void WriteReports(FeatureGalleryBenchmarkProject project, Options options, List<PairSample> pairs)
    {
        var eagerMean = pairs.Average(static pair => pair.Eager.ElapsedMilliseconds);
        var currentMean = pairs.Average(static pair => pair.Current.ElapsedMilliseconds);
        var methodology = new[]
        {
            "Real MSBuild FeatureGallery snapshot, one process and the same warmed CSharpCompilation in both arms.",
            "A fresh virtual file identity set per pair; independent fresh syntax/version objects per arm; no incremental parse cache or prior project state.",
            "Parsing, version extraction, forced full GC, validation, parity checks and reporting are outside timing.",
            "Eager: compatibility catalog plus the original three parallel writer waves (components /4, AKCSS /2 processors).",
            "Current: fresh project state, request/index/dependency construction and current unified batch, including semantic-state capture.",
            "All-thread allocations use GC.GetTotalAllocatedBytes; stage durations can overlap and are not additive wall time.",
            "Each pair validates both generated compilations and exact generated text by hint name. Warmups are excluded from the report.",
            "A bounded paired diagnostic, not a statistical significance claim or an end-to-end BenchmarkDotNet result.",
        };
        var report = new
        {
            SchemaVersion = 1,
            CreatedUtc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            GCSettings.IsServerGC,
            project.ProjectDirectory,
            project.AdditionalFileCount,
            options.Warmups,
            options.Pairs,
            Methodology = methodology,
            EagerMeanMilliseconds = eagerMean,
            CurrentMeanMilliseconds = currentMean,
            RatioOfMeans = currentMean / eagerMean,
            ExactSourceParity = true,
            CompilationsValid = true,
            Samples = pairs,
        };
        var markdown = new StringBuilder();
        markdown.AppendLine("# FeatureGallery paired cold-backend comparison");
        markdown.AppendLine();
        markdown.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}; processors: {Environment.ProcessorCount}; server GC: {GCSettings.IsServerGC}.");
        markdown.AppendLine($"Warmup pairs: {options.Warmups}; measured alternating pairs: {options.Pairs}.");
        markdown.AppendLine();
        foreach (var line in methodology)
        {
            markdown.AppendLine("- " + line);
        }

        markdown.AppendLine();
        markdown.AppendLine("| Pair | Order | Eager ms | Current ms | Ratio | Eager MiB | Current MiB | Eager semantic ms | Current semantic ms |");
        markdown.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
        foreach (var pair in pairs)
        {
            markdown.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1} | {2:F3} | {3:F3} | {4:F4} | {5:F3} | {6:F3} | {7:F3} | {8:F3} |",
                pair.Pair,
                pair.Order,
                pair.Eager.ElapsedMilliseconds,
                pair.Current.ElapsedMilliseconds,
                pair.Ratio,
                pair.Eager.AllocatedBytes / (1024d * 1024d),
                pair.Current.AllocatedBytes / (1024d * 1024d),
                pair.Eager.Statistics.SemanticBindingElapsed.TotalMilliseconds,
                pair.Current.Statistics.SemanticBindingElapsed.TotalMilliseconds));
        }

        markdown.AppendLine();
        markdown.AppendLine(FormattableString.Invariant($"Mean: Eager {eagerMean:F3} ms; Current {currentMean:F3} ms; ratio {currentMean / eagerMean:F4}x."));
        markdown.AppendLine("Both generated compilations are valid and every pair has exact hint/text parity. JSON includes per-stage totals and GC counts.");
        Directory.CreateDirectory(options.OutputDirectory);
        var jsonPath = Path.Combine(options.OutputDirectory, "comparison.json");
        var markdownPath = Path.Combine(options.OutputDirectory, "report.md");
        File.WriteAllText(jsonPath, ToCrlf(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })), new UTF8Encoding(false));
        File.WriteAllText(markdownPath, ToCrlf(markdown.ToString()), new UTF8Encoding(false));
        Console.WriteLine(FormattableString.Invariant($"Mean Current/Eager: {currentMean / eagerMean:F4}x ({currentMean:F3} / {eagerMean:F3} ms)."));
        Console.WriteLine($"Reports: {Path.GetFullPath(jsonPath)}");
        Console.WriteLine($"         {Path.GetFullPath(markdownPath)}");
    }

    private static string ToCrlf(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    private sealed record Inputs(string ProjectDirectory, ImmutableArray<AkburaSyntaxTree> Trees, ImmutableArray<DocumentSyntaxVersion> Versions);
    private sealed record MeasuredResult(Sample Sample, ImmutableArray<GeneratedSource> Sources);
    private sealed record Sample(string Backend, double ElapsedMilliseconds, long AllocatedBytes, int Gen0, int Gen1, int Gen2, int Documents, GenerationStatisticsSnapshot Statistics);
    private sealed record PairSample(int Pair, string Order, Sample Eager, Sample Current, double Ratio);
    private sealed record Options(int Warmups, int Pairs, string OutputDirectory)
    {
        public static Options Parse(string[] args)
        {
            var warmups = 1;
            var pairs = 3;
            var output = "artifacts/black-silence-cold-compare";
            for (var i = 1; i < args.Length; i++)
            {
                if (i + 1 >= args.Length)
                {
                    throw new ArgumentException($"Missing value for '{args[i]}'.");
                }

                var option = args[i];
                var value = args[++i];
                switch (option)
                {
                    case "--warmups": warmups = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--pairs": pairs = int.Parse(value, CultureInfo.InvariantCulture); break;
                    case "--output": output = value; break;
                    default: throw new ArgumentException($"Unknown cold-comparison option '{option}'.");
                }
            }

            if (warmups < 0 || pairs < 1 || string.IsNullOrWhiteSpace(output))
            {
                throw new ArgumentException("Warmups must be non-negative, pairs positive, and output non-empty.");
            }

            return new Options(warmups, pairs, output);
        }
    }
#endif
}
