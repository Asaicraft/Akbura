#if DEBUG
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Collections;
using Akbura.Pools;
using Akbura.Workspaces;
using Avalonia.Threading;
using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace Akbura.AvaloniaEdit.Performance;

/// <summary>
/// Identical opt-in workloads for the headless desktop host and the published browser host.
/// </summary>
internal static class EditorPerformanceScenario
{
    internal static async Task<string> RunAsync(AkburaWorkspace workspace, AkburaCodeEditor editor,
        Action<string> input, Func<Task> render, string runtime, Action<string>? progress = null,
        IEnumerable<int>? documentSizes = null, Action<string>? trace = null)
    {
        var results = ArrayBuilder<ArrayElement<object>>.GetInstance();
        try
        {
            foreach (var lines in documentSizes ?? [100, 1000, 5000])
            {
                progress?.Invoke($"Measuring {lines} lines…");
                editor.Text = CreateSource(lines);
                var performance = new EditorPerformance
                {
                    StageObserved = trace == null ? null : (name, elapsed) => trace(
                        elapsed == null ? $"{lines}:{name}:started" : $"{lines}:{name}:{elapsed:F3} ms"),
                };
                var contextPath = $"PerformanceContext{lines}.cs";
                var contextRegistered = false;
                var cold = Stopwatch.StartNew();
                using var session = editor.Bind(workspace.DefaultProject, new Uri($"memory:///Perf{lines}.akbura"), new()
                {
                    Scheduler = new AkburaInlineScheduler(), Performance = performance, AutomaticCompletion = false,
                    AnalysisDelay = TimeSpan.FromHours(1),
                });
                var phases = ArrayBuilder<ArrayElement<object>>.GetInstance();
                try
                {
                    await session.Ready;
                    await render();
                    var coldMs = cold.Elapsed.TotalMilliseconds;
                    if (session.LastServiceError is { } error) throw error;
                    if (!session.DiagnosticRecords.IsEmpty)
                        throw new InvalidOperationException("The benchmark base document must have no diagnostics: " + session.DiagnosticRecords[0].Message);
                    async Task Phase(string name, Func<Task> action, int samples = 20)
                    {
                        progress?.Invoke($"Measuring {lines} lines: {name}…");
                        performance.Reset();
                        for (var i = 0; i < samples; i++)
                        {
                            var watch = Stopwatch.StartNew();
                            await action();
                            performance.Record(name is "completion-input" or "context-refresh" or "stylesheet-refresh"
                                ? "Workload" : "InputToRender", watch.Elapsed.TotalMilliseconds);
                        }
                        if (session.LastServiceError is { } phaseError) throw phaseError;
                        phases.Add(new() { Value = new { Name = name, Samples = samples, Data = performance.Snapshot() } });
                    }

                    editor.CaretOffset = editor.Text.IndexOf("Привет", StringComparison.Ordinal) + 2;
                    input("я");
                    await session.RefreshAsync();
                    await render();
                    await Phase("token-input", async () => { input("я"); await render(); });
                    await Phase("beginning-edit", async () => { editor.Document.Insert(0, " "); await render(); });
                    await Phase("scroll", async () =>
                    {
                        editor.ScrollToLine(editor.TextArea.TextView.VerticalOffset < 100 ? lines - 5 : 1);
                        await render();
                    });
                    await Phase("paste-undo-redo", async () =>
                    {
                        editor.Document.Insert(0, "// Привет 😀\r\n// paste\n");
                        editor.Undo();
                        editor.Redo();
                        await render();
                    });
                    editor.CaretOffset = editor.Text.IndexOf("<TextBlock", StringComparison.Ordinal) + "<TextBlock".Length;
                    input(" ");
                    await session.RequestCompletionAsync();
                    var prefixIndex = 0;
                    const string propertyPrefix = "IsHitTestVisible";
                    await Phase("completion-input", async () =>
                    {
                        if (prefixIndex == propertyPrefix.Length)
                        {
                            editor.Document.Remove(editor.CaretOffset - prefixIndex, prefixIndex);
                            prefixIndex = 0;
                        }
                        var watch = Stopwatch.StartNew();
                        input(propertyPrefix[prefixIndex++].ToString());
                        performance.Record("InputHandling", watch.Elapsed.TotalMilliseconds);
                        await render();
                        performance.Record("CompletionInputToRender", watch.Elapsed.TotalMilliseconds);
                        await session.RequestCompletionAsync();
                        performance.Record("CompletionLatency", watch.Elapsed.TotalMilliseconds);
                    }, lines == 5000 ? 5 : 20);
                    var contextVersion = 0;
                    editor.Document.Remove(editor.CaretOffset - prefixIndex, prefixIndex);
                    await session.RequestCompletionAsync();
                    workspace.DefaultProject.AddSource(contextPath, "internal class PerformanceContext { }");
                    contextRegistered = true;
                    await Phase("context-refresh", async () =>
                    {
                        workspace.DefaultProject.UpdateSource(contextPath, $"internal class PerformanceContext {{ public int Value{contextVersion++} {{ get; set; }} }}");
                        await session.RefreshAsync();
                        await render();
                    }, lines == 5000 ? 1 : 5);
                    await Phase("stylesheet-refresh", async () =>
                    {
                        workspace.OpenOrChangeDocument(new Uri($"memory:///Perf{lines}.akcss"),
                            Microsoft.CodeAnalysis.Text.SourceText.From($"@using Avalonia.Controls;\nButton.secondary {{ Padding: {contextVersion++}; }}"));
                        await session.RefreshAsync();
                        await render();
                    }, lines == 5000 ? 1 : 5);
                    editor.Text = "using Avalonia.Controls;\n<TextBlock Text=\"" + new string('a', lines * 4) + "\" />";
                    editor.CaretOffset = 50;
                    await session.RefreshAsync();
                    await Phase("long-line", async () => { input("a"); await render(); });
                    results.Add(new() { Value = new { Lines = lines, ColdMilliseconds = coldMs, Phases = Snapshot(phases) } });
                }
                finally
                {
                    phases.Free();
                    session.Dispose();
                    if (contextRegistered) workspace.DefaultProject.RemoveSource(contextPath);
                    foreach (var uri in new[] { session.DocumentUri, new Uri($"memory:///Perf{lines}.akcss") })
                        if (workspace.CurrentSolution.TryGetDocument(uri, out var document)) workspace.RemoveDocument(document.Id);
                }
            }
            return JsonSerializer.Serialize(new
            {
                Runtime = runtime, Configuration = "Debug", AutomaticCompletion = false,
                Analysis = "Explicit refresh phases; delayed analysis disabled for isolated presentation phases",
                Results = Snapshot(results),
            });
        }
        finally
        {
            results.Free();
        }
    }

    private static System.Collections.Immutable.ImmutableArray<object> Snapshot(ArrayBuilder<ArrayElement<object>> items)
    {
        using var result = ImmutableArrayBuilder<object>.Rent(items.Count);
        foreach (var item in items)
        {
            result.Add(item.Value);
        }
        return result.ToImmutable();
    }

    internal static Task YieldAsync() => Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask();

    internal static string CreateSource(int lines)
    {
        var text = new StringBuilder("using Avalonia.Controls;\nusing Akbura.Styles.akcss;\nstate int Count = 1;\n@akcss {\n@using Avalonia.Controls;\nButton.primary { Padding: 12; }\n}\n<StackPanel>\n");
        for (var i = 9; i < lines; i++) text.AppendLine(i % 20 == 0
            ? "    <TextBlock Text={Count.ToString()} Tag=\"Привет 😀\" />"
            : "    <TextBlock Text=\"Привет 😀\" />");
        return text.Append("</StackPanel>").ToString();
    }
}
#endif
