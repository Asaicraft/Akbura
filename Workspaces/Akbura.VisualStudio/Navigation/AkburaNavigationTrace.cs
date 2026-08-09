using System.Diagnostics;
using System.Text;

namespace Akbura.VisualStudio.Navigation;

internal static class AkburaNavigationTrace
{
    private static readonly object Gate = new();

    private static readonly bool IsEnabled =
#if DEBUG
        true;
#else
        string.Equals(
            Environment.GetEnvironmentVariable(
                "AKBURA_VS_NAVIGATION_TRACE"),
            "1",
            StringComparison.Ordinal);
#endif

    private static readonly string FilePath =
        Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Akbura",
            "VisualStudio",
            "Logs",
            $"navigation-{Process.GetCurrentProcess().Id}.log");

    private static bool _isInitialized;

    public static string LogFilePath => FilePath;

    public static void Write(string message)
    {
        if (!IsEnabled)
        {
            return;
        }

        var line =
            $"{DateTimeOffset.Now:O} " +
            $"[thread {Environment.CurrentManagedThreadId}] " +
            message;

        Debug.WriteLine(line);

        try
        {
            lock (Gate)
            {
                EnsureInitialized();
                File.AppendAllText(
                    FilePath,
                    line + Environment.NewLine,
                    Encoding.UTF8);
            }
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
                $"[Akbura.Navigation] Could not write trace: " +
                exception);
        }
    }

    public static void Write(
        string message,
        Exception exception)
    {
        Write(message + Environment.NewLine + exception);
    }

    private static void EnsureInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        var directory = Path.GetDirectoryName(FilePath)!;
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            FilePath,
            $"Akbura navigation trace" + Environment.NewLine +
            $"Process: {Process.GetCurrentProcess().Id}" +
            Environment.NewLine +
            $"Started: {DateTimeOffset.Now:O}" +
            Environment.NewLine,
            Encoding.UTF8);
        _isInitialized = true;
    }
}
