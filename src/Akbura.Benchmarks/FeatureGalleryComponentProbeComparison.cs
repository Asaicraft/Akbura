using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Binder;
using Akbura.Language.CodeGeneration;
using Akbura.Language.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CSharp = Microsoft.CodeAnalysis.CSharp.Syntax;
using SyntaxFactory = Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace Akbura.Benchmarks;

/// <summary>
/// Compares separately compiled production methods over one loaded gallery.
/// Profiling assemblies are never used for performance measurements.
/// </summary>
internal static class FeatureGalleryComponentProbeComparison
{
    internal static readonly string[] Names =
    [
        "__AkburaStatementProbe", "__AkburaTypeProbe", "__AkburaReturnTypeProbe",
        "__AkburaDeclarationNameProbe", "__AkburaUsingProbe", "__AkburaCommandProbe",
        "__AkburaProbe", "__AkburaComponentMethodProbe", "__AkburaUseHookProbe",
        "__AkburaUseHookCompletionProbe", "__AkburaSemanticProbe",
    ];

    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static readonly FieldInfo s_cache = typeof(CSharpProbeContext).GetField(
        "_componentTemplates", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo? s_trace = typeof(CSharpProbeContext).GetField(
        "TemplateTrace", BindingFlags.Static | BindingFlags.NonPublic);

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--component-probe-cache") return false;
        if (args.Contains("--help", StringComparer.Ordinal))
        {
            Console.WriteLine("--component-probe-cache --variants DIR --profile-variants DIR --output DIR [--warmups 1] [--iterations 3] [--round 0]");
            Console.WriteLine("Build variants with Compare-ComponentProbeCache.ps1; profiling and timing are separate runs.");
            return true;
        }
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0
            ? args[index + 1] : fallback;
        var variants = Path.GetFullPath(Option("--variants", "artifacts/component-probe-cache/variants"));
        var output = Path.GetFullPath(Option("--output", "artifacts/component-probe-cache/results"));
        var warmups = int.Parse(Option("--warmups", "1"));
        var iterations = int.Parse(Option("--iterations", "3"));
        var round = int.Parse(Option("--round", "0"));
        var profile = args.Contains("--profile", StringComparer.Ordinal);
        var focusedOnly = args.Contains("--focused-only", StringComparer.Ordinal);
        var scenario = Option("--scenario", "");
        var profileVariants = Option("--profile-variants", "");
        if (warmups < 1 || iterations < 2) throw new ArgumentException("Use at least one warmup and two measurements.");
        Directory.CreateDirectory(output);
        Console.WriteLine("Loading real FeatureGallery (excluded from measurements)...");
        var project = FeatureGalleryBenchmarkProject.Load();
        if (profileVariants.Length != 0)
        {
            foreach (var variant in new[] { "Cache16", "Dictionary", "Cache32", "Cache64", "Cache128" })
            {
                RunIsolated(project.ExportComparisonInputs(), Path.Combine(Path.GetFullPath(profileVariants), variant), variant, output, warmups, iterations, true, focusedOnly, scenario);
                Collect();
            }
        }
        var order = new[] { "Dictionary", "Cache16", "Cache32", "Cache64", "Cache128" };
        if (profile) order = ["Cache16", "Dictionary", "Cache32", "Cache64", "Cache128"];
        else order = Enumerable.Range(0, order.Length).Select(i => order[(i + round) % order.Length]).ToArray();
        if (!profile && args.Contains("--reverse", StringComparer.Ordinal)) Array.Reverse(order);
        var onlyVariant = Option("--variant", "");
        if (onlyVariant.Length != 0) order = order.Where(variant => variant == onlyVariant).ToArray();
        foreach (var variant in order)
        {
            RunIsolated(project.ExportComparisonInputs(), Path.Combine(variants, variant), variant, output, warmups, iterations, profile, focusedOnly, scenario);
            Collect();
        }
        if (!profile && onlyVariant.Length == 0) VerifyComparison(output);
        return true;
    }

