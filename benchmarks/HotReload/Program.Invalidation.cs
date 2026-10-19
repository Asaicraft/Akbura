using Akbura.Language.CodeGeneration;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Runtime.CompilerServices;
using Akbura.TestUtilities;

internal static partial class Program
{
    private static string InvalidationBinaryFlavor => typeof(Akbura.BlackSilence.AkburaBlackSilenceGenerator).Assembly
        .GetType("Akbura.BlackSilence.BlackSilenceGenerationStatistics") == null ? "production" : "stats";

    private static readonly string[] InvalidationScenarios =
    [
        "unchanged", "fresh-driver", "akbura", "akbura-whitespace", "original-add-tree", "unrelated-body",
        "csharp-trivia", "csharp-whitespace", "inert-tree", "referenced-body", "referenced-signature", "overload", "const", "attribute",
        "global-using", "reference-compatible", "reference-incompatible", "unresolved", "nullable",
        "preprocessor", "language", "compilation-options", "add-tree", "remove-tree", "repeat", "revert", "multi-tree",
        "real-method", "real-referenced-method", "real-service", "real-property", "real-multiple", "real-mixed", "real-signature", "real-overload",
        "semantic-property", "semantic-signature", "semantic-add-overload", "semantic-remove-overload", "semantic-negative",
        "semantic-multiple", "semantic-unrelated", "semantic-mixed", "semantic-global",
    ];

