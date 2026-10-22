using Microsoft.Diagnostics.Runtime;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;

if (args.Length != 2)
{
    throw new ArgumentException("Usage: WatchDiagnostics <local-dump> <sanitized-output-directory>");
}
var dump = Path.GetFullPath(args[0]);
var output = Path.GetFullPath(args[1]);
if (Directory.Exists(output))
{
    throw new IOException("Preserve existing offline evidence; use a new directory.");
}
Directory.CreateDirectory(output);
using var target = DataTarget.LoadDump(dump);
using var runtime = target.ClrVersions.Single().CreateRuntime();
var heap = runtime.Heap;
if (!heap.CanWalkHeap)
{
    throw new InvalidOperationException("The captured heap cannot be walked.");
}
var timer = Stopwatch.StartNew();
var parents = new Dictionary<ulong, Edge>();
var queue = new Queue<ClrObject>();
var histograms = new Dictionary<string, Histogram>(StringComparer.Ordinal);
var examples = new Dictionary<string, List<(ulong Address, ulong Size)>>(StringComparer.Ordinal);
var rootCount = 0;
var processed = 0;
foreach (var root in heap.EnumerateRoots())
{
    rootCount++;
    var obj = root.Object;
    if (obj.IsValid && !obj.IsFree && parents.TryAdd(obj.Address, new Edge(0, "GC root: " + root.RootKind)))
    {
        queue.Enqueue(obj);
    }
}
while (queue.TryDequeue(out var obj))
{
    if (++processed % 100000 == 0)
    {
        Console.WriteLine($"PROGRESS root-reachable objects={processed} queued={queue.Count} elapsedSeconds={timer.Elapsed.TotalSeconds:F1}");
    }
    var name = obj.Type?.Name ?? "<unknown>";
    if (!histograms.TryGetValue(name, out var histogram))
    {
        histograms.Add(name, histogram = new Histogram());
    }
    histogram.Count++;
    var size = obj.Size;
    histogram.Bytes += size;
    if (Candidate(name))
    {
        if (!examples.TryGetValue(name, out var addresses))
        {
            examples.Add(name, addresses = []);
        }
        if (addresses.Count < 3)
        {
            addresses.Add((obj.Address, size));
        }
        else
        {
            var smallest = addresses.MinBy(item => item.Size);
            if (size > smallest.Size)
            {
                addresses.Remove(smallest);
                addresses.Add((obj.Address, size));
            }
        }
    }
    foreach (var child in obj.EnumerateReferences(carefully: true, considerDependantHandles: true))
    {
        if (child.IsValid && !child.IsFree && parents.TryAdd(child.Address, new Edge(obj.Address, "")))
        {
            queue.Enqueue(child);
        }
    }
}
var lines = new List<string> { "\"Type\",\"RootReachableCount\",\"ShallowBytes\"" };
foreach (var pair in histograms.OrderByDescending(pair => pair.Value.Bytes).ThenBy(pair => pair.Key, StringComparer.Ordinal))
{
    lines.Add("\"" + pair.Key.Replace("\"", "\"\"") + "\"," + pair.Value.Count.ToString(CultureInfo.InvariantCulture) + "," + pair.Value.Bytes.ToString(CultureInfo.InvariantCulture));
}
Save("retained-types.csv", string.Join("\r\n", lines));
var paths = new List<object>();
foreach (var pair in examples.OrderBy(pair => pair.Key, StringComparer.Ordinal))
{
    foreach (var example in pair.Value)
    {
        var address = example.Address;
        var chain = new List<string>();
        var current = address;
        while (current != 0 && chain.Count < 256)
        {
            var edge = parents[current];
            var label = edge.Parent == 0 ? edge.Label : "strong managed/dependent reference; field not resolved";
            chain.Add(label + " -> " + (heap.GetObject(current).Type?.Name ?? "<unknown>"));
            current = edge.Parent;
        }
        chain.Reverse();
        paths.Add(new { Type = pair.Key, ShallowBytes = example.Size, Chain = chain, Truncated = current != 0 });
    }
}
Save("root-paths.json", JsonSerializer.Serialize(paths, new JsonSerializerOptions { WriteIndented = true }));
File.WriteAllText(output + ".addresses.local.json", JsonSerializer.Serialize(examples.ToDictionary(pair => pair.Key,
    pair => pair.Value.Select(example => example.Address.ToString("x", CultureInfo.InvariantCulture)).ToArray())) + "\r\n", new UTF8Encoding(false));
Save("summary.json", JsonSerializer.Serialize(new { Runtime = runtime.ClrInfo.Version.ToString(),
    ClrMD = typeof(DataTarget).Assembly.GetName().Version!.ToString(), RootReachableObjects = parents.Count,
    RootReachableShallowBytes = histograms.Values.Aggregate(0UL, (sum, value) => sum + value.Bytes), Roots = rootCount,
    ManagedModules = runtime.EnumerateModules().Count(), AnalysisMs = timer.Elapsed.TotalMilliseconds,
    Meaning = "Strong-root/finalizer reachable object traversal, including dependent handles. Shallow totals are not exclusive owner sizes. Paths are representative shortest strong paths, not proof of obsolete lifetime. No object values or memory addresses are exported." }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Analyzed {parents.Count} root-reachable objects; sanitized types and reference paths saved.");

void Save(string name, string value) => File.WriteAllText(Path.Combine(output, name), value.ReplaceLineEndings("\r\n").TrimEnd('\r', '\n') + "\r\n", new UTF8Encoding(false));

static bool Candidate(string name) => name.StartsWith("Akbura.", StringComparison.Ordinal) || name is "System.String" or "System.Byte[]" or "System.Char[]" ||
    new[] { "CSharpCompilation", "SemanticModel", "NodeStateTable", "SourceText", "SourceGeneratorState", "EmitBaseline", "EditSession", "EditAndContinue", "ProjectState", "GeneratorDriverState", "CompilationState", "WatchObservation", "GenerationStatisticsSession" }
        .Any(fragment => name.Contains(fragment, StringComparison.Ordinal));

internal sealed class Histogram
{
    internal long Count;
    internal ulong Bytes;
}

internal sealed record Edge(ulong Parent, string Label);
