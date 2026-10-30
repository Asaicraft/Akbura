using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using System.Diagnostics;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Runtime;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Akbura.Benchmarks;

/// <summary>
/// Measures cold generator invocations separately from loading and validation.
/// Optional phase gates allow an external EventPipe collector to attach after warmup.
/// </summary>
internal static class FeatureGalleryColdAllocationAudit
{
    private static readonly JsonSerializerOptions s_json = new() { WriteIndented = true };
    private static GeneratorDriver? s_retainedDriver;
#if STATS
    private const bool StatisticsCompiled = true;
#else
    private const bool StatisticsCompiled = false;
#endif

    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--cold-allocation-audit") return false;
        try { Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        return true;
    }

    private static void Run(string[] args)
    {
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0 ? args[index + 1] : fallback;
        var output = Path.GetFullPath(Option("--output", "artifacts/cold-allocation-audit"));
        var mode = Option("--mode", "A");
        var diagnostics = Option("--diagnostics", "Shadow");
        var warmups = int.Parse(Option("--warmups", "1"));
        var iterations = int.Parse(Option("--iterations", "3"));
        var gate = Option("--gate", "");
        if (!new[] { "A", "B", "C", "D", "E" }.Contains(mode, StringComparer.Ordinal) || iterations < 1 || warmups < 1)
            throw new ArgumentException("Invalid mode, warmup or iteration count.");
        if ((mode == "A") == StatisticsCompiled) throw new InvalidOperationException("Wrong STATS assembly for this mode.");
        if (gate.Length != 0 && iterations != 1) throw new ArgumentException("A gated trace captures exactly one invocation.");
        Directory.CreateDirectory(output);
        _ = AllocationAuditEvents.Log;
        Console.WriteLine($"Loading FeatureGallery; mode={mode}, diagnostics={diagnostics}, STATS={StatisticsCompiled}, PID={Environment.ProcessId}.");
        var project = FeatureGalleryBenchmarkProject.Load();
        var snapshot = project.CreateSnapshot();
        var samples = new List<object>();
        var observation = new AllocationAuditObservation();
        var observerType = typeof(AkburaCompilation).Assembly.GetType("Akbura.Language.AllocationAuditHooks");
        var observerField = observerType?.GetField("Record", BindingFlags.Public | BindingFlags.Static);
        observerType?.GetField("GetAllocatedBytes", BindingFlags.Public | BindingFlags.Static)?.SetValue(null, (Func<long>)GC.GetAllocatedBytesForCurrentThread);
        for (var iteration = -warmups; iteration < iterations; iteration++)
        {
            s_retainedDriver = null;
            Collect();
            var heapBefore = GC.GetTotalMemory(false);
            var driver = NewDriver(project, snapshot, diagnostics);
            if (iteration >= 0 && gate.Length != 0)
            {
                Write(gate + ".ready.json", new { Environment.ProcessId, Mode = mode, Diagnostics = diagnostics });
                Console.WriteLine("READY for external allocation tracing: " + gate);
                var started = Stopwatch.GetTimestamp();
                while (!File.Exists(gate + ".go"))
                {
                    if (Stopwatch.GetElapsedTime(started).TotalMinutes > 5) throw new TimeoutException("Trace collector did not release the gate.");
                    Thread.Sleep(100);
                }
            }
            observation.Clear();
            if (iteration >= 0 && observerField != null)
                observerField.SetValue(null, new Action<string, object, object?, object?, object?, long, long>(observation.Record));
            Invocation measured;
            try { measured = Invoke(driver, project.Compilation, mode, diagnostics, iteration); }
            finally { observerField?.SetValue(null, null); }
            driver = null!;
            if (iteration >= 0 && gate.Length != 0) File.WriteAllText(gate + ".done", "generation complete");
            if (iteration < 0)
            {
                Console.WriteLine($"WARMUP {iteration}: {measured.Milliseconds:F2} ms, {measured.AllocatedBytes} B.");
                s_retainedDriver = null;
                continue;
            }
            Collect();
            var retainedWithDriver = GC.GetTotalMemory(false);
            var heapInfoWithDriver = GC.GetGCMemoryInfo();
            var validation = ValidateAndSave(project, snapshot, output, iteration);
            s_retainedDriver = null;
            Collect();
            var retainedAfterRelease = GC.GetTotalMemory(false);
            var observationSnapshot = observation.Snapshot();
            samples.Add(new
            {
                Iteration = iteration, measured.Milliseconds, measured.AllocatedBytes, measured.Gen0, measured.Gen1, measured.Gen2,
                HeapBefore = heapBefore, RetainedWithDriver = retainedWithDriver, RetainedAfterRelease = retainedAfterRelease,
                LastGcHeapBytes = measured.LastGcHeapBytes, HeapWithDriver = heapInfoWithDriver.HeapSizeBytes,
                FragmentedWithDriver = heapInfoWithDriver.FragmentedBytes,
                WholeProcessPeakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64,
                Validation = validation, Statistics = Describe(measured.Statistics), Observation = observationSnapshot,
            });
            Write(Path.Combine(output, "results.json"), new
            {
                Mode = mode, Diagnostics = diagnostics, StatisticsCompiled, ObserverCompiled = observerField != null,
                Environment.ProcessId, Environment.ProcessorCount, Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
                GCSettings.IsServerGC, GCSettings.LatencyMode, TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
                Warmups = warmups, Iterations = iterations,
                Generator = Fingerprint(typeof(AkburaCompilation).Assembly), BlackSilence = Fingerprint(typeof(AkburaBlackSilenceGenerator).Assembly),
                Benchmark = Fingerprint(typeof(FeatureGalleryColdAllocationAudit).Assembly), Roslyn = Fingerprint(typeof(CSharpCompilation).Assembly),
                Inputs = snapshot.AdditionalTexts.Select(file => new { Path = Path.GetRelativePath(snapshot.ProjectDirectory, file.Path), Sha256 = Hash(file.GetText()!.ToString()) }),
                References = project.Compilation.References.Select(reference => reference.Display).Order(StringComparer.Ordinal), Samples = samples,
            });
            Console.WriteLine($"SAMPLE {iteration}: {measured.Milliseconds:F2} ms, {measured.AllocatedBytes} B, GC {measured.Gen0}/{measured.Gen1}/{measured.Gen2}, retained driver/released {retainedWithDriver}/{retainedAfterRelease} B.");
        }
        s_retainedDriver = null;
        Console.WriteLine("COMPLETED " + output);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static Invocation Invoke(GeneratorDriver driver, CSharpCompilation compilation, string mode, string diagnostics, int iteration)
    {
        AllocationAuditEvents.Log.Begin(mode, diagnostics, iteration);
        var gen0 = GC.CollectionCount(0);
        var gen1 = GC.CollectionCount(1);
        var gen2 = GC.CollectionCount(2);
        var bytes = GC.GetTotalAllocatedBytes(true);
        var started = Stopwatch.GetTimestamp();
        GenerationStatisticsSession? session = mode is "C" or "D" or "E"
            ? GenerationStatistics.BeginMeasurement(mode is "D" or "E" ? GC.GetAllocatedBytesForCurrentThread : null, trackOperations: mode == "E") : null;
        try { driver = driver.RunGenerators(compilation); }
        finally { session?.Dispose(); }
        var elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var allocated = GC.GetTotalAllocatedBytes(true) - bytes;
        AllocationAuditEvents.Log.End(mode, diagnostics, iteration);
        s_retainedDriver = driver;
        return new(elapsed, allocated, GC.CollectionCount(0) - gen0, GC.CollectionCount(1) - gen1,
            GC.CollectionCount(2) - gen2, GC.GetGCMemoryInfo().HeapSizeBytes, session?.GetSnapshot());
    }

    private static GeneratorDriver NewDriver(FeatureGalleryBenchmarkProject project, FeatureGalleryBenchmarkSnapshot snapshot, string diagnostics) =>
        CSharpGeneratorDriver.Create(generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()],
            additionalTexts: snapshot.AdditionalTexts, parseOptions: project.ParseOptions,
            optionsProvider: diagnostics == "Default" ? snapshot.OptionsProvider : new DiagnosticBenchmarkOptionsProvider(snapshot.OptionsProvider, diagnostics),
            driverOptions: new GeneratorDriverOptions(IncrementalGeneratorOutputKind.None, trackIncrementalGeneratorSteps: false));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static object ValidateAndSave(FeatureGalleryBenchmarkProject project, FeatureGalleryBenchmarkSnapshot snapshot, string output, int iteration)
    {
        var driver = s_retainedDriver ?? throw new InvalidOperationException("No completed driver.");
        var run = driver.GetRunResult();
        var result = run.Results.Single();
        if (result.Exception != null || result.GeneratedSources.IsEmpty) throw new InvalidOperationException("Invalid generator result.", result.Exception);
        var compilation = project.Compilation.AddSyntaxTrees(result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal).Select(source => source.SyntaxTree));
        var compilerDiagnostics = compilation.GetDiagnostics();
        FeatureGalleryStageMeasurements.ValidateCompilation(compilation);
        string Normalize(string text) => text.Replace(snapshot.ProjectDirectory, "<project>", StringComparison.Ordinal)
            .Replace(snapshot.ProjectDirectory.Replace('\\', '/'), "<project>", StringComparison.Ordinal)
            .Replace(JsonSerializer.Serialize(snapshot.ProjectDirectory)[1..^1], "<project>", StringComparison.Ordinal);
        var sources = result.GeneratedSources.OrderBy(source => source.HintName, StringComparer.Ordinal)
            .Select(source => new { source.HintName, Text = Normalize(source.SourceText.ToString()) }).ToArray();
        var diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("generator", result.Diagnostics, false).Select(Normalize).ToArray();
        var driverDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("driver", run.Diagnostics, false).Select(Normalize).ToArray();
        var roslynDiagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("compiler", compilerDiagnostics, false).Select(Normalize).Order(StringComparer.Ordinal).ToArray();
        var sourceJson = JsonSerializer.Serialize(sources);
        var diagnosticJson = JsonSerializer.Serialize(new { Generator = diagnostics, Driver = driverDiagnostics, Compiler = roslynDiagnostics });
        Write(Path.Combine(output, $"sources-{iteration}.json"), sources);
        Write(Path.Combine(output, $"diagnostics-{iteration}.json"), new { Generator = diagnostics, Driver = driverDiagnostics, Compiler = roslynDiagnostics });
        return new { SourceCount = sources.Length, SourceCharacters = sources.Sum(source => source.Text.Length), SourceSha256 = Hash(sourceJson),
            DiagnosticSha256 = Hash(diagnosticJson), GeneratorDiagnostics = diagnostics.Length, DriverDiagnostics = driverDiagnostics.Length,
            CompilerDiagnostics = roslynDiagnostics.Length, Errors = result.Diagnostics.Concat(run.Diagnostics).Concat(compilerDiagnostics).Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error) };
    }

    private static object? Describe(GenerationStatisticsSnapshot? snapshot) => snapshot == null ? null : new
    {
        snapshot.HasAllocationMeasurements, snapshot.InvalidAllocationMeasurementCount, snapshot.OperationTrackingEnabled,
        ElapsedMilliseconds = snapshot.Elapsed.TotalMilliseconds,
        Counters = Enum.GetValues<GenerationStatisticCounter>().Where(counter => counter != GenerationStatisticCounter.Count)
            .ToDictionary(counter => counter.ToString(), snapshot.GetCounterCount),
        Stages = Enum.GetValues<GenerationStatisticStage>().Where(stage => stage != GenerationStatisticStage.Count).Select(stage => new
        {
            Stage = stage.ToString(), Invocations = snapshot.GetStageInvocationCount(stage),
            AllocatedBytes = snapshot.HasAllocationMeasurements ? (long?)snapshot.GetStageAllocatedBytes(stage) : null,
            Milliseconds = snapshot.GetStageElapsed(stage).TotalMilliseconds,
        }),
        Operations = snapshot.OperationMeasurements.Select(operation => new
        {
            Operation = operation.Operation.ToString(), operation.OwnerId, operation.SourceId, operation.SourcePath,
            operation.Start, operation.Length, operation.Name, operation.ItemCount, operation.InvocationCount,
            Milliseconds = operation.Elapsed.TotalMilliseconds, operation.AllocatedBytes, operation.InvalidAllocationMeasurementCount,
            operation.FirstInvocationAllocatedBytes, FirstInvocationMilliseconds = operation.FirstInvocationElapsed.TotalMilliseconds,
            operation.RepeatedAllocatedBytes, RepeatedMilliseconds = operation.RepeatedElapsed.TotalMilliseconds,
        }),
    };

    private sealed record Invocation(double Milliseconds, long AllocatedBytes, int Gen0, int Gen1, int Gen2, long LastGcHeapBytes, GenerationStatisticsSnapshot? Statistics);
    private static object Fingerprint(Assembly assembly) => new { assembly.Location, Mvid = assembly.ManifestModule.ModuleVersionId,
        Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(assembly.Location))) };
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static void Write(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value, s_json));
        File.Move(path + ".tmp", path, true);
    }
    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }

    [EventSource(Name = "Akbura-Allocation-Audit")]
    private sealed class AllocationAuditEvents : EventSource
    {
        public static readonly AllocationAuditEvents Log = new();
        [Event(1, Level = EventLevel.Informational)] public void Begin(string mode, string diagnostics, int iteration) => WriteEvent(1, mode, diagnostics, iteration);
        [Event(2, Level = EventLevel.Informational)] public void End(string mode, string diagnostics, int iteration) => WriteEvent(2, mode, diagnostics, iteration);
    }

    private sealed class AllocationAuditObservation
    {
        private readonly object _gate = new();
        private readonly Dictionary<object, int> _identities = new(ReferenceEqualityComparer.Instance);
        private readonly List<object> _events = [];
        private readonly HashSet<object> _fallbackLeft = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<object> _fallbackRight = new(ReferenceEqualityComparer.Instance);
        private long _symbolEqual;
        private long _displayFallback;
        private long _displayEqual;
        public void Clear()
        {
            lock (_gate)
            {
                _identities.Clear(); _events.Clear(); _fallbackLeft.Clear(); _fallbackRight.Clear();
                _symbolEqual = 0; _displayFallback = 0; _displayEqual = 0;
            }
        }
        public void Record(string kind, object owner, object? first, object? second, object? third, long bytes, long ticks)
        {
            lock (_gate)
            {
                if (kind == "TypeSame") { _symbolEqual++; return; }
                if (kind == "TypeFallback")
                {
                    _displayFallback++;
                    _displayEqual += bytes;
                    _fallbackLeft.Add(owner);
                    _fallbackRight.Add(first!);
                    return;
                }
                int Id(object? value)
                {
                    if (value == null) return 0;
                    if (!_identities.TryGetValue(value, out var id)) _identities.Add(value, id = _identities.Count + 1);
                    return id;
                }
                _events.Add(new { Kind = kind, Owner = Id(owner), First = Id(first), Second = Id(second), Third = Id(third),
                    AllocatedBytes = bytes, Milliseconds = ticks * 1000d / Stopwatch.Frequency,
                    Thread = Environment.CurrentManagedThreadId,
                    SourcePath = owner is AkburaSemanticModel model ? model.SyntaxTree.FilePath : null,
                    ProbeTextSha256 = kind == "Model" && first is SyntaxTree tree ? Hash(tree.GetRoot().ToFullString()) : null,
                    Scope = kind == "Model" && third is Akbura.Language.Syntax.AkburaSyntax syntax ? $"{syntax.GetType().Name}@{syntax.Position}:{syntax.FullWidth}" : null,
                    Stack = kind == "Build" ? Environment.StackTrace : null });
            }
        }
        public object[] Snapshot()
        {
            lock (_gate)
            {
                if (_symbolEqual == 0 && _displayFallback == 0) return _events.ToArray();
                var types = new HashSet<object>(_fallbackLeft, ReferenceEqualityComparer.Instance);
                types.UnionWith(_fallbackRight);
                return _events.Append(new
                {
                    Kind = "TypeComparisonSummary", Calls = _symbolEqual + _displayFallback,
                    SymbolEqual = _symbolEqual, DisplayFallback = _displayFallback, DisplayEqual = _displayEqual,
                    FallbackLeftTypes = _fallbackLeft.Count, FallbackRightTypes = _fallbackRight.Count, FallbackTypesUnion = types.Count,
                }).ToArray();
            }
        }
    }
}
