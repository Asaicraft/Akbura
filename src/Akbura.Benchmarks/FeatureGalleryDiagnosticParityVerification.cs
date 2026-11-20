using Akbura.BlackSilence;
using Akbura.Diagnostics;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryDiagnosticParityVerification
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--black-silence-diagnostic-parity")
        {
            return false;
        }

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("--black-silence-diagnostic-parity [--output artifacts/black-silence-diagnostic-parity] [--scenario ScenarioName] [--measure-overhead | --measure-only] [--operations 32]");
            Console.WriteLine("Checks all 16 diagnostic scenarios against fresh BlackSilence and the core diagnostic engine. Optional paired Off/Shadow/Publish measurements use this same binary.");
            return true;
        }

        var output = "artifacts/black-silence-diagnostic-parity";
        DiagnosticScenario? selected = null;
        var measure = false;
        var measureOnly = false;
        var operations = 32;
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                case "--scenario" when i + 1 < args.Length:
                    selected = Enum.Parse<DiagnosticScenario>(args[++i]);
                    break;
                case "--measure-overhead":
                    measure = true;
                    break;
                case "--measure-only":
                    measure = true;
                    measureOnly = true;
                    break;
                case "--operations" when i + 1 < args.Length:
                    operations = int.Parse(args[++i], System.Globalization.CultureInfo.InvariantCulture);
                    if (operations < 1)
                    {
                        throw new ArgumentOutOfRangeException(nameof(operations));
                    }

                    break;
                default:
                    throw new ArgumentException("Unknown or incomplete diagnostic parity option: " + args[i]);
            }
        }

        Run(Path.GetFullPath(output), selected, measure, measureOnly, operations);
        return true;
    }

    private static void Run(string output, DiagnosticScenario? selected, bool measure, bool measureOnly, int operations)
    {
        var project = FeatureGalleryDiagnosticWorkload.Project.Value;
        DiagnosticScenario[] scenarios = measureOnly ? [] : selected is { } value ? [value] : Enum.GetValues<DiagnosticScenario>();
        var results = new List<DiagnosticScenarioResult>();
        foreach (var scenario in scenarios)
        {
            Console.WriteLine("Diagnostic parity: " + scenario);
            try
            {
                var workload = FeatureGalleryDiagnosticWorkload.Prepare(project, scenario);
                if (scenario == DiagnosticScenario.ComponentSyntaxErrorFixed)
                {
                    WriteParserRecovery(output, workload);
                }

                object? statistics = null;
                GeneratorRunResult incremental;
#if STATS
                using (var measurement = BlackSilenceGenerationStatistics.BeginMeasurement())
                {
                    incremental = FeatureGalleryDiagnosticWorkload.Run(workload.Driver, workload.Compilation);
                    statistics = measurement.GetSnapshot();
                }
#else
                incremental = FeatureGalleryDiagnosticWorkload.Run(workload.Driver, workload.Compilation);
#endif
                var fresh = RunFresh(workload);
                var canonical = GetCanonicalDiagnostics(workload);
                var syntaxCount = canonical.Count(static diagnostic => diagnostic.Kind == AkburaDiagnosticKind.Syntax);
                var expected = Describe(canonical.Select(static diagnostic => AkburaDiagnosticAdapter.ToRoslyn(diagnostic)).ToImmutableArray());
                var actual = Describe(incremental.Diagnostics);
                var freshDiagnostics = Describe(fresh.Diagnostics);
                var differences = new List<string>();
                AddDiagnosticDifferences("Core diagnostic engine", expected, actual, differences);
                AddDiagnosticDifferences("Fresh BlackSilence", freshDiagnostics, actual, differences);
                CompareSources(fresh, incremental, differences);
                if (differences.Count != 0)
                {
                    WriteFailureSources(output, scenario, workload, incremental, fresh);
                }

                results.Add(new DiagnosticScenarioResult(
                    scenario.ToString(),
                    differences.Count == 0,
                    syntaxCount,
                    expected,
                    actual,
                    freshDiagnostics,
                    differences.ToArray(),
                    statistics));
                Console.WriteLine($"  {(differences.Count == 0 ? "PASS" : "FAIL")}: {actual.Length} diagnostics, {syntaxCount} syntax diagnostics.");
                foreach (var difference in differences.Take(3))
                {
                    Console.WriteLine("  " + difference);
                }
            }
            catch (Exception exception)
            {
                results.Add(new DiagnosticScenarioResult(
                    scenario.ToString(), false, 0, [], [], [], [exception.ToString()], null));
                Console.WriteLine("  FAIL: " + exception.Message);
            }
        }

        List<OverheadResult> overhead = measure ? MeasureOverhead(project, selected, operations) : [];
        var passed = results.All(static result => result.Passed);
        Directory.CreateDirectory(output);
        var report = new
        {
            SchemaVersion = 2,
            Passed = measureOnly ? (bool?)null : passed,
            VerificationExecuted = !measureOnly,
            CreatedUtc = DateTimeOffset.UtcNow,
            Results = results,
            Overhead = overhead,
        };
        File.WriteAllText(
            Path.Combine(output, "diagnostic-verification.json"),
            Crlf(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true })),
            new UTF8Encoding(false));
        var markdown = new StringBuilder("# FeatureGallery diagnostic parity\r\n\r\n");
        markdown.AppendLine("Result: " + (measureOnly ? "MEASURED (parity was not run)" : passed ? "PASS" : "FAIL"));
        markdown.AppendLine();
        markdown.AppendLine("The verification uses FeatureGallery AdditionalFiles and C# compilation. Dependency scenarios add controlled fixtures. Published diagnostics are compared with the core diagnostic engine and a fresh BlackSilence run. The comparison includes formatted messages, source locations, and semantic properties.");
        markdown.AppendLine();
        markdown.AppendLine("| Scenario | Result | Diagnostics | Syntax diagnostics |");
        markdown.AppendLine("| --- | --- | ---: | ---: |");
        foreach (var result in results)
        {
            markdown.AppendLine($"| {result.Scenario} | {(result.Passed ? "PASS" : "FAIL")} | {result.Actual.Length} | {result.SyntaxCount} |");
        }

        if (overhead.Count != 0)
        {
            markdown.AppendLine();
            markdown.AppendLine("## Paired overhead on this binary");
            markdown.AppendLine();
            markdown.AppendLine("Fast paths reuse a prepared immutable driver per mode after 32 warmups, in at least three alternating chunks of 256 operations. Their downstream semantic/diagnostic work is separately asserted to remain zero in STATS tests. Meaningful edits use an independently primed driver per operation; cold runs use fresh snapshots. Preparation and output serialization are outside timing. These are smoke measurements, not timing assertions; BenchmarkDotNet groups provide the controlled benchmark.");
            markdown.AppendLine();
            markdown.AppendLine("| Scenario | Mode | Operations | Mean ms | Mean bytes |");
            markdown.AppendLine("| --- | --- | ---: | ---: | ---: |");
            foreach (var sample in overhead)
            {
                markdown.AppendLine(FormattableString.Invariant($"| {sample.Scenario} | {sample.Mode} | {sample.Operations} | {sample.MillisecondsPerOperation:F6} | {sample.BytesPerOperation:F0} |"));
            }
        }

        File.WriteAllText(Path.Combine(output, "report.md"), Crlf(markdown.ToString()), new UTF8Encoding(false));
        Environment.ExitCode = passed ? 0 : 1;
        Console.WriteLine("Diagnostic report: " + output);
    }

    private static GeneratorRunResult RunFresh(FeatureGalleryDiagnosticWorkload workload)
    {
        var driver = FeatureGalleryDiagnosticWorkload.CreateDriver(
            workload.SourceProject, workload.Snapshot, workload.Files, new AkburaBlackSilenceGenerator());
        return FeatureGalleryDiagnosticWorkload.Run(driver, workload.Compilation);
    }

    private static void WriteParserRecovery(string output, FeatureGalleryDiagnosticWorkload workload)
    {
        var original = workload.Snapshot.ComponentOriginal;
        var validText = original.SourceText;
        var invalidText = validText.WithChanges(new TextChange(new TextSpan(validText.Length, 0), "\r\n/* diagnostic benchmark"));
        var initial = ComponentSyntaxTree.ParseText(invalidText, original.Path);
        var repaired = initial.WithChangedText(validText);
        var fresh = ComponentSyntaxTree.ParseText(validText, original.Path);
        var repairedShape = DescribeShape(repaired);
        var freshShape = DescribeShape(fresh);
        Directory.CreateDirectory(output);
        File.WriteAllText(
            Path.Combine(output, "parser-recovery.json"),
            Crlf(JsonSerializer.Serialize(new
            {
                original.Path,
                Input = validText.ToString(),
                InitialContainsDiagnostics = initial.GetRoot().ContainsDiagnostics,
                RootTextMatches = repaired.GetRoot().ToFullString() == fresh.GetRoot().ToFullString(),
                ShapeMatches = repairedShape.SequenceEqual(freshShape, StringComparer.Ordinal),
                RepairedDiagnostics = GetDiagnostics(repaired),
                FreshDiagnostics = GetDiagnostics(fresh),
                RepairedShape = repairedShape,
                FreshShape = freshShape,
            }, new JsonSerializerOptions { WriteIndented = true })),
            new UTF8Encoding(false));

        static string[] DescribeShape(ComponentSyntaxTree tree)
        {
            return tree.GetRoot().DescendantNodesAndTokensAndSelf().Select(static nodeOrToken => JsonSerializer.Serialize(new
            {
                Kind = nodeOrToken.Kind.ToString(),
                nodeOrToken.IsToken,
                nodeOrToken.IsMissing,
                nodeOrToken.Span,
                nodeOrToken.FullSpan,
                Text = nodeOrToken.ToFullString(),
            })).ToArray();
        }

        static string[] GetDiagnostics(ComponentSyntaxTree tree)
        {
            return Describe(AkburaDiagnosticEngine.Collect(tree, includeSemantic: false)
                .Where(static diagnostic => diagnostic.Kind == AkburaDiagnosticKind.Syntax)
                .Select(static diagnostic => AkburaDiagnosticAdapter.ToRoslyn(diagnostic)).ToImmutableArray());
        }
    }

    private static void WriteFailureSources(
        string output,
        DiagnosticScenario scenario,
        FeatureGalleryDiagnosticWorkload workload,
        GeneratorRunResult incremental,
        GeneratorRunResult fresh)
    {
        var directory = Path.Combine(output, "failures", scenario.ToString());
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, "inputs.json"),
            Crlf(JsonSerializer.Serialize(
                workload.Files.Select(static file => new { file.Path, Text = file.GetText()?.ToString() }).ToArray(),
                new JsonSerializerOptions { WriteIndented = true })),
            new UTF8Encoding(false));
        Write("incremental", incremental);
        Write("fresh", fresh);

        void Write(string pipeline, GeneratorRunResult result)
        {
            var pipelineDirectory = Path.Combine(directory, pipeline);
            Directory.CreateDirectory(pipelineDirectory);
            var manifest = new List<object>();
            for (var i = 0; i < result.GeneratedSources.Length; i++)
            {
                var source = result.GeneratedSources[i];
                // Numeric dump names cannot collide or escape the artifact folder;
                // the manifest preserves the exact generator hint identity.
                var fileName = i.ToString("D4", System.Globalization.CultureInfo.InvariantCulture) + ".g.cs";
                manifest.Add(new { source.HintName, File = fileName });
                File.WriteAllText(
                    Path.Combine(pipelineDirectory, fileName),
                    source.SourceText.ToString(),
                    new UTF8Encoding(false));
            }

            File.WriteAllText(
                Path.Combine(pipelineDirectory, "manifest.json"),
                Crlf(JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true })),
                new UTF8Encoding(false));
        }
    }

    private static ImmutableArray<AkburaDiagnosticRecord> GetCanonicalDiagnostics(FeatureGalleryDiagnosticWorkload workload)
    {
        var rootNamespace = workload.SourceProject.RootNamespace;
        var directory = workload.Snapshot.ProjectDirectory;
        var trees = workload.Files.Select(ParseSource)
            .OrderBy(static tree => tree.Kind)
            .ThenBy(static tree => tree.FilePath, StringComparer.Ordinal)
            .ToImmutableArray();
        var compilation = AkburaProjectIndex.Create(workload.Compilation, trees, rootNamespace, directory).Compilation;
        var diagnostics = trees.SelectMany(tree => AkburaDiagnosticEngine.Collect(
            tree,
            GlobalUsings.IsComponentFile(tree) || GlobalUsings.IsAkcssFile(tree) ? null : compilation.GetSemanticModel(tree)));

        return AkburaDiagnosticCollection.Deduplicate(diagnostics).ToImmutableArray();

        AkburaSyntaxTree ParseSource(AdditionalText file)
        {
            var text = file.GetText() ?? throw new InvalidOperationException("Missing AdditionalFile source.");
            return file.Path.EndsWith(".akcss", StringComparison.OrdinalIgnoreCase)
                ? AkcssSyntaxTree.ParseText(
                    text,
                    file.Path,
                    AkcssGeneratedModuleNames.GetMetadataName(
                        rootNamespace,
                        Path.GetRelativePath(directory, file.Path).Replace('\\', '/')))
                : ComponentSyntaxTree.ParseText(text, file.Path);
        }
    }

    private static string[] Describe(ImmutableArray<Diagnostic> diagnostics)
    {
        return FeatureGalleryParityVerification.DescribeDiagnostics("generator", diagnostics, ignoreTransport: true)
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static void AddDiagnosticDifferences(string expectedName, string[] expected, string[] actual, List<string> differences)
    {
        if (!expected.SequenceEqual(actual, StringComparer.Ordinal))
        {
            differences.Add($"{expectedName}: diagnostic sequences differ; expected {expected.Length}, actual {actual.Length}.");
            var length = Math.Min(expected.Length, actual.Length);
            for (var i = 0; i < length; i++)
            {
                if (expected[i] != actual[i])
                {
                    differences.Add($"First difference at {i}: expected {expected[i]}; actual {actual[i]}.");
                    break;
                }
            }
        }
    }

    private static void CompareSources(GeneratorRunResult fresh, GeneratorRunResult incremental, List<string> differences)
    {
        var expected = fresh.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();
        var actual = incremental.GeneratedSources.OrderBy(static source => source.HintName, StringComparer.Ordinal).ToArray();
        if (expected.Length != actual.Length)
        {
            differences.Add($"Fresh source count {expected.Length}, incremental source count {actual.Length}.");
        }

        for (var i = 0; i < Math.Min(expected.Length, actual.Length); i++)
        {
            if (expected[i].HintName != actual[i].HintName || !expected[i].SourceText.ContentEquals(actual[i].SourceText))
            {
                differences.Add("Fresh/incremental generated source mismatch: " + expected[i].HintName + " / " + actual[i].HintName);
            }
        }
    }

    private static List<OverheadResult> MeasureOverhead(
        FeatureGalleryBenchmarkProject project,
        DiagnosticScenario? selected,
        int operations)
    {
        DiagnosticScenario[] scenarios = selected is { } value
            ? [value]
            : [DiagnosticScenario.NoChanges, DiagnosticScenario.ComponentEofWhitespace, DiagnosticScenario.AkcssEofWhitespace, DiagnosticScenario.UnrelatedCSharpEdit];
        string[] modes = ["Off", "Shadow", "Publish"];
        var results = new List<OverheadResult>();
        foreach (var scenario in scenarios)
        {
            if (scenario is DiagnosticScenario.NoChanges or DiagnosticScenario.ComponentEofWhitespace or
                DiagnosticScenario.AkcssEofWhitespace or DiagnosticScenario.UnrelatedCSharpEdit)
            {
                MeasureFastPath(scenario);
                continue;
            }

            var elapsed = new double[modes.Length];
            var allocated = new long[modes.Length];
            for (var operation = -1; operation < operations; operation++)
            {
                for (var offset = 0; offset < modes.Length; offset++)
                {
                    var modeIndex = (offset + Math.Max(0, operation)) % modes.Length;
                    var workload = FeatureGalleryDiagnosticWorkload.Prepare(project, scenario, modes[modeIndex]);
                    var before = GC.GetTotalAllocatedBytes(precise: true);
                    var start = Stopwatch.GetTimestamp();
                    FeatureGalleryDiagnosticWorkload.Run(workload.Driver, workload.Compilation);
                    var duration = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var bytes = GC.GetTotalAllocatedBytes(precise: true) - before;
                    if (operation >= 0)
                    {
                        elapsed[modeIndex] += duration;
                        allocated[modeIndex] += bytes;
                    }
                }
            }

            for (var i = 0; i < modes.Length; i++)
            {
                results.Add(new OverheadResult(scenario.ToString(), modes[i], operations, elapsed[i] / operations, (double)allocated[i] / operations));
            }
        }

        return results;

        void MeasureFastPath(DiagnosticScenario scenario)
        {
            const int chunkSize = 256;
            var chunks = Math.Max(3, (operations + chunkSize - 1) / chunkSize);
            var actualOperations = chunks * chunkSize;
            var workloads = modes.Select(mode => FeatureGalleryDiagnosticWorkload.Prepare(project, scenario, mode)).ToArray();
            for (var i = 0; i < 32; i++)
            {
                foreach (var workload in workloads)
                {
                    FeatureGalleryDiagnosticWorkload.Run(workload.Driver, workload.Compilation);
                }
            }

            var elapsed = new double[modes.Length];
            var allocated = new long[modes.Length];
            for (var chunk = 0; chunk < chunks; chunk++)
            {
                for (var offset = 0; offset < modes.Length; offset++)
                {
                    var modeIndex = (offset + chunk) % modes.Length;
                    var workload = workloads[modeIndex];
                    var before = GC.GetTotalAllocatedBytes(precise: true);
                    var start = Stopwatch.GetTimestamp();
                    for (var i = 0; i < chunkSize; i++)
                    {
                        FeatureGalleryDiagnosticWorkload.Run(workload.Driver, workload.Compilation);
                    }

                    elapsed[modeIndex] += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    allocated[modeIndex] += GC.GetTotalAllocatedBytes(precise: true) - before;
                }
            }

            for (var i = 0; i < modes.Length; i++)
            {
                results.Add(new OverheadResult(
                    scenario.ToString(), modes[i], actualOperations,
                    elapsed[i] / actualOperations, (double)allocated[i] / actualOperations));
            }
        }
    }

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);

    private sealed record DiagnosticScenarioResult(
        string Scenario,
        bool Passed,
        int SyntaxCount,
        string[] Expected,
        string[] Actual,
        string[] Fresh,
        string[] Differences,
        object? Statistics);

    private sealed record OverheadResult(
        string Scenario,
        string Mode,
        int Operations,
        double MillisecondsPerOperation,
        double BytesPerOperation);
}
