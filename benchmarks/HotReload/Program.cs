using Akbura;
using Akbura.BlackSilence;
using Akbura.HotReloadIntegrationTests;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;
using System.Diagnostics;
using System.Reflection;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private const int Warmup = 2;
    private const int Samples = 8;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    private static readonly CSharpParseOptions ParseOptions = CSharpParseOptions.Default
        .WithLanguageVersion(LanguageVersion.Preview).WithPreprocessorSymbols("DEBUG");

    [STAThread]
    public static int Main(string[] args)
    {
        Write(new { Kind = "environment", Utc = DateTime.UtcNow, Runtime = Environment.Version.ToString(),
            CpuCount = Environment.ProcessorCount, OS = Environment.OSVersion.ToString(),
            Roslyn = typeof(CSharpCompilation).Assembly.GetName().Version!.ToString(),
            ClrHotReload = MetadataUpdater.IsSupported, Warmup, Samples });
        switch (args.FirstOrDefault())
        {
            case "semantic-watch":
                MeasureSemanticWatch(args[1], args[2], args[3], int.Parse(args[4]), int.Parse(args[5]), args[6], args.ElementAtOrDefault(7)).GetAwaiter().GetResult();
                break;
            case "watch-instrumentation-check":
                Assert.Equal(args[1] == "stats", typeof(AkburaBlackSilenceGenerator).Assembly.GetType("Akbura.BlackSilence.BlackSilenceWatchObservation") != null);
                break;
            case "statistics-check":
                CheckStatisticsSnapshot();
                break;
            case "collections":
                MeasureCollections();
                break;
            case "runtime":
                Assert.True(MetadataUpdater.IsSupported, "Set DOTNET_MODIFIABLE_ASSEMBLIES=debug before starting this process.");
                AppBuilder.Configure<Application>().UseHeadless(new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
                foreach (var scenario in RuntimeScenarios())
                {
                    if (args.Length == 1 || scenario.Name.Contains(args[1], StringComparison.Ordinal))
                    {
                        MeasureRuntime(scenario);
                    }
                }
                break;
            case "generator":
                MeasureGenerator(args[1], args[2], args[3]);
                break;
            case "graph":
                MeasureGenerator(args[1], args[2], "scalar", graphOnly: true);
                break;
            case "invalidation":
                MeasureInvalidation(args[1], args[2], args.ElementAtOrDefault(3), args.ElementAtOrDefault(4) ?? "trace");
                break;
            case "localized-trace":
                MeasureInvalidation(args[1], args[2], args.ElementAtOrDefault(3) ?? "localized-literal", "trace",
                    verifyEveryUpdate: true, updates: 1);
                break;
            case "localized-check":
                MeasureInvalidation(args[1], args[2], args[3], args.ElementAtOrDefault(4) ?? "off", verifyEveryUpdate: true, updates: 1);
                break;
            case "artifacts":
                Assert.Contains(args[5], new[] { "enabled", "disabled" });
                MeasureInvalidation(args[1], args[2], args[3], args[4], args[5] == "enabled", verifyEveryUpdate: true);
                break;
            case "artifact-memory":
                Assert.Contains(args[4], new[] { "enabled", "disabled" });
                MeasureArtifactRetention(args[1], args[2], int.Parse(args[3]), args[4] == "enabled");
                break;
            case "member-memory":
            case "semantic-memory":
                Assert.Contains(args[4], new[] { "enabled", "disabled" });
                var concurrentSamples = int.Parse(args.ElementAtOrDefault(5) ?? "8");
                Assert.InRange(concurrentSamples, 3, 100);
                MeasureArtifactRetention(args[1], args[2], int.Parse(args[3]), args[4] == "enabled", realMembers: true, concurrentSamples,
                    semanticDependencies: args[0] == "semantic-memory");
                break;
            case "watch":
                MeasureWatch(args[1], args[2], args[3], args.ElementAtOrDefault(4) == "repair").GetAwaiter().GetResult();
                break;
            case "binlog":
                ReadBinlog(args[1]);
                break;
            case "style":
                foreach (var path in args.Skip(1))
                {
                    var root = CSharpSyntaxTree.ParseText(File.ReadAllText(path)).GetRoot();
                    Assert.DoesNotContain(root.DescendantNodes(), node => node is
                        Microsoft.CodeAnalysis.CSharp.Syntax.IfStatementSyntax { Statement: not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax } or
                        Microsoft.CodeAnalysis.CSharp.Syntax.ForStatementSyntax { Statement: not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax } or
                        Microsoft.CodeAnalysis.CSharp.Syntax.ForEachStatementSyntax { Statement: not Microsoft.CodeAnalysis.CSharp.Syntax.BlockSyntax });
                }
                break;
            default:
                throw new ArgumentException("collections | runtime [filter] | generator <MSBuild-items.json> <file> <scenario> | graph <MSBuild-items.json> <file> | invalidation <MSBuild-items.json> <file> [scenario] [off|stats|trace] | artifacts <MSBuild-items.json> <file> <scenario> <off|stats|trace> <enabled|disabled> | artifact-memory <MSBuild-items.json> <file> <revisions> <enabled|disabled> | member-memory <MSBuild-items.json> <file> <revisions> <enabled|disabled> [concurrent-samples] | watch <project> <file> <component> | binlog <path> | style <files...>");
        }
        return 0;
    }

    private static IEnumerable<RuntimeScenario> RuntimeScenarios()
    {
        foreach (var nodes in new[] { 1, 100, 500 })
        {
            yield return new("property-" + nodes, "property", nodes);
            yield return new("akcss-token-" + nodes, "akcss-token", nodes);
        }
        foreach (var kind in new[] { "insert-remove", "reorder", "replace-type", "binding", "event", "conditional", "foreach", "state-initializer", "state-add-remove", "akcss-definition", "akcss-order" })
        {
            yield return new(kind, kind, 20);
        }
        yield return new("property-10-owners", "property", 20, 10);
        yield return new("property-100-owners", "property", 20, 100);
    }

    private static void MeasureRuntime(RuntimeScenario scenario)
    {
        var directory = Path.Combine(Path.GetTempPath(), "AkburaBenchmark", Guid.NewGuid().ToString("N"));
        var compilation = CSharpCompilation.Create("AkburaBenchmark_" + Guid.NewGuid().ToString("N"),
            [Tree("using Akbura; using Akbura.Engine; namespace Demo; public partial class Counter : AkburaControl { public Counter() : base(AkburaEngine.Empty) { } }", Path.Combine(directory, "Counter.akbura.cs"))],
            References(), new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Debug, nullableContextOptions: NullableContextOptions.Enable));
        var component = new Input(Path.Combine(directory, "Counter.akbura"), Markup(scenario, 0));
        var styles = new Input(Path.Combine(directory, "Styles.akcss"), Styles(scenario, 0));
        GeneratorDriver driver = Driver([component, styles], directory);
        var start = Stopwatch.GetTimestamp();
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var output, out var diagnostics);
        var initialGeneratorMs = Elapsed(start);
        Clean(diagnostics.Concat(output.GetDiagnostics()));
        using var reload = new ClrHotReloadSession(output, "Demo.Counter", includeAkcssTypes: true);
        var windows = Enumerable.Range(0, scenario.Owners).Select(_ => new Window
        {
            Content = Activator.CreateInstance(reload.ComponentType), Width = 800, Height = 600,
        }).ToArray();
        foreach (var window in windows)
        {
            window.Show();
        }
        var owners = windows.Select(window => (AkburaControl)window.Content!).ToArray();
        var roots = owners.Select(owner => owner.Child).ToArray();
        var countProperty = reload.ComponentType.GetProperty("count", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!;
        foreach (var owner in owners)
        {
            countProperty.SetValue(owner, 42);
        }
        Write(new { Kind = "setup", Scenario = scenario.Name, initialGeneratorMs, Nodes = scenario.Nodes, scenario.Owners });
        try
        {
            for (var index = 1; index <= Warmup + Samples; index++)
            {
                var replacement = new Input(component.Path, Markup(scenario, index));
                var nextStyles = new Input(styles.Path, Styles(scenario, index));
                var row = new Timing { Kind = "runtime", Scenario = scenario.Name, Index = index, Warmup = index <= Warmup };
                var total = Stopwatch.GetTimestamp();
                driver = driver.ReplaceAdditionalText(component, replacement).ReplaceAdditionalText(styles, nextStyles);
                start = Stopwatch.GetTimestamp();
                driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out diagnostics);
                row.GeneratorMs = Elapsed(start);
                start = Stopwatch.GetTimestamp();
                Clean(diagnostics.Concat(updated.GetDiagnostics()));
                row.ValidationMs = Elapsed(start);
                start = Stopwatch.GetTimestamp();
                reload.ApplyUpdate(updated);
                row.DeltaMs = Elapsed(start);
                start = Stopwatch.GetTimestamp();
                reload.DispatchMetadataUpdateHandlers();
                row.HandlerMs = Elapsed(start);
                row.TotalMs = Elapsed(total);
                for (var ownerIndex = 0; ownerIndex < owners.Length; ownerIndex++)
                {
                    var root = Assert.IsType<Border>(owners[ownerIndex].Child);
                    Assert.Same(roots[ownerIndex], root);
                    Assert.Equal(42, countProperty.GetValue(owners[ownerIndex]));
                    Assert.Equal(scenario.Kind is "property" or "akcss-token" or "akcss-definition" ? 10 + index : 10, root.Width);
                    var panel = Assert.IsType<StackPanel>(root.Child);
                    var expectedChildren = scenario.Nodes + (scenario.Kind == "insert-remove" && index % 2 == 1 || scenario.Kind == "conditional" && index % 2 == 0 ? 1 : 0);
                    Assert.Equal(expectedChildren, panel.Children.Count);
                    if (scenario.Kind == "binding")
                    {
                        Assert.Equal("42:" + index, Assert.IsType<TextBlock>(panel.Children[0]).Text);
                    }
                    if (scenario.Kind == "replace-type")
                    {
                        Assert.Equal(index % 2 == 0 ? typeof(TextBlock) : typeof(Button), panel.Children[0].GetType());
                    }
                    if (scenario.Kind == "reorder")
                    {
                        Assert.Equal("item-" + (index % 2 == 0 ? 0 : scenario.Nodes - 1), Assert.IsType<TextBlock>(panel.Children[0]).Text);
                    }
                    if (scenario.Kind == "akcss-order")
                    {
                        Assert.Equal(index % 2 == 0 ? 20 : 30, root.Height);
                    }
                    if (scenario.Kind == "foreach")
                    {
                        Assert.Equal("0:" + index, Assert.IsType<TextBlock>(panel.Children[0]).Text);
                    }
                    if (scenario.Kind == "event")
                    {
                        Assert.IsType<Button>(panel.Children[0]).RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                        Assert.Equal(42 + index, countProperty.GetValue(owners[ownerIndex]));
                        countProperty.SetValue(owners[ownerIndex], 42);
                    }
                }
                component = replacement;
                styles = nextStyles;
                Write(row);
            }
        }
        finally
        {
            foreach (var window in windows)
            {
                window.Close();
            }
        }
    }

    private static string Markup(RuntimeScenario scenario, int index)
    {
        var state = "state int count = " + (scenario.Kind == "state-initializer" ? 7 + index : 7) + "; ";
        if (scenario.Kind == "state-add-remove" && index % 2 == 1)
        {
            state += "state bool enabled = true; ";
        }
        var attributes = scenario.Kind switch
        {
            "akcss-token" => "w-" + (10 + index),
            "akcss-definition" => "w-10",
            "akcss-order" => index % 2 == 0 ? "Width=\"10\" h-30 h-20" : "Width=\"10\" h-20 h-30",
            _ => "Width=\"" + (scenario.Kind == "property" ? 10 + index : 10) + "\"",
        };
        IEnumerable<int> items = Enumerable.Range(0, scenario.Nodes);
        if (scenario.Kind == "reorder" && index % 2 == 1)
        {
            items = items.Reverse();
        }
        var children = string.Concat(items.Select(item => item == 0 && scenario.Kind == "binding"
            ? "<TextBlock Text={count.ToString() + \":" + index + "\"} />"
            : item == 0 && scenario.Kind == "event"
                ? "<Button Content=\"event\" Click={count += " + index + "} />"
            : item == 0 && scenario.Kind == "replace-type" && index % 2 == 1
                ? "<Button Content=\"item-0\" />" : "<TextBlock Text=\"item-" + item + "\" />"));
        if (scenario.Kind == "insert-remove" && index % 2 == 1)
        {
            children += "<TextBlock Text=\"extra\" />";
        }
        if (scenario.Kind == "conditional")
        {
            children += "$if (count > " + (index % 2 == 0 ? 0 : 100) + ") { <TextBlock Text=\"conditional\" /> }";
        }
        if (scenario.Kind == "foreach")
        {
            children = "$foreach (var item in new[] { " + string.Join(",", Enumerable.Range(0, scenario.Nodes)) +
                " }) { <TextBlock Text={item.ToString() + \":" + index + "\"} /> }";
        }
        return "using Avalonia.Controls; using Demo.Styles.akcss; namespace Demo; " + state +
            "<Border " + attributes + "><StackPanel>" + children + "</StackPanel></Border>";
    }

    private static string Styles(RuntimeScenario scenario, int index) =>
        "@using Avalonia.Controls; @utilities { Border.w-(double value) { Width: value + " +
        (scenario.Kind == "akcss-definition" ? index : 0) + "; } Border.h-(double value) { Height: value; } }";

    private static void MeasureGenerator(string itemsPath, string fileName, string scenario, bool graphOnly = false)
    {
        using var input = JsonDocument.Parse(File.ReadAllText(itemsPath));
        var properties = input.RootElement.GetProperty("Properties");
        var items = input.RootElement.GetProperty("Items");
        var directory = properties.GetProperty("MSBuildProjectDirectory").GetString()!;
        var parse = ParseOptions.WithPreprocessorSymbols(properties.GetProperty("DefineConstants").GetString()!.Split(';', StringSplitOptions.RemoveEmptyEntries));
        var trees = Paths("Compile").Select(path => CSharpSyntaxTree.ParseText(SourceText.From(File.ReadAllText(path), Encoding.UTF8), parse, path)).ToArray();
        var compilation = CSharpCompilation.Create(properties.GetProperty("RootNamespace").GetString()!, trees,
            Paths("ReferencePath").Select(path => MetadataReference.CreateFromFile(path)),
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));
        var files = Paths("AdditionalFiles").Select(path => new Input(path, File.ReadAllText(path))).ToArray();
        var original = files.Single(file => Path.GetFileName(file.Path) == fileName);
        var mode = scenario == "diagnostics-off" ? "Off" : "Publish";
        GeneratorDriver driver = Driver(files, directory, properties.GetProperty("RootNamespace").GetString()!, mode, parse);
        var previous = original;
        var previousSources = new Dictionary<string, string>();
        string[]? baselineCompilerErrors = null;
        for (var index = 0; index <= Warmup + Samples; index++)
        {
            if (scenario == "cold")
            {
                driver = Driver(files, directory, properties.GetProperty("RootNamespace").GetString()!, mode, parse);
            }
            var currentCompilation = compilation;
            if (index > 0 && scenario is not ("cold" or "noop"))
            {
                if (scenario == "csharp")
                {
                    currentCompilation = compilation.AddSyntaxTrees(CSharpSyntaxTree.ParseText("internal static class BenchmarkUnrelated { public static int Value() => " + index + "; }", parse, "BenchmarkUnrelated.cs"));
                }
                else
                {
                    var text = scenario == "whitespace" ? original.GetText() + new string(' ', index) : Edit(original.GetText().ToString(), fileName, index);
                    var next = new Input(original.Path, text);
                    driver = driver.ReplaceAdditionalText(previous, next);
                    previous = next;
                }
            }
            var start = Stopwatch.GetTimestamp();
            driver = driver.RunGeneratorsAndUpdateCompilation(currentCompilation, out var output, out var diagnostics);
            var elapsed = Elapsed(start);
            Clean(diagnostics);
            var generated = driver.GetRunResult().Results.Single().GeneratedSources.ToDictionary(source => source.HintName, source => source.SourceText.ToString());
            var changed = generated.Where(pair => !previousSources.TryGetValue(pair.Key, out var old) || pair.Value != old).Select(pair => pair.Key).ToArray();
            Write(new { Kind = graphOnly ? "graph" : "generator", Scenario = fileName + ":" + scenario, Index = index, Warmup = index <= Warmup,
                GeneratorMs = elapsed, Sources = generated.Count, Changed = changed,
                Requests = graphOnly ? RequestCounts(driver) : null,
                Steps = driver.GetRunResult().Results.Single().TrackedSteps.ToDictionary(pair => pair.Key,
                    pair => pair.Value.SelectMany(step => step.Outputs).GroupBy(result => result.Reason.ToString()).ToDictionary(group => group.Key, group => group.Count())) });
            previousSources = generated;
            if (index == 0 || index == Warmup + Samples)
            {
                var compilerErrors = ErrorKeys(output.GetDiagnostics());
                baselineCompilerErrors ??= compilerErrors;
                Assert.Equal(baselineCompilerErrors, compilerErrors);
                Write(new { Kind = "generator-validation", Scenario = fileName + ":" + scenario, Index = index, BaselineCompilerErrors = compilerErrors });
                if (index == Warmup + Samples)
                {
                    var freshFiles = files.Select(file => file.Path == original.Path ? previous : file);
                    var fresh = Driver(freshFiles, directory, properties.GetProperty("RootNamespace").GetString()!, mode, parse)
                        .RunGeneratorsAndUpdateCompilation(currentCompilation, out var freshOutput, out var freshDiagnostics);
                    Clean(freshDiagnostics);
                    var freshSources = fresh.GetRunResult().Results.Single().GeneratedSources.ToDictionary(source => source.HintName, source => source.SourceText.ToString());
                    Assert.Equal(generated.OrderBy(pair => pair.Key), freshSources.OrderBy(pair => pair.Key));
                    Assert.Equal(compilerErrors, ErrorKeys(freshOutput.GetDiagnostics()));
                    Write(new { Kind = "generator-parity", Scenario = fileName + ":" + scenario, Equal = true });
                }
            }
        }

        IEnumerable<string> Paths(string name) => items.GetProperty(name).EnumerateArray()
            .Select(item => item.GetProperty("FullPath").GetString()!).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase);

        static string[] ErrorKeys(IEnumerable<Diagnostic> diagnostics) => diagnostics
            .Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
            .Select(diagnostic => diagnostic.Id + ":" + diagnostic.GetMessage() + ":" + diagnostic.Location.GetLineSpan().Path)
            .Order(StringComparer.Ordinal).ToArray();
    }

    private static Dictionary<string, object> RequestCounts(GeneratorDriver driver)
    {
        var request = driver.GetRunResult().Results.Single().TrackedSteps["BlackSilence.GenerationRequests"].Single().Outputs.Single().Value!;
        var counts = new Dictionary<string, object>();
        foreach (var kind in new[] { "Components", "ExternalAkcss", "InlineAkcss", "Diagnostics" })
        {
            var values = ((System.Collections.IEnumerable)request.GetType().GetProperty(kind)!.GetValue(request)!).Cast<object>().ToArray();
            var dirty = values.Count(value => value.GetType().GetProperty("Previous")!.GetValue(value) == null);
            counts.Add(kind, new { Total = values.Length, Recompute = dirty, Reuse = values.Length - dirty });
        }
        return counts;
    }

    private static string Edit(string source, string fileName, int index, bool declarationChanges = false)
    {
        var edited = fileName switch
        {
            "Sidebar.akbura" => source.Replace("Text=\"Dashboard\"", "Text=\"Dashboard " + (index + 1) + "\"", StringComparison.Ordinal),
            "NeedHelp.akbura" => source.Replace("StrokeThickness=\"1\"", "StrokeThickness=\"" + (index + 1) + "\"", StringComparison.Ordinal),
            "Counter.akbura" => source.Replace("Akbura Counter", "Akbura Counter " + index, StringComparison.Ordinal),
            "MemberViewModel.cs" => declarationChanges ? EditMemberDeclarations(source, index) :
                source.Replace("return _prefix + \" 0\";", "return _prefix + \" " + index + "\";", StringComparison.Ordinal),
            _ => throw new ArgumentException("No scalar edit configured for " + fileName),
        };
        Assert.NotEqual(source, edited);
        return edited;
    }

    private static string EditMemberDeclarations(string source, int index)
    {
        var methods = string.Join("\r\n", Enumerable.Range(1, index).Select(value =>
            "    private string Label" + value + "() => _prefix + \" " + value + "\";"));
        var end = source.LastIndexOf('}');
        return source.Substring(0, end).Replace("return _prefix + \" 0\";", "return Label" + index + "();", StringComparison.Ordinal) +
            methods + "\r\n" + source.Substring(end);
    }

    private static async Task MeasureWatch(string projectPath, string sourcePath, string component, bool diagnosticRepair = false)
    {
        projectPath = Path.GetFullPath(projectPath);
        sourcePath = Path.GetFullPath(sourcePath);
        var originalBytes = File.ReadAllBytes(sourcePath);
        var original = File.ReadAllText(sourcePath);
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(projectPath)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "watch", "--verbose", "--non-interactive", "--project", projectPath, "--no-restore", "run" })
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        info.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        info.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";
        info.Environment["AKBURA_TIMING_COMPONENT"] = component;
        var gate = new object();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<double>? pending = null;
        TaskCompletionSource? diagnosticObserved = null;
        long started = 0;
        var expected = 0;
        var processId = "";
        var applicationRestarted = false;
        double? detectionLogMs = null;
        double? clearCacheMs = null;
        double? applicationCallbackMs = null;
        using var process = new Process { StartInfo = info };
        void Output(string? line)
        {
            if (line == null)
            {
                return;
            }
            lock (gate)
            {
                Write(new { Kind = "watch-log", Scenario = component, Ms = started == 0 ? 0 : Elapsed(started), Text = line });
                if (started != 0 && (line.Contains("File changed:", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("File change: Update", StringComparison.OrdinalIgnoreCase)))
                {
                    detectionLogMs ??= Elapsed(started);
                }
                if (started != 0 && line.StartsWith("MEMBER_METADATA_CLEAR ", StringComparison.Ordinal))
                {
                    clearCacheMs ??= Elapsed(started);
                }
                if (started != 0 && line.StartsWith("MEMBER_METADATA_APPLIED ", StringComparison.Ordinal))
                {
                    applicationCallbackMs ??= Elapsed(started);
                }
                if (line.StartsWith("PROBE_READY pid=", StringComparison.Ordinal))
                {
                    if (processId.Length != 0)
                    {
                        applicationRestarted = true;
                        pending?.TrySetException(new InvalidOperationException("The application restarted."));
                    }
                    processId = line["PROBE_READY pid=".Length..];
                    ready.TrySetResult();
                }
                if (line.Contains("error", StringComparison.OrdinalIgnoreCase) && line.Contains("MissingSemanticWatchMember", StringComparison.Ordinal))
                {
                    diagnosticObserved?.TrySetResult();
                }
                if (line.Contains("PROBE_VALUE " + expected + " ", StringComparison.Ordinal))
                {
                    if (!line.Contains("pid=" + processId + " ", StringComparison.Ordinal) || !line.Contains("same_control=True", StringComparison.Ordinal))
                    {
                        pending?.TrySetException(new InvalidOperationException("Live control identity was not preserved: " + line));
                    }
                    else
                    {
                        pending?.TrySetResult(Elapsed(started));
                    }
                }
                if (line.Contains("Unhandled exception.", StringComparison.Ordinal))
                {
                    var failure = new InvalidOperationException(line);
                    ready.TrySetException(failure);
                    pending?.TrySetException(failure);
                }
            }
        }
        process.OutputDataReceived += (_, item) => Output(item.Data);
        process.ErrorDataReceived += (_, item) => Output(item.Data);
        var startup = Stopwatch.GetTimestamp();
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(4));
            Write(new { Kind = "watch-startup", Scenario = component, Ms = Elapsed(startup) });
            for (var index = 1; index <= 1 + Samples; index++)
            {
                await Task.Delay(1000);
                Task<double> completed;
                lock (gate)
                {
                    Assert.False(applicationRestarted, "The original application process must survive every edit.");
                    expected = index + 1;
                    detectionLogMs = null;
                    clearCacheMs = null;
                    applicationCallbackMs = null;
                    pending = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                    completed = pending.Task;
                    started = Stopwatch.GetTimestamp();
                    File.WriteAllText(sourcePath, Edit(original, Path.GetFileName(sourcePath), index, component == "MemberDeclarations"), new UTF8Encoding(false));
                }
                var elapsed = await completed.WaitAsync(TimeSpan.FromMinutes(2));
                if (Path.GetFileName(sourcePath) == "MemberViewModel.cs")
                {
                    // Keep behavior latency at the observation instant; allow an
                    // independently scheduled post-delta callback to be logged too.
                    await Task.Delay(50);
                }
                Assert.False(applicationRestarted, "The application restarted during an edit.");
                Write(new { Kind = "watch", Scenario = component, Index = index, Warmup = index == 1, TotalMs = elapsed,
                    ApplicationPid = processId,
                    SourceDetectionLogMs = detectionLogMs, PostDeltaClearCacheCallbackMs = clearCacheMs,
                    PostDeltaApplicationCallbackMs = applicationCallbackMs, GeneratorProcessingMs = (double?)null, DeltaSubmissionMs = (double?)null });
            }
            if (diagnosticRepair)
            {
                Assert.Contains("<TextBlock Text={firstName} />", original);
                lock (gate)
                {
                    pending = null;
                    diagnosticObserved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    started = Stopwatch.GetTimestamp();
                    var invalid = Edit(original, Path.GetFileName(sourcePath), 1 + Samples)
                        .Replace("<TextBlock Text={firstName} />", "<TextBlock Text={firstName.MissingSemanticWatchMember} />", StringComparison.Ordinal);
                    File.WriteAllText(sourcePath, invalid, new UTF8Encoding(false));
                }
                await diagnosticObserved.Task.WaitAsync(TimeSpan.FromMinutes(2));
                Write(new { Kind = "watch-diagnostic", Scenario = component, ApplicationPid = processId,
                    DiagnosticObserved = true, Ms = Elapsed(started) });
                await Task.Delay(1000);
                Task<double> repaired;
                lock (gate)
                {
                    Assert.False(applicationRestarted, "The process restarted after the invalid edit.");
                    diagnosticObserved = null;
                    expected = Samples + 3;
                    pending = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
                    repaired = pending.Task;
                    started = Stopwatch.GetTimestamp();
                    File.WriteAllText(sourcePath, Edit(original, Path.GetFileName(sourcePath), Samples + 2), new UTF8Encoding(false));
                }
                var repairMs = await repaired.WaitAsync(TimeSpan.FromMinutes(2));
                Assert.False(applicationRestarted, "The process restarted during diagnostic repair.");
                Write(new { Kind = "watch-repair", Scenario = component, ApplicationPid = processId,
                    DiagnosticRepaired = true, TotalMs = repairMs, GeneratorProcessingMs = (double?)null, DeltaSubmissionMs = (double?)null });
            }
        }
        finally
        {
            if (process.Id != 0 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            File.WriteAllBytes(sourcePath, originalBytes);
        }
    }

    private static void ReadBinlog(string path)
    {
        var starts = new Dictionary<string, DateTime>();
        var source = new Microsoft.Build.Logging.BinaryLogReplayEventSource();
        DateTime start = default;
        source.BuildStarted += (_, item) => start = item.Timestamp;
        source.BuildFinished += (_, item) => Write(new { Kind = "build", Scenario = Path.GetFileNameWithoutExtension(path), TotalMs = (item.Timestamp - start).TotalMilliseconds, item.Succeeded });
        source.TaskStarted += (_, item) => starts[Key(item.BuildEventContext!)] = item.Timestamp;
        source.TaskFinished += (_, item) =>
        {
            if (starts.Remove(Key(item.BuildEventContext!), out var began) && item.TaskName is "CheckAkburaModuleManifest" or "GenerateAkburaModuleManifest" or "Csc")
            {
                Write(new { Kind = "build-task", Scenario = Path.GetFileNameWithoutExtension(path), item.TaskName, item.ProjectFile, Ms = (item.Timestamp - began).TotalMilliseconds });
            }
        };
        source.Replay(path);

        static string Key(Microsoft.Build.Framework.BuildEventContext context) => $"{context.NodeId}:{context.ProjectContextId}:{context.TaskId}";
    }

    private static GeneratorDriver Driver(IEnumerable<AdditionalText> files, string directory, string rootNamespace = "Demo", string mode = "Publish", CSharpParseOptions? parse = null, bool reuse = true) =>
        CSharpGeneratorDriver.Create([new AkburaBlackSilenceGenerator().AsSourceGenerator()], files, parse ?? ParseOptions,
            new ProjectOptions(directory, rootNamespace, mode, reuse), driverOptions: new GeneratorDriverOptions(default, true));

    private static SyntaxTree Tree(string text, string path) => CSharpSyntaxTree.ParseText(SourceText.From(text, Encoding.UTF8), ParseOptions, path);
    private static double Elapsed(long started) => Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    private static void Write(object value) => Console.WriteLine(JsonSerializer.Serialize(value, Json));

    private static void Clean(IEnumerable<Diagnostic> diagnostics)
    {
        var errors = diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
        Assert.True(errors.Length == 0, string.Join(Environment.NewLine, errors.Select(error => error.ToString())));
    }

    private static IEnumerable<MetadataReference> References()
    {
        var paths = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator).ToHashSet(StringComparer.OrdinalIgnoreCase);
        paths.Add(typeof(AkburaControl).Assembly.Location);
        paths.Add(typeof(AvaloniaObject).Assembly.Location);
        paths.Add(typeof(Control).Assembly.Location);
        return paths.Select(path => MetadataReference.CreateFromFile(path));
    }

    private sealed record RuntimeScenario(string Name, string Kind, int Nodes, int Owners = 1);
    private sealed class Timing
    {
        public required string Kind { get; init; }
        public required string Scenario { get; init; }
        public int Index { get; init; }
        public bool Warmup { get; init; }
        public double GeneratorMs { get; set; }
        public double ValidationMs { get; set; }
        public double DeltaMs { get; set; }
        public double HandlerMs { get; set; }
        public double TotalMs { get; set; }
    }
    private sealed class Input(string path, string text) : AdditionalText
    {
        private readonly SourceText _text = SourceText.From(text, Encoding.UTF8);
        public override string Path => path;
        public override SourceText GetText(CancellationToken cancellationToken = default) => _text;
    }
    private sealed class ProjectOptions(string directory, string rootNamespace, string mode, bool reuse = true) : AnalyzerConfigOptionsProvider
    {
        private static readonly AnalyzerConfigOptions Empty = new Options(new Dictionary<string, string>());
        public override AnalyzerConfigOptions GlobalOptions { get; } = new Options(new Dictionary<string, string>
        {
            ["build_property.RootNamespace"] = rootNamespace,
            ["build_property.ProjectDir"] = directory + Path.DirectorySeparatorChar,
            ["build_property.AkburaBlackSilenceDiagnostics"] = mode,
            ["build_property.AkburaCrossCompilationReuse"] = reuse.ToString(),
        });
        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => Empty;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => Empty;
    }
    private sealed class Options(IReadOnlyDictionary<string, string> values) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value) => values.TryGetValue(key, out value!);
    }
}
