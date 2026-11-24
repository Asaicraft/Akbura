using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using BlackSilenceGenerator = Akbura.BlackSilence.AkburaBlackSilenceGenerator;

namespace Akbura.Benchmarks;

public enum FeatureGalleryGeneratorScenario
{
    ColdGeneration,
    NoChanges,
    ComponentWhitespaceEdit,
    AkcssWhitespaceEdit,
    CSharpCompilationEdit,
}

[Config(typeof(FeatureGalleryGeneratorBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery")]
public class FeatureGalleryGeneratorBenchmarks
{
    private static readonly Lazy<FeatureGalleryBenchmarkProject> s_project =
        new(FeatureGalleryBenchmarkProject.Load);

    private FeatureGalleryBenchmarkProject _project = null!;
    private CSharpCompilation _benchmarkCompilation = null!;
    private GeneratorDriver _driver = null!;

    [Params(
        FeatureGalleryGeneratorScenario.ColdGeneration,
        FeatureGalleryGeneratorScenario.NoChanges,
        FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit,
        FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit,
        FeatureGalleryGeneratorScenario.CSharpCompilationEdit)]
    public FeatureGalleryGeneratorScenario Scenario { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _project = s_project.Value;
        _benchmarkCompilation = _project.Compilation;

        ValidateGenerator();

        switch (Scenario)
        {
            case FeatureGalleryGeneratorScenario.NoChanges:
                PrepareNoChanges();
                break;

            case FeatureGalleryGeneratorScenario.CSharpCompilationEdit:
                PrepareCSharpCompilationEdit();
                break;
        }
    }

    [IterationSetup]
    public void IterationSetup()
    {
        switch (Scenario)
        {
            case FeatureGalleryGeneratorScenario.ColdGeneration:
                PrepareColdGeneration();
                break;

            case FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit:
                PrepareAdditionalTextEdit(FeatureGalleryEditTarget.Component);
                break;

            case FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit:
                PrepareAdditionalTextEdit(FeatureGalleryEditTarget.Akcss);
                break;
        }
    }

    private void PrepareColdGeneration()
    {
        var snapshot = _project.CreateSnapshot();

        _driver = CreateDriver(snapshot);
        _benchmarkCompilation = _project.Compilation;
    }

    private void PrepareNoChanges()
    {
        var snapshot = _project.CreateSnapshot();

        _driver = CreateDriver(snapshot);
        _driver = _driver.RunGenerators(_project.Compilation);
        _benchmarkCompilation = _project.Compilation;
    }

    private void PrepareAdditionalTextEdit(FeatureGalleryEditTarget editTarget)
    {
        var snapshot = _project.CreateSnapshot(editTarget);

        _driver = CreateDriver(snapshot);
        _driver = _driver.RunGenerators(_project.Compilation);

        var original = editTarget == FeatureGalleryEditTarget.Component
            ? snapshot.ComponentOriginal
            : snapshot.AkcssOriginal;

        var modified = editTarget == FeatureGalleryEditTarget.Component
            ? snapshot.ComponentModified
            : snapshot.AkcssModified;

        _driver = _driver.ReplaceAdditionalText(original, modified);
        _benchmarkCompilation = _project.Compilation;
    }

    private void PrepareCSharpCompilationEdit()
    {
        var snapshot = _project.CreateSnapshot();

        _driver = CreateDriver(snapshot);
        _driver = _driver.RunGenerators(_project.Compilation);
        _benchmarkCompilation = _project.CreateCSharpEditCompilation();
    }

    [Benchmark]
    public int BlackSilence()
    {
        var driver = _driver.RunGenerators(_benchmarkCompilation);
        var result = driver.GetRunResult();
        var generatorResult = result.Results[0];

        if (generatorResult.Exception != null)
        {
            throw new InvalidOperationException(
                $"{Scenario} failed while generating Akbura.FeatureGallery.",
                generatorResult.Exception);
        }

        if (generatorResult.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException(
                $"{Scenario} did not generate any FeatureGallery sources.");
        }

        return generatorResult.GeneratedSources.Length;
    }

    private GeneratorDriver CreateDriver(FeatureGalleryBenchmarkSnapshot snapshot)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new BlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: snapshot.AdditionalTexts,
            parseOptions: _project.ParseOptions,
            optionsProvider: snapshot.OptionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: false));
    }

    private void ValidateGenerator()
    {
        var snapshot = _project.CreateSnapshot();
        const string generatorName = nameof(Akbura.BlackSilence.AkburaBlackSilenceGenerator);
        var driver = CreateDriver(snapshot);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            _project.Compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var result = driver.GetRunResult();
        var generatorResult = result.Results[0];

        if (generatorResult.Exception != null)
        {
            throw new InvalidOperationException(
                $"{generatorName} failed while generating Akbura.FeatureGallery.",
                generatorResult.Exception);
        }

        var errors = generatorDiagnostics
            .Concat(outputCompilation.GetDiagnostics())
            .Where(static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error)
            .Take(32)
            .ToArray();

        if (errors.Length != 0)
        {
            throw new InvalidOperationException(
                generatorName +
                " produced an invalid FeatureGallery compilation." +
                Environment.NewLine +
                string.Join(
                    Environment.NewLine,
                    errors.Select(static diagnostic => diagnostic.ToString())));
        }

        if (generatorResult.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException(
                $"{generatorName} did not generate any FeatureGallery sources.");
        }
    }
}

public sealed class FeatureGalleryGeneratorBenchmarkConfig : ManualConfig
{
    public FeatureGalleryGeneratorBenchmarkConfig()
    {
        var job = FeatureGalleryBenchmarkBuild.Configure(Job.Default)
            .WithId("FeatureGallery")
            .WithLaunchCount(1)
            .WithMinWarmupCount(4)
            .WithMaxWarmupCount(8)
            .WithMinIterationCount(15)
            .WithMaxIterationCount(30)
            .WithMaxRelativeError(0.03)
            .WithInvocationCount(1)
            .WithUnrollFactor(1);

        AddJob(job);
    }
}
