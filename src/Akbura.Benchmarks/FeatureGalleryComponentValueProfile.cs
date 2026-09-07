using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

internal static class FeatureGalleryComponentValueProfile
{
    private const string Command = "--component-value-profile";

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != Command)
        {
            return false;
        }

        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine(Command + " [--roundtrips 2] [--output artifacts/component-value-profile]");
            Console.WriteLine("Opt-in STATS diagnostic profile, not BenchmarkDotNet. Validates independently fresh A/B, warms one roundtrip, then measures advancing A/B edits.");
            return true;
        }

        var roundtrips = 2;
        var output = "artifacts/component-value-profile";
        for (var i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--roundtrips" when i + 1 < args.Length:
                    roundtrips = int.Parse(args[++i], CultureInfo.InvariantCulture);
                    if (roundtrips < 1 || roundtrips > int.MaxValue / 2)
                    {
                        throw new ArgumentOutOfRangeException(nameof(roundtrips));
                    }

                    break;
                case "--output" when i + 1 < args.Length:
                    output = args[++i];
                    break;
                default:
                    throw new ArgumentException("Unknown or incomplete component-value profile option: " + args[i]);
            }
        }

#if STATS
        Run(Path.GetFullPath(output), roundtrips);
        return true;
#else
        throw new InvalidOperationException("Component-value profiling requires a STATS build, for example -c ReleaseStats -p:EnableAkburaStats=true.");
#endif
    }

