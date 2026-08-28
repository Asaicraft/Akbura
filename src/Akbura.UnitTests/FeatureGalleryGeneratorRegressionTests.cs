using Akbura.Furioso;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.UnitTests;

public sealed class FeatureGalleryGeneratorRegressionTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Generator_ReportsExternalUtilityPriorityDiagnosticsAtUseSite(
        string newLine)
    {
        var component = JoinLines(
            newLine,
            "using Akbura.Markup;",
            "using Avalonia.Controls;",
            "using Styles.akcss;",
            string.Empty,
            "<Grid ${lg}:outer />");
        var styles = JoinLines(
            newLine,
            "@using Avalonia.Controls;",
            string.Empty,
            "@utilities {",
            "    Grid.outer {",
            $"        /* {new string('x', 512)} */",
            "        ColumnDefinitions:",
            "            new ColumnDefinitions(\"*,*\");",
            string.Empty,
            "        @if(true) {",
            "            RowDefinitions:",
            "                new RowDefinitions(\"Auto\");",
            "        }",
            "    }",
            "}");
        var parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Preview);
        var compilation = CSharpCompilation.Create(
            "AkburaExternalUtilityPriorityDiagnosticTests",
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary));
        var projectDirectory = Path.Combine(
            Environment.CurrentDirectory,
            "ExternalUtilityPriorityDiagnosticProject");
        var componentPath = Path.Combine(projectDirectory, "Host.akbura");
        var stylesPath = Path.Combine(projectDirectory, "Styles.akcss");
        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaCsGenerator().AsSourceGenerator(),
            ],
            additionalTexts:
            [
                new TestAdditionalText(
                    componentPath,
                    SourceText.From(component)),
                new TestAdditionalText(
                    stylesPath,
                    SourceText.From(styles)),
            ],
            parseOptions: parseOptions);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out _,
            out var generatorDiagnostics);

        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);
        var priorityDiagnostics = generatorDiagnostics
            .Where(static diagnostic =>
                diagnostic.Id ==
                ErrorCodes
                    .AKBURA_SEMANTIC_UtilityBindingPriorityTargetNotSupported)
            .ToArray();
        Assert.Equal(2, priorityDiagnostics.Length);

        foreach (var diagnostic in priorityDiagnostics)
        {
            Assert.Equal(
                componentPath,
                diagnostic.Location.GetLineSpan().Path);
            var sourceSpan = diagnostic.Location.SourceSpan;
            Assert.InRange(sourceSpan.Start, 0, component.Length - 1);
            Assert.InRange(sourceSpan.End, sourceSpan.Start + 1, component.Length);
            Assert.Contains(
                "${lg}:outer",
                component.Substring(sourceSpan.Start, sourceSpan.Length),
                StringComparison.Ordinal);
        }
    }

    private static string JoinLines(string newLine, params string[] lines) =>
        string.Join(newLine, lines) + newLine;

    private sealed class TestAdditionalText(
        string path,
        SourceText text) : AdditionalText
    {
        public override string Path { get; } = path;

        public override SourceText GetText(
            CancellationToken cancellationToken = default) => text;
    }
}
