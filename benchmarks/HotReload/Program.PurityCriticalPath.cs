using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private static async Task MeasurePurityCriticalPath(string project, string sidebarPath, string scenario, int updates, int warmups,
        string outputDirectory, string observations, string toolDirectory)
    {
        project = Path.GetFullPath(project);
        sidebarPath = Path.GetFullPath(sidebarPath);
        outputDirectory = Path.GetFullPath(outputDirectory);
        var original = File.ReadAllText(sidebarPath);
        var compilationPath = Path.Combine(Path.GetDirectoryName(sidebarPath)!, "Sidebar.CriticalPath.cs");
        var commandPath = Path.Combine(outputDirectory, "ui-command.json");
        var metadataPath = Path.Combine(outputDirectory, "revision.json");
        var instrumented = observations != "off";
        var expectedMvids = new[] { "Akbura.BlackSilence", "Akbura.Generator" }.ToDictionary(name => name, name => CriticalMvid(Path.Combine(toolDirectory, name + ".dll")));
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)!, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "watch", "--verbose", "--non-interactive", "--project", project, "--no-restore", "run" })
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        info.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        info.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";
        info.Environment["AKBURA_CRITICAL_COMMAND"] = commandPath;
        info.Environment.Remove("AKBURA_WATCH_GC_OBSERVATIONS");
        info.Environment.Remove("AKBURA_WATCH_OBSERVATIONS");
        info.Environment.Remove("AKBURA_CRITICAL_REVISION");
        if (instrumented)
        {
            observations = Path.GetFullPath(observations);
            Directory.CreateDirectory(observations);
            info.Environment["AKBURA_WATCH_OBSERVATIONS"] = observations;
            info.Environment["AKBURA_CRITICAL_REVISION"] = metadataPath;
        }
        var sync = new object();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<JsonElement>? pending = null;
        var appPid = 0;
        var revision = 0;
        var nonce = "";
        var started = 0L;
        var closing = false;
        var boundaries = new Dictionary<string, double>();
        Exception? failure = null;
        double ackMs = 0;
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        void Fail(Exception error)
        {
            failure ??= error;
            ready.TrySetException(error);
            pending?.TrySetException(error);
        }
        void Output(string? line)
        {
            if (line == null)
            {
                return;
            }
            lock (sync)
            {
                var ms = started == 0 ? 0 : Elapsed(started);
                Write(new { Kind = "purity-critical-log", Revision = revision, Ms = ms, Text = line });
                foreach (var marker in new[] { ("WatchDetection", "File change: Update"), ("DocumentUpdated", "Solution after document update"),
                    ("Capabilities", "Hot reload capabilities:"), ("BatchSending", "Sending update batch"), ("RuntimeApplying", "Applying updates to module"),
                    ("RuntimeApplied", "Updates applied.") })
                {
                    if (started != 0 && line.Contains(marker.Item2, StringComparison.OrdinalIgnoreCase) && !boundaries.ContainsKey(marker.Item1))
                    {
                        boundaries.Add(marker.Item1, ms);
                    }
                }
                if (started != 0 && line.Contains("Update batch #", StringComparison.OrdinalIgnoreCase) && line.EndsWith("completed.", StringComparison.Ordinal))
                {
                    boundaries.TryAdd("BatchCompleted", ms);
                }
                if (line.Contains("PROBE_FAILURE", StringComparison.Ordinal) || line.Contains("Unhandled exception.", StringComparison.Ordinal) ||
                    line.Contains(": error ", StringComparison.OrdinalIgnoreCase) || line.Contains("Unable to apply hot reload", StringComparison.OrdinalIgnoreCase))
                {
                    Fail(new InvalidOperationException(line));
                }
                if (!line.StartsWith("CRITICAL_JSON ", StringComparison.Ordinal))
                {
                    return;
                }
                try
                {
                    var record = JsonDocument.Parse(line["CRITICAL_JSON ".Length..]).RootElement.Clone();
                    if (record.GetProperty("Kind").GetString() == "ready")
                    {
                        Assert.Equal(0, appPid);
                        appPid = record.GetProperty("Pid").GetInt32();
                        ready.TrySetResult();
                        return;
                    }
                    Assert.Equal(appPid, record.GetProperty("Pid").GetInt32());
                    Assert.Equal(revision, record.GetProperty("Revision").GetInt32());
                    Assert.Equal(nonce, record.GetProperty("Nonce").GetString());
                    Assert.Equal(scenario, record.GetProperty("Scenario").GetString());
                    Assert.NotNull(pending);
                    Assert.False(pending!.Task.IsCompleted, "Duplicate acknowledgment");
                    foreach (var name in new[] { "OwnerPreserved", "ControlsPreserved", "StatePreserved", "CommandsValid" })
                    {
                        Assert.True(record.GetProperty(name).GetBoolean(), name);
                    }
                    ackMs = ms;
                    pending.TrySetResult(record);
                }
                catch (Exception error)
                {
                    Fail(error);
                }
            }
        }
        process.OutputDataReceived += (_, item) => Output(item.Data);
        process.ErrorDataReceived += (_, item) => Output(item.Data);
        process.Exited += (_, _) =>
        {
            lock (sync)
            {
                if (!closing)
                {
                    Fail(new InvalidOperationException("Watch exited before the session completed."));
                }
            }
        };
        Write(new { Kind = "purity-critical-protocol", scenario, Updates = updates, Warmups = warmups, Instrumented = instrumented,
            ExpectedCompilerMvids = expectedMvids, Boundary = "File write closed to verified UI acknowledgment on the parent monotonic clock",
            StageMeaning = "Watch markers are received-log times, not exact CLR/EnC operation boundaries. GeneratorDriver and EnC durations stay unavailable without direct events." });
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(5));
            Write(new { Kind = "purity-critical-ready", OrchestratorPid = Environment.ProcessId, WatchLauncherPid = process.Id, ApplicationPid = appPid });
            if (Environment.GetEnvironmentVariable("AKBURA_CRITICAL_TRACE_REQUIRED") == "1")
            {
                var traceReady = Path.Combine(outputDirectory, "trace-ready.json");
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(traceReady))
                {
                    if (deadline.Elapsed.TotalSeconds > 45)
                    {
                        throw new TimeoutException("The diagnostic EventPipe collector did not acknowledge its attachment.");
                    }
                    await Task.Delay(100);
                }
            }
            for (var index = 1; index <= updates + warmups; index++)
            {
                await Task.Delay(500);
                var value = 10 + index % 70;
                var source = scenario switch
                {
                    "supported" => original.Replace("Margin=\"10\"", $"Margin=\"{value}\"", StringComparison.Ordinal),
                    "fallback" => original.Replace("Text=\"Dashboard\"", $"Text=\"Dashboard {value}\"", StringComparison.Ordinal),
                    "binding" => original.Replace("Thickness(10d)", "Thickness(" + value + "d)", StringComparison.Ordinal),
                    "compilation" => "namespace PurityUIDashborad.Components;\r\npartial class Sidebar\r\n{\r\n    private global::Avalonia.Thickness CriticalPathMargin => new global::Avalonia.Thickness(" + value + "d);\r\n" +
                        string.Join("\r\n", Enumerable.Range(1, index).Select(number => "    internal const int Declaration" + number + " = 0;")) + "\r\n}\r\n",
                    _ => throw new ArgumentException("Unknown critical-path scenario: " + scenario),
                };
                var target = scenario == "compilation" ? compilationPath : sidebarPath;
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
                Task<JsonElement> completion;
                double writeMs;
                lock (sync)
                {
                    if (failure != null)
                    {
                        throw failure;
                    }
                    revision++;
                    nonce = Guid.NewGuid().ToString("N");
                    boundaries.Clear();
                    pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                    completion = pending.Task;
                    CriticalPublish(commandPath, JsonSerializer.Serialize(new { Revision = revision, Nonce = nonce, Scenario = scenario,
                        Margin = scenario == "fallback" ? 10 : value, Title = scenario == "fallback" ? "Dashboard " + value : "Dashboard" }));
                    if (instrumented)
                    {
                        CriticalPublish(metadataPath, JsonSerializer.Serialize(new Dictionary<string, string> { ["Revision"] = revision.ToString(CultureInfo.InvariantCulture),
                            ["SourceName"] = Path.GetFileName(target), ["SourcePath"] = target, ["SourceSha256"] = digest }));
                    }
                    var writing = Stopwatch.GetTimestamp();
                    File.WriteAllText(target, source, new UTF8Encoding(false));
                    started = Stopwatch.GetTimestamp();
                    writeMs = Stopwatch.GetElapsedTime(writing).TotalMilliseconds;
                }
                var ack = await completion.WaitAsync(TimeSpan.FromMinutes(3));
                var records = new List<Dictionary<string, string>>();
                if (instrumented)
                {
                    for (var retry = 0; retry < 100; retry++)
                    {
                        records = Directory.GetFiles(observations, "*.json").Select(path => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!)
                            .Where(record => record.GetValueOrDefault("Revision") == revision.ToString(CultureInfo.InvariantCulture) &&
                                record.GetValueOrDefault("RevisionExpectedSha256") == digest && record.GetValueOrDefault("RevisionInputMatched") == "True").ToList();
                        if (records.Any(record => record.GetValueOrDefault("Kind") == "Generation" && record.GetValueOrDefault("Canceled") != "True" &&
                            record.GetValueOrDefault("PublishedSnapshotVersion") == record.GetValueOrDefault("Version")))
                        {
                            break;
                        }
                        await Task.Delay(100);
                    }
                    var publications = records.Where(record => record.GetValueOrDefault("Kind") == "Generation" && record.GetValueOrDefault("Canceled") != "True" &&
                        record.GetValueOrDefault("ErrorDiagnostics") == "0" && record.GetValueOrDefault("PublishedSnapshotVersion") == record.GetValueOrDefault("Version"))
                        .Select(record => record.GetValueOrDefault("Observer") + "/" + record.GetValueOrDefault("Pid") + "/" + record.GetValueOrDefault("Version")).ToHashSet(StringComparer.Ordinal);
                    Assert.NotEmpty(publications);
                    records = records.Where(record => publications.Contains(record.GetValueOrDefault("Observer") + "/" + record.GetValueOrDefault("Pid") + "/" + record.GetValueOrDefault("Version"))).ToList();
                    foreach (var record in records)
                    {
                        Assert.True(record.ContainsKey("ScopeElapsedMs"), "Missing observed scope duration; do not substitute zero");
                        Assert.Equal(expectedMvids["Akbura.BlackSilence"], record["CompilerMvid"]);
                        Assert.Equal(expectedMvids["Akbura.Generator"], record["GeneratorMvid"]);
                        Assert.Contains("dotnet-watch", record["HostCommandLine"], StringComparison.OrdinalIgnoreCase);
                        Write(new { Kind = "purity-critical-causal", Revision = revision, Scenario = scenario, Record = record });
                    }
                }
                lock (sync)
                {
                    if (failure != null)
                    {
                        throw failure;
                    }
                    Write(new { Kind = "purity-critical-sample", Revision = revision, Scenario = scenario, Warmup = index <= warmups,
                        FileWriteToObservedUiMs = ackMs, FileWriteDurationMs = writeMs, SourceSha256 = digest, SourcePath = target, Application = ack,
                        WatchLogBoundaries = new Dictionary<string, double>(boundaries), CausalRecords = records.Count,
                        AkburaRequestAndBatchInclusiveMs = instrumented ? records.Sum(record => double.Parse(record.GetValueOrDefault("ScopeElapsedMs") ?? "0", CultureInfo.InvariantCulture)) : (double?)null,
                        GeneratorDriverMs = (double?)null, EnCDeltaProductionMs = (double?)null,
                        GeneratorDriverEvidence = Environment.GetEnvironmentVariable("AKBURA_CRITICAL_TRACE_REQUIRED") == "1"
                            ? "Joined after trace drain in roslyn-spans.csv using an in-process revision/hash marker" : "Not collected in this session",
                        UnavailableMeaning = "Direct EnC interval unavailable. Do not attribute the residual to one stage or add overlapping stage times." });
                    started = 0;
                }
            }
            Write(new { Kind = "purity-critical-complete", Scenario = scenario, Updates = updates, Warmups = warmups, ApplicationPid = appPid, Restarts = 0 });
            if (Environment.GetEnvironmentVariable("AKBURA_CRITICAL_TRACE_REQUIRED") == "1")
            {
                var traceReady = Path.Combine(outputDirectory, "trace-ready.json");
                File.WriteAllText(traceReady + ".stop", "stop\r\n");
                var deadline = Stopwatch.StartNew();
                while (!File.Exists(traceReady + ".stopped"))
                {
                    if (deadline.Elapsed.TotalSeconds > 30)
                    {
                        throw new TimeoutException("The diagnostic collector did not drain before the owned watch process exit.");
                    }
                    await Task.Delay(100);
                }
            }
        }
        finally
        {
            lock (sync)
            {
                closing = true;
            }
            if (process.Id != 0 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
        }
    }

    private static void CriticalPublish(string path, string payload)
    {
        File.WriteAllText(path + ".tmp", payload + "\r\n", new UTF8Encoding(false));
        var timer = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                File.Move(path + ".tmp", path, overwrite: true);
                return;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException &&
                (error.HResult & 0xFFFF) is 5 or 32 or 33 && timer.Elapsed.TotalSeconds < 10)
            {
                Thread.Sleep(25);
            }
        }
    }

    private static string CriticalMvid(string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        var reader = pe.GetMetadataReader();
        return reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
    }
}
