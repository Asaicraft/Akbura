using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Perfolizer.Horology;
using System.Collections.Immutable;
using BlackSilenceGenerator = Akbura.BlackSilence.AkburaBlackSilenceGenerator;
using FuriosoGenerator = Akbura.Furioso.AkburaCsGenerator;

namespace Akbura.Benchmarks;

/// <summary>
/// A warm editing session over the real FeatureGallery project. Every invocation
/// changes meaningful input and advances the driver, including pilot and warmup.
/// </summary>
[Config(typeof(FeatureGalleryIncrementalBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "MeaningfulIncremental")]
public class FeatureGalleryIncrementalBenchmarks
{
    private static readonly Lazy<FeatureGalleryBenchmarkProject> s_project = new(FeatureGalleryBenchmarkProject.Load);
    private FeatureGalleryIncrementalSession _session = null!;

    [ParamsAllValues]
    public FeatureGalleryIncrementalScenario Scenario { get; set; }

    [GlobalSetup(Target = nameof(Furioso))]
    public void SetupFurioso() => Setup(static () => new FuriosoGenerator());

    [GlobalSetup(Target = nameof(BlackSilence))]
    public void SetupBlackSilence() => Setup(static () => new BlackSilenceGenerator());

    [Benchmark(Baseline = true)]
    public int Furioso() => _session.ApplyNextEdit();

    [Benchmark]
    public int BlackSilence() => _session.ApplyNextEdit();

    private void Setup(Func<IIncrementalGenerator> generatorFactory)
    {
        var project = s_project.Value;
        var inputs = FeatureGalleryIncrementalScenarioFactory.Create(project, Scenario);
        _session = new FeatureGalleryIncrementalSession(project, inputs, generatorFactory);
        _session.ValidateAndPrime(Scenario);
    }
}

public sealed class FeatureGalleryIncrementalBenchmarkConfig : ManualConfig
{
    public FeatureGalleryIncrementalBenchmarkConfig()
    {
        AddJob(FeatureGalleryBenchmarkBuild.Configure(Job.Default
            .WithId("MeaningfulIncremental")
            .WithLaunchCount(3)
            .WithMinWarmupCount(4)
            .WithMaxWarmupCount(8)
            .WithMinIterationCount(15)
            .WithMaxIterationCount(30)
            .WithMaxRelativeError(0.03)
            .WithMinIterationTime(TimeInterval.FromMilliseconds(250))
            .WithMinInvokeCount(1)
            .WithUnrollFactor(1)));
        AddColumn(StatisticColumn.Median, StatisticColumn.Min, StatisticColumn.Max);
    }
}

internal sealed class FeatureGalleryIncrementalSession(
    FeatureGalleryBenchmarkProject project,
    FeatureGalleryIncrementalInputs inputs,
    Func<IIncrementalGenerator> generatorFactory)
{
    private GeneratorDriver _driver = null!;
    private bool _modified;

    public void ValidateAndPrime(FeatureGalleryIncrementalScenario scenario)
    {
        // Validation is outside timing, but uses the same inputs and publication
        // policy as timing. Compare each generator against its own fresh output:
        // Furioso and BlackSilence intentionally emit different implementations.
        var original = Capture(CreateDriver(inputs.OriginalFiles), inputs.OriginalCompilation);
        var modified = Capture(original.Driver.ReplaceAdditionalTexts(inputs.ModifiedFiles), inputs.ModifiedCompilation);
        var freshModified = Capture(CreateDriver(inputs.ModifiedFiles), inputs.ModifiedCompilation);
        VerifyEquivalent(freshModified, modified, "forward edit");

        var restored = Capture(modified.Driver.ReplaceAdditionalTexts(inputs.OriginalFiles), inputs.OriginalCompilation);
        VerifyEquivalent(original, restored, "reverse edit");

        var changedHints = original.Sources.Keys.Union(modified.Sources.Keys, StringComparer.Ordinal)
            .Where(hint => !original.Sources.TryGetValue(hint, out var before) ||
                !modified.Sources.TryGetValue(hint, out var after) ||
                !string.Equals(before, after, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();

        if (changedHints.Length == 0)
        {
            throw new InvalidOperationException($"{scenario} did not change any generated source. A no-op is not an incremental-work benchmark.");
        }

        Console.WriteLine($"Validated {scenario}: {changedHints.Length} changed generated sources; exact fresh/incremental source and diagnostic parity in both directions.");
        foreach (var hint in changedHints)
        {
            Console.WriteLine("  " + hint);
        }

        // Keep the current coherent driver, not an earlier immutable driver whose
        // generator-owned cache may have advanced during the validation round trip.
        _driver = restored.Driver;
        _modified = false;
    }

    public int ApplyNextEdit()
    {
        var modified = !_modified;
        var files = modified ? inputs.ModifiedFiles : inputs.OriginalFiles;
        var compilation = modified ? inputs.ModifiedCompilation : inputs.OriginalCompilation;
        var driver = _driver.ReplaceAdditionalTexts(files).RunGenerators(compilation);
        var result = driver.GetRunResult().Results.Single();
        ValidateResult(result);

        // Advancing the driver is essential. Repeated invocations must not replay
        // one prepared edit against a cache that already contains its result.
        _driver = driver;
        _modified = modified;
        return result.GeneratedSources.Length;
    }

    private GeneratorDriver CreateDriver(ImmutableArray<AdditionalText> files)
    {
        return CSharpGeneratorDriver.Create(
            generators: [generatorFactory().AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: project.ParseOptions,
            optionsProvider: new DiagnosticBenchmarkOptionsProvider(inputs.Snapshot.OptionsProvider, "Publish"),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: false));
    }

    private static CapturedRun Capture(GeneratorDriver driver, CSharpCompilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var driverDiagnostics);
        var result = driver.GetRunResult().Results.Single();
        ValidateResult(result);
        var compilerDiagnostics = output.GetDiagnostics();
        var errors = driverDiagnostics.Concat(compilerDiagnostics)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();

        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Incremental benchmark produced invalid C#:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }

        var diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", driverDiagnostics, ignoreTransport: true)
            .Concat(FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, ignoreTransport: true))
            .Concat(FeatureGalleryParityVerification.DescribeDiagnostics("compiler", compilerDiagnostics, ignoreTransport: true))
            .Order(StringComparer.Ordinal).ToArray();

        return new CapturedRun(driver,
            result.GeneratedSources.ToDictionary(static source => source.HintName, static source => source.SourceText.ToString(), StringComparer.Ordinal),
            diagnostics);
    }

    private static void ValidateResult(GeneratorRunResult result)
    {
        if (result.Exception != null)
        {
            throw new InvalidOperationException("Incremental benchmark generator failed.", result.Exception);
        }

        if (result.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException("Incremental benchmark generated no FeatureGallery sources.");
        }

        foreach (var diagnostic in result.Diagnostics)
        {
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                throw new InvalidOperationException("Incremental benchmark generator diagnostic: " + diagnostic);
            }
        }
    }

    private static void VerifyEquivalent(CapturedRun expected, CapturedRun actual, string phase)
    {
        if (expected.Sources.Count != actual.Sources.Count)
        {
            throw new InvalidOperationException($"{phase}: fresh/incremental generated source counts differ.");
        }

        foreach (var (hint, text) in expected.Sources)
        {
            if (!actual.Sources.TryGetValue(hint, out var actualText) || !string.Equals(text, actualText, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"{phase}: fresh/incremental generated source differs: {hint}.");
            }
        }

        if (!expected.Diagnostics.SequenceEqual(actual.Diagnostics, StringComparer.Ordinal))
        {
            throw new InvalidOperationException($"{phase}: fresh/incremental diagnostics differ (including locations, severity and multiplicity).");
        }
    }

    private sealed record CapturedRun(GeneratorDriver Driver, Dictionary<string, string> Sources, string[] Diagnostics);
}