#if STATS
    private static void Run(string output, int roundtrips)
    {
        Console.WriteLine("Component-value profile: loading the real FeatureGallery project.");
        var project = FeatureGalleryBenchmarkProject.Load();
        var inputs = FeatureGalleryIncrementalScenarioFactory.Create(project, FeatureGalleryIncrementalScenario.ComponentValueEdit);
        var freshAInputs = FeatureGalleryIncrementalScenarioFactory.Create(project, FeatureGalleryIncrementalScenario.ComponentValueEdit);
        var freshBInputs = FeatureGalleryIncrementalScenarioFactory.Create(project, FeatureGalleryIncrementalScenario.ComponentValueEdit);

        // Separate virtual paths prevent the process-wide incremental parse cache
        // from turning an independently fresh reference into a cached old parse.
        Console.WriteLine("Component-value profile: independently fresh A in isolated virtual paths.");
        var freshA = Capture(
            CreateDriver(project, freshAInputs, false).RunGenerators(freshAInputs.OriginalCompilation),
            freshAInputs.OriginalCompilation, freshAInputs.Snapshot.ProjectDirectory);
        Console.WriteLine("Component-value profile: independently fresh B in isolated virtual paths.");
        var freshB = Capture(
            CreateDriver(project, freshBInputs, true).RunGenerators(freshBInputs.ModifiedCompilation),
            freshBInputs.ModifiedCompilation, freshBInputs.Snapshot.ProjectDirectory);
        var changedHints = ChangedHints(freshA, freshB);
        if (changedHints.Length == 0)
        {
            throw new InvalidOperationException("ComponentValueEdit changed no generated source; refusing to profile a no-op.");
        }

        Console.WriteLine("Component-value profile: validating initial A and the A -> B -> A roundtrip.");
        var current = Capture(
            CreateDriver(project, inputs, false).RunGenerators(inputs.OriginalCompilation),
            inputs.OriginalCompilation, inputs.Snapshot.ProjectDirectory);
        VerifyEquivalent(freshA, current, "initial A");
        var modified = false;
        ApplyAndValidate();
        ApplyAndValidate();
        // One additional warm roundtrip after the correctness A -> B -> A.
        Console.WriteLine("Component-value profile: one additional warm A -> B -> A roundtrip.");
        ApplyAndValidate();
        ApplyAndValidate();

        var samples = new List<ProfileSample>();
        for (var i = 0; i < roundtrips * 2; i++)
        {
            if (i % 2 == 0)
            {
                Console.WriteLine($"Component-value profile: measured roundtrip {i / 2 + 1}/{roundtrips}.");
            }

            var previous = current;
            var nextModified = !modified;
            var files = nextModified ? inputs.ModifiedFiles : inputs.OriginalFiles;
            var compilation = nextModified ? inputs.ModifiedCompilation : inputs.OriginalCompilation;
            GeneratorDriver driver;
            TimeSpan elapsed;
            long allocated;
            GenerationStatisticsSnapshot statistics;
            using (var measurement = GenerationStatistics.BeginMeasurement(GC.GetAllocatedBytesForCurrentThread, trackOperations: true))
            {
                var before = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                driver = previous.Driver.ReplaceAdditionalTexts(files).RunGenerators(compilation);
                elapsed = Stopwatch.GetElapsedTime(start);
                allocated = GC.GetTotalAllocatedBytes(precise: true) - before;
                measurement.Dispose();
                statistics = measurement.GetSnapshot();
            }

            // GetRunResult, compiler checks, exact comparisons, snapshot identity
            // inspection and report construction are all outside the measurement.
            current = Capture(driver, compilation, inputs.Snapshot.ProjectDirectory);
            modified = nextModified;
            VerifyEquivalent(modified ? freshB : freshA, current, "measured " + (modified ? "A -> B" : "B -> A"));
            var changed = ChangedHints(previous, current);
            if (changed.Length == 0)
            {
                throw new InvalidOperationException("A measured transition changed no generated source.");
            }

            var stages = Enum.GetValues<GenerationStatisticStage>()
                .Where(static stage => stage != GenerationStatisticStage.Count)
                .Select(stage => new StageSample(
                    stage.ToString(), statistics.GetStageElapsed(stage).TotalMilliseconds,
                    statistics.GetStageAllocatedBytes(stage), statistics.GetStageInvocationCount(stage)))
                .ToArray();
            var sample = new ProfileSample(
                i + 1, modified ? "A -> B" : "B -> A", elapsed.TotalMilliseconds, allocated,
                previous.Snapshot.Version, current.Snapshot.Version, changed,
                DescribeEntries(previous.Snapshot.Entries, current.Snapshot.Entries, static identity => identity),
                DescribeEntries(previous.Snapshot.DiagnosticEntries, current.Snapshot.DiagnosticEntries,
                    path => Path.GetRelativePath(inputs.Snapshot.ProjectDirectory, path).Replace('\\', '/')),
                current.Sources.Count, current.Diagnostics.Length, statistics, stages,
                DescribeOperationDuplication(statistics, inputs.Snapshot.ProjectDirectory));
            samples.Add(sample);
            Console.WriteLine(FormattableString.Invariant(
                $"{sample.Number}: {sample.Direction}, {sample.DriverMilliseconds:F3} ms, {sample.DriverAllocatedBytes:N0} bytes, source entries regenerated/reused {sample.SourceEntries.Regenerated.Length}/{sample.SourceEntries.Reused.Length}."));
        }

        WriteReport(output, roundtrips, inputs, freshAInputs, freshBInputs, changedHints, samples);
        Console.WriteLine("Component-value profile: " + output);

        void ApplyAndValidate()
        {
            var nextModified = !modified;
            var files = nextModified ? inputs.ModifiedFiles : inputs.OriginalFiles;
            var compilation = nextModified ? inputs.ModifiedCompilation : inputs.OriginalCompilation;
            current = Capture(current.Driver.ReplaceAdditionalTexts(files).RunGenerators(compilation),
                compilation, inputs.Snapshot.ProjectDirectory);
            modified = nextModified;
            VerifyEquivalent(modified ? freshB : freshA, current, modified ? "validation/warmup B" : "validation/warmup A");
        }
    }

    private static GeneratorDriver CreateDriver(FeatureGalleryBenchmarkProject project, FeatureGalleryIncrementalInputs inputs, bool modified)
    {
        return CSharpGeneratorDriver.Create(
            generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: modified ? inputs.ModifiedFiles : inputs.OriginalFiles,
            parseOptions: project.ParseOptions,
            optionsProvider: new DiagnosticBenchmarkOptionsProvider(inputs.Snapshot.OptionsProvider, "Publish"),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: true));
    }

    private static CapturedRun Capture(GeneratorDriver driver, CSharpCompilation compilation, string virtualRoot)
    {
        var run = driver.GetRunResult();
        var result = run.Results.Single();
        if (result.Exception != null || result.GeneratedSources.IsEmpty)
        {
            throw new InvalidOperationException("Component-value profile generation failed or emitted no sources.", result.Exception);
        }

        var compilerDiagnostics = compilation.AddSyntaxTrees(result.GeneratedSources.Select(static source => source.SyntaxTree)).GetDiagnostics();
        var errors = run.Diagnostics.Concat(result.Diagnostics).Concat(compilerDiagnostics)
            .Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
        {
            throw new InvalidOperationException("Component-value profile produced errors:" + Environment.NewLine +
                string.Join(Environment.NewLine, errors.Select(static diagnostic => diagnostic.ToString())));
        }

        var diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", run.Diagnostics, ignoreTransport: true)
            .Concat(FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, ignoreTransport: true))
            .Concat(FeatureGalleryParityVerification.DescribeDiagnostics("compiler", compilerDiagnostics, ignoreTransport: true))
            .Select(text => NormalizeVirtualRoot(text, virtualRoot)).Order(StringComparer.Ordinal).ToArray();
        var request = result.TrackedSteps["BlackSilence.GenerationRequests"].SelectMany(static step => step.Outputs)
            .Select(static output => output.Value).OfType<BlackSilenceGenerationRequest>().Single();
        var snapshot = request.State.TryGetSnapshot(request.Options) ??
            throw new InvalidOperationException("The generator did not publish its completed project snapshot.");

        // Capture the immutable snapshot now. Reading the old request's shared
        // State after the next run would observe that newer run instead.
        return new CapturedRun(driver, snapshot,
            result.GeneratedSources.ToDictionary(static source => source.HintName,
                source => NormalizeVirtualRoot(source.SourceText.ToString(), virtualRoot), StringComparer.Ordinal),
            diagnostics);
    }

    private static string NormalizeVirtualRoot(string text, string root)
    {
        var roots = new[] { root, root.Replace('\\', '/'), root.Replace('/', '\\') };
        var escapedRoots = roots.Select(static path => JsonSerializer.Serialize(path)[1..^1]).ToArray();
        // Mapped #line paths can retain C#-escaped backslashes; serializing the
        // diagnostic escapes them once more. Relocate only that same known root.
        var forms = roots.Concat(escapedRoots)
            .Concat(escapedRoots.Select(static path => JsonSerializer.Serialize(path)[1..^1]))
            .Distinct(StringComparer.Ordinal).OrderByDescending(static path => path.Length);
        foreach (var form in forms)
        {
            text = text.Replace(form, "__AKBURA_PROFILE_PROJECT__", StringComparison.Ordinal);
        }

        return text;
    }

    private static string[] ChangedHints(CapturedRun before, CapturedRun after)
    {
        return before.Sources.Keys.Union(after.Sources.Keys, StringComparer.Ordinal)
            .Where(hint => !before.Sources.TryGetValue(hint, out var oldText) ||
                !after.Sources.TryGetValue(hint, out var newText) || !string.Equals(oldText, newText, StringComparison.Ordinal))
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static void VerifyEquivalent(CapturedRun expected, CapturedRun actual, string phase)
    {
        var changed = ChangedHints(expected, actual);
        if (changed.Length != 0)
        {
            throw new InvalidOperationException(phase + ": independently fresh/generated source mismatch: " + string.Join(", ", changed));
        }

        if (!expected.Diagnostics.SequenceEqual(actual.Diagnostics, StringComparer.Ordinal))
        {
            var expectedCounts = expected.Diagnostics.GroupBy(static diagnostic => diagnostic, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
            var actualCounts = actual.Diagnostics.GroupBy(static diagnostic => diagnostic, StringComparer.Ordinal)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal);
            var missing = expectedCounts.Where(pair => pair.Value > actualCounts.GetValueOrDefault(pair.Key))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
            var extra = actualCounts.Where(pair => pair.Value > expectedCounts.GetValueOrDefault(pair.Key))
                .OrderBy(static pair => pair.Key, StringComparer.Ordinal).ToArray();
            var details = new StringBuilder(phase + ": independently fresh diagnostics differ, including location, severity or multiplicity.");
            details.AppendLine();
            details.AppendLine($"Expected {expected.Diagnostics.Length} diagnostics ({expectedCounts.Count} distinct), actual {actual.Diagnostics.Length} ({actualCounts.Count} distinct).");
            details.AppendLine($"Missing occurrences: {missing.Sum(pair => pair.Value - actualCounts.GetValueOrDefault(pair.Key))}; extra occurrences: {extra.Sum(pair => pair.Value - expectedCounts.GetValueOrDefault(pair.Key))}.");
            foreach (var pair in missing.Take(5))
            {
                details.AppendLine($"Missing: expected count {pair.Value}, actual count {actualCounts.GetValueOrDefault(pair.Key)}: {pair.Key}");
            }

            foreach (var pair in extra.Take(5))
            {
                details.AppendLine($"Extra: expected count {expectedCounts.GetValueOrDefault(pair.Key)}, actual count {pair.Value}: {pair.Key}");
            }

            throw new InvalidOperationException(details.ToString());
        }
    }

    private static EntryChanges DescribeEntries<T>(ImmutableDictionary<string, T> before, ImmutableDictionary<string, T> after, Func<string, string> label)
        where T : class
    {
        var reused = new List<string>();
        var regenerated = new List<string>();
        var added = new List<string>();
        var removed = new List<string>();
        foreach (var identity in before.Keys.Union(after.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            if (!after.TryGetValue(identity, out var current))
            {
                removed.Add(label(identity));
            }
            else if (!before.TryGetValue(identity, out var previous))
            {
                added.Add(label(identity));
                regenerated.Add(label(identity));
            }
            else if (ReferenceEquals(previous, current))
            {
                reused.Add(label(identity));
            }
            else
            {
                regenerated.Add(label(identity));
            }
        }

        return new EntryChanges(regenerated.ToArray(), reused.ToArray(), added.ToArray(), removed.ToArray());
    }

    private static OperationDuplicationProfile[] DescribeOperationDuplication(GenerationStatisticsSnapshot statistics, string virtualRoot)
    {
        return statistics.OperationMeasurements.GroupBy(static row => row.Operation)
            .OrderBy(static group => group.Key)
            .Select(group =>
            {
                var rows = group.ToArray();
                // SourceId denotes a red root reference. Name/path are display
                // labels only, including when foreign-root paths are unknown.
                var declarations = rows.GroupBy(static row => (row.SourceId, row.Start, row.Length, row.ItemCount))
                    .Select(declaration => Summarize(declaration.ToArray(), ownerId: null)).ToArray();
                var owners = rows.Select(row => Summarize(new[] { row }, row.OwnerId)).ToArray();
                return new OperationDuplicationProfile(
                    group.Key.ToString(), rows.Sum(static row => row.InvocationCount), declarations.Length, rows.Length,
                    rows.Sum(static row => Math.Max(0, row.InvocationCount - 1)), rows.Length - declarations.Length,
                    rows.Sum(static row => row.RepeatedAllocatedBytes), rows.Sum(static row => row.RepeatedElapsed.TotalMilliseconds),
                    rows.Sum(static row => row.InvalidAllocationMeasurementCount), Top(declarations), Top(owners));
            }).ToArray();

        OperationKeyProfile Summarize(GenerationOperationStatistics[] rows, int? ownerId)
        {
            var first = rows[0];
            var ownerCount = rows.Select(static row => row.OwnerId).Distinct().Count();
            return new OperationKeyProfile(
                ownerId, first.SourceId, first.Start, first.Length, first.ItemCount,
                string.IsNullOrEmpty(first.SourcePath) ? string.Empty : NormalizeVirtualRoot(first.SourcePath, virtualRoot),
                first.Name, ownerCount, rows.Sum(static row => row.InvocationCount),
                rows.Sum(static row => Math.Max(0, row.InvocationCount - 1)), ownerCount - 1,
                rows.Sum(static row => row.FirstInvocationAllocatedBytes), rows.Sum(static row => row.FirstInvocationElapsed.TotalMilliseconds),
                rows.Sum(static row => row.RepeatedAllocatedBytes), rows.Sum(static row => row.RepeatedElapsed.TotalMilliseconds),
                rows.Sum(static row => row.InvalidAllocationMeasurementCount));
        }

        static OperationKeyProfile[] Top(OperationKeyProfile[] rows)
        {
            return rows.OrderByDescending(static row => row.RepeatedAllocatedBytes)
                .ThenByDescending(static row => row.SameOwnerRepeatCalls)
                .ThenByDescending(static row => row.FirstInvocationAllocatedBytes)
                .ThenBy(static row => row.SourceId).ThenBy(static row => row.Start).ThenBy(static row => row.OwnerId)
                .Take(10).ToArray();
        }
    }

    private static void WriteOperationDuplication(StringBuilder markdown, OperationDuplicationProfile[] profiles)
    {
        markdown.AppendLine("### Operation duplication");
        markdown.AppendLine();
        markdown.AppendLine("Reference IDs are local to this measurement. Declaration keys ignore OwnerId but retain operation, red-root SourceId, span and item count; owner keys retain all of them. Name/path are labels, and an empty path means an unlocated foreign root, not a shared file. Repeated allocations are measured inclusive work, not achievable cache savings or exclusive costs. Lookup includes nested utility-parameter binding; do not sum the two operations. Empty lookup layers are not tracked.");
        markdown.AppendLine();
        if (profiles.Length == 0)
        {
            markdown.AppendLine("No completed tracked operations were observed in this sample.");
            markdown.AppendLine();
            return;
        }

        foreach (var profile in profiles)
        {
            markdown.AppendLine("#### " + profile.Operation);
            markdown.AppendLine();
            markdown.AppendLine(FormattableString.Invariant($"Calls: {profile.TotalCalls}; declaration keys: {profile.UniqueDeclarationKeys}; owner/declaration keys: {profile.OwnerDeclarationKeys}; same-owner repeat calls: {profile.SameOwnerRepeatCalls}; additional-owner first calls: {profile.AdditionalOwnerFirstCalls}; repeat bytes: {profile.RepeatedAllocatedBytes}; repeat ms: {profile.RepeatedMilliseconds:F3}; invalid allocation samples: {profile.InvalidAllocationMeasurementCount}."));
            markdown.AppendLine();
            WriteKeys("Top 10 declaration keys (owners combined)", profile.TopDeclarations);
            WriteKeys("Top 10 owner/declaration keys", profile.TopOwners);
        }

        void WriteKeys(string title, OperationKeyProfile[] rows)
        {
            markdown.AppendLine(title + ", ordered by repeated bytes, repeated calls, then first-invocation bytes:");
            markdown.AppendLine();
            markdown.AppendLine("| Owner | SourceId:span/items | Calls/repeats | First bytes | Repeat bytes | First ms | Repeat ms | Label/source path |");
            markdown.AppendLine("| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |");
            foreach (var row in rows)
            {
                var owner = row.OwnerId?.ToString(CultureInfo.InvariantCulture) ?? "all (" + row.OwnerCount + ")";
                var path = row.SourcePath.Length == 0 ? "unlocated foreign root" : row.SourcePath;
                markdown.AppendLine(FormattableString.Invariant($"| {owner} | {row.SourceId}:{row.Start}+{row.Length}/{row.ItemCount} | {row.InvocationCount}/{row.SameOwnerRepeatCalls} | {row.FirstInvocationAllocatedBytes} | {row.RepeatedAllocatedBytes} | {row.FirstInvocationMilliseconds:F3} | {row.RepeatedMilliseconds:F3} | {Cell(row.Name)} / {Cell(path)} |"));
            }

            markdown.AppendLine();
            markdown.AppendLine("First totals are the first invocation per owner key, including when the declaration table combines owners; repeat totals contain only later invocations of those same owner keys.");
            markdown.AppendLine();
        }

        static string Cell(string text) => text.Replace("|", "\\|", StringComparison.Ordinal)
            .Replace("\r", " ", StringComparison.Ordinal).Replace("\n", " ", StringComparison.Ordinal);
    }

    private static void WriteReport(
        string output, int roundtrips, FeatureGalleryIncrementalInputs inputs,
        FeatureGalleryIncrementalInputs freshAInputs, FeatureGalleryIncrementalInputs freshBInputs,
        string[] changedHints, List<ProfileSample> samples)
    {
        Directory.CreateDirectory(output);
        var report = new
        {
            SchemaVersion = 2,
            Scenario = nameof(FeatureGalleryIncrementalScenario.ComponentValueEdit),
            DiagnosticsMode = "Publish",
            Kind = "Opt-in diagnostic profile, not a BenchmarkDotNet statistical result",
            CreatedUtc = DateTimeOffset.UtcNow,
            WarmupRoundtrips = 1,
            MeasuredRoundtrips = roundtrips,
            CorrectnessVerified = true,
            Correctness = "Independently fresh A/B in distinct virtual paths; exact source and driver/generator/compiler diagnostic parity for initial A, A-B-A, warmup and every measured transition; no compiler errors.",
            VirtualPathNormalization = "Only each known snapshot root and its slash, escaped and twice-escaped forms map to __AKBURA_PROFILE_PROJECT__. The second escaping accounts for serialized mapped #line paths. All remaining source and diagnostic content is compared exactly; only four documented diagnostic transport properties are excluded.",
            ProfileVirtualRoot = inputs.Snapshot.ProjectDirectory,
            FreshAVirtualRoot = freshAInputs.Snapshot.ProjectDirectory,
            FreshBVirtualRoot = freshBInputs.Snapshot.ProjectDirectory,
            ChangedHintNames = changedHints,
            AllocationMethod = "Driver: process-wide GC.GetTotalAllocatedBytes(true). Stages: inclusive same-thread scope deltas aggregated across parallel workers, not additive; cross-thread scope samples are invalid and excluded.",
            AllocationAttribution = "A parent DocumentBatch scope records only its own thread allocations, not allocations on parallel worker threads, unlike its elapsed wall time. Do not subtract child bytes from parent bytes to infer exclusive allocations.",
            IncrementalStepTrackingEnabled = true,
            OperationTrackingEnabled = samples.All(static sample => sample.Statistics.OperationTrackingEnabled),
            LookupCacheMeasurement = "AkcssLookupSymbols measures descriptor binding on cache misses. AkcssLookupRequestCount includes all nonempty lookup requests; AkcssLookupReusedCount counts descriptor hits. Every request still creates fresh symbol wrappers outside the operation scope but inside the driver/stage measurements. Concurrent cold misses may compute more than once.",
            OperationTracking = "Rows are grouped by operation, owner reference, red syntax-root reference, span and item count. Session-local IDs represent references, not stable cross-sample identities; paths and names are labels only. SourcePath may be empty for an unlocated foreign root; empty paths do not merge keys. Repeated inclusive allocations are observed cost, not achievable savings or exclusive cost. AKCSS lookup includes nested utility-parameter binding. Empty lookup layers are not tracked.",
            StageMeaning = "CSharpProbeCompilation measures the base probe compilation only. CSharpProbeBinding includes nested semantic-model creation and diagnostics stages.",
            HasAllocationMeasurements = samples.All(static sample => sample.Statistics.HasAllocationMeasurements),
            InvalidAllocationMeasurementCount = samples.Sum(static sample => sample.Statistics.InvalidAllocationMeasurementCount),
            InvalidOperationAllocationMeasurementCount = samples.Sum(static sample =>
                sample.Statistics.OperationMeasurements.Sum(static row => row.InvalidAllocationMeasurementCount)),
            Samples = samples,
        };
        File.WriteAllText(Path.Combine(output, "profile.json"), Crlf(JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true })), new UTF8Encoding(false));

        var markdown = new StringBuilder("# ComponentValueEdit diagnostic profile\r\n\r\n");
        markdown.AppendLine("BlackSilence, full FeatureGallery, diagnostics `Publish`. One extra warm roundtrip follows correctness validation; every measured A -> B -> A edit advances the driver. This is an opt-in diagnostic profile, **not a BenchmarkDotNet statistical result**.");
        markdown.AppendLine();
        markdown.AppendLine("Fresh A and B use separate virtual paths to avoid incremental parse-cache pollution. Only those known root paths (including escaped and twice-escaped serialized mapped paths) are relocated before exact source/diagnostic comparisons; four documented diagnostic transport properties are excluded. GetRunResult, generated-C# checks, parity validation and snapshot/report inspection run outside timing, including between samples; this can affect later cache warmth.");
        markdown.AppendLine();
        markdown.AppendLine("Driver bytes are process-wide `GC.GetTotalAllocatedBytes(true)` deltas. Stage bytes are inclusive same-thread scope deltas aggregated across parallel workers; elapsed scopes also overlap. **Do not sum stages or equate their totals to driver bytes/wall time.** Cross-thread allocation samples are excluded and counted as invalid. Instrumentation overhead remains included.");
        markdown.AppendLine();
        markdown.AppendLine("A parent `DocumentBatch` allocation scope excludes allocations on parallel worker threads, unlike its wall time. Subtracting child bytes from parent bytes cannot establish exclusive allocations. Incremental step tracking is enabled for this profile and its overhead is included; ordinary benchmark settings are unchanged.");
        markdown.AppendLine();
        markdown.AppendLine("`CSharpProbeCompilation` is base probe construction only. `CSharpProbeBinding` includes nested semantic-model creation and diagnostic work. Regenerated/reused lists compare actual immutable entry identity; regenerated means added/replaced entry, not necessarily changed emitted text.");
        markdown.AppendLine();
        markdown.AppendLine("| Sample | Edit | Driver ms | Driver bytes | Source regenerated/reused | Diagnostics regenerated/reused | Invalid allocation samples |");
        markdown.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: |");
        foreach (var sample in samples)
        {
            markdown.AppendLine(FormattableString.Invariant($"| {sample.Number} | {sample.Direction} | {sample.DriverMilliseconds:F3} | {sample.DriverAllocatedBytes} | {sample.SourceEntries.Regenerated.Length}/{sample.SourceEntries.Reused.Length} | {sample.DiagnosticEntries.Regenerated.Length}/{sample.DiagnosticEntries.Reused.Length} | {sample.Statistics.InvalidAllocationMeasurementCount} |"));
        }

        foreach (var sample in samples)
        {
            markdown.AppendLine();
            markdown.AppendLine($"## Sample {sample.Number}: {sample.Direction}");
            markdown.AppendLine();
            markdown.AppendLine("| Stage (inclusive) | ms | Bytes on scope threads | Completed scopes |");
            markdown.AppendLine("| --- | ---: | ---: | ---: |");
            foreach (var stage in sample.Stages)
            {
                markdown.AppendLine(FormattableString.Invariant($"| {stage.Stage} | {stage.Milliseconds:F3} | {stage.AllocatedBytes} | {stage.InvocationCount} |"));
            }

            markdown.AppendLine();
            markdown.AppendLine(FormattableString.Invariant($"AKCSS lookup requests: {sample.Statistics.AkcssLookupRequestCount}; cached descriptor hits: {sample.Statistics.AkcssLookupReusedCount}. The AkcssLookupSymbols operation measures descriptor binding on cache misses, not fresh symbol-wrapper creation on every lookup."));
            markdown.AppendLine();
            WriteOperationDuplication(markdown, sample.OperationDuplication);
            markdown.AppendLine("Changed generated text: " + string.Join(", ", sample.ChangedHints));
            WriteEntries("Source entries", sample.SourceEntries);
            WriteEntries("Diagnostic entries", sample.DiagnosticEntries);
        }

        File.WriteAllText(Path.Combine(output, "report.md"), Crlf(markdown.ToString()), new UTF8Encoding(false));

        void WriteEntries(string title, EntryChanges entries)
        {
            markdown.AppendLine();
            markdown.AppendLine(title + " regenerated: " + string.Join(", ", entries.Regenerated));
            markdown.AppendLine();
            markdown.AppendLine(title + " reused: " + string.Join(", ", entries.Reused));
            if (entries.Added.Length != 0 || entries.Removed.Length != 0)
            {
                markdown.AppendLine();
                markdown.AppendLine(title + " added: " + string.Join(", ", entries.Added) + "; removed: " + string.Join(", ", entries.Removed));
            }
        }
    }

    private static string Crlf(string text) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
    private sealed record CapturedRun(GeneratorDriver Driver, BlackSilenceProjectSnapshot Snapshot, Dictionary<string, string> Sources, string[] Diagnostics);
    private sealed record EntryChanges(string[] Regenerated, string[] Reused, string[] Added, string[] Removed);
    private sealed record StageSample(string Stage, double Milliseconds, long AllocatedBytes, long InvocationCount);
    private sealed record ProfileSample(
        int Number, string Direction, double DriverMilliseconds, long DriverAllocatedBytes,
        long PreviousSnapshotVersion, long SnapshotVersion, string[] ChangedHints,
        EntryChanges SourceEntries, EntryChanges DiagnosticEntries, int GeneratedSourceCount, int DiagnosticCount,
        GenerationStatisticsSnapshot Statistics, StageSample[] Stages, OperationDuplicationProfile[] OperationDuplication);

    private sealed record OperationDuplicationProfile(
        string Operation, long TotalCalls, int UniqueDeclarationKeys, int OwnerDeclarationKeys,
        long SameOwnerRepeatCalls, long AdditionalOwnerFirstCalls,
        long RepeatedAllocatedBytes, double RepeatedMilliseconds, long InvalidAllocationMeasurementCount,
        OperationKeyProfile[] TopDeclarations, OperationKeyProfile[] TopOwners);

    private sealed record OperationKeyProfile(
        int? OwnerId, int SourceId, int Start, int Length, int ItemCount, string SourcePath, string Name,
        int OwnerCount, long InvocationCount, long SameOwnerRepeatCalls, long AdditionalOwnerFirstCalls,
        long FirstInvocationAllocatedBytes, double FirstInvocationMilliseconds,
        long RepeatedAllocatedBytes, double RepeatedMilliseconds, long InvalidAllocationMeasurementCount);
#endif
}
