#requires -Version 7.2
param([string]$CheckpointSource = '', [switch]$ExpectFailure)
$ErrorActionPreference = 'Stop'
if (-not $CheckpointSource) { $CheckpointSource = Join-Path $PSScriptRoot 'Program.WatchMemory.cs' }
# Compile the exact checkpoint implementation, with only its logging/assertion dependencies stubbed.
$source = "#nullable enable`r`nusing System;`r`nusing System.IO;`r`nusing System.Threading.Tasks;`r`n" + [IO.File]::ReadAllText($CheckpointSource) + @'

internal static partial class Program
{
    private static void Write(object value) { }

    public static async Task TestCheckpointTransport()
    {
        var root = Path.Combine(Path.GetTempPath(), "Akbura-CheckpointChannel-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var response = Path.Combine(root, "response.json");
        var request = Path.Combine(root, "request.json");
        var checkpoint = new WatchMemoryCheckpoint(root);
        var application = JsonSerializer.SerializeToElement(new { });
        File.WriteAllText(response, "{\"Nonce\":\"stale\",\"Succeeded\":true}");
        Task pause;
        using (var held = File.Open(response, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            pause = checkpoint.Pause("revision", 1, 1, 2, application);
            await Task.Delay(150);
            if (pause.IsCompleted)
            {
                await pause;
                throw new InvalidOperationException("An exclusively locked response was not retried.");
            }
        }
        Respond(response, Nonce(request));
        await pause.WaitAsync(TimeSpan.FromSeconds(5));

        var oldRequest = File.ReadAllText(request);
        using (var held = File.Open(request, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            pause = checkpoint.Pause("revision", 2, 1, 2, application);
            await Task.Delay(150);
            Check(!pause.IsCompleted && File.ReadAllText(request) == oldRequest, "A blocked request lost its previous atomic payload.");
        }
        await Until(() => File.ReadAllText(request) != oldRequest);
        Respond(response, "stale");
        await Task.Delay(150);
        Check(!pause.IsCompleted, "A stale nonce resumed a checkpoint.");
        Respond(response, Nonce(request));
        await pause.WaitAsync(TimeSpan.FromSeconds(5));

        for (var revision = 3; revision < 53; revision++)
        {
            File.Delete(response);
            pause = checkpoint.Pause("revision", revision, 1, 2, application);
            Respond(response, Nonce(request));
            await pause.WaitAsync(TimeSpan.FromSeconds(5));
        }

        pause = checkpoint.Pause("revision", 53, 1, 2, application);
        Respond(response, Nonce(request), succeeded: false);
        await Reject<InvalidOperationException>(pause);
        pause = checkpoint.Pause("revision", 54, 1, 2, application);
        Replace(response, "{");
        await Reject<JsonException>(pause);

        File.Delete(response);
        Directory.CreateDirectory(response);
        await Reject<IOException>(checkpoint.Pause("revision", 55, 1, 2, application));
        Directory.Delete(response);
        File.Delete(request);
        Directory.CreateDirectory(request);
        await Reject<IOException>(checkpoint.Pause("revision", 56, 1, 2, application));
        Directory.Delete(request);

        File.WriteAllText(response, "{\"Nonce\":\"stale\",\"Succeeded\":true}");
        using (var held = File.Open(response, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var timer = Stopwatch.StartNew();
            await Reject<IOException>(checkpoint.Pause("revision", 57, 1, 2, application));
            Check(timer.Elapsed.TotalSeconds >= 9 && timer.Elapsed.TotalSeconds < 15, "A persistent read lock did not obey its bounded retry deadline.");
        }
    }

    private static string Nonce(string request)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(request));
        return document.RootElement.GetProperty("Nonce").GetString()!;
    }

    private static void Respond(string path, string nonce, bool succeeded = true)
    {
        Replace(path, JsonSerializer.Serialize(new { Nonce = nonce, Succeeded = succeeded }));
    }

    private static void Replace(string path, string payload)
    {
        File.WriteAllText(path + ".test-tmp", payload);
        File.Move(path + ".test-tmp", path, overwrite: true);
    }

    private static async Task Until(Func<bool> condition)
    {
        var timer = Stopwatch.StartNew();
        while (!condition())
        {
            Check(timer.Elapsed.TotalSeconds < 5, "A blocked request was never published.");
            await Task.Delay(10);
        }
    }

    private static async Task Reject<T>(Task task) where T : Exception
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(15));
        }
        catch (T)
        {
            return;
        }
        throw new InvalidOperationException("Expected rejection: " + typeof(T).Name);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal static class Assert
{
    internal static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
'@
$type = Add-Type -TypeDefinition $source -PassThru | Where-Object FullName -eq 'Program'
$failed = $false
try { $null = $type.GetMethod('TestCheckpointTransport').Invoke($null, @()).GetAwaiter().GetResult() }
catch {
    if (-not $ExpectFailure) { throw }
    $cause = $_.Exception
    while ($cause.InnerException) { $cause = $cause.InnerException }
    if ($cause -isnot [IO.IOException] -or ($cause.HResult -band 0xFFFF) -notin @(5,32,33) -or -not $cause.Message.Contains('response.json')) { throw }
    $failed = $true
    Write-Output 'REPRODUCED: original checkpoint reader fails on a transient Windows response-file sharing lock.'
}
if ($ExpectFailure -and -not $failed) { throw 'The original reader did not reproduce the sharing failure.' }
if (-not $ExpectFailure) {
    Write-Output 'PASS: locked reads and atomic request publication recover; stale nonces and 50 sequential handshakes verified; failed/malformed responses and invalid paths rejected; persistent read lock fails within the retry deadline.'
}
