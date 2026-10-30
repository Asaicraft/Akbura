using Akbura.BlackSilence;
using Akbura.Collections;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

/// <summary>
/// Replays actual local dictionary lifetimes and compares isolated gallery builds.
/// Instrumented assemblies supply traces only, never generator timing samples.
/// </summary>
internal static class FeatureGallerySmallDictionaryComparison
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static int s_sink;
    private static object? s_mapSink;

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--small-dictionary") return false;
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0
            ? args[index + 1] : fallback;
        var variants = Path.GetFullPath(Option("--variants", ""));
        var output = Path.GetFullPath(Option("--output", ""));
        var round = int.Parse(Option("--round", "0"));
        var iterations = int.Parse(Option("--iterations", "3"));
        var focused = args.Contains("--focused-only", StringComparer.Ordinal);
        if (iterations < 2) throw new ArgumentOutOfRangeException(nameof(iterations));
        Directory.CreateDirectory(output);
        Console.WriteLine("Loading real FeatureGallery outside measurements.");
        var project = FeatureGalleryBenchmarkProject.Load();
        var inputs = project.ExportComparisonInputs();
        Isolated(inputs, Path.Combine(variants, "Profile"), "Profile", output, iterations, focused, round);
        if (focused) return true;
        var order = new[] { "Dictionary", "Capacity3", "SmallDictionary" };
        if (round % 2 != 0) Array.Reverse(order);
        foreach (var variant in order)
            Isolated(inputs, Path.Combine(variants, variant), variant, output, iterations, false, round);
        foreach (var scenario in Scenarios)
        {
            var expected = File.ReadAllBytes(Path.Combine(output, "Dictionary-" + scenario + "-parity.json"));
            foreach (var variant in order.Append("Profile"))
                if (!expected.AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, variant + "-" + scenario + "-parity.json"))))
                    throw new InvalidOperationException(variant + ": source/diagnostic mismatch for " + scenario);
        }
        Write(Path.Combine(output, "parity-ok.json"), new { Scenarios, Variants = order.Append("Profile"), ExactSourcesAndDiagnostics = true });
        Console.WriteLine("PASS: all four builds produce identical sources and diagnostics in every scenario.");
        return true;
    }

    private static readonly string[] Scenarios = ["ColdGeneration", "ComponentBindingEdit", "ComponentContractEdit"];

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void Isolated(object inputs, string directory, string variant, string output, int iterations, bool focused, int round)
    {
        var context = new VariantContext(directory);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "Akbura.Benchmarks.dll"));
            assembly.GetType(typeof(FeatureGallerySmallDictionaryComparison).FullName!, true)!
                .GetMethod(nameof(RunVariant))!.Invoke(null, [inputs, variant, output, iterations, focused, round]);
        }
        catch (TargetInvocationException error) when (error.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error.InnerException).Throw();
        }
        finally { context.Unload(); Collect(); }
    }

    public static void RunVariant(object shared, string variant, string output, int iterations, bool focused, int round)
    {
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs((object[])shared);
        var traceField = typeof(SmallDictionary<,>).Assembly.GetType("Akbura.Collections.SmallMapTrace")?
            .GetField("Record", BindingFlags.Static | BindingFlags.Public);
        if ((traceField != null) != (variant == "Profile")) throw new InvalidOperationException("Wrong instrumentation build.");
        var measurements = new List<object>();
        var profiles = new List<object>();
        var micro = new List<object>();
        foreach (var scenario in focused ? Scenarios.Take(1) : Scenarios)
        {
            var snapshot = project.CreateSnapshot();
            var originalCompilation = project.Compilation;
            var modifiedCompilation = project.Compilation;
            var originalFiles = snapshot.AdditionalTexts;
            var modifiedFiles = originalFiles;
            if (scenario != "ColdGeneration")
            {
                var inputs = FeatureGalleryIncrementalScenarioFactory.Create(project,
                    Enum.Parse<FeatureGalleryIncrementalScenario>(scenario));
                snapshot = inputs.Snapshot;
                originalCompilation = inputs.OriginalCompilation;
                modifiedCompilation = inputs.ModifiedCompilation;
                originalFiles = inputs.OriginalFiles;
                modifiedFiles = inputs.ModifiedFiles;
            }
            GeneratorDriver NewDriver() => CSharpGeneratorDriver.Create(
                generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()], additionalTexts: originalFiles,
                parseOptions: project.ParseOptions,
                optionsProvider: new DiagnosticBenchmarkOptionsProvider(snapshot.OptionsProvider, "Publish"));
            var driver = NewDriver();
            if (scenario != "ColdGeneration") driver = Run(driver, originalCompilation);
            if (traceField != null)
            {
                var capture = new Capture();
                traceField.SetValue(null, new Action<object, string, object?, object?>(capture.Record));
                try { driver = Run(driver.ReplaceAdditionalTexts(modifiedFiles), modifiedCompilation); }
                finally { traceField.SetValue(null, null); }
                var maps = capture.Maps;
                profiles.Add(new { Scenario = scenario, Maps = maps.Select(map => new
                {
                    map.Category, Key = map.KeyType.FullName, Value = map.ValueType.FullName,
                    Count = map.Keys.Count, Operations = map.Operations.Count, Threads = map.Threads.Order().ToArray(),
                    Trace = map.Operations.Select(op => new { op.Kind, Key = DescribeKey(op.Key) }),
                }) });
                foreach (var group in maps.GroupBy(map => map.Category).OrderBy(group => group.Key, StringComparer.Ordinal))
                {
                    Console.WriteLine($"PROFILE {scenario}/{group.Key}: {group.Count()} maps; sizes " +
                        string.Join(", ", group.GroupBy(map => map.Keys.Count).OrderBy(bucket => bucket.Key).Select(bucket => $"{bucket.Key}:{bucket.Count()}")));
                    if (group.Any(map => map.Threads.Count != 1)) throw new InvalidOperationException("A mutable dictionary crossed threads.");
                    if (scenario == "ColdGeneration")
                    {
                        var method = typeof(FeatureGallerySmallDictionaryComparison).GetMethod(nameof(Micro), BindingFlags.NonPublic | BindingFlags.Static)!
                            .MakeGenericMethod(group.First().KeyType, group.First().ValueType);
                        micro.Add(method.Invoke(null, [group.Key, group.ToArray(), round])!);
                    }
                }
            }
            else
            {
                for (var iteration = -1; iteration < iterations; iteration++)
                {
                    var forward = scenario == "ColdGeneration" || (iteration + 1) % 2 == 0;
                    if (scenario == "ColdGeneration") driver = NewDriver();
                    driver = driver.ReplaceAdditionalTexts(forward ? modifiedFiles : originalFiles);
                    Collect();
                    var bytes = GC.GetTotalAllocatedBytes(true);
                    var gen0 = GC.CollectionCount(0);
                    var start = Stopwatch.GetTimestamp();
                    driver = Run(driver, forward ? modifiedCompilation : originalCompilation);
                    var elapsed = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    var allocated = GC.GetTotalAllocatedBytes(true) - bytes;
                    if (iteration >= 0) measurements.Add(new { Scenario = scenario, Iteration = iteration, Forward = forward,
                        Milliseconds = elapsed, AllocatedBytes = allocated, Gen0 = GC.CollectionCount(0) - gen0 });
                    Console.WriteLine($"TIMING {variant}/{scenario} iteration {iteration}: {elapsed:F2} ms; {allocated} B");
                }
                // End on the edited input before comparing with a fresh run.
                // The extra run is excluded from the performance samples.
                if (scenario != "ColdGeneration") driver = Run(driver.ReplaceAdditionalTexts(modifiedFiles), modifiedCompilation);
            }
            if (!focused)
            {
                var actual = Snapshot(driver, modifiedCompilation, snapshot.ProjectDirectory);
                var fresh = Snapshot(Run(NewDriver().ReplaceAdditionalTexts(modifiedFiles), modifiedCompilation), modifiedCompilation, snapshot.ProjectDirectory);
                if (actual != fresh) throw new InvalidOperationException(variant + ": incremental/fresh mismatch for " + scenario);
                File.WriteAllText(Path.Combine(output, variant + "-" + scenario + "-parity.json"), actual);
            }
            Console.WriteLine($"DONE {variant}/{scenario}");
            // Save after every scenario so a later failure retains completed work.
            // The runner treats a process as successful only after its exit code is zero.
            Write(Path.Combine(output, variant + "-results.json"), new
            {
                Variant = variant, Environment.ProcessId, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                GCSettings.IsServerGC, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                Generator = Fingerprint(typeof(SmallDictionary<,>).Assembly), Benchmark = Fingerprint(typeof(FeatureGallerySmallDictionaryComparison).Assembly),
                BlackSilence = Fingerprint(typeof(AkburaBlackSilenceGenerator).Assembly),
                Inputs = project.CreateSnapshot().AdditionalTexts.Select(file => new { Name = Path.GetFileName(file.Path), Hash = Hash(file.GetText()!.ToString()) }),
                References = project.Compilation.References.Select(reference => reference.Display).Order(StringComparer.Ordinal),
                Measurements = measurements, Profiles = profiles, Micro = micro,
            });
        }
    }

    private static string DescribeKey(object key) => key is Akbura.Language.Syntax.AkburaSyntax syntax
        ? $"{syntax.GetType().Name}@{syntax.Position}:{syntax}" : key.ToString()!;

    private static object Micro<TKey, TValue>(string category, MapTrace[] maps, int round) where TKey : notnull
    {
        var comparer = (IEqualityComparer<TKey>)maps[0].Comparer;
        var traces = maps.Select(map => new Trace<TKey, TValue>(map.Operations.Select(op =>
            new Operation<TKey, TValue>(op.Kind, (TKey)op.Key, op.Value == null ? default! : (TValue)op.Value)).ToArray(), map.Keys.Count)).ToArray();
        var results = new List<object>();
        foreach (var bucket in new[] { "All", "0", "1", "2", "3-4", "5-8", ">8" })
        {
            var selected = traces.Where(trace => bucket == "All" || Bucket(trace.Count) == bucket).ToArray();
            if (selected.Length == 0) continue;
            var order = new[] { "Dictionary", "Capacity3", "SmallDictionary" };
            if (round % 2 != 0) Array.Reverse(order);
            foreach (var variant in order)
            {
                var result = variant == "SmallDictionary"
                    ? MeasureMaps<TKey, TValue, SmallMap<TKey, TValue>>(selected, comparer, 0)
                    : MeasureMaps<TKey, TValue, StandardMap<TKey, TValue>>(selected, comparer, variant == "Capacity3" ? 3 : 0);
                results.Add(new { Bucket = bucket, Variant = variant, Maps = selected.Length, Result = result });
            }
        }
        Console.WriteLine($"MICRO {category}: actual keys, values, hit/miss/add/update sequences validated and measured.");
        return new { Category = category, Results = results };
    }

    private static object MeasureMaps<TKey, TValue, TMap>(Trace<TKey, TValue>[] traces, IEqualityComparer<TKey> comparer, int capacity)
        where TKey : notnull where TMap : struct, IMap<TKey, TValue, TMap>
    {
        TMap Build(Trace<TKey, TValue> trace, bool verify)
        {
            var map = TMap.Create(comparer, capacity);
            foreach (var op in trace.Operations)
            {
                if (op.Kind is "Hit" or "Miss")
                {
                    var hit = map.TryGetValue(op.Key, out var value);
                    s_sink += hit ? 1 : 0;
                    if (verify && (hit != (op.Kind == "Hit") || hit && !EqualityComparer<TValue>.Default.Equals(value, op.Value)))
                        throw new InvalidOperationException("Trace replay changed a lookup result.");
                }
                else if (op.Kind == "Add") map.Add(op.Key, op.Value);
                else map.Set(op.Key, op.Value);
            }
            s_mapSink = map.Instance;
            return map;
        }
        var built = traces.Select(trace => Build(trace, true)).ToArray();
        var lookups = traces.Select(trace => trace.Operations.Where(op => op.Kind is "Hit" or "Miss").ToArray()).ToArray();
        var lookupCount = lookups.Sum(ops => ops.Length);
        var construction = Measure(() => { foreach (var trace in traces) Build(trace, false); }, traces.Length);
        object? reads = lookupCount == 0 ? null : Measure(() =>
        {
            var checksum = 0;
            for (var i = 0; i < built.Length; i++)
                foreach (var op in lookups[i]) checksum += built[i].TryGetValue(op.Key, out _) ? 1 : 0;
            s_sink = checksum;
        }, lookupCount);
        return new { ConstructionPerMap = construction, RepeatedLookupPerOperation = reads };
    }

    private static object Measure(Action action, int units)
    {
        var repetitions = 1;
        while (true)
        {
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < repetitions; i++) action();
            if (Stopwatch.GetElapsedTime(start).TotalMilliseconds >= 40 || repetitions >= 131072) break;
            repetitions *= 2;
        }
        var samples = new List<object>();
        for (var sample = 0; sample < 9; sample++)
        {
            Collect();
            var bytes = GC.GetAllocatedBytesForCurrentThread();
            var start = Stopwatch.GetTimestamp();
            for (var i = 0; i < repetitions; i++) action();
            var elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds;
            samples.Add(new { Nanoseconds = elapsed / (repetitions * (double)units),
                Bytes = (GC.GetAllocatedBytesForCurrentThread() - bytes) / (repetitions * (double)units) });
        }
        return new { Repetitions = repetitions, Units = units, Samples = samples };
    }

    private static string Bucket(int count) => count switch { 0 => "0", 1 => "1", 2 => "2", <= 4 => "3-4", <= 8 => "5-8", _ => ">8" };
    private interface IMap<TKey, TValue, TSelf> where TKey : notnull where TSelf : IMap<TKey, TValue, TSelf>
    {
        static abstract TSelf Create(IEqualityComparer<TKey> comparer, int capacity);
        object Instance { get; }
        bool TryGetValue(TKey key, out TValue value);
        void Add(TKey key, TValue value);
        void Set(TKey key, TValue value);
    }
    private readonly struct StandardMap<TKey, TValue>(Dictionary<TKey, TValue> map) : IMap<TKey, TValue, StandardMap<TKey, TValue>> where TKey : notnull
    {
        public static StandardMap<TKey, TValue> Create(IEqualityComparer<TKey> comparer, int capacity) => new(new Dictionary<TKey, TValue>(capacity, comparer));
        public object Instance => map;
        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value!);
        public void Add(TKey key, TValue value) => map.Add(key, value);
        public void Set(TKey key, TValue value) => map[key] = value;
    }
    private readonly struct SmallMap<TKey, TValue>(SmallDictionary<TKey, TValue> map) : IMap<TKey, TValue, SmallMap<TKey, TValue>> where TKey : notnull
    {
        public static SmallMap<TKey, TValue> Create(IEqualityComparer<TKey> comparer, int capacity) => new(new SmallDictionary<TKey, TValue>(comparer));
        public object Instance => map;
        public bool TryGetValue(TKey key, out TValue value) => map.TryGetValue(key, out value!);
        public void Add(TKey key, TValue value) => map.Add(key, value);
        public void Set(TKey key, TValue value) => map[key] = value;
    }
    private sealed record Trace<TKey, TValue>(Operation<TKey, TValue>[] Operations, int Count);
    private sealed record Operation<TKey, TValue>(string Kind, TKey Key, TValue Value);
    private sealed record RawOperation(string Kind, object Key, object? Value);
    private sealed class MapTrace(string category, Type keyType, Type valueType, object comparer)
    {
        public string Category { get; } = category;
        public Type KeyType { get; } = keyType;
        public Type ValueType { get; } = valueType;
        public object Comparer { get; } = comparer;
        public HashSet<object> Keys { get; } = [];
        public List<RawOperation> Operations { get; } = [];
        public HashSet<int> Threads { get; } = [];
    }
    private sealed class Capture
    {
        private readonly Dictionary<object, MapTrace> _maps = new(ReferenceEqualityComparer.Instance);
        public MapTrace[] Maps => _maps.Values.ToArray();
        public void Record(object dictionary, string kind, object? key, object? value)
        {
            lock (_maps)
            {
                if (kind == "Create")
                {
                    var types = dictionary.GetType().GenericTypeArguments;
                    _maps.Add(dictionary, new MapTrace((string)key!, types[0], types[1], value!));
                }
                else
                {
                    var map = _maps[dictionary];
                    map.Operations.Add(new(kind, key!, value));
                    if (kind is "Add" or "Set") map.Keys.Add(key!);
                }
                _maps[dictionary].Threads.Add(Environment.CurrentManagedThreadId);
            }
        }
    }
    private static GeneratorDriver Run(GeneratorDriver driver, CSharpCompilation compilation)
    {
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results.Single();
        if (result.Exception != null) throw new InvalidOperationException("Generation failed.", result.Exception);
        if (result.GeneratedSources.IsEmpty || result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("Empty or invalid generated output: " + string.Join("; ", result.Diagnostics));
        return driver;
    }
    private static string Snapshot(GeneratorDriver driver, CSharpCompilation compilation, string root)
    {
        var result = driver.GetRunResult().Results.Single();
        var output = compilation.AddSyntaxTrees(result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SyntaxTree));
        FeatureGalleryStageMeasurements.ValidateCompilation(output);
        string Normalize(string text) => text.Replace(root, "<project>", StringComparison.Ordinal)
            .Replace(root.Replace('\\', '/'), "<project>", StringComparison.Ordinal)
            .Replace(JsonSerializer.Serialize(root)[1..^1], "<project>", StringComparison.Ordinal);
        return JsonSerializer.Serialize(new
        {
            Sources = result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => new { source.HintName, Text = Normalize(source.SourceText.ToString()) }),
            Diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, false).Select(Normalize),
            DriverDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", driver.GetRunResult().Diagnostics, false).Select(Normalize),
            // Roslyn enumerates unused-using diagnostics in parallel.
            // Preserve generator order and canonicalize only compiler diagnostics.
            CompilerDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("compiler", output.GetDiagnostics(), false).Select(Normalize).Order(StringComparer.Ordinal),
        }, s_json);
    }
    private sealed class VariantContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name is "Akbura.Benchmarks" or "Akbura.Generator" or "Akbura.BlackSilence"
            ? LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : Default.LoadFromAssemblyName(name);
    }
    private static object Fingerprint(Assembly assembly) => new { assembly.Location, Mvid = assembly.ManifestModule.ModuleVersionId,
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Write(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, s_json));
        File.Move(path + ".tmp", path, true);
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
}
