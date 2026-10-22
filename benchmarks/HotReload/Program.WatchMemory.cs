using System.Diagnostics;
using System.Text;
using System.Text.Json;

internal static partial class Program
{
    private sealed class WatchMemoryCheckpoint(string directory)
    {
        private int _sequence;

        internal async Task Pause(string phase, int revision, int launcherPid, int applicationPid, JsonElement application)
        {
            Directory.CreateDirectory(directory);
            var sequence = ++_sequence;
            var path = Path.Combine(directory, "request.json");
            var nonce = Guid.NewGuid().ToString("N");
            await Publish(path, JsonSerializer.Serialize(new { Sequence = sequence, Nonce = nonce, Phase = phase,
                Revision = revision, LauncherPid = launcherPid, ApplicationPid = applicationPid, Application = application, RequestedUtc = DateTime.UtcNow }) + "\r\n");
            Write(new { Kind = "watch-memory-paused", Sequence = sequence, phase, revision, launcherPid, applicationPid });
            var started = Stopwatch.GetTimestamp();
            var responsePath = Path.Combine(directory, "response.json");
            while (Stopwatch.GetElapsedTime(started).TotalMinutes < 10)
            {
                var payload = await ReadResponse(responsePath);
                if (payload != null)
                {
                    using var response = JsonDocument.Parse(payload);
                    if (response.RootElement.GetProperty("Nonce").GetString() == nonce)
                    {
                        Assert.True(response.RootElement.GetProperty("Succeeded").GetBoolean(), response.RootElement.ToString());
                        Write(new { Kind = "watch-memory-resumed", Sequence = sequence, phase, revision });
                        return;
                    }
                }
                await Task.Delay(100);
            }
            throw new TimeoutException("The external memory checkpoint did not complete. Preserve the partial session.");
        }

        private static async Task Publish(string path, string payload)
        {
            if (Directory.Exists(path))
            {
                throw new IOException("The checkpoint destination is a directory: " + path);
            }
            File.WriteAllText(path + ".tmp", payload, new UTF8Encoding(false));
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                try
                {
                    File.Move(path + ".tmp", path, overwrite: true);
                    return;
                }
                catch (Exception error) when (IsTransient(error) && Stopwatch.GetElapsedTime(started).TotalSeconds < 10)
                {
                    await Task.Delay(25);
                }
            }
        }

        private static async Task<string?> ReadResponse(string path)
        {
            if (Directory.Exists(path))
            {
                throw new IOException("The checkpoint response is a directory: " + path);
            }
            var started = Stopwatch.GetTimestamp();
            while (true)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var reader = new StreamReader(stream, Encoding.UTF8);
                    return await reader.ReadToEndAsync();
                }
                catch (FileNotFoundException)
                {
                    return null;
                }
                catch (Exception error) when (IsTransient(error) && Stopwatch.GetElapsedTime(started).TotalSeconds < 10)
                {
                    await Task.Delay(25);
                }
            }
        }

        private static bool IsTransient(Exception error)
        {
            return error is IOException or UnauthorizedAccessException && (error.HResult & 0xFFFF) is 5 or 32 or 33;
        }
    }
}
