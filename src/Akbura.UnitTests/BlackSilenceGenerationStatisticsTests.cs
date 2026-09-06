#if STATS
using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class BlackSilenceGenerationStatisticsTests
{
    [Fact]
    public void Measurement_CountsActualGenerationButNotCachedCallbacks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlackSilenceStatistics", Guid.NewGuid().ToString("N"));
        var component = new TestAdditionalText(
            Path.Combine(directory, "StatisticsView.akbura"),
            "using Avalonia.Controls;\r\n<Border />\r\n");
        var styles = new TestAdditionalText(
            Path.Combine(directory, "Styles.akcss"),
            "@using Avalonia.Controls;\r\n.button { Width: 10; }\r\n");
        var compilation = CSharpCompilation.Create(
            "BlackSilenceStatistics",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: [component, styles],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        GenerationStatisticsSnapshot cold;
        using (var measurement = BlackSilenceGenerationStatistics.BeginMeasurement())
        {
            driver = driver.RunGenerators(compilation);
            cold = measurement.GetSnapshot();
        }

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        Assert.Equal(2, result.GeneratedSources.Length);
        Assert.Equal(2, cold.ReadSourceTextCount);
        Assert.Equal(2, cold.FullParseCount);
        Assert.Equal(0, cold.IncrementalParseCount);
        Assert.Equal(1, cold.CompilationCreatedCount);
        Assert.True(cold.SemanticModelCreatedCount >= 2);
        Assert.Equal(1, cold.ComponentGeneratedCount);
        Assert.Equal(1, cold.AkcssGeneratedCount);
        Assert.Equal(2, cold.GeneratedSourceTextCreatedCount);

        using var cachedMeasurement = BlackSilenceGenerationStatistics.BeginMeasurement();
        var cachedDriver = driver.RunGenerators(compilation);
        var cached = cachedMeasurement.GetSnapshot();

        Assert.Null(Assert.Single(cachedDriver.GetRunResult().Results).Exception);
        Assert.Equal(0, cached.ReadSourceTextCount);
        Assert.Equal(0, cached.FullParseCount);
        Assert.Equal(0, cached.IncrementalParseCount);
        Assert.Equal(0, cached.CompilationCreatedCount);
        Assert.Equal(0, cached.SemanticModelCreatedCount);
        Assert.Equal(0, cached.ComponentGeneratedCount);
        Assert.Equal(0, cached.AkcssGeneratedCount);
        Assert.Equal(0, cached.GeneratedSourceTextCreatedCount);
        Assert.Equal(TimeSpan.Zero, cached.ComponentGenerationElapsed);
        Assert.Equal(TimeSpan.Zero, cached.AkcssGenerationElapsed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void WhitespaceEdit_ParsesOneFileWithoutCreatingSemanticModelsOrSources(bool editAkcss)
    {
        var directory = Path.Combine(Path.GetTempPath(), "BlackSilenceStatistics", Guid.NewGuid().ToString("N"));
        var component = new TestAdditionalText(
            Path.Combine(directory, "StatisticsView.akbura"),
            "using Avalonia.Controls;\r\n<Border />");
        var styles = new TestAdditionalText(
            Path.Combine(directory, "Styles.akcss"),
            "@using Avalonia.Controls;\r\n.button { Width: 10; }");
        var compilation = CSharpCompilation.Create(
            "BlackSilenceStatistics",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: [component, styles],
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
        driver = driver.RunGenerators(compilation);
        var original = editAkcss ? styles : component;
        var changed = new TestAdditionalText(original.Path, original.GetText().ToString() + "\r\n \t\r\n");

        using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
        var updated = driver.ReplaceAdditionalText(original, changed).RunGenerators(compilation);
        var statistics = measurement.GetSnapshot();

        Assert.Null(Assert.Single(updated.GetRunResult().Results).Exception);
        Assert.Equal(2, Assert.Single(updated.GetRunResult().Results).GeneratedSources.Length);
        Assert.Equal(1, statistics.ReadSourceTextCount);
        Assert.Equal(0, statistics.FullParseCount);
        Assert.Equal(1, statistics.IncrementalParseCount);
        Assert.Equal(0, statistics.CompilationCreatedCount);
        Assert.Equal(0, statistics.SemanticModelCreatedCount);
        Assert.Equal(0, statistics.ComponentGeneratedCount);
        Assert.Equal(0, statistics.AkcssGeneratedCount);
        Assert.Equal(0, statistics.GeneratedSourceTextCreatedCount);
        Assert.Equal(TimeSpan.Zero, statistics.DocumentBatchElapsed);
    }

    [Fact]
    public void NestedMeasurements_RestoreTheirParentWithoutMixingCounters()
    {
        using var outer = BlackSilenceGenerationStatistics.BeginMeasurement();
        GenerationStatistics.Increment(GenerationStatisticCounter.ComponentGenerated);

        using (var inner = BlackSilenceGenerationStatistics.BeginMeasurement())
        {
            Parallel.For(0, 23, static _ =>
                GenerationStatistics.Increment(GenerationStatisticCounter.AkcssGenerated));

            Assert.Equal(23, inner.GetSnapshot().AkcssGeneratedCount);
            Assert.Equal(0, inner.GetSnapshot().ComponentGeneratedCount);
        }

        GenerationStatistics.Increment(GenerationStatisticCounter.ComponentGenerated);

        Assert.Equal(2, outer.GetSnapshot().ComponentGeneratedCount);
        Assert.Equal(0, outer.GetSnapshot().AkcssGeneratedCount);
    }

    [Fact]
    public async Task ConcurrentMeasurements_IsolateParallelWorkers()
    {
        var results = await Task.WhenAll(MeasureAsync(31), MeasureAsync(47));

        Assert.Equal(31, results[0].ComponentGeneratedCount);
        Assert.Equal(47, results[1].ComponentGeneratedCount);

        static Task<GenerationStatisticsSnapshot> MeasureAsync(int count)
        {
            return Task.Run(() =>
            {
                using var measurement = BlackSilenceGenerationStatistics.BeginMeasurement();
                Parallel.For(0, count, static _ =>
                    GenerationStatistics.Increment(GenerationStatisticCounter.ComponentGenerated));
                return measurement.GetSnapshot();
            });
        }
    }

    private sealed class TestAdditionalText(string path, string text) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(text);

        public override string Path => path;

        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
}
#endif
