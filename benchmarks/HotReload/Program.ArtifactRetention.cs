using Akbura.BlackSilence;
using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private static BlackSilenceGenerationRequest ArtifactRequest(GeneratorDriver driver) =>
        (BlackSilenceGenerationRequest)driver.GetRunResult().Results.Single().TrackedSteps["BlackSilence.GenerationRequests"]
            .SelectMany(step => step.Outputs).Single().Value;

    private static void ForceArtifactGc()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
    }

    private static void MeasureArtifactRetention(string itemsPath, string fileName, int revisions, bool reuse,
        bool realMembers = false, int concurrentSamples = 1)
    {
        Assert.True(revisions >= 1);
        Write(new { Kind = "artifact-memory-provenance", RepositoryRevision = GitRevision(),
            ToolSha256 = FileDigest(typeof(Program).Assembly.Location),
            BlackSilenceSha256 = FileDigest(typeof(AkburaBlackSilenceGenerator).Assembly.Location),
            GeneratorSha256 = FileDigest(typeof(GenerationStatistics).Assembly.Location),
            InputsSha256 = FileDigest(itemsPath), Revisions = revisions, Optimization = reuse ? "enabled" : "disabled", RealMembers = realMembers, ConcurrentSamples = concurrentSamples });
        var result = RunArtifactRetention(itemsPath, fileName, revisions, reuse, realMembers, concurrentSamples);
        for (var attempt = 0; attempt < 6; attempt++)
        {
            ForceArtifactGc();
        }
        var counts = result.Weak.GroupBy(item => item.Kind).ToDictionary(group => group.Key, group => new
        {
            Observed = group.Count(),
            Alive = group.Where(item => item.Reference.IsAlive).Select(item => item.Reference.Target)
                .Distinct(ReferenceEqualityComparer.Instance).Count(),
        });
        foreach (var pair in counts)
        {
            Assert.InRange(pair.Value.Alive, 0, pair.Key == "DetachedSnapshot" && reuse ? 1 : 0);
        }
        Write(new { Kind = "artifact-memory-result", Project = result.Project, Revisions = revisions, Optimization = reuse ? "enabled" : "disabled",
            Objects = counts, PostGcHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
            Entries = result.Owner.Latest?.Entries.Count ?? 0, IdentitySlots = result.Owner.Read()?.Identities.Length ?? 0,
            RootPolicy = "Only the newest detached store and weak observations remain; all drivers and compilations released.",
            FreshParityChecks = revisions + 1 + concurrentSamples * 2, ConcurrentBranches = concurrentSamples * 2, ConcurrentSamples = concurrentSamples });
        GC.KeepAlive(result.Owner);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static ArtifactRetentionResult RunArtifactRetention(string itemsPath, string fileName, int revisions, bool reuse,
        bool realMembers, int concurrentSamples)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(itemsPath));
        var properties = input.RootElement.GetProperty("Properties");
        var items = input.RootElement.GetProperty("Items");
        var directory = properties.GetProperty("MSBuildProjectDirectory").GetString()!;
        var rootNamespace = properties.GetProperty("RootNamespace").GetString()!;
        var parse = ParseOptions.WithPreprocessorSymbols(properties.GetProperty("DefineConstants").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries));
        IEnumerable<string> Paths(string name) => items.GetProperty(name).EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
        var compilation = CSharpCompilation.Create(rootNamespace,
            Paths("Compile").Select(path => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path), Encoding.UTF8), parse, path)),
            Paths("ReferencePath").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        var files = Paths("AdditionalFiles").Select(path => new Input(path, File.ReadAllText(path))).ToArray();
        var selected = files.Single(file => Path.GetFileName(file.Path) == fileName);
        if (realMembers)
        {
            selected = new Input(selected.Path, BindingSource(selected.GetText().ToString(), fileName, "real-referenced-method"));
            files = files.Select(file => file.Path == selected.Path ? selected : file).ToArray();
        }
        SyntaxTree Leaf(int value) => CSharpSyntaxTree.ParseText(SourceText.From(
            realMembers ? RealMemberSource("real-referenced-method", value) :
                "internal static class RetentionLeaf { public static int Value() => " + value + "; }", Encoding.UTF8), parse, "RetentionLeaf.cs");
        var leaf = Leaf(0);
        compilation = compilation.AddSyntaxTrees(leaf);
        var driver = Driver(files, directory, rootNamespace, parse: parse, reuse: reuse);
        var weak = new List<(string Kind, WeakReference Reference)>();
        for (var index = 0; index <= revisions; index++)
        {
            if (index > 0)
            {
                var next = Leaf(index);
                compilation = compilation.ReplaceSyntaxTree(leaf, next);
                leaf = next;
            }
            using var measurement = GenerationStatistics.BeginMeasurement(GC.GetAllocatedBytesForCurrentThread);
            var allocated = GC.GetTotalAllocatedBytes(precise: true);
            var collections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var started = System.Diagnostics.Stopwatch.GetTimestamp();
            driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
            var elapsed = Elapsed(started);
            allocated = GC.GetTotalAllocatedBytes(precise: true) - allocated;
            measurement.Dispose();
            Assert.Null(driver.GetRunResult().Results.Single().Exception);
            ValidateArtifactDiagnosticStamps(diagnostics, ArtifactRequest(driver).Version);
            Clean(diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(source => source.HintName, source => source.SourceText.ToString());
            VerifyFreshArtifacts(files, directory, rootNamespace, parse, compilation, generated, DiagnosticKeys(diagnostics.Concat(output.GetDiagnostics())), false);
            CaptureArtifactObjects(driver, weak);
            ForceArtifactGc();
            var request = ArtifactRequest(driver);
            var stats = measurement.GetSnapshot();
            Write(new { Kind = "artifact-memory-sample", Project = rootNamespace, Optimization = reuse ? "enabled" : "disabled", Index = index,
                GeneratorMs = elapsed, ProcessAllocatedBytes = allocated, PostGcHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
                Entries = request.State.Artifacts.Latest?.Entries.Count ?? 0, IdentitySlots = request.State.Artifacts.Read()?.Identities.Length ?? 0,
                CumulativeCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray(),
                CollectionsSinceUpdateStartIncludingOracle = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray(),
                ActiveCompilationOwners = weak.Where(item => item.Kind == "ProjectState" && item.Reference.IsAlive)
                    .Select(item => item.Reference.Target).Distinct(ReferenceEqualityComparer.Instance).Count(),
                SemanticModels = stats.GetCounterCount(GenerationStatisticCounter.SemanticModelCreated),
                ArtifactHits = stats.GetCounterCount(GenerationStatisticCounter.CrossCompilationArtifactHit) });
        }

        // Fork the real large driver. Each branch is independently checked against a
        // cache-disabled fresh driver; cache publication order cannot decide output.
        var fork = driver;
        var parentCompilation = compilation;
        var parentLeaf = leaf;
        for (var concurrentIndex = 0; concurrentIndex < concurrentSamples; concurrentIndex++)
        {
            using var concurrentMeasurement = GenerationStatistics.BeginMeasurement(GC.GetAllocatedBytesForCurrentThread);
            var concurrentAllocated = GC.GetTotalAllocatedBytes(precise: true);
            var concurrentCollections = Enumerable.Range(0, 3).Select(GC.CollectionCount).ToArray();
            var startedForks = System.Diagnostics.Stopwatch.GetTimestamp();
            var branches = Task.WhenAll(Enumerable.Range(1, 2).Select(branch => Task.Run(() =>
            {
                var revision = revisions + concurrentIndex * 2 + branch;
                var current = parentCompilation.ReplaceSyntaxTree(parentLeaf, Leaf(revision));
                var changedText = realMembers ? selected.GetText().ToString().Replace("ContractFixture.Current.Vm.GetName()",
                    "ContractFixture.Current.Vm.GetName() + \" " + revision + "\"", StringComparison.Ordinal) :
                    Edit(selected.GetText().ToString(), fileName, revision);
                var changed = new Input(selected.Path, changedText);
                var changedFiles = files.Select(file => file.Path == selected.Path ? changed : file).ToArray();
                var preparedDriver = fork.ReplaceAdditionalText(selected, changed);
                var branchStarted = System.Diagnostics.Stopwatch.GetTimestamp();
                var currentDriver = preparedDriver.RunGeneratorsAndUpdateCompilation(current, out var output, out var diagnostics);
                var driverMs = Elapsed(branchStarted);
                var generated = currentDriver.GetRunResult().Results.Single().GeneratedSources
                    .ToDictionary(source => source.HintName, source => source.SourceText.ToString());
                var generationMs = Elapsed(startedForks);
                return (GenerationMs: generationMs, DriverMs: driverMs, Driver: currentDriver, Compilation: current, Files: changedFiles,
                    Generated: generated, Diagnostics: diagnostics, Output: output);
            }))).GetAwaiter().GetResult();
            var concurrentMs = Elapsed(startedForks);
            concurrentAllocated = GC.GetTotalAllocatedBytes(precise: true) - concurrentAllocated;
            concurrentMeasurement.Dispose();
            var concurrentStats = concurrentMeasurement.GetSnapshot();
            var generationCollections = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - concurrentCollections[generation]).ToArray();
            ForceArtifactGc();
            var concurrentHeap = GC.GetTotalMemory(false);
            var concurrentWorkingSet = Environment.WorkingSet;
            foreach (var branch in branches)
            {
                // Both measured generations have completed before either fresh oracle
                // starts, so one branch's verification cannot inflate the other's time.
                VerifyFreshArtifacts(branch.Files, directory, rootNamespace, parse, branch.Compilation, branch.Generated,
                    DiagnosticKeys(branch.Diagnostics.Concat(branch.Output.GetDiagnostics())), false);
                ValidateArtifactDiagnosticStamps(branch.Diagnostics, ArtifactRequest(branch.Driver).Version);
                CaptureArtifactObjects(branch.Driver, weak);
            }
            Write(new { Kind = "artifact-concurrent", Project = rootNamespace, Optimization = reuse ? "enabled" : "disabled",
                Index = concurrentIndex,
                GeneratorMs = concurrentMs, ProcessAllocatedBytes = concurrentAllocated, Collections = generationCollections,
                PostGcHeapBytes = concurrentHeap, WorkingSetBytes = concurrentWorkingSet,
                Counters = Enum.GetValues<GenerationStatisticCounter>().Where(counter => counter != GenerationStatisticCounter.Count)
                    .ToDictionary(counter => counter.ToString(), concurrentStats.GetCounterCount),
                Stages = Enum.GetValues<GenerationStatisticStage>().Where(stage => stage != GenerationStatisticStage.Count)
                    .Select(stage => new { Stage = stage.ToString(), Count = concurrentStats.GetStageInvocationCount(stage),
                        InclusiveMs = concurrentStats.GetStageElapsed(stage).TotalMilliseconds }).ToArray(),
                BranchCompletionMs = branches.Select(branch => branch.GenerationMs).ToArray(), IncludesFreshOracle = false,
                BranchGeneratorMs = branches.Select(branch => branch.DriverMs).ToArray(),
                EndToEndWithOraclesMs = Elapsed(startedForks) });
        }
        return new ArtifactRetentionResult(rootNamespace, ArtifactRequest(driver).State.Artifacts, weak);
    }

    private static void CaptureArtifactObjects(GeneratorDriver driver, List<(string Kind, WeakReference Reference)> weak)
    {
        var request = ArtifactRequest(driver);
        weak.Add(("CSharpCompilation", new WeakReference(request.State.CSharpCompilation)));
        weak.Add(("AkburaCompilation", new WeakReference(request.Index.Compilation)));
        weak.Add(("ProjectIndex", new WeakReference(request.Index)));
        weak.Add(("ProjectState", new WeakReference(request.State)));
        weak.Add(("ProjectSnapshot", new WeakReference(request.State.TryGetSnapshot(request.Options))));
        weak.Add(("DetachedSnapshot", new WeakReference(request.State.Artifacts.Latest)));
        foreach (var document in request.Documents)
        {
            if (request.Index.Compilation.HasCreatedSemanticModel(document.SyntaxTree))
            {
                var model = request.Index.Compilation.GetSemanticModel(document.SyntaxTree);
                weak.Add(("AkburaSemanticModel", new WeakReference(model)));
                weak.Add(("ProbeContext", new WeakReference(model.BindingSession.ProbeContext)));
                weak.Add(("ProbeCompilation", new WeakReference(request.Index.Compilation.CSharpProbeCompilation)));
            }
        }
    }

    private sealed record ArtifactRetentionResult(string Project, BoundedArtifactStore Owner, List<(string Kind, WeakReference Reference)> Weak);
}
