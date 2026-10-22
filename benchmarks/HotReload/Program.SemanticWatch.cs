using System.Diagnostics;
using System.Globalization;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;

internal static partial class Program
{
    private static async Task MeasureSemanticWatch(string project, string commandPath, string scenario, int updates, int warmups, string observations)
    {
        project = Path.GetFullPath(project);
        commandPath = Path.GetFullPath(commandPath);
        var sourcePath = Path.Combine(Path.GetDirectoryName(project)!, "Counter.akbura");
        var compilationPath = Path.Combine(Path.GetDirectoryName(project)!, "CompilationControl.cs");
        var personPath = Path.Combine(Path.GetDirectoryName(project)!, "WatchPerson.cs");
        var original = File.ReadAllText(sourcePath);
        var originalCompilation = File.ReadAllText(compilationPath);
        var originalPerson = File.ReadAllText(personPath);
        Assert.Contains("Margin=\"10\"", original);
        var expectedMvids = XDocument.Load(project).Descendants("Analyzer").ToDictionary(
            node => Path.GetFileNameWithoutExtension(node.Attribute("Include")!.Value), node => ReadMvid(node.Attribute("Include")!.Value));
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.GetDirectoryName(project)!,
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in new[] { "watch", "--verbose", "--non-interactive", "--project", project, "--no-restore", "run" })
        {
            info.ArgumentList.Add(argument);
        }
        info.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        info.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        info.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"] = "1";
        info.Environment["AKBURA_WATCH_COMMAND"] = commandPath;
        info.Environment.Remove("AKBURA_WATCH_OBSERVATIONS");
        info.Environment.Remove("AKBURA_WATCH_GC_OBSERVATIONS");
        if (observations != "off")
        {
            observations = Path.GetFullPath(observations);
            Directory.CreateDirectory(observations);
            info.Environment["AKBURA_WATCH_OBSERVATIONS"] = observations;
            if (scenario == "stress")
            {
                info.Environment["AKBURA_WATCH_GC_OBSERVATIONS"] = "1";
            }
        }
        Write(new { Kind = "semantic-watch-protocol", scenario, Updates = updates, Warmups = warmups, ExpectedCompilerMvids = expectedMvids,
            Environment = new { DOTNET_CLI_UI_LANGUAGE = info.Environment["DOTNET_CLI_UI_LANGUAGE"],
                DOTNET_WATCH_SUPPRESS_EMOJIS = info.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"],
                DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH = info.Environment["DOTNET_WATCH_SUPPRESS_BROWSER_REFRESH"],
                AKBURA_WATCH_OBSERVATIONS = observations },
            Boundary = "Completed file write to received verified UI acknowledgment; intentional delay and post-update telemetry reading excluded." });
        var gate = new object();
        var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<JsonElement>? pending = null;
        TaskCompletionSource? diagnostic = null;
        TaskCompletionSource? diagnosticApplied = null;
        var revision = 0;
        var expected = 0;
        var expectedPhase = "";
        var applicationPid = 0;
        var shuttingDown = false;
        var started = 0L;
        var acknowledgedMs = 0.0;
        Exception? protocolFailure = null;
        double? detection = null;
        using var process = new Process { StartInfo = info, EnableRaisingEvents = true };
        void Fail(Exception exception)
        {
            protocolFailure ??= exception;
            ready.TrySetException(exception);
            pending?.TrySetException(exception);
            diagnostic?.TrySetException(exception);
            diagnosticApplied?.TrySetException(exception);
        }
        void Output(string? line)
        {
            if (line == null)
            {
                return;
            }
            lock (gate)
            {
                Write(new { Kind = "semantic-watch-log", scenario, Revision = expected, Ms = started == 0 ? 0 : Elapsed(started), Text = line });
                if (line.Contains("File change: Update", StringComparison.OrdinalIgnoreCase) || line.Contains("File changed:", StringComparison.OrdinalIgnoreCase))
                {
                    detection ??= started == 0 ? null : Elapsed(started);
                }
                if (line.Contains("MissingSemanticWatchMember", StringComparison.Ordinal) && line.Contains("error", StringComparison.OrdinalIgnoreCase))
                {
                    diagnostic?.TrySetResult();
                }
                if (diagnostic?.Task.IsCompletedSuccessfully == true && line.Contains("C# and Razor changes applied", StringComparison.Ordinal))
                {
                    diagnosticApplied?.TrySetResult();
                }
                if (line.Contains("PROBE_FAILURE", StringComparison.Ordinal) || line.Contains("Unhandled exception.", StringComparison.Ordinal))
                {
                    Fail(new InvalidOperationException(line));
                }
                if (!line.StartsWith("PROBE_JSON ", StringComparison.Ordinal))
                {
                    return;
                }
                try
                {
                    var value = JsonDocument.Parse(line["PROBE_JSON ".Length..]).RootElement.Clone();
                    var pid = value.GetProperty("Pid").GetInt32();
                    if (value.GetProperty("Kind").GetString() == "ready")
                    {
                        if (applicationPid != 0)
                        {
                            throw new InvalidOperationException("The application restarted.");
                        }
                        applicationPid = pid;
                        ready.TrySetResult(pid);
                    }
                    else
                    {
                        if (pid != applicationPid || value.GetProperty("Revision").GetInt32() != expected || pending == null || pending.Task.IsCompleted)
                        {
                            throw new InvalidOperationException("Duplicate, stale, out-of-order or foreign acknowledgment.");
                        }
                        Assert.Equal(expectedPhase, value.GetProperty("Phase").GetString());
                        foreach (var field in new[] { "StatePreserved", "InputPreserved", "ControlsPreserved", "UnaffectedBindingsValid" })
                        {
                            Assert.True(value.GetProperty(field).GetBoolean(), field);
                        }
                        var invalidExpression = value.GetProperty("Phase").GetString() == "invalid-runtime-check";
                        Assert.Equal(!invalidExpression, value.GetProperty("BindingsValid").GetBoolean());
                        Assert.Equal(invalidExpression ? 3 : 4, value.GetProperty("ValidatedReactiveBindings").GetInt32());
                        if (invalidExpression)
                        {
                            Assert.True(value.GetProperty("InvalidExpressionFrozen").GetBoolean());
                            Assert.Equal(value.GetProperty("ExpectedInvalidExpressionValue").GetString(), value.GetProperty("InvalidExpressionValue").GetString());
                        }
                        Assert.Equal(1, value.GetProperty("Subscriptions").GetInt32());
                        acknowledgedMs = Elapsed(started);
                        pending.TrySetResult(value);
                    }
                }
                catch (Exception exception)
                {
                    Fail(exception);
                }
            }
        }
        process.OutputDataReceived += (_, item) => Output(item.Data);
        process.ErrorDataReceived += (_, item) => Output(item.Data);
        process.Exited += (_, _) =>
        {
            lock (gate)
            {
                if (!shuttingDown)
                {
                    Fail(new InvalidOperationException("Watch exited before verification completed."));
                }
            }
        };
        var margin = 10;
        var width = 100;
        var title = "Akbura Counter";
        var marker = 0;
        var currentSource = original;
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            await ready.Task.WaitAsync(TimeSpan.FromMinutes(4));
            await Update("initial-check", null, warmup: true);
            for (var index = 1; index <= updates + warmups; index++)
            {
                await Task.Delay(500);
                if (scenario is "margin" or "stress")
                {
                    margin = 10 + index % 90;
                }
                else if (scenario == "text")
                {
                    title = "Akbura Counter " + index;
                }
                else if (scenario == "scope")
                {
                    width = 100 + index;
                }
                else if (scenario == "compilation")
                {
                    marker = index;
                }
                else
                {
                    throw new ArgumentException(scenario);
                }
                currentSource = Source();
                await Update(scenario, currentSource, index <= warmups, scenario == "compilation");
            }
            if (scenario == "stress")
            {
                foreach (var value in new[] { 21, 22, 21, 22, 21, 22 })
                {
                    margin = value;
                    await Update("undo-redo", currentSource = Source(), warmup: true);
                }
                title = "Akbura Counter unsupported";
                await Update("text-control", currentSource = Source(), warmup: true);
                width = 101;
                await Update("scope-control", currentSource = Source(), warmup: true);
                marker = 1;
                await Update("compilation-control", currentSource, warmup: true, compilationChange: true);
                var beforeInvalid = observations == "off" ? new HashSet<string>() : Directory.GetFiles(observations, "*.json").ToHashSet(StringComparer.Ordinal);
                var invalidSource = currentSource.Replace("Current.FirstName}", "Current.MissingSemanticWatchMember}", StringComparison.Ordinal);
                Assert.NotEqual(currentSource, invalidSource);
                lock (gate)
                {
                    diagnostic = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    diagnosticApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    File.WriteAllText(sourcePath, invalidSource, new UTF8Encoding(false));
                }
                await diagnostic.Task.WaitAsync(TimeSpan.FromMinutes(2));
                await diagnosticApplied.Task.WaitAsync(TimeSpan.FromMinutes(2));
                if (observations != "off")
                {
                    var invalidHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(invalidSource)));
                    var failures = Directory.GetFiles(observations, "*.json").Where(path => !beforeInvalid.Contains(path))
                        .Select(path => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!)
                        .Where(record => record.GetValueOrDefault("Kind") == "Generation" && record.GetValueOrDefault("source.Counter.akbura") == invalidHash &&
                            int.Parse(record["ErrorDiagnostics"], CultureInfo.InvariantCulture) > 0).ToArray();
                    Assert.NotEmpty(failures);
                    foreach (var failure in failures)
                    {
                        Assert.Equal(expectedMvids["Akbura.BlackSilence"], failure["CompilerMvid"]);
                        Assert.Equal(expectedMvids["Akbura.Generator"], failure["GeneratorMvid"]);
                        Assert.Contains("dotnet-watch", failure["HostCommandLine"], StringComparison.OrdinalIgnoreCase);
                        if (failure["ProbeStore"] != "0")
                        {
                            Assert.NotEqual(failure["Version"], failure["PublishedGenerationVersion"]);
                        }
                        Write(new { Kind = "semantic-watch-invalid-publication", Record = failure });
                    }
                }
                Write(new { Kind = "semantic-watch-diagnostic", applicationPid, ExpectedDiagnostic = true, FallbackApplied = true,
                    Contract = "The invalid member retains its last displayed value without a live binding; all three unaffected name bindings must remain reactive." });
                await Update("invalid-runtime-check", null, warmup: true);
                margin = 23;
                await Update("repair", currentSource = Source(), warmup: true);
                margin = 24;
                await Update("post-repair-margin", currentSource = Source(), warmup: true);
            }
            await Update("memory", null, warmup: true);
            if (protocolFailure != null)
            {
                throw protocolFailure;
            }
            Write(new { Kind = "semantic-watch-complete", scenario, Updates = updates, Warmups = warmups, applicationPid, Restarts = 0 });
        }
        finally
        {
            lock (gate)
            {
                shuttingDown = true;
            }
            if (process.Id != 0 && !process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync();
            }
            File.WriteAllText(sourcePath, original, new UTF8Encoding(false));
            File.WriteAllText(compilationPath, originalCompilation, new UTF8Encoding(false));
            File.WriteAllText(personPath, originalPerson, new UTF8Encoding(false));
        }

        string Source() => original.Replace("Margin=\"10\"", $"Margin=\"{margin}\"", StringComparison.Ordinal)
            .Replace("Width=\"100\"", $"Width=\"{width}\"", StringComparison.Ordinal)
            .Replace("using Avalonia.Controls;", width == 100 ? "using Avalonia.Controls;" : "using Avalonia.Controls;\r\nusing System;", StringComparison.Ordinal)
            .Replace("Text=\"Akbura Counter\"", $"Text=\"{title}\"", StringComparison.Ordinal);

        async Task Update(string phase, string? source, bool warmup, bool compilationChange = false)
        {
            var before = observations == "off" ? new HashSet<string>() : Directory.GetFiles(observations, "*.json").ToHashSet(StringComparer.Ordinal);
            Task<JsonElement> acknowledged;
            double writeMs;
            lock (gate)
            {
                if (protocolFailure != null)
                {
                    throw protocolFailure;
                }
                expected = ++revision;
                expectedPhase = phase;
                pending = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
                acknowledged = pending.Task;
                detection = null;
                var command = JsonSerializer.Serialize(new { Revision = expected, Margin = margin, Width = width, Title = title, CompilationMarker = marker, Phase = phase });
                File.WriteAllText(commandPath + ".tmp", command, new UTF8Encoding(false));
                File.Move(commandPath + ".tmp", commandPath, overwrite: true);
                var beforeWrite = Stopwatch.GetTimestamp();
                if (compilationChange)
                {
                    File.WriteAllText(personPath, originalPerson.Replace("get => _firstName;", "get => _firstName + global::System.String.Empty;", StringComparison.Ordinal), new UTF8Encoding(false));
                    File.WriteAllText(compilationPath, originalCompilation.Replace("=> 0;", $"=> {marker};", StringComparison.Ordinal), new UTF8Encoding(false));
                }
                else if (source != null)
                {
                    File.WriteAllText(sourcePath, source, new UTF8Encoding(false));
                }
                started = Stopwatch.GetTimestamp();
                writeMs = Elapsed(beforeWrite);
            }
            var ack = await acknowledged.WaitAsync(TimeSpan.FromMinutes(2));
            double latency;
            lock (gate)
            {
                if (protocolFailure != null)
                {
                    throw protocolFailure;
                }
                latency = acknowledgedMs;
            }
            var records = new List<Dictionary<string, string>>();
            if (observations != "off" && source != null)
            {
                var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source)));
                for (var retry = 0; retry < 50; retry++)
                {
                    records = Directory.GetFiles(observations, "*.json").Where(path => !before.Contains(path))
                        .Select(path => JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))!)
                        .Where(record => record.GetValueOrDefault("source.Counter.akbura") == digest).ToList();
                    if (records.Any(record => record.GetValueOrDefault("Kind") == "Generation" && record.GetValueOrDefault("source.Counter.akbura") == digest))
                    {
                        break;
                    }
                    await Task.Delay(100);
                }
                Assert.Contains(records, record => record.GetValueOrDefault("Kind") == "Generation" && record.GetValueOrDefault("source.Counter.akbura") == digest);
                foreach (var record in records)
                {
                    Assert.Equal(expectedMvids["Akbura.BlackSilence"], record["CompilerMvid"]);
                    Assert.Equal(expectedMvids["Akbura.Generator"], record["GeneratorMvid"]);
                    Assert.Contains("dotnet-watch", record["HostCommandLine"], StringComparison.OrdinalIgnoreCase);
                }
                static string PublicationKey(Dictionary<string, string> record) => string.Join("/", new[] { "Observer", "Pid", "ProjectState", "Version" }
                    .Select(name => record.GetValueOrDefault(name)));
                var published = records.Where(record => record.GetValueOrDefault("Kind") == "Generation" &&
                    record.GetValueOrDefault("Canceled") != "True" && record.GetValueOrDefault("ErrorDiagnostics") == "0" &&
                    record.GetValueOrDefault("PublishedSnapshotVersion") == record.GetValueOrDefault("Version") &&
                    (record.GetValueOrDefault("ProbeStore") == "0" || record.GetValueOrDefault("PublishedGenerationVersion") == record.GetValueOrDefault("Version")))
                    .Select(PublicationKey).ToHashSet(StringComparer.Ordinal);
                Assert.NotEmpty(published);
                records = records.Where(record => record.GetValueOrDefault("Kind") == "Environment" || published.Contains(PublicationKey(record))).ToList();
                foreach (var record in records)
                {
                    Write(new { Kind = "semantic-watch-causal", Revision = expected, Phase = phase, Record = record });
                }
            }
            Write(new { Kind = "semantic-watch-sample", Revision = expected, Phase = phase, Warmup = warmup,
                FileWriteToObservedUiMs = source == null ? (double?)null : latency, FileWriteDurationMs = writeMs,
                SourceDetectionLogMs = detection, SourceSha256 = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(source ?? currentSource))),
                Application = ack, WatchLauncherWorkingSetBytes = process.WorkingSet64, WatchLauncherPid = process.Id, CausalRecords = records.Count,
                Causality = observations == "off" ? "Unavailable in non-STATS production run" : "Actual watched analyzer records",
                GeneratorProcessingMs = (double?)null, ClrDeltaSubmissionMs = (double?)null });
        }

        static string ReadMvid(string path)
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            var reader = pe.GetMetadataReader();
            return reader.GetGuid(reader.GetModuleDefinition().Mvid).ToString();
        }
    }
}
