using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryStageMeasurements
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--black-silence-stages")
        {
            return false;
        }

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine(
                "--black-silence-stages [--warmups 1] [--iterations 3] " +
                "[--output artifacts/black-silence-stages] [--scenario ScenarioName]");
            Console.WriteLine("Build with -p:EnableAkburaStats=true. The default runs all five generator scenarios.");
            return true;
        }

#if STATS
        Run(MeasurementOptions.Parse(args));
        return true;
#else
        throw new InvalidOperationException(
            "Stage measurements require STATS. Build the benchmark project with -p:EnableAkburaStats=true.");
#endif
    }

    internal static void ValidateCompilation(Compilation compilation)
    {
        var errors = compilation.GetDiagnostics()
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(32)
            .ToArray();

        if (errors.Length != 0)
        {
            throw new InvalidOperationException(
                "BlackSilence produced an invalid FeatureGallery compilation." +
                Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }
    }

#if STATS
    private static void Run(MeasurementOptions options)
    {
        Console.WriteLine("Loading the real Akbura.FeatureGallery project...");
        var project = FeatureGalleryBenchmarkProject.Load();
        var results = new List<ScenarioMeasurement>();
        var scenarios = options.Scenario is { } scenario
            ? new[] { scenario }
            : Enum.GetValues<FeatureGalleryGeneratorScenario>();

        foreach (var currentScenario in scenarios)
        {
            Console.WriteLine($"{currentScenario}: {options.Warmups} warmups, {options.Iterations} measured runs.");

            for (var i = 0; i < options.Warmups; i++)
            {
                var warmup = Prepare(project, currentScenario);
                RunGenerator(warmup);
            }

            var samples = new List<MeasurementSample>(options.Iterations);

            for (var i = 0; i < options.Iterations; i++)
            {
                // A new primed driver per sample prevents mutable project-state reuse
                // from turning the second edit measurement into a no-op.
                var workload = Prepare(project, currentScenario);
                GeneratorRunResult generated;
                MeasurementSample sample;

                using (var measurement = BlackSilenceGenerationStatistics.BeginMeasurement())
                {
                    var allocatedBefore = GC.GetTotalAllocatedBytes(precise: true);
                    var started = Stopwatch.GetTimestamp();

                    generated = RunGenerator(workload);

                    var elapsed = Stopwatch.GetElapsedTime(started);
                    var allocatedBytes = GC.GetTotalAllocatedBytes(precise: true) - allocatedBefore;
                    sample = new MeasurementSample(
                        i + 1,
                        elapsed.TotalMilliseconds,
                        allocatedBytes,
                        generated.GeneratedSources.Length,
                        measurement.GetSnapshot());
                }

                // Compilation validation and reporting are deliberately outside timing
                // and outside the statistics session.
                ValidateCompilation(workload.Compilation.AddSyntaxTrees(
                    generated.GeneratedSources.Select(static source => source.SyntaxTree)));

                samples.Add(sample);
                Console.WriteLine(string.Format(
                    CultureInfo.InvariantCulture,
                    "  Run {0}: {1:F3} ms, {2:F3} MiB, {3} documents.",
                    i + 1,
                    sample.ElapsedMilliseconds,
                    sample.AllocatedBytes / (1024d * 1024d),
                    sample.GeneratedDocuments));
            }

            results.Add(new ScenarioMeasurement(currentScenario, samples));
        }

        WriteReports(project, options, results);
    }

    private static PreparedWorkload Prepare(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryGeneratorScenario scenario)
    {
        var editTarget = scenario switch
        {
            FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit => FeatureGalleryEditTarget.Component,
            FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit => FeatureGalleryEditTarget.Akcss,
            _ => FeatureGalleryEditTarget.None,
        };

        var snapshot = project.CreateSnapshot(editTarget);
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: snapshot.AdditionalTexts,
            parseOptions: project.ParseOptions,
            optionsProvider: snapshot.OptionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false));

        var compilation = project.Compilation;

        if (scenario != FeatureGalleryGeneratorScenario.ColdGeneration)
        {
            driver = driver.RunGenerators(compilation);
            ValidateResult(driver.GetRunResult().Results.Single());
        }

        switch (scenario)
        {
            case FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit:
                driver = driver.ReplaceAdditionalText(snapshot.ComponentOriginal, snapshot.ComponentModified);
                break;

            case FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit:
                driver = driver.ReplaceAdditionalText(snapshot.AkcssOriginal, snapshot.AkcssModified);
                break;

            case FeatureGalleryGeneratorScenario.CSharpCompilationEdit:
                compilation = project.CreateCSharpEditCompilation();
                break;
        }

        return new PreparedWorkload(driver, compilation);
    }

    private static GeneratorRunResult RunGenerator(PreparedWorkload workload)
    {
        var driver = workload.Driver.RunGenerators(workload.Compilation);
        var result = driver.GetRunResult().Results.Single();
        ValidateResult(result);
        return result;
    }

    private static void ValidateResult(GeneratorRunResult result)
    {
        if (result.Exception != null)
        {
            throw new InvalidOperationException("BlackSilence failed during a stage measurement.", result.Exception);
        }

        if (result.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException("BlackSilence did not generate any FeatureGallery documents.");
        }

        if (result.Diagnostics.Any(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
        {
            throw new InvalidOperationException(string.Join(Environment.NewLine, result.Diagnostics));
        }
    }

    private static void WriteReports(
        FeatureGalleryBenchmarkProject project,
        MeasurementOptions options,
        List<ScenarioMeasurement> results)
    {
        var report = new
        {
            SchemaVersion = 1,
            CreatedUtc = DateTimeOffset.UtcNow,
            Runtime = RuntimeInformation.FrameworkDescription,
            OperatingSystem = RuntimeInformation.OSDescription,
            Environment.ProcessorCount,
            GCSettings.IsServerGC,
            Project = project.ProjectDirectory,
            project.AdditionalFileCount,
            project.ComponentEditFile,
            project.AkcssEditFile,
            options.Warmups,
            options.Iterations,
            Methodology = new[]
            {
                "Real MSBuild FeatureGallery compilation and AdditionalFiles; only the two compared generators are removed.",
                "Driver creation, priming, edit creation, validation and report serialization are outside the measured region.",
                "Each sample has a fresh driver and unique virtual AdditionalFile paths, including every edit scenario.",
                "ColdGeneration is a cold generator driver in a warmed process, not a cold OS process or MSBuild load.",
                "AllocatedBytes is a process-wide GC.GetTotalAllocatedBytes delta and includes worker-thread allocations.",
                "Stage times can overlap and nest. Worker-document planning/emission totals must not be added to wall time.",
                "Backend BenchmarkDotNet groups are sequential repeated planner+writer workloads with pre-bound semantic inputs.",
            },
            Scenarios = results.Select(static result => new
            {
                result.Scenario,
                Summary = Summarize(result.Samples),
                result.Samples,
            }),
        };

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() },
        };

        var markdown = new StringBuilder();
        markdown.AppendLine("# BlackSilence FeatureGallery stage measurements");
        markdown.AppendLine();
        markdown.AppendLine($"Runtime: {RuntimeInformation.FrameworkDescription}; processors: {Environment.ProcessorCount}.");
        markdown.AppendLine($"Warmups: {options.Warmups}; measured runs per scenario: {options.Iterations}.");
        markdown.AppendLine($"Component edit: `{project.ComponentEditFile}`; AKCSS edit: `{project.AkcssEditFile}`.");
        markdown.AppendLine();
        markdown.AppendLine("Setup, validation and reporting are excluded. Each sample uses a fresh primed driver.");
        markdown.AppendLine("Allocations include all worker threads. Stage durations overlap/nest; they are not an additive wall-time breakdown.");
        markdown.AppendLine("Planning includes remaining lazy semantic binding; backend benchmarks warm those caches in GlobalSetup.");
        markdown.AppendLine();
        markdown.AppendLine("| Scenario | Mean ms | Median ms | Min ms | Max ms | StdDev ms | Mean MiB | Documents |");
        markdown.AppendLine("| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");

        foreach (var result in results)
        {
            var summary = Summarize(result.Samples);
            markdown.AppendLine(string.Format(
                CultureInfo.InvariantCulture,
                "| {0} | {1:F3} | {2:F3} | {3:F3} | {4:F3} | {5:F3} | {6:F3} | {7} |",
                result.Scenario,
                summary.MeanMilliseconds,
                summary.MedianMilliseconds,
                summary.MinMilliseconds,
                summary.MaxMilliseconds,
                summary.StandardDeviationMilliseconds,
                summary.MeanAllocatedBytes / (1024d * 1024d),
                result.Samples[0].GeneratedDocuments));
        }

        foreach (var result in results)
        {
            markdown.AppendLine();
            markdown.AppendLine($"## {result.Scenario}");
            markdown.AppendLine();
            markdown.AppendLine("| Stage | Mean ms |");
            markdown.AppendLine("| --- | ---: |");
            WriteStage("Parse", static value => value.ParseElapsed);
            WriteStage("Compilation", static value => value.CompilationElapsed);
            WriteStage("C# probe compilation", static value => value.CSharpProbeCompilationElapsed);
            WriteStage("Catalog (inclusive)", static value => value.CatalogElapsed);
            WriteStage("Semantic input resolution", static value => value.SemanticBindingElapsed);
            WriteStage("Component planning", static value => value.ComponentPlanningElapsed);
            WriteStage("AKCSS planning", static value => value.AkcssPlanningElapsed);
            WriteStage("C# emission", static value => value.CSharpEmissionElapsed);
            WriteStage("SourceText creation", static value => value.SourceTextCreationElapsed);
            WriteStage("Component generation (worker sum)", static value => value.ComponentGenerationElapsed);
            WriteStage("AKCSS generation (worker sum)", static value => value.AkcssGenerationElapsed);
            WriteStage("Component batch (wall)", static value => value.ComponentBatchElapsed);
            WriteStage("AKCSS batches (wall)", static value => value.AkcssBatchElapsed);
            WriteStage("Unified document batch (wall)", static value => value.DocumentBatchElapsed);
            markdown.AppendLine();
            markdown.AppendLine("| Counter | Mean per run |");
            markdown.AppendLine("| --- | ---: |");
            WriteCounter("ReadSourceTextCount", static value => value.ReadSourceTextCount);
            WriteCounter("FullParseCount", static value => value.FullParseCount);
            WriteCounter("IncrementalParseCount", static value => value.IncrementalParseCount);
            WriteCounter("CompilationCreatedCount", static value => value.CompilationCreatedCount);
            WriteCounter("CompilationReusedCount", static value => value.CompilationReusedCount);
            WriteCounter("SemanticModelCreatedCount", static value => value.SemanticModelCreatedCount);
            WriteCounter("ComponentGeneratedCount", static value => value.ComponentGeneratedCount);
            WriteCounter("ComponentReusedCount", static value => value.ComponentReusedCount);
            WriteCounter("AkcssGeneratedCount", static value => value.AkcssGeneratedCount);
            WriteCounter("AkcssReusedCount", static value => value.AkcssReusedCount);
            WriteCounter("GeneratedSourceTextCreatedCount", static value => value.GeneratedSourceTextCreatedCount);

            void WriteStage(string name, Func<GenerationStatisticsSnapshot, TimeSpan> selector)
            {
                var mean = result.Samples.Average(sample => selector(sample.Statistics).TotalMilliseconds);
                markdown.AppendLine(FormattableString.Invariant($"| {name} | {mean:F3} |"));
            }

            void WriteCounter(string name, Func<GenerationStatisticsSnapshot, long> selector)
            {
                var mean = result.Samples.Average(sample => selector(sample.Statistics));
                markdown.AppendLine(FormattableString.Invariant($"| {name} | {mean:F1} |"));
            }
        }

        Directory.CreateDirectory(options.OutputDirectory);
        var jsonPath = Path.Combine(options.OutputDirectory, "statistics.json");
        var markdownPath = Path.Combine(options.OutputDirectory, "report.md");
        File.WriteAllText(jsonPath, ToCrlf(JsonSerializer.Serialize(report, jsonOptions)), new UTF8Encoding(false));
        File.WriteAllText(markdownPath, ToCrlf(markdown.ToString()), new UTF8Encoding(false));
        Console.WriteLine($"Reports: {jsonPath}");
        Console.WriteLine($"         {markdownPath}");
    }

    private static MeasurementSummary Summarize(List<MeasurementSample> samples)
    {
        var ordered = samples.Select(static sample => sample.ElapsedMilliseconds).Order().ToArray();
        var mean = ordered.Average();
        var middle = ordered.Length / 2;
        var median = ordered.Length % 2 == 0 ? (ordered[middle - 1] + ordered[middle]) / 2 : ordered[middle];
        var variance = ordered.Length > 1
            ? ordered.Sum(value => (value - mean) * (value - mean)) / (ordered.Length - 1)
            : 0;

        return new MeasurementSummary(
            mean,
            median,
            ordered[0],
            ordered[^1],
            Math.Sqrt(variance),
            samples.Average(static sample => sample.AllocatedBytes));
    }

    private static string ToCrlf(string text)
    {
        return text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    }

    private sealed record PreparedWorkload(GeneratorDriver Driver, CSharpCompilation Compilation);

    private sealed record MeasurementSample(
        int Iteration,
        double ElapsedMilliseconds,
        long AllocatedBytes,
        int GeneratedDocuments,
        GenerationStatisticsSnapshot Statistics);

    private sealed record ScenarioMeasurement(
        FeatureGalleryGeneratorScenario Scenario,
        List<MeasurementSample> Samples);

    private sealed record MeasurementSummary(
        double MeanMilliseconds,
        double MedianMilliseconds,
        double MinMilliseconds,
        double MaxMilliseconds,
        double StandardDeviationMilliseconds,
        double MeanAllocatedBytes);

    private sealed record MeasurementOptions(
        int Warmups,
        int Iterations,
        string OutputDirectory,
        FeatureGalleryGeneratorScenario? Scenario)
    {
        public static MeasurementOptions Parse(string[] args)
        {
            var warmups = 1;
            var iterations = 3;
            var outputDirectory = Path.Combine("artifacts", "black-silence-stages");
            FeatureGalleryGeneratorScenario? scenario = null;

            for (var i = 1; i < args.Length; i++)
            {
                var option = args[i];
                if (++i == args.Length)
                {
                    throw new ArgumentException($"Missing value for '{option}'.");
                }

                var value = args[i];

                switch (option)
                {
                    case "--warmups":
                        warmups = ParseCount(value, allowZero: true);
                        break;

                    case "--iterations":
                        iterations = ParseCount(value, allowZero: false);
                        break;

                    case "--output":
                        outputDirectory = value;
                        break;

                    case "--scenario" when Enum.TryParse<FeatureGalleryGeneratorScenario>(
                        value,
                        ignoreCase: true,
                        out var parsedScenario) && Enum.IsDefined(parsedScenario):
                        scenario = parsedScenario;
                        break;

                    default:
                        throw new ArgumentException($"Unknown option or invalid value: '{option} {value}'.");
                }
            }

            return new MeasurementOptions(warmups, iterations, Path.GetFullPath(outputDirectory), scenario);
        }

        private static int ParseCount(string value, bool allowZero)
        {
            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var count) ||
                count < (allowZero ? 0 : 1))
            {
                throw new ArgumentException($"Invalid measurement count '{value}'.");
            }

            return count;
        }
    }
#endif
}
