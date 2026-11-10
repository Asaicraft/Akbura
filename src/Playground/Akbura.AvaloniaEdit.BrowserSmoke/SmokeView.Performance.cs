#if DEBUG
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.AvaloniaEdit.Performance;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal sealed partial class SmokeView
{
    private bool _measuring;
    private SmokePerformanceView? _performanceView;

    private void InitializePerformance()
    {
        var view = _performanceView = new SmokePerformanceView(MeasurePerformanceAsync);
        Avalonia.Controls.DockPanel.SetDock(view, Avalonia.Controls.Dock.Top);
        root.Children.Insert(1, view);
    }

    private async Task MeasurePerformanceAsync()
    {
        if (_measuring)
        {
            return;
        }

        var view = _performanceView ?? throw new InvalidOperationException("The debug performance panel is not initialized.");
        _measuring = true;
        view.MeasureButton.IsEnabled = false;
        view.Output.IsVisible = true;
        try
        {
            _session?.Dispose();
            _stylesheetSession?.Dispose();
            var json = await EditorPerformanceScenario.RunAsync(Workspace, editor, editor.TextArea.PerformTextInput,
                RenderPerformanceFrameAsync, "Browser WASM single UI thread, inline scheduler, two animation frames",
                message =>
                {
                    status.Text = message;
                    Console.WriteLine("AKBURA_PERFORMANCE_STAGE " + message);
                }, documentSizes: [view.DocumentSize],
                trace: message => Console.WriteLine("AKBURA_PERFORMANCE_SERVICE " + message));
            view.Output.Text = json;
            Console.WriteLine("AKBURA_PERFORMANCE " + json);
            status.Text = "Performance measurement complete. JSON below.";
        }
        catch (Exception error)
        {
            status.Text = $"Performance FAILED: {error}";
        }
        finally
        {
            _session = editor.Bind(Workspace.DefaultProject, new Uri("memory:///Views/Smoke.akbura"),
                new() { Scheduler = new AkburaInlineScheduler() });
            await _session.Ready;
            _stylesheetSession = stylesEditor.Bind(Workspace.DefaultProject, StylesUri,
                new() { Scheduler = new AkburaInlineScheduler() });
            await _stylesheetSession.Ready;
            _measuring = false;
            view.MeasureButton.IsEnabled = true;
        }
    }

    private Task RenderPerformanceFrameAsync()
    {
        var frame = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var root = Avalonia.Controls.TopLevel.GetTopLevel(editor)
            ?? throw new InvalidOperationException("The performance editor must be attached.");
        // The second callback allows the preceding frame to consume updated visual lines.
        // This measures a render opportunity, not GPU scan-out or physical display latency.
        root.RequestAnimationFrame(_ => root.RequestAnimationFrame(_ => frame.TrySetResult()));
        return frame.Task;
    }
}
#endif
