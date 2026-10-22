using Microsoft.Diagnostics.NETCore.Client;
using Microsoft.Diagnostics.Tracing;
using System.Diagnostics.Tracing;
using System.Reflection;
using System.Text;
using System.Text.Json;

if (args[0] == "manifest")
{
    var assembly = Assembly.LoadFrom(Path.GetFullPath(args[1]));
    var type = assembly.GetTypes().Single(type => type.Name == "CodeAnalysisEventSource");
    File.WriteAllText(args[2], EventSource.GenerateManifest(type, Path.GetFullPath(args[1]))!.ReplaceLineEndings("\r\n"), new UTF8Encoding(false));
    return;
}
var pid = int.Parse(args[0]);
var output = Path.GetFullPath(args[1]);
var readyPath = Path.GetFullPath(args[2]);
using var writer = new StreamWriter(output, append: false, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\r\n" };
using var session = new DiagnosticsClient(pid).StartEventPipeSession(new[] {
    new EventPipeProvider("Microsoft-CodeAnalysis-General", EventLevel.Verbose, 1),
    new EventPipeProvider("RoslynEventSource", EventLevel.Verbose, -1),
    new EventPipeProvider("Akbura-CriticalPath", EventLevel.Verbose, -1) }, requestRundown: false);
using var source = new EventPipeEventSource(session.EventStream);
var count = 0;
using var cancellation = new CancellationTokenSource();
var stop = Task.Run(async () =>
{
    while (!File.Exists(readyPath + ".stop"))
    {
        await Task.Delay(50, cancellation.Token);
    }
    session.Stop();
}, cancellation.Token);
source.Dynamic.All += item =>
{
    if (item.ProviderName is not ("Microsoft-CodeAnalysis-General" or "RoslynEventSource" or "Akbura-CriticalPath") || item.TaskName == "NodeTransform")
    {
        return;
    }
    var payload = new Dictionary<string, object?>();
    foreach (var name in item.PayloadNames)
    {
        if (name is "id" or "generatorName" or "elapsedTicks" or "elapsedTime" or "functionId" or "blockId" or "tick" or "revision" or "sourceSha256")
        {
            payload[name] = item.PayloadByName(name)?.ToString();
        }
    }
    writer.WriteLine(JsonSerializer.Serialize(new { Provider = item.ProviderName, Event = item.EventName, Task = item.TaskName,
        Opcode = item.OpcodeName, Pid = item.ProcessID, Thread = item.ThreadID, LocalMs = item.TimeStampRelativeMSec,
        Activity = item.ActivityID, Payload = payload }));
    count++;
};
File.WriteAllText(readyPath, JsonSerializer.Serialize(new { Pid = pid, CollectorPid = Environment.ProcessId,
    Clock = "One EventPipe trace-local clock. Correlate driver spans using an in-process Akbura revision/hash marker, never by callback delivery time or cross-process timestamp subtraction." }) + "\r\n", new UTF8Encoding(false));
try
{
    source.Process();
}
finally
{
    cancellation.Cancel();
}
if (!File.Exists(readyPath + ".stop"))
{
    throw new InvalidOperationException("The event stream ended without the explicit pre-exit drain barrier.");
}
stop.GetAwaiter().GetResult();
if (source.EventsLost != 0)
{
    throw new InvalidOperationException("The diagnostic event stream lost events; do not accept incomplete attribution.");
}
File.WriteAllText(readyPath + ".stopped", JsonSerializer.Serialize(new { Events = count, EventsLost = source.EventsLost, Drained = true }) + "\r\n", new UTF8Encoding(false));
Console.WriteLine("Collected diagnostic events: " + count);
