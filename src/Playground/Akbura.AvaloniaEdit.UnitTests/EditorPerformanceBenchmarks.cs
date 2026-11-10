#if DEBUG
using Akbura.AvaloniaEdit.Performance;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Avalonia.Headless;
using System.Diagnostics;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class EditorPerformanceBenchmarks
{
    [Fact]
    public Task RepresentativeDocumentHasNoDiagnostics() => EditorTestHost.Run(async () =>
    {
        using var workspace = new AkburaWorkspace();
        workspace.DefaultProject.UseAvalonia(AkburaReferenceSet.FromFiles(
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "CompilerReferences.txt"))));
        var editor = new AkburaCodeEditor { Text = EditorPerformanceScenario.CreateSource(100) };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///ValidPerformance.akbura"),
            new() { Scheduler = new Infrastructure.AkburaInlineScheduler(), AutomaticCompletion = false });
        await session.Ready;
        Assert.Null(session.LastServiceError);
        Assert.Empty(session.DiagnosticRecords);
    });

    [Fact]
    public Task OptInDebugWorkload() => EditorTestHost.Run(async () =>
    {
        var output = Environment.GetEnvironmentVariable("AKBURA_PERFORMANCE_OUTPUT");
        if (string.IsNullOrEmpty(output)) return;
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        var progressLog = output + ".progress.log";
        var elapsed = Stopwatch.StartNew();
        File.WriteAllText(progressLog, "Preparing metadata and the headless editor." + Environment.NewLine);
        using var workspace = new AkburaWorkspace();
        workspace.DefaultProject.UseAvalonia(AkburaReferenceSet.FromFiles(
            File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "CompilerReferences.txt"))));
        var editor = new AkburaCodeEditor { FontSize = 16, ShowLineNumbers = true };
        var window = new Window { Content = editor, Width = 1280, Height = 900 };
        window.Show();
        editor.TextArea.Focus();
        try
        {
            var json = await EditorPerformanceScenario.RunAsync(workspace, editor, window.KeyTextInput, async () =>
            {
                window.UpdateLayout();
                window.CaptureRenderedFrame();
                await EditorPerformanceScenario.YieldAsync();
            }, $"Windows headless .NET {Environment.Version}, inline scheduler",
                message =>
                {
                    File.WriteAllText(output + ".progress", message);
                    File.AppendAllText(progressLog, $"[{elapsed.Elapsed:c}] {message}{Environment.NewLine}");
                });
            await File.WriteAllTextAsync(output, json);
            File.AppendAllText(progressLog, $"[{elapsed.Elapsed:c}] Results saved: {output}{Environment.NewLine}");
        }
        finally { window.Close(); }
    });
}
#endif
