using Akbura.BlackSilence;
using Akbura.Language;
using Akbura.Language.Operations;
using Akbura.Language.Symbols;
using Akbura.TestUtilities;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace Akbura.Benchmarks;

/// <summary>
/// Reuses the paired gallery harness and separately observes metadata lookup work.
/// Production assemblies never contain the optional profiling hooks.
/// </summary>
internal static class FeatureGalleryMetadataLookupComparison
{
    public static bool TryRun(string[] args)
    {
        if (args.Length == 0 || args[0] != "--metadata-akcss-shared-lazy") return false;
        try { Run(args); }
        catch (Exception exception) { Console.Error.WriteLine(exception); Environment.ExitCode = 1; }
        return true;
    }

    private static void Run(string[] args)
    {
        string Option(string name, string fallback) => Array.IndexOf(args, name) is var index && index >= 0 ? args[index + 1] : fallback;
        var variants = Path.GetFullPath(Option("--variants", "artifacts/metadata-lookup/variants"));
        var output = Path.GetFullPath(Option("--output", "artifacts/metadata-lookup/results"));
        var round = int.Parse(Option("--round", "0"));
        var profile = args.Contains("--profile", StringComparer.Ordinal);
        Directory.CreateDirectory(output);
        Console.WriteLine("Loading FeatureGallery outside measurements.");
        var project = FeatureGalleryBenchmarkProject.Load();
        var fixtures = round == 0 || profile ? MetadataAkcssLookupFixture.Cases().Select(item => (object)new object[]
            { item.Name, MetadataAkcssLookupFixture.Consumer(MetadataAkcssLookupFixture.Emit(item.Compilation)) }).ToArray() : [];
        foreach (var variant in round % 2 == 0 ? new[] { "Baseline", "SharedLazy" } : ["SharedLazy", "Baseline"])
        {
            using var context = new VariantContext(Path.Combine(variants, (profile ? "Profile" : "") + variant));
            if (profile) context.Call(typeof(FeatureGalleryMetadataLookupComparison), nameof(Profile), [project.ExportComparisonInputs(), output, variant]);
            else
            {
                context.Call(typeof(FeatureGalleryMetadataLookupComparison), nameof(VerifyPrimary), []);
                context.Call(typeof(FeatureGallerySymbolDisplayComparison), nameof(FeatureGallerySymbolDisplayComparison.Macro), [project.ExportComparisonInputs(), output, variant, round == 0]);
            }
            context.Call(typeof(FeatureGalleryMetadataLookupComparison), nameof(Fixtures), [fixtures, output, variant]);
            Collect();
        }
        foreach (var baseline in Directory.EnumerateFiles(output, "Baseline-*-parity.json"))
            if (!File.ReadAllBytes(baseline).AsSpan().SequenceEqual(File.ReadAllBytes(Path.Combine(output, "SharedLazy" + Path.GetFileName(baseline)[8..]))))
                throw new InvalidOperationException("Output parity failed: " + baseline);
        Console.WriteLine("COMPLETED " + output);
        GC.KeepAlive(project);
    }

    private static Type? Hooks => typeof(AkburaReferencedModule).Assembly.GetType("Akbura.Language.Operations.MetadataLookupTrace");
    public static void VerifyPrimary()
    {
        if (Hooks != null) throw new InvalidOperationException("Profiling hooks in primary assembly.");
    }

