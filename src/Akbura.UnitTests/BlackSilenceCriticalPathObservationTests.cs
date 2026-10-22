#if STATS
using Akbura.BlackSilence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akbura.UnitTests;

[Collection(BlackSilenceStatisticsCollection.Name)]
public sealed class BlackSilenceCriticalPathObservationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RevisionTelemetry_PreservesFreshSourceAndDiagnosticParity(bool malformedMetadata)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AkburaCriticalObservation", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var observationPath = Path.Combine(directory, "records");
        var metadataPath = Path.Combine(directory, "revision.json");
        var path = Path.Combine(directory, "TelemetryView.akbura");
        var source = "using Avalonia.Controls;\r\n<Border Width=\"10\" />\r\n";
        var additional = new Source(path, source);
        var compilation = CSharpCompilation.Create("CriticalObservation", references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var previousDirectory = Environment.GetEnvironmentVariable("AKBURA_WATCH_OBSERVATIONS");
        var previousMetadata = Environment.GetEnvironmentVariable("AKBURA_CRITICAL_REVISION");
        try
        {
            Environment.SetEnvironmentVariable("AKBURA_WATCH_OBSERVATIONS", null);
            Environment.SetEnvironmentVariable("AKBURA_CRITICAL_REVISION", null);
            var baseline = Run();
            File.WriteAllText(metadataPath, malformedMetadata ? "{}" : JsonSerializer.Serialize(new Dictionary<string, string> {
                ["Revision"] = "7", ["SourceName"] = "TelemetryView.akbura", ["SourcePath"] = path,
                ["SourceSha256"] = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source))) }));
            Environment.SetEnvironmentVariable("AKBURA_WATCH_OBSERVATIONS", observationPath);
            Environment.SetEnvironmentVariable("AKBURA_CRITICAL_REVISION", metadataPath);
            var instrumented = Run();
            Assert.Equal(Sources(baseline), Sources(instrumented));
            Assert.Equal(baseline.Diagnostics.Select(item => item.ToString()), instrumented.Diagnostics.Select(item => item.ToString()));
            var generation = Directory.GetFiles(observationPath, "*.json").Select(file => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(file))!)
                .Single(record => record.GetValueOrDefault("Kind") == "Generation");
            Assert.True(generation.ContainsKey("stageMs.ComponentPlanning"));
            Assert.True(generation.ContainsKey("ScopeElapsedMs"));
            if (malformedMetadata)
            {
                Assert.Equal("KeyNotFoundException", generation["RevisionCorrelationError"]);
            }
            else
            {
                Assert.Equal("7", generation["Revision"]);
                Assert.Equal("True", generation["RevisionInputMatched"]);
                Assert.True(int.Parse(generation["GeneratedSourceHintCount"]) > 0);
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("AKBURA_WATCH_OBSERVATIONS", previousDirectory);
            Environment.SetEnvironmentVariable("AKBURA_CRITICAL_REVISION", previousMetadata);
        }

        GeneratorRunResult Run()
        {
            GeneratorDriver driver = CSharpGeneratorDriver.Create(generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
                additionalTexts: [additional], parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));
            var result = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Results);
            Assert.Null(result.Exception);
            return result;
        }

        static string[] Sources(GeneratorRunResult result)
        {
            return result.GeneratedSources.Select(item => item.HintName + "\n" + item.SourceText).OrderBy(item => item, StringComparer.Ordinal).ToArray();
        }
    }

    private sealed class Source(string path, string text) : AdditionalText
    {
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => SourceText.From(text, Encoding.UTF8);
    }
}
#endif
