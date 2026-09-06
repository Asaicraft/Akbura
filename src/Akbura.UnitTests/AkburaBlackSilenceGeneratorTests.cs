using Akbura.BlackSilence;
using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Akbura.UnitTests;

public sealed class AkburaBlackSilenceGeneratorTests
{
    private const string SyntaxTreesTrackingName = "BlackSilence.SyntaxTrees";
    private const string SourceTextsTrackingName = "BlackSilence.SourceTexts";
    private const string GeneratedComponentsTrackingName = "BlackSilence.GeneratedComponents";
    private const string GeneratedExternalAkcssTrackingName = "BlackSilence.GeneratedExternalAkcss";
    private const string GeneratedInlineAkcssTrackingName = "BlackSilence.GeneratedInlineAkcss";

    private static readonly AnalyzerConfigOptionsProvider s_emptyOptionsProvider =
        new TestAnalyzerConfigOptionsProvider(string.Empty, string.Empty);

    [Fact]
    public void UpdatingOneAdditionalFile_ReparsesOnlyThatFile()
    {
        var a = new TestAdditionalText(
            "A.akbura",
            SourceText.From(
                """
                using Avalonia.Controls;

                <Border />
                """));

        var oldBText = SourceText.From(
            """
            using Avalonia.Controls;

            state double count = 0d;

            <Border Width={count} />
            """);

        var b = new TestAdditionalText("B.akbura", oldBText);

        var c = new TestAdditionalText(
            "C.akcss",
            SourceText.From(
                """
                @using Avalonia.Controls;

                .button {
                    Width: 10;
                }
                """));

        const string csharpSource =
            """
            using Akbura;
            using Akbura.Engine;

            public partial class A : AkburaControl
            {
                public A()
                    : base(AkburaEngine.Empty)
                {
                }
            }

            public partial class B : AkburaControl
            {
                public B()
                    : base(AkburaEngine.Empty)
                {
                }
            }
            """;

        var compilation = CreateCompilation(csharpSource);
        var driver = CreateDriver(s_emptyOptionsProvider, a, b, c);

        driver = driver.RunGenerators(compilation);

        var initial = GetSyntaxTreeOutputs(driver);

        Assert.Equal(3, initial.Count);
        Assert.All(initial.Values, static output =>
            Assert.Equal(IncrementalStepRunReason.New, output.Reason));

        var initialComponentReasons = GetOutputReasons(driver, GeneratedComponentsTrackingName);
        var initialAkcssReasons = GetOutputReasons(driver, GeneratedExternalAkcssTrackingName);

        Assert.Equal(2, initialComponentReasons.Length);
        Assert.All(initialComponentReasons, static reason =>
            Assert.Equal(IncrementalStepRunReason.New, reason));

        Assert.Equal(
            IncrementalStepRunReason.New,
            Assert.Single(initialAkcssReasons));

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var changeStart = oldBText.ToString().IndexOf("0d", StringComparison.Ordinal);

        var newBText = oldBText.WithChanges(
            new TextChange(
                new TextSpan(changeStart, length: 1),
                "1"));

        var updatedB = new TestAdditionalText(b.Path, newBText);

        driver = driver.ReplaceAdditionalText(b, updatedB);
        driver = driver.RunGenerators(compilation);

        var afterAdditionalTextChange = GetSyntaxTreeOutputs(driver);

        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[a.Path].Reason);

        Assert.Equal(
            IncrementalStepRunReason.Modified,
            afterAdditionalTextChange[b.Path].Reason);

        Assert.Equal(
            IncrementalStepRunReason.Cached,
            afterAdditionalTextChange[c.Path].Reason);

        Assert.Same(
            initial[a.Path].SyntaxTree,
            afterAdditionalTextChange[a.Path].SyntaxTree);

        Assert.NotSame(
            initial[b.Path].SyntaxTree,
            afterAdditionalTextChange[b.Path].SyntaxTree);

        Assert.Same(
            initial[c.Path].SyntaxTree,
            afterAdditionalTextChange[c.Path].SyntaxTree);

        var oldBTree = Assert.IsType<ComponentSyntaxTree>(initial[b.Path].SyntaxTree);
        var newBTree = Assert.IsType<ComponentSyntaxTree>(afterAdditionalTextChange[b.Path].SyntaxTree);