    public static void Profile(object[] shared, string output, string variant)
    {
        var project = FeatureGalleryBenchmarkProject.ImportComparisonInputs(shared);
        var snapshot = project.CreateSnapshot();
        var recorder = new LookupCapture();
        Attach(recorder);
        GeneratorDriver driver;
        try { driver = Generate(project, snapshot); }
        finally { Detach(); }
        Collect();
        if (recorder.Contexts.Any(reference => reference.IsAlive)) throw new InvalidOperationException("A context escaped metadata initialization.");
        var run = driver.GetRunResult();
        if (run.Results.Single().Exception != null || run.Results.Single().GeneratedSources.Length == 0) throw new InvalidOperationException("Profile generation failed.");
        Write(Path.Combine(output, variant + "-counts.json"), new { Environment.ProcessId, Generator = typeof(AkburaReferencedModule).Assembly.ManifestModule.ModuleVersionId,
            GeneratedSources = run.Results.Single().GeneratedSources.Length, Counters = recorder.Summary() });
        Console.WriteLine(variant + ": full-generation lookup counters captured; temporary contexts collected while driver remains alive.");
        GC.KeepAlive(driver);
        GC.KeepAlive(project);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GeneratorDriver Generate(FeatureGalleryBenchmarkProject project, FeatureGalleryBenchmarkSnapshot snapshot) =>
        CSharpGeneratorDriver.Create(generators: [new AkburaBlackSilenceGenerator().AsSourceGenerator()], additionalTexts: snapshot.AdditionalTexts,
            parseOptions: project.ParseOptions, optionsProvider: new DiagnosticBenchmarkOptionsProvider(snapshot.OptionsProvider, "Shadow")).RunGenerators(project.Compilation);

    public static void Fixtures(object[] fixtures, string output, string variant)
    {
        foreach (var fixture in fixtures.Cast<object[]>())
        {
            var name = (string)fixture[0]; var compilation = (CSharpCompilation)fixture[1];
            var recorder = new LookupCapture();
            if (Hooks != null) Attach(recorder);
            AkburaReferencedModule[] modules;
            try { modules = LoadFixture(compilation); }
            finally { if (Hooks != null) Detach(); }
            var operations = modules.SelectMany(module => module.GetAkcssModuleNames().SelectMany(moduleName => module.GetAkcssModuleSymbolsByLogicalName(moduleName)))
                .Select(module => new { module.MetadataName, Symbols = module.AkcssSymbols.Cast<IMetadataAkcssSymbol>().Select(symbol => new
                { symbol.Name, symbol.MetadataName, symbol.HasErrors, Operations = symbol.Operations.Cast<MetadataAkcssOperation>().Select(Describe) }) }).ToArray();
            Write(Path.Combine(output, variant + "-Fixture" + name + "-parity.json"), new { Modules = operations,
                Diagnostics = FeatureGalleryParityVerification.DescribeDiagnostics("compiler", compilation.GetDiagnostics()).Order(StringComparer.Ordinal) });
            if (Hooks != null)
            {
                Collect();
                var expected = name is "NoApply" or "EmptyApply" ? 0 : 1;
                if (variant == "SharedLazy" && recorder.Builds != expected) throw new InvalidOperationException(name + ": wrong lookup construction count.");
                Write(Path.Combine(output, variant + "-Fixture" + name + "-counts.json"), recorder.Summary());
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static AkburaReferencedModule[] LoadFixture(CSharpCompilation compilation) => AkburaReferencedModule.Load(compilation).ToArray();

    private static object Describe(MetadataAkcssOperation operation) => new
    {
        operation.Kind, operation.Order, operation.ParentOrder, operation.Depth, operation.HasErrors, operation.SourcePath, operation.SourceSpan,
        AppliedNames = (operation as MetadataAkcssApplyOperation)?.AppliedSymbolMetadataNames,
        Targets = (operation as MetadataAkcssApplyOperation)?.AppliedSymbols.Cast<IMetadataAkcssSymbol>()
            .Select(symbol => symbol.MetadataModule.MetadataName + ":" + symbol.MetadataCarrierType.Name),
        Children = operation.Children.Cast<MetadataAkcssOperation>().Select(Describe),
    };

    private static void Attach(LookupCapture capture)
    {
        var hooks = Hooks ?? throw new InvalidOperationException("Profiling hooks absent.");
        hooks.GetField("Record")!.SetValue(null, new Action<string, object?, object?, long, long>(capture.Record));
        hooks.GetField("Bytes")!.SetValue(null, (Func<long>)GC.GetAllocatedBytesForCurrentThread);
    }
    private static void Detach()
    {
        Hooks!.GetField("Record")!.SetValue(null, null);
        Hooks.GetField("Bytes")!.SetValue(null, null);
    }

    private sealed class LookupCapture
    {
        public List<WeakReference> Contexts { get; } = [];
        public int Builds;
        private long _symbolsVisited, _allocated, _ticks, _requests;
        private int _symbols, _withApply, _modules;
        private readonly List<object> _dictionaries = [];
        private readonly HashSet<IMetadataAkcssModuleSymbol> _seenModules = new(ReferenceEqualityComparer.Instance);
        public void Record(string kind, object? first, object? second, long bytes, long ticks)
        {
            switch (kind)
            {
                case "Context": Contexts.Add(new WeakReference(first!)); break;
                case "Symbol":
                    _symbols++;
                    if (((List<MetadataAkcssOperationData>)second!).Any(data => data.Kind == MetadataAkcssOperationKind.Apply)) _withApply++;
                    if (_seenModules.Add(((IMetadataAkcssSymbol)first!).MetadataModule)) _modules++;
                    break;
                case "Request": _requests++; break;
                case "Build":
                    Builds++; _symbolsVisited += (long)second!; _allocated += bytes; _ticks += ticks;
                    var dictionary = (IReadOnlyDictionary<string, System.Collections.Immutable.ImmutableArray<IAkcssSymbol>>)first!;
                    if (_dictionaries.Count == 0) _dictionaries.Add(new { Names = dictionary.Count, CandidateCounts = dictionary.Values.GroupBy(value => value.Length)
                        .OrderBy(group => group.Key).Select(group => new { Candidates = group.Key, Names = group.Count() }).ToArray() });
                    break;
                default: throw new InvalidOperationException(kind);
            }
        }
        public object Summary() => new { MetadataSymbols = _symbols, SymbolsWithApply = _withApply, MetadataModules = _modules, LookupRequests = _requests,
            ContextInstances = Contexts.Count, DictionaryConstructions = Builds, SymbolsVisited = _symbolsVisited, LookupAllocatedBytes = _allocated,
            LookupMilliseconds = _ticks * 1000d / Stopwatch.Frequency, Dictionaries = _dictionaries, ContextsAliveAfterGc = Contexts.Count(reference => reference.IsAlive) };
    }

    private static void Collect() { GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); }
    private static void Write(string path, object value) => File.WriteAllText(path, JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true }));
    private sealed class VariantContext(string directory) : AssemblyLoadContext(isCollectible: true), IDisposable
    {
        protected override Assembly? Load(AssemblyName name) => name.Name is "Akbura.Benchmarks" or "Akbura.Generator" or "Akbura.BlackSilence"
            ? LoadFromAssemblyPath(Path.Combine(directory, name.Name + ".dll")) : Default.LoadFromAssemblyName(name);
        public object? Call(Type type, string method, object[] arguments)
        {
            try { return LoadFromAssemblyPath(Path.Combine(directory, "Akbura.Benchmarks.dll")).GetType(type.FullName!)!.GetMethod(method)!.Invoke(null, arguments); }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            { System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw(); throw; }
        }
        public void Dispose() => Unload();
    }
}