    private static void MeasureInvalidation(string itemsPath, string fileName, string? selectedScenario, string profiling,
        bool reuse = true, bool verifyEveryUpdate = false)
    {
        var optimization = reuse ? "enabled" : "disabled";
        Assert.Contains(profiling, new[] { "off", "stats", "trace" });
        if (selectedScenario != null)
        {
            Assert.Contains(selectedScenario, InvalidationScenarios);
        }
        using var input = JsonDocument.Parse(File.ReadAllText(itemsPath));
        var properties = input.RootElement.GetProperty("Properties");
        var items = input.RootElement.GetProperty("Items");
        var directory = properties.GetProperty("MSBuildProjectDirectory").GetString()!;
        var rootNamespace = properties.GetProperty("RootNamespace").GetString()!;
        var parse = ParseOptions.WithPreprocessorSymbols(properties.GetProperty("DefineConstants").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries));
        var trees = Paths("Compile").Select(path => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path), Encoding.UTF8), parse, path)).ToArray();
        var baseline = CSharpCompilation.Create(rootNamespace, trees, Paths("ReferencePath").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        var sourceFiles = Paths("AdditionalFiles").Select(path => new Input(path, File.ReadAllText(path))).ToArray();
        var selected = sourceFiles.Single(file => Path.GetFileName(file.Path) == fileName);
        Write(new { Kind = "invalidation-provenance", Project = rootNamespace, File = fileName, Profiling = profiling, BinaryFlavor = InvalidationBinaryFlavor,
            Optimization = optimization, VerifyEveryUpdate = verifyEveryUpdate,
            RepositoryRevision = GitRevision(), Sdk = typeof(CSharpCompilation).Assembly.GetCustomAttributes(false)
                .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion,
            CSharpTrees = trees.Length, AdditionalFiles = sourceFiles.Length,
            BlackSilence = typeof(Akbura.BlackSilence.AkburaBlackSilenceGenerator).Assembly.Location,
            Generator = typeof(GenerationStatistics).Assembly.Location,
            BlackSilenceSha256 = FileDigest(typeof(Akbura.BlackSilence.AkburaBlackSilenceGenerator).Assembly.Location),
            GeneratorSha256 = FileDigest(typeof(GenerationStatistics).Assembly.Location),
            InputsSha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(itemsPath))) });

        foreach (var scenario in InvalidationScenarios.Where(name => selectedScenario == null || name == selectedScenario))
        {
            RunScenario(scenario);
        }

        IEnumerable<string> Paths(string name) => items.GetProperty(name).EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        void RunScenario(string scenario)
        {
            var useExpression = scenario is "referenced-body" or "referenced-signature" or "overload" or "const" or "attribute" or
                "reference-compatible" or "reference-incompatible" or "unresolved" || scenario.StartsWith("real-", StringComparison.Ordinal) || scenario.StartsWith("semantic-", StringComparison.Ordinal);
            var support = Support(0);
            var unrelated = SupportTree("internal static class BenchmarkUnrelated { public static int Value() => 0; }", "BenchmarkUnrelated.cs");
            var original = useExpression ? new Input(selected.Path, BindingSource(selected.GetText().ToString(), fileName, scenario)) : selected;
            var files = sourceFiles.Select(file => file.Path == selected.Path ? original : file).ToArray();
            if (scenario.StartsWith("semantic-", StringComparison.Ordinal))
            {
                files = SemanticFiles(files, directory, scenario);
                Write(new { Kind = "semantic-fixture", Project = rootNamespace, Scenario = scenario, OriginalDocuments = sourceFiles.Length,
                    ControlledIndependentConsumers = 2, ExtraProfileConsumer = scenario == "semantic-multiple", TotalDocuments = files.Length });
            }
            var initialCompilation = baseline.AddSyntaxTrees(support, unrelated);
            var reference0 = scenario.StartsWith("reference-", StringComparison.Ordinal) ? Library(0) : null;
            if (reference0 != null)
            {
                initialCompilation = initialCompilation.AddReferences(reference0);
            }
            GeneratorDriver driver = Driver(files, directory, rootNamespace, parse: parse, reuse: reuse);
            var previousInput = original;
            var previousSources = new Dictionary<string, string>();
            var previousInputs = new Dictionary<string, GenerationInvalidationEvent>();
            var previousDecisions = new Dictionary<string, GenerationInvalidationEvent>();
            GenerationInvalidationEvent? previousContext = null;
            var previousReads = new Dictionary<string, string>();
            var previousVersions = new Dictionary<string, GenerationInvalidationEvent>();
            string[] baselineDiagnostics = [];
            string[]? baselineCompilerErrors = null;
            CSharpCompilation? repeatedCompilation = null;
            using var process = Process.GetCurrentProcess();
            for (var index = 0; index <= Warmup + Samples; index++)
            {
                var compilation = initialCompilation;
                var currentParse = parse;
                if (index > 0)
                {
                    switch (scenario)
                    {
                        case "fresh-driver":
                            driver = Driver(files, directory, rootNamespace, parse: parse, reuse: reuse);
                            break;
                        case "akbura":
                        case "akbura-whitespace":
                            var text = scenario == "akbura" ? Edit(original.GetText().ToString(), fileName, index) : original.GetText() + new string(' ', index);
                            var replacement = new Input(original.Path, text);
                            driver = driver.ReplaceAdditionalText(previousInput, replacement);
                            previousInput = replacement;
                            break;
                        case "original-add-tree":
                            // Preserve the original harness scenario: each run starts from the original
                            // compilation and adds a new tree. This is not ReplaceSyntaxTree in an IDE.
                            compilation = initialCompilation.AddSyntaxTrees(SupportTree("internal static class AddedByOriginalBenchmark { public static int Value() => " + index + "; }", "OriginalAdd.cs"));
                            break;
                        case "unrelated-body":
                        case "csharp-trivia":
                        case "csharp-whitespace":
                        case "multi-tree":
                            var unrelatedText = unrelated.GetText().ToString();
                            unrelatedText = scenario == "csharp-trivia" ? unrelatedText + " /* edit " + index + " */ " :
                                scenario == "csharp-whitespace" ? unrelatedText + new string(' ', index) : unrelatedText.Replace("=> 0", "=> " + index, StringComparison.Ordinal);
                            compilation = compilation.ReplaceSyntaxTree(unrelated, SupportTree(unrelatedText, unrelated.FilePath));
                            if (scenario == "multi-tree")
                            {
                                compilation = compilation.ReplaceSyntaxTree(support, Support(index));
                            }
                            break;
                        case "referenced-body":
                        case "referenced-signature":
                        case "overload":
                        case "const":
                        case "attribute":
                        case "unresolved":
                            compilation = compilation.ReplaceSyntaxTree(support, Support(index));
                            break;
                        case "real-method":
                        case "real-referenced-method":
                        case "real-service":
                        case "real-property":
                        case "real-multiple":
                        case "real-mixed":
                        case "real-signature":
                        case "real-overload":
                        case "semantic-property":
                        case "semantic-signature":
                        case "semantic-add-overload":
                        case "semantic-remove-overload":
                        case "semantic-negative":
                        case "semantic-multiple":
                        case "semantic-unrelated":
                        case "semantic-mixed":
                        case "semantic-global":
                            compilation = compilation.ReplaceSyntaxTree(support, Support(index));
                            if (scenario is "real-mixed" or "semantic-mixed")
                            {
                                var mixed = new Input(original.Path, original.GetText().ToString().Replace(
                                    scenario == "real-mixed" ? "ContractFixture.Current.Vm.GetName()" : "SemanticFixture.ProfileService.GetName().ToString()",
                                    scenario == "real-mixed" ? "ContractFixture.Current.Vm.GetName() + \" " + index + "\"" : "SemanticFixture.ProfileService.GetName().ToString() + \" " + index + "\"", StringComparison.Ordinal));
                                driver = driver.ReplaceAdditionalText(previousInput, mixed);
                                previousInput = mixed;
                            }
                            break;
                        case "global-using":
                            compilation = compilation.ReplaceSyntaxTree(support, SupportTree("global using " +
                                (index % 2 == 1 ? "System.Text" : "System.Threading.Tasks") + ";\n" + Support(0).GetText(), support.FilePath));
                            break;
                        case "reference-compatible":
                        case "reference-incompatible":
                            compilation = compilation.ReplaceReference(reference0!, Library(index));
                            break;
                        case "nullable":
                            compilation = compilation.WithOptions(compilation.Options.WithNullableContextOptions(index % 2 == 1 ? NullableContextOptions.Enable : NullableContextOptions.Disable));
                            break;
                        case "compilation-options":
                            compilation = compilation.WithOptions(compilation.Options.WithAllowUnsafe(index % 2 == 1));
                            break;
                        case "language":
                        case "preprocessor":
                            currentParse = scenario == "language" ? parse.WithLanguageVersion(index % 2 == 1 ? LanguageVersion.CSharp14 : LanguageVersion.Preview) :
                                parse.WithPreprocessorSymbols(parse.PreprocessorSymbolNames.Concat(index % 2 == 1 ? new[] { "INVESTIGATION_EDIT" } : []));
                            compilation = compilation.RemoveAllSyntaxTrees().AddSyntaxTrees(initialCompilation.SyntaxTrees.Select(tree =>
                                CSharpSyntaxTree.ParseText(tree.GetText(), currentParse, tree.FilePath)));
                            driver = driver.WithUpdatedParseOptions(currentParse);
                            break;
                        case "add-tree":
                            compilation = compilation.AddSyntaxTrees(SupportTree("internal static class InvestigationAdded { public const int Value = " + index + "; }", "Added.cs"));
                            break;
                        case "inert-tree":
                            compilation = compilation.AddSyntaxTrees(SupportTree("namespace InvestigationFixture; internal sealed class InertCandidate" + index + " { }", "Inert.cs"));
                            break;
                        case "remove-tree":
                            compilation = compilation.RemoveSyntaxTrees(index % 2 == 1 ? unrelated : support);
                            break;
                        case "repeat":
                            repeatedCompilation ??= compilation.ReplaceSyntaxTree(unrelated, SupportTree(unrelated.GetText() + " // repeated edit", unrelated.FilePath));
                            compilation = repeatedCompilation;
                            break;
                        case "revert":
                            if (index % 2 == 1)
                            {
                                compilation = compilation.ReplaceSyntaxTree(unrelated, SupportTree(unrelated.GetText() + " // temporary edit", unrelated.FilePath));
                            }
                            break;
                    }
                }

                using var measurement = profiling == "off" ? null : GenerationStatistics.BeginMeasurement(GC.GetAllocatedBytesForCurrentThread,
                    trackInvalidation: profiling == "trace");
                process.Refresh();
                var cpu = process.TotalProcessorTime;
                var collections = new[] { GC.CollectionCount(0), GC.CollectionCount(1), GC.CollectionCount(2) };
                var threadAllocated = GC.GetAllocatedBytesForCurrentThread();
                var processAllocated = GC.GetTotalAllocatedBytes(precise: true);
                var start = Stopwatch.GetTimestamp();
                driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
                var wallMs = Elapsed(start);
                processAllocated = GC.GetTotalAllocatedBytes(precise: true) - processAllocated;
                threadAllocated = GC.GetAllocatedBytesForCurrentThread() - threadAllocated;
                process.Refresh();
                var cpuMs = (process.TotalProcessorTime - cpu).TotalMilliseconds;
                measurement?.Dispose();
                var statistics = measurement?.GetSnapshot();
                if (index == 0 && profiling != "off")
                {
                    Assert.True(statistics!.GetStageInvocationCount(GenerationStatisticStage.CSharpEnvironmentConstruction) > 0,
                        "Profiling requires DebugStats analyzers. Run Run.ps1 -Suite invalidation -Prepare.");
                }
                var result = driver.GetRunResult().Results.Single();
                Assert.Null(result.Exception);
                ValidateArtifactDiagnosticStamps(diagnostics, ArtifactRequest(driver).Version);
                Assert.DoesNotContain(diagnostics, diagnostic => diagnostic.Id == "AKBURA_GENERATOR_FAILURE");
                if (reuse && index > 0 && scenario.StartsWith("semantic-", StringComparison.Ordinal) && scenario != "semantic-global")
                {
                    var request = ArtifactRequest(driver);
                    foreach (var independent in new[] { "SemanticOrders.akbura", "SemanticSettings.akbura" })
                    {
                        Assert.NotNull(request.Components.Single(component => component.Version.Document.FilePath.EndsWith(independent, StringComparison.Ordinal)).Previous);
                        Assert.NotNull(request.Diagnostics.Single(diagnostic => diagnostic.Document.FilePath.EndsWith(independent, StringComparison.Ordinal)).Previous);
                    }
                }
                var expectedIncompleteInput = scenario is "unresolved" or "semantic-negative" && index % 2 == 0;
                if (!expectedIncompleteInput)
                {
                    Clean(diagnostics);
                }
                var generated = result.GeneratedSources.ToDictionary(source => source.HintName, source => source.SourceText.ToString());
                var changed = generated.Keys.Union(previousSources.Keys).Where(key => !generated.TryGetValue(key, out var value) ||
                    !previousSources.TryGetValue(key, out var old) || value != old).ToArray();
                var events = statistics?.InvalidationEvents ?? [];
                var currentInputs = events.Where(row => row.Kind == "DocumentInputs" && row.Decision != "Removed").ToDictionary(row => row.Path);
                var currentDecisions = events.Where(row => row.Kind == "GenerationDecision").ToDictionary(row => row.Data["Identity"]);
                var context = events.SingleOrDefault(row => row.Kind == "ProjectContext");
                var readDeltas = events.Where(row => row.Kind == "SourceRead").Select(row => new
                {
                    row.Path,
                    Changed = previousReads.TryGetValue(row.Path, out var old) ? (bool?)(old != row.Data["Source"]) : null,
                }).ToArray();
                var versionDeltas = events.Where(row => row.Kind == "DocumentVersion").Select(row => new
                {
                    row.Path,
                    PreviousObserved = previousVersions.ContainsKey(row.Path),
                    Changed = row.Data.Where(pair => previousVersions.TryGetValue(row.Path, out var old) && old.Data[pair.Key] != pair.Value)
                        .Select(pair => pair.Key).ToArray(),
                }).ToArray();
                foreach (var observation in events)
                {
                    if (observation.Kind == "SourceRead")
                    {
                        previousReads[observation.Path] = observation.Data["Source"];
                    }
                    if (observation.Kind == "DocumentVersion")
                    {
                        previousVersions[observation.Path] = observation;
                    }
                }
                var inputDeltas = currentInputs.Select(pair => new
                {
                    Path = pair.Key,
                    PreviousObserved = previousInputs.ContainsKey(pair.Key),
                    Changed = pair.Value.Data.Where(value => value.Key is "Source" or "GenerationShape" or "Surface" or "DependencyNames")
                        .Where(value => !previousInputs.TryGetValue(pair.Key, out var old) || old.Data[value.Key] != value.Value).Select(value => value.Key).ToArray(),
                }).ToArray();
                var dependencyDeltas = currentDecisions.Select(pair => new
                {
                    Identity = pair.Key,
                    PreviousObserved = previousDecisions.ContainsKey(pair.Key),
                    DependenciesChanged = previousDecisions.TryGetValue(pair.Key, out var old) ?
                        (bool?)(old.Data["Dependencies"] != pair.Value.Data["Dependencies"]) : null,
                }).ToArray();
                Write(new { Kind = "invalidation", Project = rootNamespace, File = fileName, Scenario = scenario, Profiling = profiling,
                    BinaryFlavor = InvalidationBinaryFlavor, Optimization = optimization, VerifyEveryUpdate = verifyEveryUpdate,
                    Index = index, First = index == 0, Warmup = index is > 0 and <= Warmup, GeneratorMs = wallMs,
                    ExpectedIncompleteInput = expectedIncompleteInput, GeneratorErrors = diagnostics.Count(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error),
                    OutsideDocumentBatchMs = statistics == null ? (double?)null : wallMs - statistics.DocumentBatchElapsed.TotalMilliseconds,
                    ProcessCpuMs = cpuMs, Collections = Enumerable.Range(0, 3).Select(generation => GC.CollectionCount(generation) - collections[generation]).ToArray(),
                    CurrentThreadAllocatedBytes = threadAllocated, ProcessAllocatedBytes = processAllocated,
                    ManagedHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
                    Sources = generated.Count, ChangedSources = changed, InputDeltas = inputDeltas,
                    SourceReadDeltas = readDeltas, DocumentVersionDeltas = versionDeltas,
                    DependencyDeltas = dependencyDeltas,
                    ContextDeltas = context == null || previousContext == null ? null :
                        context.Data.Where(pair => pair.Key is "State" or "Index" or "AkburaCompilation" or "CSharpCompilation" or "DeclarationEnvironment" or "Options")
                            .ToDictionary(pair => pair.Key, pair => pair.Value != previousContext.Data[pair.Key]),
                    RemovedInputs = currentInputs.Count == 0 ? [] : previousInputs.Keys.Except(currentInputs.Keys).ToArray(),
                    Counters = statistics == null ? null : Enum.GetValues<GenerationStatisticCounter>().Where(counter => counter != GenerationStatisticCounter.Count)
                        .ToDictionary(counter => counter.ToString(), statistics.GetCounterCount),
                    Stages = statistics == null ? null : Enum.GetValues<GenerationStatisticStage>().Where(stage => stage != GenerationStatisticStage.Count)
                        .Select(stage => new { Stage = stage.ToString(), Count = statistics.GetStageInvocationCount(stage),
                            InclusiveMs = statistics.GetStageElapsed(stage).TotalMilliseconds, InclusiveCurrentThreadBytes = statistics.GetStageAllocatedBytes(stage) }).ToArray(),
                    InvalidAllocationSamples = statistics?.InvalidAllocationMeasurementCount, Events = events,
                    Steps = result.TrackedSteps.ToDictionary(pair => pair.Key, pair => pair.Value.SelectMany(step => step.Outputs)
                        .GroupBy(value => value.Reason.ToString()).ToDictionary(group => group.Key, group => group.Count())) });
                previousSources = generated;
                if (currentInputs.Count > 0)
                {
                    previousInputs = currentInputs;
                    previousDecisions = currentDecisions;
                    previousContext = context;
                }
                if (verifyEveryUpdate || index == 0 || index >= Warmup + Samples - 1)
                {
                    var diagnosticKeys = DiagnosticKeys(diagnostics.Concat(output.GetDiagnostics()));
                    var compilerErrors = DiagnosticKeys(output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error));
                    baselineCompilerErrors ??= compilerErrors;
                    if (scenario is not ("unresolved" or "semantic-negative"))
                    {
                        Assert.Equal(baselineCompilerErrors, compilerErrors);
                    }
                    if (index == 0)
                    {
                        baselineDiagnostics = diagnosticKeys;
                    }
                    Write(new { Kind = "invalidation-validation", Project = rootNamespace, File = fileName, Scenario = scenario, Profiling = profiling,
                        BinaryFlavor = InvalidationBinaryFlavor, Optimization = optimization, VerifyEveryUpdate = verifyEveryUpdate,
                        Index = index, Diagnostics = diagnosticKeys, AddedDiagnostics = diagnosticKeys.Except(baselineDiagnostics).ToArray(),
                        RemovedDiagnostics = baselineDiagnostics.Except(diagnosticKeys).ToArray() });
                    if (verifyEveryUpdate || index >= Warmup + Samples - 1)
                    {
                        var freshFiles = files.Select(file => file.Path == original.Path ? previousInput : file);
                        VerifyFreshArtifacts(freshFiles, directory, rootNamespace, currentParse, compilation, generated, diagnosticKeys, expectedIncompleteInput);
                        Write(new { Kind = "invalidation-parity", Project = rootNamespace, File = fileName, Scenario = scenario, Profiling = profiling,
                            BinaryFlavor = InvalidationBinaryFlavor, Optimization = optimization, VerifyEveryUpdate = verifyEveryUpdate,
                            Index = index, EqualSources = true, EqualDiagnostics = true,
                            DiagnosticCount = diagnosticKeys.Length, SourceDigests = generated.ToDictionary(pair => pair.Key,
                                pair => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(pair.Value)))) });
                    }
                }
                if (verifyEveryUpdate)
                {
                    ForceArtifactGc();
                    var request = ArtifactRequest(driver);
                    Write(new { Kind = "artifact-heap", Project = rootNamespace, File = fileName, Scenario = scenario,
                        Profiling = profiling, BinaryFlavor = InvalidationBinaryFlavor, Optimization = optimization, Index = index,
                        PostGcHeapBytes = GC.GetTotalMemory(false), WorkingSetBytes = Environment.WorkingSet,
                        Entries = request.State.Artifacts.Latest?.Entries.Count ?? 0,
                        IdentitySlots = request.State.Artifacts.Read()?.Identities.Length ?? 0 });
                    GC.KeepAlive(driver);
                }
            }

            SyntaxTree Support(int index)
            {
                if (scenario.StartsWith("real-", StringComparison.Ordinal) || scenario.StartsWith("semantic-", StringComparison.Ordinal))
                {
                    return SupportTree(RealMemberSource(scenario, index), "ContractViewModels.cs");
                }
                var type = scenario == "referenced-signature" && index % 2 == 1 ? "string" : "int";
                var expression = type == "string" ? "\"" + index + "\"" :
                    (scenario is "referenced-body" or "referenced-signature" or "multi-tree" ? index : 0).ToString(CultureInfo.InvariantCulture);
                var attribute = scenario == "attribute" && index % 2 == 1 ? "[System.Obsolete(\"investigation warning\")] " : "";
                var method = scenario == "unresolved" && index % 2 == 0 ? "" : attribute + "public static " + type + " Read() => " + expression + ";";
                var overload = scenario == "overload" && index % 2 == 1 ? "public static int Read(string value) => value.Length;" : "";
                return SupportTree("namespace InvestigationFixture; public static class Support { " + method + overload +
                    " public const int Number = " + (scenario == "const" ? index : 0) + "; }", "InvestigationSupport.cs");
            }

            SyntaxTree SupportTree(string text, string path) => CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), parse, path);

            PortableExecutableReference Library(int index)
            {
                var returnType = scenario == "reference-incompatible" && index % 2 == 1 ? "string" : "int";
                var expression = returnType == "string" ? "\"changed\"" : index.ToString(CultureInfo.InvariantCulture);
                var library = CSharpCompilation.Create("InvestigationLibrary", [SupportTree("namespace InvestigationLibrary; public static class ExternalSupport { public static " + returnType + " Read() => " + expression + "; }", "Library.cs")],
                    baseline.References, new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
                using var stream = new MemoryStream();
                var emit = library.Emit(stream);
                Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
                return MetadataReference.CreateFromImage(stream.ToArray());
            }
        }
    }

    private static string BindingSource(string source, string fileName, string scenario)
    {
        var original = fileName switch { "Counter.akbura" => "Text=\"Akbura Counter\"", "Sidebar.akbura" => "Text=\"Dashboard\"", "NeedHelp.akbura" => "Text={Title}", _ => throw new ArgumentException(fileName) };
        var expression = scenario.StartsWith("reference-", StringComparison.Ordinal) ? "global::InvestigationLibrary.ExternalSupport.Read().ToString()" :
            scenario.StartsWith("real-", StringComparison.Ordinal) || scenario.StartsWith("semantic-", StringComparison.Ordinal) ? RealMemberExpression(scenario) :
            scenario == "const" ? "global::InvestigationFixture.Support.Number.ToString()" : "global::InvestigationFixture.Support.Read().ToString()";
        Assert.Contains(original, source);
        return source.Replace(original, "Text={" + expression + "}", StringComparison.Ordinal);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void VerifyFreshArtifacts(IEnumerable<AdditionalText> files, string directory, string rootNamespace,
        CSharpParseOptions parse, CSharpCompilation compilation, Dictionary<string, string> generated, string[] diagnostics, bool incomplete)
    {
        using var isolatedMeasurement = GenerationStatistics.BeginMeasurement();
        var fresh = Driver(files, directory, rootNamespace, parse: parse, reuse: false)
            .RunGeneratorsAndUpdateCompilation(compilation, out var output, out var currentDiagnostics);
        Assert.Null(fresh.GetRunResult().Results.Single().Exception);
        ValidateArtifactDiagnosticStamps(currentDiagnostics, ArtifactRequest(fresh).Version);
        Assert.DoesNotContain(currentDiagnostics, diagnostic => diagnostic.Id == "AKBURA_GENERATOR_FAILURE");
        if (!incomplete)
        {
            Clean(currentDiagnostics);
        }
        Assert.Equal(generated.OrderBy(pair => pair.Key), fresh.GetRunResult().Results.Single().GeneratedSources
            .ToDictionary(source => source.HintName, source => source.SourceText.ToString()).OrderBy(pair => pair.Key));
        Assert.Equal(diagnostics, DiagnosticKeys(currentDiagnostics.Concat(output.GetDiagnostics())));
    }

    private static string[] DiagnosticKeys(IEnumerable<Diagnostic> diagnostics) => diagnostics.Select(DiagnosticParity.Key).Order(StringComparer.Ordinal).ToArray();

    private static void ValidateArtifactDiagnosticStamps(IEnumerable<Diagnostic> diagnostics, long version)
    {
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Properties.TryGetValue("akbura.document-version", out var stamp))
            {
                Assert.InRange(long.Parse(stamp!, CultureInfo.InvariantCulture), 1, version);
            }
        }
    }

    private static string FileDigest(string path) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));

    private static string GitRevision()
    {
        using var process = Process.Start(new ProcessStartInfo("git", "rev-parse HEAD") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })!;
        var value = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        Assert.Equal(0, process.ExitCode);
        return value;
    }
}
