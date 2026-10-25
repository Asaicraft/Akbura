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
/// Compares ordinary Release assemblies using identical gallery inputs.
/// Optional profiling assemblies capture actual type pairs outside measurements.
/// </summary>
internal static class FeatureGallerySymbolDisplayComparison
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static GeneratorDriver? s_driver;

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--symbol-display-cache") return false;
        try { Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        return true;
    }

    private static void Run(string[] args)
    {
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0 ? args[index + 1] : fallback;
        var variants = Path.GetFullPath(Option("--variants", "artifacts/symbol-display/variants"));
        var output = Path.GetFullPath(Option("--output", "artifacts/symbol-display/results"));
        var round = int.Parse(Option("--round", "0"));
        var focused = args.Contains("--focused", StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        Console.WriteLine("Loading real FeatureGallery outside measurements.");
        var project = FeatureGalleryBenchmarkProject.Load();
        var order = round % 2 == 0 ? new[] { "Baseline", "Cached" } : ["Cached", "Baseline"];
        if (focused)
        {
            using var capture = new VariantContext(Path.Combine(variants, "ProfileBaseline"));
            var pairs = (object[])capture.Call(nameof(Capture), [project.ExportComparisonInputs(), output, "ProfileBaseline", true])!;
            using (var profile = new VariantContext(Path.Combine(variants, "ProfileCached")))
                profile.Call(nameof(Capture), [project.ExportComparisonInputs(), output, "ProfileCached", false]);
            foreach (var variant in order)
            {
                using var context = new VariantContext(Path.Combine(variants, variant));
                context.Call(nameof(Focused), [pairs, output, variant]);
                Collect();
            }
        }
        else
        {
            foreach (var variant in order)
            {
                using var context = new VariantContext(Path.Combine(variants, variant));
                context.Call(nameof(Macro), [project.ExportComparisonInputs(), output, variant, round == 0]);
                Collect();
            }
            foreach (var file in Directory.EnumerateFiles(output, "Baseline-*-parity.json"))
                if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "Cached" + Path.GetFileName(file)[8..]))))
                    throw new InvalidOperationException("Source/diagnostic mismatch: " + file);
        }
        Console.WriteLine("COMPLETED " + output);
        GC.KeepAlive(project);
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
                    if (iteration == 0)
                    {
                        var actual = Snapshot(s_driver!, compilation, input.Snapshot.ProjectDirectory);
                        var fresh = Snapshot(Driver(project, input, input.ModifiedFiles, "Shadow").RunGenerators(input.ModifiedCompilation), compilation, input.Snapshot.ProjectDirectory);
                        if (actual != fresh) throw new InvalidOperationException(name + ": incremental/fresh mismatch.");
                        File.WriteAllText(Path.Combine(output, variant + "-" + name + "-parity.json"), actual);
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
                if (actual != File.ReadAllText(Path.Combine(output, variant + "-ColdGeneration-parity.json")))
                    throw new InvalidOperationException(mode + ": diagnostic mode mismatch.");
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

    private static Type? Hooks => typeof(AkburaSemanticModel).Assembly.GetType("Akbura.Language.SymbolDisplayTrace");

    public static object[] Capture(object[] shared, string output, string variant, bool keepPairs)
    {
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs(shared);
        var snapshot = project.CreateSnapshot();
        var input = new FeatureGalleryIncrementalInputs(project.Compilation, project.Compilation, snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot);
        var recorder = new PairCapture(keepPairs);
        var hooks = Hooks ?? throw new InvalidOperationException("Missing profiling hooks.");
        hooks.GetField("Compare")!.SetValue(null, new Action<ITypeSymbol, ITypeSymbol, bool>(recorder.Compare));
        hooks.GetField("Format")!.SetValue(null, new Action<ITypeSymbol, long>(recorder.Format));
        hooks.GetField("Bytes")!.SetValue(null, (Func<long>)GC.GetAllocatedBytesForCurrentThread);
        hooks.GetField("Lookup")!.SetValue(null, new Action<bool>(recorder.Lookup));
        try
        {
            var driver = Driver(project, input, input.OriginalFiles, "Shadow").RunGenerators(project.Compilation);
            Snapshot(driver, project.Compilation, snapshot.ProjectDirectory);
            Write(Path.Combine(output, variant + "-counts.json"), recorder.Summary());
            GC.KeepAlive(driver);
        }
        finally
        {
            foreach (var field in new[] { "Compare", "Format", "Bytes", "Lookup" }) hooks.GetField(field)!.SetValue(null, null);
        }
        Console.WriteLine(variant + ": actual formatting and comparison counts captured.");
        return [recorder.Symbols.ToArray(), recorder.Left.ToArray(), recorder.Right.ToArray(), recorder.Expected.ToArray()];
    }

    public static void Focused(object[] sharedPairs, string output, string variant)
    {
        if (Hooks != null) throw new InvalidOperationException("Profiling assembly used for timing.");
        var symbols = (ITypeSymbol[])sharedPairs[0]; var left = (int[])sharedPairs[1]; var right = (int[])sharedPairs[2]; var expected = (bool[])sharedPairs[3];
        var compare = typeof(AkburaSemanticModel).GetMethod("IsSameType", BindingFlags.Static | BindingFlags.NonPublic)!.CreateDelegate<Func<ITypeSymbol, ITypeSymbol, bool>>();
        var helper = typeof(AkburaSemanticModel).Assembly.GetType("Akbura.Collections.CachedSymbolDisplay");
        var cache = (ConditionalWeakTable<ITypeSymbol, string>?)helper?.GetField("s_fullyQualifiedNames", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null);
        var display = helper?.GetMethod("GetFullyQualifiedName")!.CreateDelegate<Func<ITypeSymbol, string>>() ?? (type => type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat));
        for (var i = 0; i < left.Length; i++)
            if (compare(symbols[left[i]], symbols[right[i]]) != expected[i]) throw new InvalidOperationException("Actual pair parity failed: " + i);
        var cases = new Dictionary<string, int[]>
        {
            ["ObservedOrder"] = Enumerable.Range(0, left.Length).ToArray(),
            ["SymbolEqual"] = Enumerable.Range(0, left.Length).Where(i => SymbolEqualityComparer.Default.Equals(symbols[left[i]], symbols[right[i]])).ToArray(),
            ["Unequal"] = Enumerable.Range(0, left.Length).Where(i => !expected[i]).ToArray(),
            ["DisplayOnlyEqual"] = Enumerable.Range(0, left.Length).Where(i => expected[i] && !SymbolEqualityComparer.Default.Equals(symbols[left[i]], symbols[right[i]])).ToArray(),
        };
        var samples = new List<object>();
        foreach (var (name, sequence) in cases)
        {
            if (sequence.Length == 0) throw new InvalidOperationException("Empty focused case: " + name);
            foreach (var cold in new[] { true, false })
                for (var iteration = -1; iteration < 3; iteration++)
                {
                    cache?.Clear();
                    if (!cold) foreach (var symbol in symbols) display(symbol);
                    var repeats = Math.Max(1, 100_000 / sequence.Length);
                    var before = GC.GetTotalAllocatedBytes(true); var start = Stopwatch.GetTimestamp();
                    var matches = 0;
                    for (var repeat = 0; repeat < repeats; repeat++)
                        foreach (var index in sequence) if (compare(symbols[left[index]], symbols[right[index]])) matches++;
                    var ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                    var bytes = GC.GetTotalAllocatedBytes(true) - before;
                    if (matches != sequence.Count(i => expected[i]) * repeats) throw new InvalidOperationException("Measured pair parity failed.");
                    if (iteration >= 0) samples.Add(new { Name = name, Cold = cold, Iteration = iteration, Operations = (long)sequence.Length * repeats, Nanoseconds = ns, AllocatedBytes = bytes, Matches = matches });
                }
        }
        for (var iteration = -1; iteration < 3; iteration++)
        {
            cache?.Clear();
            var before = GC.GetTotalAllocatedBytes(true); var start = Stopwatch.GetTimestamp();
            Parallel.For(0, left.Length, i => { if (compare(symbols[left[i]], symbols[right[i]]) != expected[i]) throw new InvalidOperationException("Parallel parity failed."); });
            var ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds; var bytes = GC.GetTotalAllocatedBytes(true) - before;
            if (iteration >= 0) samples.Add(new { Name = "ParallelObservedOrder", Cold = true, Iteration = iteration, Operations = (long)left.Length, Nanoseconds = ns, AllocatedBytes = bytes });
        }
        for (var iteration = -1; iteration < 3; iteration++)
        {
            double ns = 0; long bytes = 0;
            for (var cycle = 0; cycle < 20; cycle++)
            {
                cache?.Clear();
                var before = GC.GetAllocatedBytesForCurrentThread(); var start = Stopwatch.GetTimestamp();
                foreach (var symbol in symbols) display(symbol);
                ns += Stopwatch.GetElapsedTime(start).TotalNanoseconds;
                bytes += GC.GetAllocatedBytesForCurrentThread() - before;
            }
            if (iteration >= 0) samples.Add(new { Name = "UniqueFirstLookup", Cold = true, Iteration = iteration, Operations = (long)symbols.Length * 20, Nanoseconds = ns, AllocatedBytes = bytes });
        }
        Write(Path.Combine(output, variant + "-focused.json"), new { Environment.ProcessId, Environment.ProcessorCount, PairCount = left.Length, DistinctSymbols = symbols.Length,
            CacheEntries = cache?.Count(), CachedCharacters = cache?.Sum(pair => pair.Value.Length), Generator = Fingerprint(typeof(AkburaSemanticModel).Assembly), Samples = samples });
        Console.WriteLine(variant + ": ordered actual-pair, fast-path, unequal, cross-context fallback, parallel and first-lookup measurements complete.");
        GC.KeepAlive(sharedPairs);
    }

    private sealed class PairCapture(bool keepPairs)
    {
        private readonly Dictionary<ITypeSymbol, int> _ids = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<ITypeSymbol> _formatted = new(ReferenceEqualityComparer.Instance);
        public List<ITypeSymbol> Symbols { get; } = [];
        public List<int> Left { get; } = []; public List<int> Right { get; } = []; public List<bool> Expected { get; } = [];
        private long _comparisons, _fast, _fallbackEqual, _formats, _formatBytes, _hits, _misses;
        private int Id(ITypeSymbol symbol)
        {
            if (_ids.TryGetValue(symbol, out var id)) return id;
            id = Symbols.Count; _ids.Add(symbol, id); Symbols.Add(symbol); return id;
        }
        public void Compare(ITypeSymbol left, ITypeSymbol right, bool result)
        {
            lock (_ids)
            {
                _comparisons++;
                if (SymbolEqualityComparer.Default.Equals(left, right)) _fast++; else if (result) _fallbackEqual++;
                if (keepPairs) { Left.Add(Id(left)); Right.Add(Id(right)); Expected.Add(result); }
            }
        }
        public void Format(ITypeSymbol symbol, long bytes) { lock (_ids) { _formats++; _formatBytes += bytes; _formatted.Add(symbol); } }
        public void Lookup(bool hit) { lock (_ids) { if (hit) _hits++; else _misses++; } }
        public object Summary() => new { Comparisons = _comparisons, SymbolEqual = _fast, FallbackEqual = _fallbackEqual, FormatterCalls = _formats,
            FormatterAllocatedBytes = _formatBytes, DistinctFormattedSymbols = _formatted.Count, LookupHits = _hits, LookupMisses = _misses,
            PairOrder = "Linearized at completion under the capture lock; concurrent worker scheduling is not a deterministic global order." };
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
            try { return LoadFromAssemblyPath(Path.Combine(directory, "Akbura.Benchmarks.dll")).GetType(typeof(FeatureGallerySymbolDisplayComparison).FullName!)!.GetMethod(name)!.Invoke(null, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        public void Dispose() => Unload();
    }
}
