using Microsoft.Build.Framework;
using System.Diagnostics;

namespace Akbura.Build;

public sealed class CheckAkburaModuleManifest : AkburaManifestTask
{
    [Output]
    public bool RequiresGeneration { get; set; }

    [Output]
    public string InputFingerprint { get; set; } = string.Empty;

    [Output]
    public string CacheStatePath { get; set; } = string.Empty;

    [Output]
    public string OutputLockPath { get; set; } = string.Empty;

    public override bool Execute()
    {
        var timer = Stopwatch.StartNew();
        var cache = new AkburaManifestCache(this);
        CacheStatePath = cache.StatePath;
        OutputLockPath = cache.LockPath;
        try
        {
            using var outputLock = cache.AcquireOutputLock();
            InputFingerprint = cache.ComputeFingerprint();
            RequiresGeneration = !cache.IsCurrent(InputFingerprint);
            if (RequiresGeneration)
            {
                cache.Invalidate();
            }

            Log.LogMessage(MessageImportance.Normal,
                $"Akbura manifest: {(RequiresGeneration ? "generation required" : "up to date")}; validation {timer.Elapsed.TotalMilliseconds:F1} ms; hashed {cache.HashedBytes} bytes.");
            return true;
        }
        catch (Exception exception)
        {
            cache.Invalidate();
            Log.LogErrorFromException(exception, showStackTrace: true);
            return false;
        }
    }
}
