using Akbura.BlackSilence;
using Akbura.Language;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

/// <summary>
/// Compares isolated component metadata name variants and captures separate cache profiles.
/// </summary>
internal static class FeatureGalleryComponentMetadataNameComparison
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static GeneratorDriver? s_driver;
    private static Type? Hooks => typeof(AkburaCompilation).Assembly.GetType("Akbura.Language.DiagnosticTrace");

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--component-metadata-name-cache") return false;
        try { Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        return true;
    }

    private static void Run(string[] args)
    {
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0 ? args[index + 1] : fallback;
        var variants = Path.GetFullPath(Option("--variants", "artifacts/metadata-name/variants"));
        var output = Path.GetFullPath(Option("--output", "artifacts/metadata-name/results"));
        var round = int.Parse(Option("--round", "0"));
        var profile = args.Contains("--profile", StringComparer.Ordinal);
        var focused = args.Contains("--focused", StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        Console.WriteLine("Loading real FeatureGallery outside measurements.");
        var project = FeatureGalleryBenchmarkProject.Load();
        foreach (var variant in round % 2 == 0 ? new[] { "Baseline", "Cached" } : ["Cached", "Baseline"])
        {
            using var context = new VariantContext(Path.Combine(variants, (profile ? "Observed" : "") + variant));
            context.Call(profile ? nameof(Profile) : focused ? nameof(FocusedUpdates) : nameof(Macro),
                profile || focused ? [project.ExportComparisonInputs(), output, variant] : [project.ExportComparisonInputs(), output, variant, round == 0]);
            Collect();
        }
        foreach (var baseline in Directory.EnumerateFiles(output, "Baseline-*-parity.json"))
            if (!File.ReadAllBytes(baseline).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Cached" + Path.GetFileName(baseline)[8..]))))
                throw new InvalidOperationException("Source/diagnostic/symbol parity failed: " + baseline);
        Console.WriteLine("COMPLETED " + output);
        GC.KeepAlive(project);
    }

    public static void Profile(object[] shared, string output, string variant)
    {
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs(shared);
        var snapshot = project.CreateSnapshot();
        var input = new FeatureGalleryIncrementalInputs(project.Compilation, project.Compilation, snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot);
        Driver(project, input, input.OriginalFiles, "Shadow").RunGenerators(project.Compilation);
        Collect();
        var capture = new NameCapture();
        var hooks = Hooks ?? throw new InvalidOperationException("Missing temporary observation hooks.");
        hooks.GetField("Bytes")!.SetValue(null, (Func<long>)GC.GetAllocatedBytesForCurrentThread);
        hooks.GetField("Record")!.SetValue(null, new Action<string, object?, object?, object?, object?, long, long, long, long>(capture.Record));
        GeneratorDriver driver;
        try { driver = Driver(project, input, input.OriginalFiles, "Shadow").RunGenerators(project.Compilation); }
        finally { hooks.GetField("Record")!.SetValue(null, null); }
        File.WriteAllText(Path.Combine(output, variant + "-Observed-parity.json"), Snapshot(driver, project.Compilation, snapshot.ProjectDirectory));
        Write(Path.Combine(output, variant + "-observed.json"), capture.Snapshot());
        Console.WriteLine(variant + " observation complete.");
        GC.KeepAlive(driver);
    }

    public static void FocusedUpdates(object[] shared, string output, string variant)
    {
        if (Hooks != null) throw new InvalidOperationException("Instrumentation in focused parity assemblies.");
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs(shared);
        var directory = project.CreateSnapshot().ProjectDirectory;
        var focusedCompilation = project.Compilation.RemoveAllSyntaxTrees().WithAssemblyName("CacheFixture");
        const string initialComponent = "using Avalonia.Controls;\r\n<Border />\r\n";
        foreach (var scenario in new[] { "Rename", "Namespace", "Directory", "RootNamespace", "ProjectDirectory" })
        {
            var originalPath = Path.Combine(directory, "Views", "Card.akbura");
            var initialFiles = ImmutableArray.Create<AdditionalText>(
                new BenchmarkAdditionalText(originalPath, Microsoft.CodeAnalysis.Text.SourceText.From(initialComponent)),
                new BenchmarkAdditionalText(Path.Combine(directory, "Host.akbura"), Microsoft.CodeAnalysis.Text.SourceText.From("using CacheFixture.Views;\r\n<Card />\r\n")));
            var nextRoot = scenario == "RootNamespace" ? "ChangedFixture" : "CacheFixture";
            var nextDirectory = scenario == "ProjectDirectory" ? Path.Combine(directory, "OtherProject") : directory;
            var nextPath = scenario switch
            {
                "Rename" => Path.Combine(directory, "Views", "Renamed.akbura"),
                "Directory" => Path.Combine(directory, "Other", "Card.akbura"),
                "ProjectDirectory" => Path.Combine(nextDirectory, "Views", "Card.akbura"),
                _ => originalPath,
            };
            var targetName = scenario == "Rename" ? "Renamed" : "Card";
            var nextNamespace = scenario == "Namespace" ? "Changed.Namespace" : nextRoot + (scenario == "Directory" ? ".Other" : ".Views");
            var nextSource = scenario == "Namespace" ? "namespace Changed.Namespace;\r\n" + initialComponent : initialComponent;
            var nextFiles = ImmutableArray.Create<AdditionalText>(
                new BenchmarkAdditionalText(nextPath, Microsoft.CodeAnalysis.Text.SourceText.From(nextSource)),
                new BenchmarkAdditionalText(Path.Combine(nextDirectory, "Host.akbura"), Microsoft.CodeAnalysis.Text.SourceText.From($"using {nextNamespace};\r\n<{targetName} />\r\n")));
            var options = new BenchmarkAnalyzerConfigOptionsProvider("CacheFixture", directory);
            var updatedOptions = new BenchmarkAnalyzerConfigOptionsProvider(nextRoot, nextDirectory);
            GeneratorDriver New(ImmutableArray<AdditionalText> files, Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider provider) =>
                CSharpGeneratorDriver.Create([new AkburaBlackSilenceGenerator().AsSourceGenerator()], additionalTexts: files, parseOptions: project.ParseOptions,
                    optionsProvider: new DiagnosticBenchmarkOptionsProvider(provider, "Shadow"));
            var driver = New(initialFiles, options).RunGenerators(focusedCompilation);
            driver = driver.ReplaceAdditionalTexts(nextFiles).WithUpdatedAnalyzerConfigOptions(new DiagnosticBenchmarkOptionsProvider(updatedOptions, "Shadow")).RunGenerators(focusedCompilation);
            var actual = Snapshot(driver, focusedCompilation, directory);
            var fresh = Snapshot(New(nextFiles, updatedOptions).RunGenerators(focusedCompilation), focusedCompilation, directory);
            if (actual != fresh) throw new InvalidOperationException(scenario + ": incremental/fresh mismatch.");
            File.WriteAllText(Path.Combine(output, variant + "-Focused" + scenario + "-parity.json"), actual);
            var tree = AkburaSyntaxTree.ParseText(nextSource, nextPath);
            var hostTree = AkburaSyntaxTree.ParseText(nextFiles[1].GetText()!.ToString(), nextFiles[1].Path);
            var compilation = new AkburaCompilation(focusedCompilation, [tree, hostTree], nextRoot, nextDirectory);
            var model = compilation.GetSemanticModel(hostTree);
            var name = model.GetAkburaComponentMetadataName(tree);
            if (name != nextNamespace + "." + targetName) throw new InvalidOperationException(scenario + ": stale metadata name.");
            var element = hostTree.GetRoot().DescendantNodes().OfType<Akbura.Language.Syntax.MarkupElementSyntax>().Single();
            var symbol = model.GetSymbolInfo(element).Symbol as Akbura.Language.Symbols.IMarkupComponentSymbol;
            if (symbol?.AkburaComponent?.MetadataName != name) throw new InvalidOperationException(scenario + ": component resolution failed.");
            Write(Path.Combine(output, variant + "-Focused" + scenario + "-symbols-parity.json"), new { Name = name, Resolved = symbol.AkburaComponent.MetadataName });
            Console.WriteLine("PASS focused " + scenario);
        }
        CacheCosts(output, variant);
    }

    private static void CacheCosts(string output, string variant)
    {
        var field = typeof(AkburaCompilation).GetField("_componentMetadataNames", BindingFlags.Instance | BindingFlags.NonPublic);
        if (field == null) return;
        var trees = Enumerable.Range(0, 34).Select(index => AkburaSyntaxTree.ParseText("", $"Views/Card{index}.akbura")).ToArray();
        var compilation = new AkburaCompilation(CSharpCompilation.Create("CacheCosts"), trees, "Cost.Root");
        var model = compilation.GetSemanticModel(trees[0]);
        var names = trees.Select(model.GetAkburaComponentMetadataName).ToArray();
        var cache = (System.Collections.Concurrent.ConcurrentDictionary<AkburaSyntaxTree, string>)field.GetValue(compilation)!;
        var reads = MeasureDictionary(cache, trees, names, add: false);
        var existingAdds = MeasureDictionary(cache, trees, names, add: true);
        var copies = new System.Collections.Concurrent.ConcurrentDictionary<AkburaSyntaxTree, string>[1024];
        Collect();
        var heapBefore = GC.GetTotalMemory(false);
        var allocatedBefore = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        for (var index = 0; index < copies.Length; index++)
        {
            var copy = copies[index] = new(cache.Comparer);
            for (var key = 0; key < trees.Length; key++) copy.GetOrAdd(trees[key], names[key]);
        }
        var insertionMilliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
        Collect();
        var retained = GC.GetTotalMemory(false) - heapBefore;
        Write(Path.Combine(output, variant + "-cache-costs.json"), new
        {
            EntriesPerDictionary = trees.Length, Dictionaries = copies.Length, TryGetValue = reads, GetOrAddExisting = existingAdds,
            ConstructionAndInsertionMillisecondsPerDictionary = insertionMilliseconds / copies.Length,
            AllocatedBytesPerPopulatedDictionary = (double)allocated / copies.Length,
            RetainedBytesPerPopulatedDictionary = (double)retained / copies.Length,
            Note = "Separate micro measurement, no tracing or observer. Keys and strings already exist; includes dictionary construction and entries. Retained value is forced-GC heap difference over 1024 dictionaries, not a precise object-size census.",
        });
        GC.KeepAlive(copies);
        GC.KeepAlive(compilation);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object MeasureDictionary(System.Collections.Concurrent.ConcurrentDictionary<AkburaSyntaxTree, string> cache,
        AkburaSyntaxTree[] trees, string[] values, bool add)
    {
        const int count = 2_000_000;
        for (var index = 0; index < 10_000; index++) cache.TryGetValue(trees[index % trees.Length], out _);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var started = Stopwatch.GetTimestamp();
        var matches = 0;
        for (var index = 0; index < count; index++)
        {
            var key = index % trees.Length;
            string? result;
            if (add) result = cache.GetOrAdd(trees[key], values[key]);
            else cache.TryGetValue(trees[key], out result);
            if (ReferenceEquals(result, values[key])) matches++;
        }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var bytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (matches != count) throw new InvalidOperationException("Dictionary micro lookup mismatch.");
        return new { Requests = count, Milliseconds = elapsed, NanosecondsPerRequest = elapsed * 1_000_000 / count, AllocatedBytes = bytes };
    }

    private sealed class NameCapture
    {
        private readonly object _gate = new();
        private readonly Dictionary<string, long[]> _methods = new(StringComparer.Ordinal);
        private readonly HashSet<object> _owners = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _trees = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<string> _values = new(StringComparer.Ordinal);
        private readonly HashSet<object> _instances = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<(object Owner, object Tree), long> _computations = new();
        private long _requests, _hits, _misses, _bypasses, _duplicates;
        public void Record(string kind, object? owner, object? tree, object? input, object? result, long selfBytes, long inclusiveBytes, long selfTicks, long inclusiveTicks)
        {
            lock (_gate)
            {
                if (!_methods.TryGetValue(kind, out var item)) _methods.Add(kind, item = new long[5]);
                item[0]++; item[1] += selfBytes; item[2] += inclusiveBytes; item[3] += selfTicks; item[4] += inclusiveTicks;
                if (kind == "Request")
                {
                    _requests++;
                    _owners.Add(owner!); _trees.Add(tree!);
                    if (result is string value) { _values.Add(value); _instances.Add(value); }
                }
                if (kind == "CacheRead") { if (result != null) _hits++; else _misses++; }
                if (kind == "Bypass") _bypasses++;
                if (kind == "Compute")
                {
                    var key = (owner!, tree!);
                    _computations.TryGetValue(key, out var count);
                    _computations[key] = count + 1;
                }
                if (kind == "CacheStore" && result is string stored && input is string computed && !ReferenceEquals(stored, computed)) _duplicates++;
            }
        }
        public object Snapshot()
        {
            var field = typeof(AkburaCompilation).GetField("_componentMetadataNames", BindingFlags.Instance | BindingFlags.NonPublic);
            var entries = _owners.Select(owner => new { Entries = field == null ? 0 : (int)field.FieldType.GetProperty("Count")!.GetValue(field.GetValue(owner))! }).ToArray();
            return new { Environment.ProcessId, Requests = _requests, Hits = _hits, Misses = _misses, Bypasses = _bypasses,
                Computations = _methods.GetValueOrDefault("Compute")?[0] ?? 0,
                DistinctTrees = _trees.Count, DistinctResultStrings = _values.Count, DistinctResultInstances = _instances.Count,
                OwningCompilations = _owners.Count, TotalEntries = entries.Sum(entry => entry.Entries), Compilations = entries,
                DuplicateComputationResults = _duplicates, RepeatedComputeKeys = _computations.Count(pair => pair.Value > 1),
                Methods = _methods.OrderBy(pair => pair.Key).Select(pair => new { Name = pair.Key, Calls = pair.Value[0], SelfBytes = pair.Value[1], InclusiveBytes = pair.Value[2],
                    SelfMilliseconds = pair.Value[3] * 1000d / Stopwatch.Frequency, InclusiveMilliseconds = pair.Value[4] * 1000d / Stopwatch.Frequency }),
                Warning = "Observed only; recorder pins objects and instrumentation changes allocation, inlining and timing. Inclusive rows overlap." };
        }
    }

    public static void Macro(object[] shared, string output, string variant, bool modes)
    {
        if (Hooks != null) throw new InvalidOperationException("Instrumentation in primary measurements.");
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs(shared);
        var snapshot = project.CreateSnapshot();
        var workloads = new List<(string Name, FeatureGalleryIncrementalInputs Input)>
        {
            ("ColdGeneration", new(project.Compilation, project.Compilation, snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot)),
        };
        foreach (var scenario in new[] { FeatureGalleryIncrementalScenario.ComponentBindingEdit, FeatureGalleryIncrementalScenario.ComponentContractEdit })
            workloads.Add((scenario.ToString(), FeatureGalleryIncrementalScenarioFactory.Create(project, scenario)));
        var samples = new List<object>();
        foreach (var (name, input) in workloads)
        {
            var cold = name == "ColdGeneration";
            var driver = Driver(project, input, input.OriginalFiles, "Shadow");
            if (!cold) driver = driver.RunGenerators(input.OriginalCompilation);
            var warmups = cold ? 1 : 2;
            var iterations = cold ? 3 : 4;
            for (var iteration = -warmups; iteration < iterations; iteration++)
            {
                var forward = cold || (iteration + warmups) % 2 == 0;
                var compilation = forward ? input.ModifiedCompilation : input.OriginalCompilation;
                if (cold) driver = Driver(project, input, input.OriginalFiles, "Shadow");
                driver = driver.ReplaceAdditionalTexts(forward ? input.ModifiedFiles : input.OriginalFiles);
                Collect();
                var heapBefore = GC.GetTotalMemory(false);
                var measured = Invoke(driver, compilation);
                driver = null!;
                if (iteration >= 0)
                {
                    Collect();
                    var retained = GC.GetTotalMemory(false);
                    if (iteration < (cold ? 1 : 2))
                    {
                        var actual = Snapshot(s_driver!, compilation, input.Snapshot.ProjectDirectory);
                        var fresh = Snapshot(Driver(project, input, forward ? input.ModifiedFiles : input.OriginalFiles, "Shadow").RunGenerators(compilation), compilation, input.Snapshot.ProjectDirectory);
                        if (actual != fresh) throw new InvalidOperationException(name + ": incremental/fresh mismatch.");
                        File.WriteAllText(Path.Combine(output, variant + "-" + name + (cold ? "" : forward ? "-forward" : "-reverse") + "-parity.json"), actual);
                    }
                    if (cold)
                    {
                        s_driver = null;
                        Collect();
                    }
                    samples.Add(new { Name = name, Iteration = iteration, Forward = forward, measured.Milliseconds, measured.AllocatedBytes,
                        measured.Gen0, measured.Gen1, measured.Gen2, HeapBefore = heapBefore, RetainedWithDriver = retained,
                        RetainedAfterRelease = cold ? (long?)GC.GetTotalMemory(false) : null, PeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 });
                    Console.WriteLine($"{variant} {name}/{iteration}: {measured.Milliseconds:F2} ms, {measured.AllocatedBytes} B.");
                }
                if (!cold) driver = s_driver!;
                s_driver = null;
            }
            driver = null!;
            Collect();
        }
        if (modes)
            foreach (var mode in new[] { "Off", "Shadow", "Publish" })
            {
                var input = workloads[0].Input;
                var actual = Snapshot(Driver(project, input, input.OriginalFiles, mode).RunGenerators(input.OriginalCompilation), project.Compilation, input.Snapshot.ProjectDirectory);
                File.WriteAllText(Path.Combine(output, variant + "-Mode" + mode + "-parity.json"), actual);

            }
        Write(Path.Combine(output, variant + "-macro.json"), new
        {
            Environment.ProcessId, Environment.ProcessorCount, GCSettings.IsServerGC,
            TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            Generator = Fingerprint(typeof(AkburaSemanticModel).Assembly), Benchmark = Fingerprint(typeof(FeatureGallerySymbolDisplayComparison).Assembly),
            BlackSilence = Fingerprint(typeof(AkburaBlackSilenceGenerator).Assembly), Roslyn = Fingerprint(typeof(CSharpCompilation).Assembly),
            Inputs = snapshot.AdditionalTexts.Select(file => new { Path = Path.GetRelativePath(snapshot.ProjectDirectory, file.Path), Sha256 = Hash(file.GetText()!.ToString()) }),
            References = project.Compilation.References.Select(reference => reference.Display).Order(StringComparer.Ordinal), Samples = samples,
        });
        s_driver = null;
        GC.KeepAlive(project);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Measurement Invoke(GeneratorDriver driver, CSharpCompilation compilation)
    {
        var gen0 = GC.CollectionCount(0); var gen1 = GC.CollectionCount(1); var gen2 = GC.CollectionCount(2);
        var bytes = GC.GetTotalAllocatedBytes(true);
        var start = Stopwatch.GetTimestamp();
        driver = driver.RunGenerators(compilation);
        var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(true) - bytes;
        s_driver = driver;
        return new(elapsed, allocated, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1, GC.CollectionCount(2) - gen2);
    }

    private static GeneratorDriver Driver(FeatureGalleryBenchmarkProject project, FeatureGalleryIncrementalInputs input, ImmutableArray<AdditionalText> files, string mode) =>
        CSharpGeneratorDriver.Create(generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()], additionalTexts: files,
            parseOptions: project.ParseOptions, optionsProvider: new DiagnosticBenchmarkOptionsProvider(input.Snapshot.OptionsProvider, mode));

    private static string Snapshot(GeneratorDriver driver, CSharpCompilation compilation, string root)
    {
        var run = driver.GetRunResult(); var result = run.Results.Single();
        if (result.Exception != null || result.GeneratedSources.IsEmpty) throw new InvalidOperationException("Generation failed.", result.Exception);
        var output = compilation.AddSyntaxTrees(result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SyntaxTree));
        var diagnostics = output.GetDiagnostics();
        FeatureGalleryStageMeasurements.ValidateCompilation(output);
        string Normalize(string text) => text.Replace(root, "<project>", StringComparison.Ordinal).Replace(root.Replace('\\', '/'), "<project>", StringComparison.Ordinal)
            .Replace(JsonSerializer.Serialize(root)[1..^1], "<project>", StringComparison.Ordinal);
        return JsonSerializer.Serialize(new
        {
            Sources = result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => new { source.HintName, Text = Normalize(source.SourceText.ToString()) }),
            Diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, false).Select(Normalize),
            DriverDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", run.Diagnostics, false).Select(Normalize),
            CompilerDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("compiler", diagnostics, false).Select(Normalize).Order(StringComparer.Ordinal),
        }, s_json);
    }

    private static object Fingerprint(Assembly assembly) => new { assembly.Location, Mvid = assembly.ManifestModule.ModuleVersionId, Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, s_json));
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private sealed record Measurement(double Milliseconds, long AllocatedBytes, int Gen0, int Gen1, int Gen2);

    private sealed class VariantContext(string directory) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        protected override Assembly? Load(AssemblyName name) => name.Name is "Akbura.Benchmarks" or "Akbura.Generator" or "Akbura.BlackSilence"
            ? LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : Default.LoadFromAssemblyName(name);
        public object? Call(string name, object[] arguments)
        {
            try { return LoadFromAssemblyPath(Path.Combine(directory, "Akbura.Benchmarks.dll")).GetType(typeof(FeatureGalleryComponentMetadataNameComparison).FullName!)!.GetMethod(name)!.Invoke(null, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        public void Dispose() => Unload();
    }
}
