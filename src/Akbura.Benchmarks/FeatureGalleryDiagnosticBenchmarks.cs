using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Syntax;
using BenchmarkDotNet.Attributes;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Benchmarks;

public enum DiagnosticScenario
{
    ColdDiagnostics,
    NoChanges,
    ComponentEofWhitespace,
    AkcssEofWhitespace,
    ComponentSyntaxErrorAdded,
    ComponentSyntaxErrorFixed,
    ComponentSemanticErrorAdded,
    ComponentSemanticErrorFixed,
    AkcssSemanticErrorAdded,
    AkcssSemanticErrorFixed,
    ComponentSurfaceEdit,
    TransitiveAkcssDependencyEdit,
    UnrelatedCSharpEdit,
    RelevantCSharpMemberEdit,
    AdditionalFileAdded,
    AdditionalFileRemoved,
}

[Config(typeof(FeatureGalleryGeneratorBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "Diagnostics")]
public class FeatureGalleryDiagnosticBenchmarks
{
    private FeatureGalleryBenchmarkProject _project = null!;
    private FeatureGalleryDiagnosticWorkload _workload = null!;

    [ParamsAllValues]
    public DiagnosticScenario Scenario { get; set; }

    [Params("Off", "Shadow", "Publish")]
    public string Mode { get; set; } = "Publish";

    [GlobalSetup]
    public void Setup() => _project = FeatureGalleryDiagnosticWorkload.Project.Value;

    [IterationSetup]
    public void Prepare() => _workload = FeatureGalleryDiagnosticWorkload.Prepare(_project, Scenario, Mode);

    [Benchmark]
    public int BlackSilence()
    {
        return FeatureGalleryDiagnosticWorkload.Run(_workload.Driver, _workload.Compilation).Diagnostics.Length;
    }
}

[Config(typeof(FeatureGalleryGeneratorBenchmarkConfig))]
[MemoryDiagnoser]
[BenchmarkCategory("SourceGenerator", "FeatureGallery", "Diagnostics", "NoOp")]
public class FeatureGalleryDiagnosticNoOpBenchmarks
{
    private FeatureGalleryDiagnosticWorkload _workload = null!;

    [Params("Off", "Shadow", "Publish")]
    public string Mode { get; set; } = "Publish";

    [GlobalSetup]
    public void Setup()
    {
        _workload = FeatureGalleryDiagnosticWorkload.Prepare(
            FeatureGalleryDiagnosticWorkload.Project.Value,
            DiagnosticScenario.NoChanges,
            Mode);
    }

    [Benchmark(OperationsPerInvoke = 256)]
    public int NoChanges()
    {
        var count = 0;
        for (var i = 0; i < 256; i++)
        {
            count += FeatureGalleryDiagnosticWorkload.Run(_workload.Driver, _workload.Compilation).Diagnostics.Length;
        }

        return count;
    }
}

internal sealed record FeatureGalleryDiagnosticWorkload(
    GeneratorDriver Driver,
    CSharpCompilation Compilation,
    ImmutableArray<AdditionalText> Files,
    FeatureGalleryBenchmarkSnapshot Snapshot,
    FeatureGalleryBenchmarkProject SourceProject,
    string Mode)
{
    public static readonly Lazy<FeatureGalleryBenchmarkProject> Project = new(FeatureGalleryBenchmarkProject.Load);

    public static FeatureGalleryDiagnosticWorkload Prepare(
        FeatureGalleryBenchmarkProject project,
        DiagnosticScenario scenario,
        string mode = "Publish")
    {
        var snapshot = project.CreateSnapshot();
        var initialFiles = snapshot.AdditionalTexts;
        var currentFiles = initialFiles;
        var compilation = project.Compilation;
        var currentCompilation = compilation;
        AdditionalText? original = null;
        AdditionalText? modified = null;
        var fix = scenario is DiagnosticScenario.ComponentSyntaxErrorFixed or
            DiagnosticScenario.ComponentSemanticErrorFixed or DiagnosticScenario.AkcssSemanticErrorFixed;

        switch (scenario)
        {
            case DiagnosticScenario.ComponentEofWhitespace:
                original = snapshot.ComponentOriginal;
                modified = snapshot.ComponentModified;
                break;
            case DiagnosticScenario.AkcssEofWhitespace:
                original = snapshot.AkcssOriginal;
                modified = snapshot.AkcssModified;
                break;
            case DiagnosticScenario.ComponentSyntaxErrorAdded:
            case DiagnosticScenario.ComponentSyntaxErrorFixed:
                original = snapshot.ComponentOriginal;
                modified = Append(snapshot.ComponentOriginal, "\r\n/* diagnostic benchmark");
                break;
            case DiagnosticScenario.ComponentSemanticErrorAdded:
            case DiagnosticScenario.ComponentSemanticErrorFixed:
                original = snapshot.ComponentOriginal;
                modified = AddComponentPropertyError(snapshot.ComponentOriginal);
                break;
            case DiagnosticScenario.AkcssSemanticErrorAdded:
            case DiagnosticScenario.AkcssSemanticErrorFixed:
                original = snapshot.AkcssOriginal;
                modified = Append(snapshot.AkcssOriginal, "\r\n.__diagnostic { global::Avalonia.Controls.Control.Missing: 1; }\r\n");
                break;
            case DiagnosticScenario.UnrelatedCSharpEdit:
                currentCompilation = project.CreateCSharpEditCompilation();
                break;
            case DiagnosticScenario.ComponentSurfaceEdit:
            case DiagnosticScenario.TransitiveAkcssDependencyEdit:
            case DiagnosticScenario.RelevantCSharpMemberEdit:
                AddDependencyFixtures();
                break;
            case DiagnosticScenario.AdditionalFileAdded:
            case DiagnosticScenario.AdditionalFileRemoved:
                var added = File("DiagnosticBenchmarks/Added.akbura", "using Avalonia.Controls;\r\n<Button Missing=\"1\" />");
                if (scenario == DiagnosticScenario.AdditionalFileAdded)
                {
                    currentFiles = initialFiles.Add(added);
                }
                else
                {
                    initialFiles = initialFiles.Add(added);
                }

                break;
        }

        if (original != null && modified != null)
        {
            if (fix)
            {
                initialFiles = initialFiles.Replace(original, modified);
            }
            else
            {
                currentFiles = initialFiles.Replace(original, modified);
            }
        }

        var driver = CreateDriver(project, snapshot, initialFiles, new AkburaBlackSilenceGenerator(), mode);
        if (scenario != DiagnosticScenario.ColdDiagnostics)
        {
            driver = driver.RunGenerators(compilation);
            Validate(driver.GetRunResult().Results.Single());
        }

        if (scenario != DiagnosticScenario.ColdDiagnostics && !initialFiles.Equals(currentFiles))
        {
            driver = driver.ReplaceAdditionalTexts(currentFiles);
        }

        return new FeatureGalleryDiagnosticWorkload(driver, currentCompilation, currentFiles, snapshot, project, mode);

        BenchmarkAdditionalText File(string path, string source)
        {
            return new BenchmarkAdditionalText(
                Path.Combine(snapshot.ProjectDirectory, path.Replace('/', Path.DirectorySeparatorChar)),
                SourceText.From(source));
        }

        void AddDependencyFixtures()
        {
            var namespaceName = project.RootNamespace + ".DiagnosticBenchmarks";
            var value = File("DiagnosticBenchmarks/ValueView.akbura", "using Avalonia.Controls;\r\nparam double Value = 0d;\r\n<Border />");
            var consumer = File("DiagnosticBenchmarks/Consumer.akbura", "using " + namespaceName + ";\r\n<ValueView Value=\"10\" />");
            var basic = File("DiagnosticBenchmarks/Base.akcss", "@using Avalonia.Controls;\r\nBorder.basic { Width: 10; }");
            var top = File("DiagnosticBenchmarks/Top.akcss", "@using " + namespaceName + ".Base.akcss;\r\n.top { @apply basic; }");
            var styled = File("DiagnosticBenchmarks/Styled.akbura", "using Avalonia.Controls;\r\nusing " + namespaceName + ".Top.akcss;\r\n<Border class=\"top\" />");
            var probe = File("DiagnosticBenchmarks/Probe.akbura", "using Avalonia.Controls;\r\nusing " + namespaceName + ";\r\n<TextBlock Text={DiagnosticProbe.Text} />");
            initialFiles = initialFiles.AddRange(new AdditionalText[] { value, consumer, basic, top, styled, probe });
            currentFiles = initialFiles;
            var probeTree = CSharpSyntaxTree.ParseText(
                "namespace " + namespaceName + "; internal static class DiagnosticProbe { public static string Text => \"value\"; }",
                project.ParseOptions,
                Path.Combine(snapshot.ProjectDirectory, "DiagnosticProbe.cs"));
            compilation = compilation.AddSyntaxTrees(probeTree);
            currentCompilation = compilation;

            if (scenario == DiagnosticScenario.ComponentSurfaceEdit)
            {
                original = value;
                modified = Replace(value, "param double Value = 0d;", "param string Value = \"text\";");
            }
            else if (scenario == DiagnosticScenario.TransitiveAkcssDependencyEdit)
            {
                original = basic;
                modified = Replace(basic, "Width: 10;", "Missing: 10;");
            }
            else
            {
                currentCompilation = compilation.ReplaceSyntaxTree(probeTree, probeTree.WithChangedText(
                    SourceText.From(probeTree.GetText().ToString().Replace("string Text", "string Removed", StringComparison.Ordinal))));
            }
        }
    }

    public static GeneratorDriver CreateDriver(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryBenchmarkSnapshot snapshot,
        ImmutableArray<AdditionalText> files,
        IIncrementalGenerator generator,
        string mode = "Publish")
    {
        return CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: project.ParseOptions,
            optionsProvider: new DiagnosticBenchmarkOptionsProvider(snapshot.OptionsProvider, mode));
    }

    public static GeneratorRunResult Run(GeneratorDriver driver, CSharpCompilation compilation)
    {
        var result = driver.RunGenerators(compilation).GetRunResult().Results.Single();
        Validate(result);
        return result;
    }

    private static void Validate(GeneratorRunResult result)
    {
        if (result.Exception != null || result.Diagnostics.Any(static diagnostic => diagnostic.Id is "CS8784" or "CS8785" or "AKBURA_GENERATOR_FAILURE"))
        {
            throw new InvalidOperationException("Diagnostic benchmark generator failed.", result.Exception);
        }

        if (result.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException("Diagnostic benchmark generated no FeatureGallery sources.");
        }
    }

    private static BenchmarkAdditionalText Append(BenchmarkAdditionalText file, string suffix)
    {
        return new BenchmarkAdditionalText(file.Path, file.SourceText.WithChanges(
            new TextChange(new TextSpan(file.SourceText.Length, 0), suffix)));
    }

    private static BenchmarkAdditionalText AddComponentPropertyError(BenchmarkAdditionalText file)
    {
        var tree = ComponentSyntaxTree.ParseText(file.SourceText, file.Path);
        var attribute = tree.GetRootSyntax().DescendantNodesAndSelf().OfType<MarkupPlainAttributeSyntax>().FirstOrDefault() ??
            throw new InvalidOperationException("The selected FeatureGallery component has no plain property attribute to edit.");
        return new BenchmarkAdditionalText(file.Path, file.SourceText.WithChanges(
            new TextChange(attribute.Name.Span, "__AkburaDiagnosticMissing")));
    }

    private static BenchmarkAdditionalText Replace(BenchmarkAdditionalText file, string before, string after)
    {
        var start = file.SourceText.ToString().IndexOf(before, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("Diagnostic fixture edit target was not found.");
        }

        return new BenchmarkAdditionalText(file.Path, file.SourceText.WithChanges(new TextChange(new TextSpan(start, before.Length), after)));
    }
}

internal sealed class DiagnosticBenchmarkOptionsProvider(AnalyzerConfigOptionsProvider underlying, string mode = "Publish") : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new DiagnosticBenchmarkOptions(underlying.GlobalOptions, mode);
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => underlying.GetOptions(tree);
    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => underlying.GetOptions(textFile);
}

internal sealed class DiagnosticBenchmarkOptions(AnalyzerConfigOptions underlying, string mode) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, out string value)
    {
        if (key == "build_property.AkburaBlackSilenceDiagnostics")
        {
            value = mode;
            return true;
        }

        return underlying.TryGetValue(key, out value!);
    }
}
