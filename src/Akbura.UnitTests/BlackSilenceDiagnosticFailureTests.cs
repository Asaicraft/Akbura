using Akbura.BlackSilence;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class BlackSilenceDiagnosticFailureTests
{
    [Theory]
    [InlineData("Off", "Auto")]
    [InlineData("Shadow", "Auto")]
    [InlineData("Publish", "Auto")]
    [InlineData("Off", "None")]
    [InlineData("Shadow", "None")]
    [InlineData("Publish", "None")]
    public void UnexpectedFailure_IsPublishedEvenWhenOrdinaryDiagnosticsAreDisabled(string mode, string publisher)
    {
        var compilation = CSharpCompilation.Create(
            "FailureTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var file = new FailedUtilityFile();
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: [file],
            optionsProvider: new Options(mode, publisher),
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview));

        var result = Assert.Single(driver.RunGenerators(compilation).GetRunResult().Results);

        Assert.Null(result.Exception);
        Assert.Empty(result.GeneratedSources);
        var failure = Assert.Single(result.Diagnostics);
        Assert.Equal("AKBURA_GENERATOR_FAILURE", failure.Id);
        Assert.Equal(DiagnosticSeverity.Error, failure.Severity);
        Assert.Equal("infrastructure", failure.Properties["akbura.kind"]);
        Assert.Contains("Tailwind utility parameter symbol name cannot be empty", failure.GetMessage(), StringComparison.Ordinal);
    }

    private sealed class FailedUtilityFile : AdditionalText
    {
        public override string Path => System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FailedUtility.akcss");

        // This malformed declaration currently triggers a shared binder invariant.
        // The BlackSilence boundary must surface it, never silently cache success.
        public override SourceText GetText(CancellationToken cancellationToken = default) =>
            SourceText.From("@utilities { .w-(double) { } }");
    }

    private sealed class Options(string mode, string publisher) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Values(mode, publisher);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => GlobalOptions;
    }

    private sealed class Values(string mode, string publisher) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            value = key switch
            {
                "build_property.AkburaBlackSilenceDiagnostics" => mode,
                "build_property.AkburaDiagnosticPublisher" => publisher,
                "build_property.RootNamespace" => "FailureTests",
                "build_property.ProjectDir" => Path.GetTempPath(),
                _ => string.Empty,
            };
            return value.Length != 0;
        }
    }
}
