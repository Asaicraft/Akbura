using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Text.Json;
using BlackSilenceGenerator = Akbura.BlackSilence.AkburaBlackSilenceGenerator;
using FuriosoGenerator = Akbura.Furioso.AkburaCsGenerator;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryParityVerification
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--black-silence-verify-parity")
        {
            return false;
        }

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("--black-silence-verify-parity [--output artifacts/black-silence-parity]");
            Console.WriteLine("Validates real FeatureGallery compilations, generator identities, and all five incremental/fresh scenarios. No timing or STATS requirement.");
            return true;
        }

        var outputDirectory = "artifacts/black-silence-parity";
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] != "--output" || i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]))
            {
                throw new ArgumentException($"Unknown or incomplete parity option '{args[i]}'.");
            }

            outputDirectory = args[++i];
        }

        Run(Path.GetFullPath(outputDirectory));
        return true;
    }

    private static void Run(string outputDirectory)
    {
        Console.WriteLine("Loading the real FeatureGallery project for correctness verification (not a benchmark)...");
        var project = FeatureGalleryBenchmarkProject.Load();
        var report = new VerificationReport(project.ProjectDirectory, project.AdditionalFileCount);

        Console.WriteLine("Furioso / BlackSilence: checking generated compilations and component/AKCSS metadata identities...");
        try
        {
            report.Compatibility = VerifyCompatibility(project);
            Console.WriteLine($"  {(report.Compatibility.Passed ? "PASS" : "FAIL")}: " +
                $"components {report.Compatibility.FuriosoComponents.Length}/{report.Compatibility.BlackSilenceComponents.Length}, " +
                $"AKCSS carriers {report.Compatibility.FuriosoAkcss.Length}/{report.Compatibility.BlackSilenceAkcss.Length}.");
            PrintDifferences(report.Compatibility.Differences);
        }
        catch (Exception exception)
        {
            report.Failures.Add("Generator compatibility: " + exception);
            Console.WriteLine("  FAIL: " + exception.Message);
        }

        foreach (var scenario in Enum.GetValues<FeatureGalleryGeneratorScenario>())
        {
            Console.WriteLine($"{scenario}: checking exact incremental/fresh sources and all diagnostics...");
            try
            {
                var result = VerifyScenario(project, scenario);
                report.Scenarios.Add(result);
                Console.WriteLine($"  {(result.Passed ? "PASS" : "FAIL")}: {result.IncrementalSourceCount} sources, {result.IncrementalDiagnosticCount} diagnostics.");
                PrintDifferences(result.Differences);
            }
            catch (Exception exception)
            {
                report.Failures.Add(scenario + ": " + exception);
                Console.WriteLine("  FAIL: " + exception.Message);
            }
        }

        WriteReport(outputDirectory, report);
        Environment.ExitCode = report.Passed ? 0 : 1;
        Console.WriteLine(report.Passed ? "FeatureGallery parity verification passed." : "FeatureGallery parity verification FAILED. See the artifact for every difference.");
    }

    private static CompatibilityResult VerifyCompatibility(FeatureGalleryBenchmarkProject project)
    {
        var snapshot = project.CreateSnapshot();
        var furioso = RunGenerator(CreateDriver(project, snapshot, snapshot.AdditionalTexts, new FuriosoGenerator()), project.Compilation);
        var blackSilence = RunGenerator(CreateDriver(project, snapshot, snapshot.AdditionalTexts, new BlackSilenceGenerator()), project.Compilation);
        var furiosoTypes = GetGeneratedTypes(furioso);
        var blackSilenceTypes = GetGeneratedTypes(blackSilence);
        var differences = new List<string>();
        AddDifferences("Component metadata types", furiosoTypes.Components, blackSilenceTypes.Components, differences);
        AddDifferences("AKCSS carrier metadata types", furiosoTypes.Akcss, blackSilenceTypes.Akcss, differences);
        AddDifferences("Generated hints (including infrastructure)", furioso.Sources.Keys, blackSilence.Sources.Keys, differences);
        AddDiagnosticDifferences(furioso.CanonicalDiagnostics, blackSilence.CanonicalDiagnostics, differences);

        if (furiosoTypes.Components.Length == 0 || furiosoTypes.Akcss.Length == 0 ||
            blackSilenceTypes.Components.Length == 0 || blackSilenceTypes.Akcss.Length == 0)
        {
            differences.Add("At least one generator did not expose both component and AKCSS carrier types.");
        }

        return new CompatibilityResult(
            furiosoTypes.Components,
            blackSilenceTypes.Components,
            furiosoTypes.Akcss,
            blackSilenceTypes.Akcss,
            furiosoTypes.InfrastructureHints,
            blackSilenceTypes.InfrastructureHints,
            furioso.Diagnostics,
            blackSilence.Diagnostics,
            differences.ToArray());
    }

    private static ScenarioResult VerifyScenario(FeatureGalleryBenchmarkProject project, FeatureGalleryGeneratorScenario scenario)
    {
        var editTarget = scenario switch
        {
            FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit => FeatureGalleryEditTarget.Component,
            FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit => FeatureGalleryEditTarget.Akcss,
            _ => FeatureGalleryEditTarget.None,
        };
        var snapshot = project.CreateSnapshot(editTarget);
        var files = snapshot.AdditionalTexts;
        var compilation = project.Compilation;
        var driver = CreateDriver(project, snapshot, files, new BlackSilenceGenerator());

        if (scenario != FeatureGalleryGeneratorScenario.ColdGeneration)
        {
            driver = RunGenerator(driver, compilation).Driver;
        }

        switch (scenario)
        {
            case FeatureGalleryGeneratorScenario.ComponentWhitespaceEdit:
                driver = driver.ReplaceAdditionalText(snapshot.ComponentOriginal, snapshot.ComponentModified);
                files = files.Replace(snapshot.ComponentOriginal, snapshot.ComponentModified);
                break;
            case FeatureGalleryGeneratorScenario.AkcssWhitespaceEdit:
                driver = driver.ReplaceAdditionalText(snapshot.AkcssOriginal, snapshot.AkcssModified);
                files = files.Replace(snapshot.AkcssOriginal, snapshot.AkcssModified);
                break;
            case FeatureGalleryGeneratorScenario.CSharpCompilationEdit:
                compilation = project.CreateCSharpEditCompilation();
                break;
        }

        var incremental = RunGenerator(driver, compilation);
        // A fresh driver receives the exact same current files, options and C# input.
        // New virtual paths here would invalidate both source mappings and the proof.
        var fresh = RunGenerator(CreateDriver(project, snapshot, files, new BlackSilenceGenerator()), compilation);
        var differences = new List<string>();
        AddDifferences("Generated hints", fresh.Sources.Keys, incremental.Sources.Keys, differences);

        foreach (var pair in fresh.Sources)
        {
            if (!incremental.Sources.TryGetValue(pair.Key, out var source))
            {
                continue;
            }

            var expected = pair.Value.ToString();
            var actual = source.ToString();
            if (!string.Equals(expected, actual, StringComparison.Ordinal))
            {
                differences.Add($"Generated text differs: {pair.Key}; first differing character {FindDifference(expected, actual)}; lengths {expected.Length}/{actual.Length}.");
            }
        }

        AddDiagnosticDifferences(fresh.CanonicalDiagnostics, incremental.CanonicalDiagnostics, differences);

        return new ScenarioResult(
            scenario.ToString(),
            incremental.Sources.Count,
            fresh.Sources.Count,
            incremental.Diagnostics.Length,
            fresh.Diagnostics.Length,
            incremental.Diagnostics,
            fresh.Diagnostics,
            differences.ToArray());
    }

    private static GeneratorDriver CreateDriver(
        FeatureGalleryBenchmarkProject project,
        FeatureGalleryBenchmarkSnapshot snapshot,
        ImmutableArray<AdditionalText> files,
        IIncrementalGenerator generator)
    {
        return CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            additionalTexts: files,
            parseOptions: project.ParseOptions,
            optionsProvider: new DiagnosticPublishOptionsProvider(snapshot.OptionsProvider));
    }

    private static CapturedRun RunGenerator(GeneratorDriver driver, CSharpCompilation compilation)
    {
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var driverDiagnostics);
        var result = driver.GetRunResult().Results.Single();
        if (result.Exception != null)
        {
            throw new InvalidOperationException("A FeatureGallery generator failed.", result.Exception);
        }

        if (result.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException("A FeatureGallery generator produced no sources.");
        }

        var compilerDiagnostics = output.GetDiagnostics();
        var errors = driverDiagnostics.Concat(result.Diagnostics).Concat(compilerDiagnostics)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Generated FeatureGallery compilation has errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }

        var descriptions = DescribeDiagnostics("driver", driverDiagnostics)
            .Concat(DescribeDiagnostics("generator", result.Diagnostics))
            .Concat(DescribeDiagnostics("compiler", compilerDiagnostics))
            .Order(StringComparer.Ordinal).ToArray();
        var canonicalDescriptions = DescribeDiagnostics("driver", driverDiagnostics, ignoreTransport: true)
            .Concat(DescribeDiagnostics("generator", result.Diagnostics, ignoreTransport: true))
            .Concat(DescribeDiagnostics("compiler", compilerDiagnostics, ignoreTransport: true))
            .Order(StringComparer.Ordinal).ToArray();
        return new CapturedRun(
            driver,
            output,
            result,
            result.GeneratedSources.ToDictionary(static source => source.HintName, static source => source.SourceText, StringComparer.Ordinal),
            descriptions,
            canonicalDescriptions);
    }

    private static GeneratedTypes GetGeneratedTypes(CapturedRun run)
    {
        var components = new SortedSet<string>(StringComparer.Ordinal);
        var akcss = new SortedSet<string>(StringComparer.Ordinal);
        var infrastructureHints = new List<string>();
        foreach (var source in run.Result.GeneratedSources)
        {
            var target = source.HintName.StartsWith("Akbura.Component.", StringComparison.Ordinal) ? components :
                source.HintName.StartsWith("Akbura.Akcss.", StringComparison.Ordinal) ? akcss : null;
            if (target == null)
            {
                infrastructureHints.Add(source.HintName);
                continue;
            }

            var model = run.Output.GetSemanticModel(source.SyntaxTree);
            foreach (var declaration in source.SyntaxTree.GetRoot().DescendantNodes().OfType<BaseTypeDeclarationSyntax>())
            {
                if (declaration.Ancestors().Any(static ancestor => ancestor is BaseTypeDeclarationSyntax))
                {
                    continue;
                }

                var symbol = model.GetDeclaredSymbol(declaration) as INamedTypeSymbol ??
                    throw new InvalidOperationException($"Cannot resolve generated type identity in '{source.HintName}'.");
                var namespaceName = symbol.ContainingNamespace.IsGlobalNamespace ? string.Empty : symbol.ContainingNamespace.ToDisplayString() + ".";
                target.Add(namespaceName + symbol.MetadataName);
            }
        }

        return new GeneratedTypes(components.ToArray(), akcss.ToArray(), infrastructureHints.Order(StringComparer.Ordinal).ToArray());
    }

    internal static IEnumerable<string> DescribeDiagnostics(
        string stage,
        ImmutableArray<Diagnostic> diagnostics,
        bool ignoreTransport = false)
    {
        return diagnostics.Select(diagnostic => JsonSerializer.Serialize(new
        {
            Stage = stage,
            diagnostic.Id,
            Severity = diagnostic.Severity.ToString(),
            diagnostic.WarningLevel,
            diagnostic.IsSuppressed,
            Message = diagnostic.GetMessage(CultureInfo.InvariantCulture),
            Title = diagnostic.Descriptor.Title.ToString(CultureInfo.InvariantCulture),
            diagnostic.Descriptor.Category,
            Description = diagnostic.Descriptor.Description.ToString(CultureInfo.InvariantCulture),
            diagnostic.Descriptor.HelpLinkUri,
            diagnostic.Descriptor.DefaultSeverity,
            diagnostic.Descriptor.IsEnabledByDefault,
            CustomTags = diagnostic.Descriptor.CustomTags.ToArray(),
            Location = DescribeLocation(diagnostic.Location),
            AdditionalLocations = diagnostic.AdditionalLocations.Select(DescribeLocation).ToArray(),
            Properties = diagnostic.Properties.Where(pair => !ignoreTransport || !IsTransportProperty(pair.Key))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray(),
        }));
    }

    private static bool IsTransportProperty(string key)
    {
        // These four documented publisher fields are not semantic identity.
        // Do not ignore arbitrary properties or a whole property-name prefix.
        return key is "akbura.origin" or "akbura.kind" or "akbura.logical-id" or "akbura.document-version";
    }

    private static object DescribeLocation(Location location)
    {
        var mapped = location.GetMappedLineSpan();
        var original = location.GetLineSpan();
        return new
        {
            Kind = location.Kind.ToString(),
            FilePath = original.Path,
            OriginalIsValid = original.IsValid,
            OriginalHasMappedPath = original.HasMappedPath,
            OriginalStartLine = original.StartLinePosition.Line,
            OriginalStartCharacter = original.StartLinePosition.Character,
            OriginalEndLine = original.EndLinePosition.Line,
            OriginalEndCharacter = original.EndLinePosition.Character,
            location.SourceSpan.Start,
            location.SourceSpan.Length,
            MappedPath = mapped.Path,
            MappedIsValid = mapped.IsValid,
            mapped.HasMappedPath,
            StartLine = mapped.StartLinePosition.Line,
            StartCharacter = mapped.StartLinePosition.Character,
            EndLine = mapped.EndLinePosition.Line,
            EndCharacter = mapped.EndLinePosition.Character,
        };
    }

    private static void AddDiagnosticDifferences(string[] expected, string[] actual, List<string> differences)
    {
        if (expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            return;
        }

        differences.Add("Driver/generator/compiler diagnostics differ, including multiplicity, locations, descriptor metadata or semantic properties.");
        var expectedCounts = expected.GroupBy(static diagnostic => diagnostic, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        var actualCounts = actual.GroupBy(static diagnostic => diagnostic, StringComparer.Ordinal)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
        foreach (var diagnostic in expectedCounts.Keys.Union(actualCounts.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            expectedCounts.TryGetValue(diagnostic, out var expectedCount);
            actualCounts.TryGetValue(diagnostic, out var actualCount);
            if (expectedCount != actualCount)
            {
                differences.Add($"Diagnostic count expected/Furioso {expectedCount}, actual/BlackSilence {actualCount}: {diagnostic}");
            }
        }
    }

    private static void AddDifferences(string label, IEnumerable<string> expected, IEnumerable<string> actual, List<string> differences)
    {
        foreach (var missing in expected.Except(actual, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            differences.Add(label + " only in expected/Furioso: " + missing);
        }

        foreach (var added in actual.Except(expected, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            differences.Add(label + " only in actual/BlackSilence: " + added);
        }
    }

    private static int FindDifference(string expected, string actual)
    {
        var length = Math.Min(expected.Length, actual.Length);
        for (var i = 0; i < length; i++)
        {
            if (expected[i] != actual[i])
            {
                return i;
            }
        }

        return length;
    }

    private static void PrintDifferences(string[] differences)
    {
        foreach (var difference in differences.Take(8))
        {
            Console.WriteLine("    " + difference);
        }

        if (differences.Length > 8)
        {
            Console.WriteLine($"    {differences.Length - 8} further differences are included in the artifact.");
        }
    }

    private static void WriteReport(string outputDirectory, VerificationReport report)
    {
        var markdown = new StringBuilder();
        markdown.AppendLine("# FeatureGallery generator parity verification");
        markdown.AppendLine();
        markdown.AppendLine("Result: " + (report.Passed ? "PASS" : "FAIL"));
        markdown.AppendLine();
        markdown.AppendLine("Real MSBuild-loaded FeatureGallery inputs. Every checked generated compilation must have zero errors; warnings and hidden diagnostics remain part of incremental/fresh parity.");
        markdown.AppendLine("Furioso/BlackSilence compatibility compares top-level component and AKCSS carrier metadata identities, not private nested helper types or source-text formatting. Every generated hint, including infrastructure, is accounted for; hint differences fail verification and are reported.");
        markdown.AppendLine("Both cross-generator and incremental/fresh comparisons check every diagnostic channel, multiplicity, original/mapped location, descriptor metadata and semantic property. Only akbura.origin, akbura.kind, akbura.logical-id and akbura.document-version are excluded as transport metadata; raw properties remain in the JSON report. Descriptor message templates may differ, but their formatted invariant messages must match exactly.");
        markdown.AppendLine("Each incremental/fresh comparison uses the same current AdditionalFile paths/text/options and C# compilation, with a fresh BlackSilence driver as reference. Sources are compared ordinally, not by hashes. Diagnostics are explicitly enabled in Publish mode. This command does not measure performance.");
        markdown.AppendLine();
        if (report.Compatibility is { } compatibility)
        {
            markdown.AppendLine($"Furioso/BlackSilence: {(compatibility.Passed ? "PASS" : "FAIL")}; component types {compatibility.FuriosoComponents.Length}/{compatibility.BlackSilenceComponents.Length}; AKCSS carriers {compatibility.FuriosoAkcss.Length}/{compatibility.BlackSilenceAkcss.Length}.");
            WriteDifferences(compatibility.Differences);
        }

        foreach (var scenario in report.Scenarios)
        {
            markdown.AppendLine();
            markdown.AppendLine($"{scenario.Scenario}: {(scenario.Passed ? "PASS" : "FAIL")}; sources {scenario.IncrementalSourceCount}/{scenario.FreshSourceCount}; diagnostics {scenario.IncrementalDiagnosticCount}/{scenario.FreshDiagnosticCount}.");
            WriteDifferences(scenario.Differences);
        }

        WriteDifferences(report.Failures.ToArray());
        Directory.CreateDirectory(outputDirectory);
        var jsonPath = Path.Combine(outputDirectory, "verification.json");
        var markdownPath = Path.Combine(outputDirectory, "report.md");
        File.WriteAllText(jsonPath, ToCrlf(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })), new UTF8Encoding(false));
        File.WriteAllText(markdownPath, ToCrlf(markdown.ToString()), new UTF8Encoding(false));
        Console.WriteLine("Reports: " + jsonPath);
        Console.WriteLine("         " + markdownPath);

        void WriteDifferences(string[] differences)
        {
            if (differences.Length == 0)
            {
                return;
            }

            markdown.AppendLine();
            foreach (var difference in differences)
            {
                markdown.AppendLine("- " + difference);
            }
        }
    }

    private static string ToCrlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    private sealed record CapturedRun(
        GeneratorDriver Driver,
        Compilation Output,
        GeneratorRunResult Result,
        Dictionary<string, SourceText> Sources,
        string[] Diagnostics,
        string[] CanonicalDiagnostics);

    private sealed class DiagnosticPublishOptionsProvider(AnalyzerConfigOptionsProvider underlying) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } = new DiagnosticPublishOptions(underlying.GlobalOptions);
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => underlying.GetOptions(tree);
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => underlying.GetOptions(textFile);
    }

    private sealed class DiagnosticPublishOptions(AnalyzerConfigOptions underlying) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.AkburaBlackSilenceDiagnostics")
            {
                value = "Publish";
                return true;
            }

            return underlying.TryGetValue(key, out value!);
        }
    }
    private sealed record GeneratedTypes(string[] Components, string[] Akcss, string[] InfrastructureHints);
    private sealed record CompatibilityResult(
        string[] FuriosoComponents,
        string[] BlackSilenceComponents,
        string[] FuriosoAkcss,
        string[] BlackSilenceAkcss,
        string[] FuriosoInfrastructureHints,
        string[] BlackSilenceInfrastructureHints,
        string[] FuriosoDiagnostics,
        string[] BlackSilenceDiagnostics,
        string[] Differences)
    {
        public bool Passed => Differences.Length == 0;
    }

    private sealed record ScenarioResult(
        string Scenario,
        int IncrementalSourceCount,
        int FreshSourceCount,
        int IncrementalDiagnosticCount,
        int FreshDiagnosticCount,
        string[] IncrementalDiagnostics,
        string[] FreshDiagnostics,
        string[] Differences)
    {
        public bool Passed => Differences.Length == 0;
    }

    private sealed class VerificationReport(string projectDirectory, int additionalFileCount)
    {
        public int SchemaVersion => 1;
        public DateTimeOffset CreatedUtc { get; } = DateTimeOffset.UtcNow;
        public string ProjectDirectory { get; } = projectDirectory;
        public int AdditionalFileCount { get; } = additionalFileCount;
        public CompatibilityResult? Compatibility { get; set; }
        public List<ScenarioResult> Scenarios { get; } = [];
        public List<string> Failures { get; } = [];
        public bool Passed => Compatibility?.Passed == true && Failures.Count == 0 &&
            Scenarios.Count == Enum.GetValues<FeatureGalleryGeneratorScenario>().Length && Scenarios.All(static scenario => scenario.Passed);
    }
}