    private static void VerifyComparison(string output)
    {
        using var baseline = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, "Cache16-timing.json")));
        foreach (var variant in new[] { "Dictionary", "Cache16", "Cache32", "Cache64", "Cache128" })
        {
            using var current = JsonDocument.Parse(File.ReadAllText(Path.Combine(output, variant + "-timing.json")));
            var root = current.RootElement;
            foreach (var name in new[] { "ProcessId", "Inputs", "References", "Buckets" })
                if (root.GetProperty(name).GetRawText() != baseline.RootElement.GetProperty(name).GetRawText())
                    throw new InvalidOperationException(variant + ": comparison input mismatch: " + name);
            foreach (var name in new[] { "BlackSilence", "Benchmark" })
                if (root.GetProperty(name).GetProperty("Sha256").GetString() != baseline.RootElement.GetProperty(name).GetProperty("Sha256").GetString())
                    throw new InvalidOperationException(variant + ": wrong assembly: " + name);
            if (root.GetProperty("Focused").GetProperty("OutputFingerprint").GetString() != baseline.RootElement.GetProperty("Focused").GetProperty("OutputFingerprint").GetString())
                throw new InvalidOperationException(variant + ": focused syntax differs from starting Cache16.");
            var profilePath = Path.Combine(output, variant + "-profile.json");
            if (File.Exists(profilePath))
            {
                using var counters = JsonDocument.Parse(File.ReadAllText(profilePath));
                if (counters.RootElement.GetProperty("ProcessId").GetInt32() != root.GetProperty("ProcessId").GetInt32() ||
                    counters.RootElement.GetProperty("Buckets").GetRawText() != root.GetProperty("Buckets").GetRawText())
                    throw new InvalidOperationException(variant + ": stale profiling data.");
                foreach (var result in root.GetProperty("Focused").GetProperty("Results").EnumerateArray())
                {
                    var actual = counters.RootElement.GetProperty("Focused").GetProperty("Results").EnumerateArray()
                        .Single(value => value.GetProperty("Name").GetString() == result.GetProperty("Name").GetString() &&
                            value.GetProperty("Cold").GetBoolean() == result.GetProperty("Cold").GetBoolean());
                    foreach (var metric in new[] { "FactoryCalls", "RepeatedFactoryCalls" })
                        if (actual.GetProperty(metric).GetInt32() != result.GetProperty("Simulation").GetProperty(metric).GetInt32())
                            throw new InvalidOperationException(variant + ": factory model mismatch: " + metric);
                }
            }
            foreach (var file in Directory.EnumerateFiles(output, "Cache16-*-parity.json"))
                if (!File.ReadAllBytes(file).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, variant + Path.GetFileName(file)[7..]))))
                    throw new InvalidOperationException(variant + ": FeatureGallery output differs from starting Cache16.");
        }
        Console.WriteLine("Verified five variants: identical inputs, shared hash seed, exact source/diagnostic and focused syntax parity, factory counters.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void RunIsolated(object project, string directory, string variant, string output, int warmups, int iterations, bool profile, bool focusedOnly, string scenario)
    {
        var context = new VariantLoadContext(directory);
        try
        {
            var assembly = context.LoadFromAssemblyPath(Path.Combine(directory, "Akbura.Benchmarks.dll"));
            var type = assembly.GetType(typeof(FeatureGalleryComponentProbeComparison).FullName!, throwOnError: true)!;
            type.GetMethod(nameof(RunVariant), BindingFlags.Static | BindingFlags.Public)!
                .Invoke(null, [project, variant, output, warmups, iterations, profile, focusedOnly, scenario]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
        finally
        {
            context.Unload();
        }
    }

    public static void RunVariant(object sharedProject, string variant, string output, int warmups, int iterations, bool profile, bool focusedOnly, string scenario)
    {
        if ((s_trace != null) != profile) throw new InvalidOperationException("Profiling/timing assembly mismatch.");
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs((object[])sharedProject);
        Console.WriteLine($"{variant}: {(profile ? "profile" : "uninstrumented Release")}; generator {typeof(CSharpProbeContext).Assembly.ManifestModule.ModuleVersionId}");
        var samples = new List<object>();
        var traces = new List<object>();
        var snapshot = project.CreateSnapshot();
        var cases = new List<Workload>
        {
            new("ColdGeneration", project.Compilation, project.Compilation, snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot),
            new("NoChanges", project.Compilation, project.Compilation, snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot),
            new("ComponentWhitespaceEdit", project.Compilation, project.Compilation, snapshot.AdditionalTexts,
                snapshot.AdditionalTexts.Replace(snapshot.ComponentOriginal, snapshot.ComponentModified), snapshot),
            new("CSharpCompilationEdit", project.Compilation, project.CreateCSharpEditCompilation(), snapshot.AdditionalTexts, snapshot.AdditionalTexts, snapshot),
        };
        foreach (var editScenario in new[] { FeatureGalleryIncrementalScenario.ComponentValueEdit,
            FeatureGalleryIncrementalScenario.ComponentBindingEdit, FeatureGalleryIncrementalScenario.ComponentContractEdit,
            FeatureGalleryIncrementalScenario.ReferencedCSharpRename })
        {
            var inputs = FeatureGalleryIncrementalScenarioFactory.Create(project, editScenario);
            cases.Add(new(editScenario.ToString(), inputs.OriginalCompilation, inputs.ModifiedCompilation,
                inputs.OriginalFiles, inputs.ModifiedFiles, inputs.Snapshot));
        }
        foreach (var workload in focusedOnly ? [] : cases.Where(workload => scenario.Length == 0 || workload.Name == scenario))
        {
            if (profile)
            {
                var driver = Driver(project, workload, workload.OriginalFiles);
                if (workload.Name != "ColdGeneration") driver = Run(driver, workload.OriginalCompilation);
                var capture = new TraceCapture(workload.Snapshot.ProjectDirectory);
                s_trace!.SetValue(null, new Action<object, object, string, bool, object, object>(capture.Record));
                try { driver = Run(driver.ReplaceAdditionalTexts(workload.ModifiedFiles), workload.ModifiedCompilation); }
                finally { s_trace.SetValue(null, null); }
                traces.Add(new { workload.Name, Contexts = capture.Summaries() });
                if (workload.Name == "ColdGeneration" && variant == "Cache16")
                    Write(Path.Combine(output, "focused-input.json"), capture.Representative());
            }
            else
            {
                var cold = workload.Name == "ColdGeneration";
                var driver = Driver(project, workload, workload.OriginalFiles);
                if (!cold) driver = Run(driver, workload.OriginalCompilation);
                var verified = false;
                for (var iteration = -warmups; iteration < iterations; iteration++)
                {
                    var forward = cold || (iteration + warmups) % 2 == 0;
                    var currentCompilation = forward ? workload.ModifiedCompilation : workload.OriginalCompilation;
                    if (cold) driver = Driver(project, workload, workload.OriginalFiles);
                    driver = driver.ReplaceAdditionalTexts(forward ? workload.ModifiedFiles : workload.OriginalFiles);
                    Collect();
                    var before = GC.GetTotalAllocatedBytes(true);
                    var gen0 = GC.CollectionCount(0);
                    var started = Stopwatch.GetTimestamp();
                    driver = Run(driver, currentCompilation);
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    var allocated = GC.GetTotalAllocatedBytes(true) - before;
                    var collections = GC.CollectionCount(0) - gen0;
                    if (iteration >= 0) samples.Add(new { workload.Name, Iteration = iteration, Forward = forward, Milliseconds = elapsed, AllocatedBytes = allocated, Gen0 = collections });
                    if (iteration >= 0 && forward && !verified)
                    {
                        VerifyAndSave(project, workload, driver, output, variant);
                        verified = true;
                    }
                }
            }
            Console.WriteLine($"  {variant}: {workload.Name} complete");
        }
        var cancelledDriver = Driver(project, cases[0], cases[0].OriginalFiles);
        using (var cancellation = new CancellationTokenSource())
        {
            cancellation.Cancel();
            try
            {
                cancelledDriver.RunGenerators(project.Compilation, cancellation.Token);
                throw new InvalidOperationException("A cancelled driver completed successfully.");
            }
            catch (OperationCanceledException) { }
        }
        var focused = Focused(project, output, warmups, iterations, profile);
        Write(Path.Combine(output, variant + (profile ? "-profile.json" : "-timing.json")), new
        {
            Variant = variant, Profile = profile, FocusedOnly = focusedOnly, Environment.ProcessId, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            GCSettings.IsServerGC, Warmups = warmups, Iterations = iterations,
            Generator = Fingerprint(typeof(CSharpProbeContext).Assembly), BlackSilence = Fingerprint(typeof(AkburaBlackSilenceGenerator).Assembly),
            Benchmark = Fingerprint(typeof(FeatureGalleryComponentProbeComparison).Assembly),
            Inputs = snapshot.AdditionalTexts.Select(file => new { Path = Path.GetRelativePath(snapshot.ProjectDirectory, file.Path), Hash = Hash(file.GetText()!.ToString()) }),
            References = project.Compilation.References.Select(reference => reference.Display).Order(StringComparer.Ordinal),
            Buckets = Names.Select(name => new { Name = name, Hash = StringComparer.Ordinal.GetHashCode(name),
                Cache16 = StringComparer.Ordinal.GetHashCode(name) & 15, Cache32 = StringComparer.Ordinal.GetHashCode(name) & 31,
                Cache64 = StringComparer.Ordinal.GetHashCode(name) & 63, Cache128 = StringComparer.Ordinal.GetHashCode(name) & 127 }),
            CancellationVerified = true, Samples = samples, Traces = traces, Focused = focused,
        });
    }

    private static object Focused(FeatureGalleryBenchmarkProject project, string output, int warmups, int iterations, bool profile)
    {
        var input = JsonSerializer.Deserialize<FocusedInput>(File.ReadAllText(Path.Combine(output, "focused-input.json")))!;
        var snapshot = project.CreateSnapshot();
        var trees = snapshot.AdditionalTexts.Select(file => Path.GetExtension(file.Path) == ".akcss"
            ? (AkburaSyntaxTree)AkcssSyntaxTree.ParseText(file.GetText()!, file.Path,
                AkcssGeneratedModuleNames.GetMetadataName(project.RootNamespace, Path.GetRelativePath(snapshot.ProjectDirectory, file.Path).Replace('\\', '/')))
            : ComponentSyntaxTree.ParseText(file.GetText()!, file.Path)).ToImmutableArray();
        var compilation = new AkburaCompilation(project.Compilation, trees.Where(tree => tree is ComponentSyntaxTree).ToImmutableArray(),
            trees.OfType<AkcssSyntaxTree>().ToImmutableArray(), project.RootNamespace, snapshot.ProjectDirectory);
        var tree = trees.Single(tree => Path.GetRelativePath(snapshot.ProjectDirectory, tree.FilePath).Replace('\\', '/') == input.Path);
        var model = compilation.GetSemanticModel(tree);
        var binder = model.BindingSession.GetCSharpProbeBinder(tree.GetRoot().Members[0]);
        var context = model.BindingSession.ProbeContext;
        var requests = input.Requests.Select(request => new ProbeRequest(request.Name,
            request.Members.Select(member => SyntaxFactory.ParseMemberDeclaration(member)!).ToImmutableArray(),
            request.Usings.Select(text => SyntaxFactory.ParseCompilationUnit(text).Usings.Single()).ToImmutableArray())).ToArray();
        var first = requests[0];
        var frequent = requests.GroupBy(request => request.Name).OrderByDescending(group => group.Count()).Select(group => group.Key).ToArray();
        ProbeRequest[] WithNames(IEnumerable<string> names) => names.Select(name => first with { Name = name }).ToArray();
        var distributions = new Dictionary<string, ProbeRequest[]>(StringComparer.Ordinal)
        {
            ["Single"] = WithNames(frequent.Take(1)), ["TopFour"] = WithNames(frequent.Take(4)),
            ["ObservedNames"] = WithNames(frequent), ["ObservedOrder"] = requests,
            ["VerifiedRoundRobin"] = WithNames(Names),
            ["Skewed"] = WithNames(Enumerable.Repeat(frequent[0], 27).Concat(Names.Where(name => name != frequent[0]))),
            ["ForcedCollision"] = WithNames(Names),
        };
        if (profile) return ProfileFocused(context, Build, distributions);
        var results = new List<object>();
        var expected = new Dictionary<ProbeRequest, string>();
        foreach (var request in distributions.Values.SelectMany(value => value).Distinct())
            expected.Add(request, Build(request).ToFullString());
        foreach (var (name, sequence) in distributions)
        {
            var force = name == "ForcedCollision";
            foreach (var cold in new[] { true, false })
            {
                var count = cold ? 64 : Math.Max(256, 16384 / sequence.Length);
                var data = new List<object>();
                for (var round = -warmups; round < iterations; round++)
                {
                    if (!cold) { ResetCache(context, force); foreach (var request in sequence) Build(request); }
                    Collect();
                    var before = GC.GetTotalAllocatedBytes(true);
                    var gen0 = GC.CollectionCount(0);
                    var started = Stopwatch.GetTimestamp();
                    long checksum = 0;
                    for (var repeat = 0; repeat < count; repeat++)
                    {
                        if (cold) ResetCache(context, force);
                        foreach (var request in sequence) checksum += Build(request).FullSpan.Length;
                    }
                    var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
                    var allocated = GC.GetTotalAllocatedBytes(true) - before;
                    if (checksum == 0) throw new InvalidOperationException("No probe work.");
                    if (round >= 0) data.Add(new { NanosecondsPerOperation = elapsed / (count * sequence.Length),
                        BytesPerOperation = (double)allocated / (count * sequence.Length), Gen0 = GC.CollectionCount(0) - gen0 });
                }
                ResetCache(context, force);
                foreach (var request in sequence)
                    if (Build(request).ToFullString() != expected[request]) throw new InvalidOperationException("Probe output changed.");
                var simulation = Simulate(sequence.Select(request => request.Name).ToArray(), count, cold, force);
                results.Add(new { Name = name, Cold = cold, Operations = count * sequence.Length, Samples = data, Simulation = simulation });
            }
        }
        var parallel = new List<object>();
        foreach (var force in new[] { false, true })
        {
            for (var round = -warmups; round < iterations; round++)
            {
                ResetCache(context, force);
                Collect();
                var before = GC.GetTotalAllocatedBytes(true);
                var gen0 = GC.CollectionCount(0);
                var started = Stopwatch.GetTimestamp();
                Parallel.For(0, 8192, index =>
                {
                    var request = requests[index % requests.Length];
                    if (Build(request).ToFullString() != expected[request]) throw new InvalidOperationException("Parallel probe output changed.");
                });
                var elapsed = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
                if (round >= 0) parallel.Add(new { ForceCollision = force, NanosecondsPerOperation = elapsed / 8192,
                    BytesPerOperation = (GC.GetTotalAllocatedBytes(true) - before) / 8192d, Gen0 = GC.CollectionCount(0) - gen0 });
            }
        }
        var retained = new List<object>();
        foreach (var names in new[] { frequent, Names })
        {
            var empty = Enumerable.Range(0, 128).Select(_ => NewCache(false)).ToArray();
            s_cache.SetValue(context, empty[0]);
            Collect();
            var before = GC.GetTotalMemory(true);
            var caches = Enumerable.Range(0, 128).Select(_ => NewCache(false)).ToArray();
            foreach (var cache in caches)
            {
                s_cache.SetValue(context, cache);
                foreach (var request in WithNames(names)) Build(request);
            }
            Collect();
            var bytes = GC.GetTotalMemory(true) - before;
            retained.Add(new { Names = names, BytesPerPopulatedCache = bytes / 128d,
                EffectiveEntries = (int)caches[0].GetType().GetProperty("Count")!.GetValue(caches[0])! });
            GC.KeepAlive(caches);
            GC.KeepAlive(empty);
        }
        return new { input.Path, RequestCount = requests.Length, Names = frequent,
            OutputFingerprint = Hash(string.Join("\0", expected.Values)), Results = results, Parallel = parallel, Retained = retained };

        CSharp.CompilationUnitSyntax Build(ProbeRequest request) => context.CreateComponentCompilationUnit(binder, request.Members, request.Name, request.Usings);
    }

    private static object ProfileFocused(CSharpProbeContext context, Func<ProbeRequest, CSharp.CompilationUnitSyntax> build,
        Dictionary<string, ProbeRequest[]> distributions)
    {
        var results = new List<object>();
        foreach (var (name, sequence) in distributions)
        {
            foreach (var cold in new[] { true, false })
            {
                var capture = new ProbeCounters();
                var count = cold ? 64 : Math.Max(256, 16384 / sequence.Length);
                ResetCache(context, name == "ForcedCollision");
                if (!cold)
                {
                    foreach (var request in sequence) build(request);
                    capture.Seen.UnionWith(sequence.Select(request => request.Name));
                }
                s_trace!.SetValue(null, new Action<object, object, string, bool, object, object>(capture.Record));
                try
                {
                    for (var repeat = 0; repeat < count; repeat++)
                    {
                        if (cold) { ResetCache(context, name == "ForcedCollision"); capture.Seen.Clear(); }
                        foreach (var request in sequence) build(request);
                    }
                }
                finally { s_trace.SetValue(null, null); }
                results.Add(new { Name = name, Cold = cold, capture.Requests, FactoryCalls = capture.Misses,
                    RepeatedFactoryCalls = capture.Repeated, HitRatio = 1d - (double)capture.Misses / capture.Requests });
            }
        }
        var parallel = new List<object>();
        foreach (var force in new[] { false, true })
        {
            ResetCache(context, force);
            var capture = new ProbeCounters();
            s_trace!.SetValue(null, new Action<object, object, string, bool, object, object>(capture.Record));
            try { Parallel.For(0, 8192, index => build(distributions["ObservedOrder"][index % distributions["ObservedOrder"].Length])); }
            finally { s_trace.SetValue(null, null); }
            parallel.Add(new { ForceCollision = force, capture.Requests, FactoryCalls = capture.Misses,
                RepeatedFactoryCalls = capture.Repeated, HitRatio = 1d - (double)capture.Misses / capture.Requests });
        }
        return new { Results = results, Parallel = parallel };
    }

    private sealed class ProbeCounters
    {
        public HashSet<string> Seen { get; } = new(StringComparer.Ordinal);
        public int Requests; public int Misses; public int Repeated;
        public void Record(object context, object model, string name, bool hit, object members, object usings)
        {
            lock (Seen)
            {
                Requests++;
                if (!hit) { Misses++; if (!Seen.Add(name)) Repeated++; }
            }
        }
    }

    private static object Simulate(string[] sequence, int repeats, bool cold, bool force)
    {
        var dictionary = s_cache.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>);
        var cache = NewCache(force);
        var size = dictionary ? 0 : (int)cache.GetType().BaseType!.GetField("mask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(cache)! + 1;
        var slots = new Dictionary<int, string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var unique = new HashSet<string>(StringComparer.Ordinal);
        var misses = 0; var rebuilds = 0; var evictions = 0;
        void Visit(string name, bool measure)
        {
            var slot = dictionary ? Array.IndexOf(Names, name) : (force ? 0 : StringComparer.Ordinal.GetHashCode(name) & (size - 1));
            if (slots.TryGetValue(slot, out var previous) && previous == name) return;
            if (measure)
            {
                misses++;
                if (!seen.Add(name)) rebuilds++;
                if (previous != null) evictions++;
            }
            slots[slot] = name;
            unique.Add(name);
        }
        if (!cold) foreach (var name in sequence) Visit(name, false);
        if (!cold) seen.UnionWith(unique);
        for (var repeat = 0; repeat < repeats; repeat++)
        {
            if (cold) { slots.Clear(); seen.Clear(); }
            foreach (var name in sequence) Visit(name, true);
        }
        var operations = repeats * sequence.Length;
        return new { Operations = operations, FactoryCalls = misses, RepeatedFactoryCalls = rebuilds,
            Evictions = evictions, HitRatio = 1d - (double)misses / operations, EffectiveEntries = slots.Count };
    }

    private static object NewCache(bool force)
    {
        var comparer = force ? (IEqualityComparer<string>)new ConstantHashComparer() : StringComparer.Ordinal;
        if (s_cache.FieldType.GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>))
            return Activator.CreateInstance(s_cache.FieldType, comparer)!;
        var original = s_cache.GetValue(s_currentContext ?? throw new InvalidOperationException("No cache fixture."))!;
        var mask = (int)original.GetType().BaseType!.GetField("mask", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(original)!;
        return Activator.CreateInstance(s_cache.FieldType, mask + 1, comparer)!;
    }

    private static object? s_currentContext;
    private static void ResetCache(CSharpProbeContext context, bool force)
    {
        s_currentContext = context;
        s_cache.SetValue(context, NewCache(force));
    }

    private static GeneratorDriver Driver(FeatureGalleryBenchmarkProject project, Workload workload, ImmutableArray<AdditionalText> files) =>
        CSharpGeneratorDriver.Create(generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: files, parseOptions: project.ParseOptions,
            optionsProvider: new DiagnosticBenchmarkOptionsProvider(workload.Snapshot.OptionsProvider, "Publish"));

    private static GeneratorDriver Run(GeneratorDriver driver, CSharpCompilation compilation)
    {
        driver = driver.RunGenerators(compilation);
        var result = driver.GetRunResult().Results.Single();
        if (result.Exception != null) throw new InvalidOperationException("FeatureGallery generation failed.", result.Exception);
        if (result.GeneratedSources.IsEmpty || result.Diagnostics.Any(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error))
            throw new InvalidOperationException("FeatureGallery generated empty or invalid output: " + string.Join("; ", result.Diagnostics));
        return driver;
    }

    private static void VerifyAndSave(FeatureGalleryBenchmarkProject project, Workload workload, GeneratorDriver driver, string output, string variant)
    {
        var fresh = Run(Driver(project, workload, workload.ModifiedFiles), workload.ModifiedCompilation);
        var actual = Snapshot(driver, workload.ModifiedCompilation, workload.Snapshot.ProjectDirectory);
        var expected = Snapshot(fresh, workload.ModifiedCompilation, workload.Snapshot.ProjectDirectory);
        if (actual != expected)
        {
            File.WriteAllText(Path.Combine(output, variant + "-" + workload.Name + "-actual.json"), actual);
            File.WriteAllText(Path.Combine(output, variant + "-" + workload.Name + "-fresh.json"), expected);
            throw new InvalidOperationException(workload.Name + ": incremental/fresh outputs differ.");
        }
        var file = Path.Combine(output, variant + "-" + workload.Name + "-parity.json");
        File.WriteAllText(file, actual);
    }

    private static string Snapshot(GeneratorDriver driver, CSharpCompilation compilation, string root)
    {
        var result = driver.GetRunResult().Results.Single();
        var output = compilation.AddSyntaxTrees(result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SyntaxTree));
        var diagnostics = output.GetDiagnostics();
        FeatureGalleryStageMeasurements.ValidateCompilation(output);
        string Normalize(string text) => text.Replace(root, "<project>", StringComparison.Ordinal)
            .Replace(root.Replace('\\', '/'), "<project>", StringComparison.Ordinal)
            .Replace(JsonSerializer.Serialize(root)[1..^1], "<project>", StringComparison.Ordinal);
        return JsonSerializer.Serialize(new
        {
            Sources = result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
                .Select(source => new { source.HintName, Text = Normalize(source.SourceText.ToString()) }),
            Diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, ignoreTransport: false).Select(Normalize),
            DriverDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", driver.GetRunResult().Diagnostics, ignoreTransport: false).Select(Normalize),
            // Roslyn's parallel unused-using diagnostics vary in enumeration
            // order even between identical fresh runs; preserve Akbura order above.
            CompilerDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("compiler", diagnostics, ignoreTransport: false).Select(Normalize).Order(StringComparer.Ordinal),
        }, s_json);
    }

    private static object Fingerprint(Assembly assembly) => new { assembly.Location, Mvid = assembly.ManifestModule.ModuleVersionId,
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, s_json));
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    private sealed class VariantLoadContext(string directory) : AssemblyLoadContext(isCollectible: true)
    {
        protected override Assembly? Load(AssemblyName name) => name.Name is "Akbura.Benchmarks" or "Akbura.Generator" or "Akbura.BlackSilence"
            ? LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll"))
            : Default.LoadFromAssemblyName(name);
    }

    private sealed class ConstantHashComparer : IEqualityComparer<string>
    {
        public bool Equals(string? left, string? right) => StringComparer.Ordinal.Equals(left, right);
        public int GetHashCode(string value) => 0;
    }

    private sealed record Workload(string Name, CSharpCompilation OriginalCompilation, CSharpCompilation ModifiedCompilation,
        ImmutableArray<AdditionalText> OriginalFiles, ImmutableArray<AdditionalText> ModifiedFiles, FeatureGalleryBenchmarkSnapshot Snapshot);
    private sealed record ProbeRequest(string Name, ImmutableArray<CSharp.MemberDeclarationSyntax> Members, ImmutableArray<CSharp.UsingDirectiveSyntax> Usings);
    private sealed record RequestInput(string Name, string[] Members, string[] Usings);
    private sealed record FocusedInput(string Path, RequestInput[] Requests);

    private sealed class TraceCapture(string root)
    {
        private readonly Dictionary<object, ContextTrace> _contexts = new(ReferenceEqualityComparer.Instance);
        public void Record(object context, object model, string name, bool hit, object members, object usings)
        {
            lock (_contexts)
            {
                if (!_contexts.TryGetValue(context, out var trace))
                    _contexts.Add(context, trace = new ContextTrace(Path.GetRelativePath(root, ((AkburaSemanticModel)model).SyntaxTree.FilePath).Replace('\\', '/')));
                trace.Threads.Add(Environment.CurrentManagedThreadId);
                trace.Requests.Add(new(name, ((ImmutableArray<CSharp.MemberDeclarationSyntax>)members).Select(node => node.NormalizeWhitespace().ToFullString()).ToArray(),
                    ((ImmutableArray<CSharp.UsingDirectiveSyntax>)usings) is var imports && !imports.IsDefault
                        ? imports.Select(node => node.NormalizeWhitespace().ToFullString()).ToArray() : ((AkburaSemanticModel)model).GetCSharpUsingDirectives().Select(node => node.NormalizeWhitespace().ToFullString()).ToArray()));
                trace.Hits.Add(hit);
            }
        }
        public object[] Summaries() => _contexts.Select(pair => (object)new
        {
            Context = Summary(pair.Key, pair.Value),
            Path = pair.Value.Path, Total = pair.Value.Requests.Count, Distinct = pair.Value.Requests.Select(request => request.Name).Distinct().Count(),
            Hits = pair.Value.Hits.Count(hit => hit), Misses = pair.Value.Hits.Count(hit => !hit),
            Names = pair.Value.Requests.Select((request, index) => (request.Name, Hit: pair.Value.Hits[index])).GroupBy(request => request.Name)
                .Select(group => new { Name = group.Key, Requests = group.Count(), Misses = group.Count(request => !request.Hit) }),
            Order = pair.Value.Requests.Select(request => request.Name),
        }).ToArray();
        private static object Summary(object owner, ContextTrace trace)
        {
            var cache = s_cache.GetValue(owner)!;
            var dictionary = cache.GetType().GetGenericTypeDefinition() == typeof(System.Collections.Concurrent.ConcurrentDictionary<,>);
            var entries = dictionary ? null : (Array?)cache.GetType().BaseType!.GetField("_entries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache);
            var retained = dictionary ? (int)cache.GetType().GetProperty("Count")!.GetValue(cache)! : entries?.Cast<object?>().Count(entry => entry != null) ?? 0;
            var mask = dictionary ? 0 : (int)cache.GetType().BaseType!.GetField("mask", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cache)!;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var slots = new Dictionary<int, string>();
            var repeated = 0; var evictions = 0;
            for (var index = 0; index < trace.Requests.Count; index++)
            {
                var name = trace.Requests[index].Name;
                if (!seen.Add(name) && !trace.Hits[index]) repeated++;
                if (dictionary) continue;
                var slot = StringComparer.Ordinal.GetHashCode(name) & mask;
                if (slots.TryGetValue(slot, out var previous) && previous != name) evictions++;
                slots[slot] = name;
            }
            return new { RetainedEntries = retained, RepeatedMisses = repeated, Threads = trace.Threads.Count,
                SerialReplayEvictions = evictions, DistinctKeyCollisions = dictionary ? 0 : seen.Count - seen.Select(name => StringComparer.Ordinal.GetHashCode(name) & mask).Distinct().Count() };
        }
        public FocusedInput Representative()
        {
            var trace = _contexts.Values.OrderByDescending(value => value.Requests.Select(request => request.Name).Distinct().Count())
                .ThenByDescending(value => value.Requests.Count).First();
            return new(trace.Path, trace.Requests.ToArray());
        }
        private sealed class ContextTrace(string path)
        {
            public string Path { get; } = path;
            public List<RequestInput> Requests { get; } = [];
            public List<bool> Hits { get; } = [];
            public HashSet<int> Threads { get; } = [];
        }
    }
}