        Assert.Same(oldBTree.GreenRoot.Members[0], newBTree.GreenRoot.Members[0]);
        Assert.NotSame(oldBTree.GreenRoot.Members[1], newBTree.GreenRoot.Members[1]);
        Assert.Same(oldBTree.GreenRoot.Members[2], newBTree.GreenRoot.Members[2]);

        var changedComponentReasons = GetOutputReasons(driver, GeneratedComponentsTrackingName);

        Assert.Equal(2, changedComponentReasons.Length);
        Assert.Equal(
            1,
            changedComponentReasons.Count(static reason =>
                reason == IncrementalStepRunReason.Modified));

        Assert.Equal(
            1,
            changedComponentReasons.Count(static reason =>
                reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged));

        AssertReused(
            Assert.Single(
                GetOutputReasons(
                    driver,
                    GeneratedExternalAkcssTrackingName)));

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);

        var changedCompilation = compilation.AddSyntaxTrees(
            CSharpSyntaxTree.ParseText(
                "internal sealed class Changed { }",
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        driver = driver.RunGenerators(changedCompilation);

        var afterCompilationChange = GetSyntaxTreeOutputs(driver);

        Assert.All(afterCompilationChange.Values, static output =>
            Assert.Equal(IncrementalStepRunReason.Cached, output.Reason));

        Assert.All(
            GetOutputReasons(driver, GeneratedComponentsTrackingName),
            AssertReused);

        Assert.All(
            GetOutputReasons(driver, GeneratedExternalAkcssTrackingName),
            AssertReused);

        Assert.Equal(1, a.ReadCount);
        Assert.Equal(1, b.ReadCount);
        Assert.Equal(1, updatedB.ReadCount);
        Assert.Equal(1, c.ReadCount);
    }

    [Fact]
    public void GenerateSources_EmitsComponentExternalAndInlineAkcssDocuments()
    {
        const string rootNamespace = "Demo";

        const string componentSource =
            """
            using Avalonia.Controls;
            using Demo.Styles.Shared.akcss;

            @akcss {
                @using Avalonia.Controls;

                .local {
                    Width: 10;
                }
            }

            <Border class="local shared" />
            """;

        const string externalAkcssSource =
            """
            @using Avalonia.Controls;

            .shared {
                Height: 20;
            }

            @utilities {
                .spacing-(double value) {
                    Width: value;
                }
            }
            """;

        const string csharpSource =
            """
            using Akbura;
            using Akbura.Engine;

            namespace Demo.Views;

            public partial class PlannerView : AkburaControl
            {
                public PlannerView()
                    : base(AkburaEngine.Empty)
                {
                }
            }
            """;

        var projectDirectory = Path.Combine(
            Path.GetTempPath(),
            "AkburaBlackSilenceGeneratorTests");

        var componentPath = Path.Combine(
            projectDirectory,
            "Views",
            "PlannerView.akbura");

        var externalAkcssPath = Path.Combine(
            projectDirectory,
            "Styles",
            "Shared.akcss");

        var component = new TestAdditionalText(
            componentPath,
            SourceText.From(componentSource));

        var externalAkcss = new TestAdditionalText(
            externalAkcssPath,
            SourceText.From(externalAkcssSource));

        var optionsProvider = new TestAnalyzerConfigOptionsProvider(
            rootNamespace,
            projectDirectory);

        var compilation = CreateCompilation(csharpSource);
        var driver = CreateDriver(optionsProvider, component, externalAkcss);

        driver = driver.RunGeneratorsAndUpdateCompilation(
            compilation,
            out var outputCompilation,
            out var generatorDiagnostics);

        var result = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(result.Exception);
        Assert.Equal(3, result.GeneratedSources.Length);

        Assert.DoesNotContain(
            generatorDiagnostics,
            static diagnostic =>
                diagnostic.Severity == DiagnosticSeverity.Error);

        var componentGenerated = Assert.Single(
            result.GeneratedSources,
            static source =>
                source.HintName.StartsWith(
                    "Akbura.Component.",
                    StringComparison.Ordinal));

        var akcssGenerated = result.GeneratedSources
            .Where(static source =>
                source.HintName.StartsWith(
                    "Akbura.Akcss.",
                    StringComparison.Ordinal))
            .ToArray();

        Assert.Equal(2, akcssGenerated.Length);

        var externalGenerated = Assert.Single(
            akcssGenerated,
            static source =>
                source.SourceText.ToString().Contains(
                    "SourcePath = \"Styles/Shared.akcss\";",
                    StringComparison.Ordinal));

        var inlineGenerated = Assert.Single(
            akcssGenerated,
            static source =>
                source.SourceText.ToString().Contains(
                    "SourcePath = \"Views/PlannerView.akbura\";",
                    StringComparison.Ordinal));

        var componentText = componentGenerated.SourceText.ToString();
        var externalText = externalGenerated.SourceText.ToString();
        var inlineText = inlineGenerated.SourceText.ToString();

        Assert.Contains(
            "partial class PlannerView",
            componentText,
            StringComparison.Ordinal);

        Assert.Contains(
            "private sealed class Style_0 : global::Akbura.Akcss.AkcssClass",
            externalText,
            StringComparison.Ordinal);

        Assert.Contains(
            "private sealed class Style_1 : global::Akbura.Akcss.AkcssUtility<",
            externalText,
            StringComparison.Ordinal);

        Assert.Contains(
            "[global::Akbura.CompilerAnotations.InlinedStyleAttribute]",
            inlineText,
            StringComparison.Ordinal);

        var externalModuleType = AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
            rootNamespace,
            "Styles/Shared.akcss");

        var inlineModuleType = AkcssGeneratedModuleNames.GetFullyQualifiedTypeName(
            rootNamespace,
            "Views/PlannerView.akbura.inline.0.akcss");

        Assert.Contains(
            externalModuleType,
            componentText,
            StringComparison.Ordinal);

        Assert.Contains(
            inlineModuleType,
            componentText,
            StringComparison.Ordinal);

        var compilationDiagnostics = outputCompilation
            .GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            compilationDiagnostics.Length == 0,
            string.Join(
                Environment.NewLine,
                compilationDiagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            string.Join(
                Environment.NewLine + Environment.NewLine,
                result.GeneratedSources.Select(static source => source.SourceText.ToString())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChangingProjectOptions_UpdatesAkcssIdentityWithoutReadingTextAgain(bool changeProjectDirectory)
    {
        const string source =
            """
            @using Avalonia.Controls;

            Border.shared {
                Height: 20;
            }
            """;

        var projectDirectory = Path.Combine(Path.GetTempPath(), "BlackSilenceProjectOptionsTests");
        var sourcePath = Path.Combine(projectDirectory, "Styles", "Shared.akcss");
        var file = new TestAdditionalText(sourcePath, SourceText.From(source));
        var options = new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var compilation = CreateCompilation(string.Empty);
        var driver = CreateDriver(options, file);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        var initialTree = Assert.IsType<AkcssSyntaxTree>(GetSyntaxTreeOutputs(driver)[sourcePath].SyntaxTree);

        Assert.Equal("Demo.Styles.Shared.akcss", initialTree.LogicalName);
        Assert.Equal(1, file.ReadCount);

        var rootNamespace = changeProjectDirectory ? "Demo" : "Renamed";
        var updatedDirectory = changeProjectDirectory ? Path.Combine(projectDirectory, "Styles") : projectDirectory;
        var relativePath = changeProjectDirectory ? "Shared.akcss" : "Styles/Shared.akcss";

        driver = driver.WithUpdatedAnalyzerConfigOptions(
            new TestAnalyzerConfigOptionsProvider(rootNamespace, updatedDirectory));

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updatedCompilation, out _);
        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedTree = Assert.IsType<AkcssSyntaxTree>(GetSyntaxTreeOutputs(driver)[sourcePath].SyntaxTree);

        Assert.NotSame(initialTree, updatedTree);
        Assert.Equal(AkcssGeneratedModuleNames.GetMetadataName(rootNamespace, relativePath), updatedTree.LogicalName);
        Assert.Equal(1, file.ReadCount);
        Assert.Equal(IncrementalStepRunReason.Cached, Assert.Single(GetOutputReasons(driver, SourceTextsTrackingName)));

        var generatedSource = Assert.Single(Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        var generatedText = generatedSource.SourceText.ToString();

        Assert.Contains("namespace " + rootNamespace + ".Generated", generatedText, StringComparison.Ordinal);
        Assert.Contains("SourcePath = \"" + relativePath + "\";", generatedText, StringComparison.Ordinal);
        Assert.Contains(updatedTree.LogicalName, generatedText, StringComparison.Ordinal);
        Assert.Equal(
            IncrementalStepRunReason.Modified,
            Assert.Single(GetOutputReasons(driver, GeneratedExternalAkcssTrackingName)));
    }

    [Fact]
    public void GenerateSources_PreservesBatchOrderAndContentAfterUnrelatedCompilationChanges()
    {
        const int componentCount = 4;
        var projectDirectory = Path.Combine(Path.GetTempPath(), "BlackSilenceBatchTests");
        var files = new TestAdditionalText[componentCount * 2];
        var declarations = new string[componentCount];

        for (var i = 0; i < componentCount; i++)
        {
            var componentSource =
                "using Avalonia.Controls;\r\n" +
                "using Demo.Styles.Shared" + i + ".akcss;\r\n" +
                "@akcss {\r\n" +
                "    @using Avalonia.Controls;\r\n" +
                "    .local { Width: " + (i + 10) + "; }\r\n" +
                "}\r\n" +
                "<Border class=\"local shared\" />\r\n";

            var akcssSource =
                "@using Avalonia.Controls;\r\n" +
                ".shared { Height: " + (i + 20) + "; }\r\n" +
                "@utilities { .width-(double value) { Width: value; } }\r\n";

            files[i * 2] = new TestAdditionalText(
                Path.Combine(projectDirectory, "Views", "View" + i + ".akbura"),
                SourceText.From(componentSource));

            files[i * 2 + 1] = new TestAdditionalText(
                Path.Combine(projectDirectory, "Styles", "Shared" + i + ".akcss"),
                SourceText.From(akcssSource));

            declarations[i] =
                "public partial class View" + i + " : global::Akbura.AkburaControl\r\n" +
                "{\r\n" +
                "    public View" + i + "() : base(global::Akbura.Engine.AkburaEngine.Empty) { }\r\n" +
                "}\r\n";
        }

        var compilation = CreateCompilation("namespace Demo.Views;\r\n" + string.Join("\r\n", declarations));
        var options = new TestAnalyzerConfigOptionsProvider("Demo", projectDirectory);
        var driver = CreateDriver(options, files);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var initialCompilation, out _);
        AssertGeneratedCompilation(driver, initialCompilation);

        var initialSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;

        Assert.Equal(componentCount * 3, initialSources.Length);
        Assert.Equal(initialSources.Length, initialSources.Select(static source => source.HintName).Distinct().Count());
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedComponentsTrackingName).Length);
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedExternalAkcssTrackingName).Length);
        Assert.Equal(componentCount, GetOutputReasons(driver, GeneratedInlineAkcssTrackingName).Length);

        var changedCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText(
            "internal sealed class Unrelated { }",
            CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview)));

        driver = driver.RunGeneratorsAndUpdateCompilation(changedCompilation, out var updatedCompilation, out _);
        AssertGeneratedCompilation(driver, updatedCompilation);

        var updatedSources = Assert.Single(driver.GetRunResult().Results).GeneratedSources;

        Assert.Equal(initialSources.Length, updatedSources.Length);

        for (var i = 0; i < initialSources.Length; i++)
        {
            Assert.Equal(initialSources[i].HintName, updatedSources[i].HintName);
            Assert.True(initialSources[i].SourceText.ContentEquals(updatedSources[i].SourceText));
        }

        Assert.All(GetOutputReasons(driver, GeneratedComponentsTrackingName), AssertReused);
        Assert.All(GetOutputReasons(driver, GeneratedExternalAkcssTrackingName), AssertReused);
        Assert.All(GetOutputReasons(driver, GeneratedInlineAkcssTrackingName), AssertReused);
        Assert.All(files, static file => Assert.Equal(1, file.ReadCount));
    }

    [Fact]
    public void GenerateSources_CanceledRunDoesNotReadFilesAndDriverCanRunAgain()
    {
        var file = new TestAdditionalText(
            "Canceled.akcss",
            SourceText.From("@using Avalonia.Controls; Border.shared { Height: 20; }"));

        var compilation = CreateCompilation(string.Empty);
        var driver = CreateDriver(s_emptyOptionsProvider, file);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => driver.RunGenerators(compilation, cancellation.Token));
        Assert.Equal(0, file.ReadCount);

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out _);
        AssertGeneratedCompilation(driver, outputCompilation);

        Assert.Single(Assert.Single(driver.GetRunResult().Results).GeneratedSources);
        Assert.Equal(1, file.ReadCount);
    }

    private static void AssertGeneratedCompilation(GeneratorDriver driver, Compilation compilation)
    {
        var result = Assert.Single(driver.GetRunResult().Results);
        Assert.Null(result.Exception);

        Assert.DoesNotContain(
            result.Diagnostics,
            static diagnostic => diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error);

        var diagnostics = compilation.GetDiagnostics()
            .Where(static diagnostic =>
                diagnostic.Severity is DiagnosticSeverity.Warning or DiagnosticSeverity.Error)
            .ToArray();

        Assert.True(
            diagnostics.Length == 0,
            string.Join(Environment.NewLine, diagnostics.Select(static diagnostic => diagnostic.ToString())) +
            Environment.NewLine +
            string.Join(Environment.NewLine, result.GeneratedSources.Select(static source => source.SourceText.ToString())));
    }

    private static GeneratorDriver CreateDriver(
        AnalyzerConfigOptionsProvider optionsProvider,
        params AdditionalText[] additionalTexts)
    {
        return CSharpGeneratorDriver.Create(
            generators:
            [
                new AkburaBlackSilenceGenerator().AsSourceGenerator(),
            ],
            additionalTexts: additionalTexts,
            parseOptions: CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
            optionsProvider: optionsProvider,
            driverOptions: new GeneratorDriverOptions(
                IncrementalGeneratorOutputKind.None,
                trackIncrementalGeneratorSteps: true));
    }

    private static CSharpCompilation CreateCompilation(string source)
    {
        var parseOptions = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview);

        return CSharpCompilation.Create(
            "AkburaBlackSilenceGeneratorTests",
            syntaxTrees:
            [
                CSharpSyntaxTree.ParseText(source, parseOptions),
            ],
            references: SymbolTests.CreateAvaloniaReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Enable));
    }

    private static Dictionary<string, (AkburaSyntaxTree SyntaxTree, IncrementalStepRunReason Reason)>
        GetSyntaxTreeOutputs(GeneratorDriver driver)
    {
        var generatorResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(generatorResult.Exception);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(
                SyntaxTreesTrackingName,
                out var steps));

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output =>
                (
                    SyntaxTree: Assert.IsType<AkburaSyntaxTree>(
                        output.Value,
                        exactMatch: false),
                    output.Reason
                ))
            .ToDictionary(
                static output => output.SyntaxTree.FilePath,
                static output => output,
                StringComparer.Ordinal);
    }

    private static IncrementalStepRunReason[] GetOutputReasons(
        GeneratorDriver driver,
        string trackingName)
    {
        var generatorResult = Assert.Single(driver.GetRunResult().Results);

        Assert.Null(generatorResult.Exception);
        Assert.True(
            generatorResult.TrackedSteps.TryGetValue(
                trackingName,
                out var steps));

        return steps
            .SelectMany(static step => step.Outputs)
            .Select(static output => output.Reason)
            .ToArray();
    }

    private static void AssertReused(IncrementalStepRunReason reason)
    {
        Assert.True(
            reason is IncrementalStepRunReason.Cached or IncrementalStepRunReason.Unchanged,
            $"Expected Cached or Unchanged, but received {reason}.");
    }

    private sealed class TestAdditionalText : AdditionalText
    {
        private readonly SourceText _sourceText;
        private int _readCount;

        public TestAdditionalText(string path, SourceText sourceText)
        {
            Path = path;
            _sourceText = sourceText;
        }

        public override string Path { get; }

        public int ReadCount => Volatile.Read(ref _readCount);

        public override SourceText GetText(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _readCount);

            return _sourceText;
        }
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions s_emptyOptions =
            new TestAnalyzerConfigOptions(
                new Dictionary<string, string>());

        private readonly AnalyzerConfigOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(
            string rootNamespace,
            string projectDirectory)
        {
            _globalOptions = new TestAnalyzerConfigOptions(
                new Dictionary<string, string>(StringComparer.Ordinal)
                {
                    ["build_property.RootNamespace"] = rootNamespace,
                    ["build_property.ProjectDir"] = projectDirectory,
                });
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree)
        {
            return s_emptyOptions;
        }

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
        {
            return s_emptyOptions;
        }
    }

    private sealed class TestAnalyzerConfigOptions : AnalyzerConfigOptions
    {
        private readonly IReadOnlyDictionary<string, string> _values;

        public TestAnalyzerConfigOptions(
            IReadOnlyDictionary<string, string> values)
        {
            _values = values;
        }

        public override bool TryGetValue(string key, out string value)
        {
            if (_values.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = null!;
            return false;
        }
    }
}
