using System.Diagnostics;

namespace Akbura.HotReloadIntegrationTests;

internal static class HotReloadChildProcess
{
    private const string Marker = "AKBURA_CLR_HOT_RELOAD_TEST";

    public static bool IsChild(string testName) => Environment.GetEnvironmentVariable(Marker) == testName;

    public static async Task RunAsync(string testName)
    {
        var assemblyPath = typeof(HotReloadChildProcess).Assembly.Location;
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Path.GetDirectoryName(assemblyPath)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("vstest");
        start.ArgumentList.Add(assemblyPath);
        start.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=" + testName);
        start.ArgumentList.Add("/logger:console;verbosity=normal");
        start.Environment[Marker] = testName;
        start.Environment["DOTNET_MODIFIABLE_ASSEMBLIES"] = "debug";
        start.Environment["DOTNET_CLI_UI_LANGUAGE"] = "en-US";
        using var process = Assert.IsAssignableFrom<Process>(Process.Start(start));
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail("CLR Hot Reload child timed out.\n" + await stdout + "\n" + await stderr);
        }

        var output = await stdout + "\n" + await stderr;
        Assert.True(process.ExitCode == 0, "CLR Hot Reload child failed.\n" + output);
        Assert.Contains("Passed " + testName, output, StringComparison.Ordinal);
    }
}
