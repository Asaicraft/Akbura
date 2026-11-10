using Akbura.Pools;
using Akbura.AvaloniaEdit.Highlighting;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Workspaces.Classification;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class EditorArchitectureTests
{
#if DEBUG
    [Fact]
    public Task QueuedProjectNotificationSharesExplicitRefreshAndInvalidatesOldResults() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var performance = new EditorPerformance();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button />" };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///ContextCoalescing.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), AutomaticCompletion = false, Performance = performance,
        });
        await session.Ready;
        var colorizer = Assert.IsType<WorkspaceColorizer>(Assert.Single(editor.TextArea.TextView.LineTransformers, static transformer => transformer is WorkspaceColorizer));
        var provisional = colorizer.Spans;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        performance.Reset();
        performance.BeforeService = async (stream, _) =>
        {
            if (stream == "classification") { entered.TrySetResult(); await release.Task; }
        };
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(workspace.DefaultProject.Context));
        var refresh = session.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        await EditorPerformanceYield();
        try
        {
            Assert.Empty(session.Classifications);
            Assert.True(provisional == colorizer.Spans);
            Assert.False(refresh.IsCompleted);
        }
        finally { release.TrySetResult(); }
        await refresh;
        Assert.NotEmpty(session.Classifications);
        Assert.Equal(1, performance.CountOf("Classification"));
        Assert.Equal(1, performance.CountOf("Diagnostics"));
    });
#endif

#if DEBUG
    [Fact]
    public Task AutomaticInputCoalescesBeforeCallingServicesAndEscapeRejectsLateWork() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var performance = new EditorPerformance();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button W />" };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Burst.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), Performance = performance, AnalysisDelay = TimeSpan.FromHours(1),
        });
        var window = new Window { Content = editor, Width = 800, Height = 500 };
        try
        {
            window.Show();
            editor.TextArea.Focus();
            await session.Ready;
            editor.CaretOffset = editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
            performance.Reset();
            // Headless KeyTextInput pumps queued dispatcher jobs between calls.
            // PerformTextInput sends the same editor events within a single dispatcher turn.
            editor.TextArea.PerformTextInput("i");
            editor.TextArea.PerformTextInput("d");
            Assert.Equal(0, performance.CountOf("CompletionProvider"));
            await session.CompletionIdle;
            Assert.Equal(1, performance.CountOf("CompletionProvider"));
            Assert.Equal(0, performance.CountOf("Parse"));
            Assert.Equal(1, performance.CountOf("WithText"));
            Assert.NotNull(session.CompletionPopup);
            var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            performance.BeforeService = async (stream, _) =>
            {
                if (stream == "completion") { entered.TrySetResult(); await release.Task; }
            };
            window.KeyTextInput("t");
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            release.TrySetResult();
            await session.CompletionIdle;
            Assert.Null(session.CompletionPopup);
            Assert.Contains("Widt", editor.Text);
        }
        finally { window.Close(); }
    });
#endif

    [Fact]
    public Task TabOnARetainedRowObtainsACurrentCanonicalEditAndOneUndo() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button W />" };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Retained.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
        });
        var window = new Window { Content = editor, Width = 800, Height = 500 };
        try
        {
            window.Show();
            editor.TextArea.Focus();
            await session.Ready;
            editor.CaretOffset = editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
            await session.RequestCompletionAsync();
            var popup = session.CompletionPopup!;
            popup.CompletionList.SelectedItem = Assert.Single(session.Completions, item => item.DisplayText == "Width");
            window.KeyTextInput("i");
            Assert.Same(popup, session.CompletionPopup);
            var before = editor.Text;
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Tab, Avalonia.Input.RawInputModifiers.None);
            await session.CompletionCommitIdle;
            Assert.Contains("Width=", editor.Text);
            editor.Undo();
            Assert.Equal(before, editor.Text);
        }
        finally { window.Close(); }
    });

    [Fact]
    public async Task ServiceQueueCoalescesPendingStreamsAndPrioritizesInteractiveWork()
    {
#if DEBUG
        var performance = new EditorPerformance();
#endif
        using var queue = new EditorServiceQueue(new AkburaInlineScheduler())
        {
#if DEBUG
            Performance = performance,
#endif
        };
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = queue.RunAsync("running", 1, async _ => { await release.Task; return 0; }, CancellationToken.None);
        var order = ArrayBuilder<string>.GetInstance();
        try
        {
            Task<int> Enqueue(string stream, int priority, string value, CancellationToken token = default) =>
                queue.RunAsync(stream, priority, _ => { order.Add(value); return Task.FromResult(1); }, token);
            var obsolete = Enqueue("diagnostics", 2, "obsolete");
            var latest = Enqueue("diagnostics", 2, "latest");
            var colors = Enqueue("colors", 1, "colors");
            var interactive = Enqueue("completion", 0, "completion");
            using var cancellation = new CancellationTokenSource();
            var hover = Enqueue("hover", 0, "canceled", cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => obsolete);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hover);
            release.TrySetResult();
            await Task.WhenAll(running, latest, colors, interactive);
            Assert.Equal(new[] { "completion", "colors", "latest" }, order);
#if DEBUG
            Assert.Equal(1, performance.CountOf("ServiceSuperseded"));
            Assert.Equal(1, performance.CountOf("ServiceCanceled"));
#endif
        }
        finally
        {
            order.Free();
        }
    }

#if DEBUG
    [Fact]
    public Task SessionFeaturesReuseSyntaxAcrossRequestsAndProjectOnlyChanges() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var performance = new EditorPerformance();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button W />" };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Reuse.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), AutomaticCompletion = false,
            Performance = performance, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await session.Ready;
        editor.CaretOffset = editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
        await session.RequestCompletionAsync();
        await session.GetHoverAsync(0);
        await session.RefreshAsync();
        Assert.Equal(1, performance.CountOf("Parse"));
        Assert.Equal(0, performance.CountOf("WithText"));
        editor.Document.Insert(0, "// Я😀\r\n");
        editor.Document.Insert(0, "\n");
        await session.RequestCompletionAsync();
        Assert.Equal(1, performance.CountOf("WithText"));
        var context = workspace.DefaultProject.Context;
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context));
        await EditorPerformanceYield();
        await session.RefreshAsync();
        Assert.Equal(1, performance.CountOf("Parse"));
        Assert.Equal(1, performance.CountOf("WithText"));
    });
#endif

    [Fact]
    public Task ReplacingAndDisposingSyntaxRejectsUncooperativeLateResults() => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var services = new EditorServiceQueue(scheduler);
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, _ => true);
        var held = scheduler.HoldNextResult();
        var first = cache.GetAsync(new(new object(), 0, "memory:///Old.akbura", SourceText.From("state int Old = 0;")),
            CancellationToken.None);
        await held.Started.Task;
        cache.Clear();
        Assert.Null(cache.Accepted);
        var replacement = await cache.GetAsync(new(new object(), 0, "memory:///New.akbura", SourceText.From("state int New = 0;")),
            CancellationToken.None);
        held.Release.TrySetResult();
        await first;
        Assert.Same(replacement, cache.Accepted);
        held = scheduler.HoldNextResult();
        var pending = cache.GetAsync(new(new object(), 0, "memory:///Disposed.akbura", SourceText.From("")),
            CancellationToken.None);
        await held.Started.Task;
        cache.Dispose();
        Assert.Null(cache.Accepted);
        held.Release.TrySetResult();
        await pending;
        Assert.Null(cache.Accepted);
    });

#if DEBUG
    [Fact]
    public Task SyntaxIsSharedAndFeatureCancellationDoesNotCancelItsOwner() => EditorTestHost.Run(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var performance = new EditorPerformance
        {
            BeforeService = async (stream, _) =>
            {
                if (stream == "syntax") { entered.TrySetResult(); await release.Task; }
            },
        };
        using var services = new EditorServiceQueue(new AkburaInlineScheduler()) { Performance = performance };
        var buffer = new object();
        using var cache = new EditorSyntaxCache(services, CancellationToken.None, _ => true) { Performance = performance };
        var text = SourceText.From("// Привет 😀\r\nstate int Count = 1;");
        var key = new EditorSyntaxCache.Key(buffer, 0, "memory:///Cache.akbura", text);
        using var hoverCancellation = new CancellationTokenSource();
        var hover = cache.GetAsync(key, hoverCancellation.Token);
        await entered.Task;
        var completion = cache.GetAsync(key, CancellationToken.None);
        hoverCancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => hover);
        release.TrySetResult();
        var syntax = await completion;
        Assert.Same(syntax, await cache.GetAsync(key, CancellationToken.None));
        Assert.Equal(1, performance.CountOf("Parse"));
        performance.BeforeService = null;
        var updated = text.WithChanges(new TextChange(new TextSpan(0, 0), "\n"))
            .WithChanges(new TextChange(new TextSpan(text.Length, 0), "2"));
        var next = await cache.GetAsync(new(buffer, 2, key.FilePath, updated), CancellationToken.None);
        Assert.Equal(updated.ToString(), next.Text.ToString());
        Assert.Equal(1, performance.CountOf("WithText"));
        Assert.Equal(1, performance.CountOf("Parse"));
        await cache.GetAsync(new(new object(), 3, "memory:///Cache.akcss", updated), CancellationToken.None);
        Assert.Equal(2, performance.CountOf("Parse"));
    });
#endif

#if DEBUG
    [Fact]
    public Task HighlightingPublishesWhileDiagnosticsAreHeldAndRefreshStillWaits() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(workspace.DefaultProject.Context,
            "namespace Demo { public class ViewModel { public string Title { get; set; } } }"));
        var performance = new EditorPerformance();
        var editor = new AkburaCodeEditor
        {
            Text = "using Avalonia.Controls; using Demo; param ViewModel Vm;\n<TextBlock Text=${x.bind Vm.Title} />",
        };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Split.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), Performance = performance, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await session.Ready;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        performance.BeforeService = async (stream, _) =>
        {
            if (stream == "diagnostics") { entered.TrySetResult(); await release.Task; }
        };
        var updated = 0;
        session.AnalysisUpdated += (_, _) => updated++;
        editor.Document.Insert(0, "// 😀\r\n");
        var refresh = session.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.NotEmpty(session.Classifications);
            Assert.Contains(session.Classifications, span => span.Kind == AkburaClassificationKind.PropertyName);
            Assert.False(refresh.IsCompleted);
            Assert.Equal(0, updated);
        }
        finally { release.TrySetResult(); }
        await refresh;
        Assert.Equal(1, updated);
        Assert.Equal(1, performance.CountOf("Parse"));
        Assert.Equal(1, performance.CountOf("WithText"));
        var diagnosticEvents = 0;
        session.DiagnosticsChanged += (_, _) => diagnosticEvents++;
        await session.RefreshAsync();
        Assert.Equal(0, diagnosticEvents);
        editor.Text = "";
        await session.RefreshAsync();
        Assert.Empty(session.Classifications);
        Assert.Empty(Assert.IsType<WorkspaceColorizer>(Assert.Single(editor.TextArea.TextView.LineTransformers, static transformer => transformer is WorkspaceColorizer)).Spans);
    });
#endif

    [Fact]
    public Task MultilineOverlapsPreserveProviderPrecedenceAndResourceChanges() => EditorTestHost.Run(async () =>
    {
        var editor = new AkburaCodeEditor { Text = "first\r\nsecond 😀\nthird" };
        var colors = new WorkspaceColorizer(editor);
        editor.TextArea.TextView.LineTransformers.Add(colors);
        colors.SetSpans(editor.Document,
        [
            new(new TextSpan(0, editor.Text.Length), AkburaClassificationKind.Comment),
            new(new TextSpan(7, 6), AkburaClassificationKind.String),
        ]);
        var window = new Window { Content = editor, Width = 800, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var view = editor.TextArea.TextView;
            view.EnsureVisualLines();
            var commentBrush = EditorBrushes.Get(editor, nameof(AkburaClassificationKind.Comment));
            var stringBrush = EditorBrushes.Get(editor, nameof(AkburaClassificationKind.String));
            Assert.All(view.GetVisualLine(1).Elements, element => Assert.Equal(commentBrush, element.TextRunProperties.ForegroundBrush));
            Assert.Equal(stringBrush, view.GetVisualLine(2).Elements[0].TextRunProperties.ForegroundBrush);
            Assert.Equal(commentBrush, view.GetVisualLine(2).Elements[^1].TextRunProperties.ForegroundBrush);
            Assert.All(view.GetVisualLine(3).Elements, element => Assert.Equal(commentBrush, element.TextRunProperties.ForegroundBrush));

            colors.SetSpans(editor.Document,
            [
                new(new TextSpan(7, 6), AkburaClassificationKind.String),
                new(new TextSpan(0, editor.Text.Length), AkburaClassificationKind.Comment),
            ]);
            view.EnsureVisualLines();
            Assert.All(view.GetVisualLine(2).Elements, element => Assert.Equal(commentBrush, element.TextRunProperties.ForegroundBrush));
            view.LineTransformers.Remove(colors);

            using var workspace = EditorTestHost.Workspace();
            editor.Text = "state int Count = 1;";
            using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Appearance.akbura"),
                new() { Scheduler = new AkburaInlineScheduler(), AutomaticCompletion = false });
            await session.Ready;
            editor.Resources["AkburaEditor.KeywordBrush"] = Avalonia.Media.Brushes.Magenta;
            await EditorPerformanceYield();
            window.UpdateLayout();
            editor.TextArea.TextView.EnsureVisualLines();
            var first = editor.TextArea.TextView.GetVisualLine(1).Elements[0];
            Assert.Same(Avalonia.Media.Brushes.Magenta, first.TextRunProperties.ForegroundBrush);
            editor.Resources["AkburaEditor.KeywordBrush"] = Avalonia.Media.Brushes.Green;
            await EditorPerformanceYield();
            window.UpdateLayout();
            editor.TextArea.TextView.EnsureVisualLines();
            Assert.Same(Avalonia.Media.Brushes.Green, editor.TextArea.TextView.GetVisualLine(1).Elements[0].TextRunProperties.ForegroundBrush);
        }
        finally { window.Close(); }
    });

    private static ImmutableArray<AkburaClassifiedSpan> CreateClassifications(int count, int stride)
    {
        using var spans = ImmutableArrayBuilder<AkburaClassifiedSpan>.Rent(count);
        for (var index = 0; index < count; index++)
        {
            spans.Add(new(new TextSpan(index * stride, 4), AkburaClassificationKind.Keyword));
        }
        return spans.ToImmutable();
    }

    private static Task EditorPerformanceYield() => Avalonia.Threading.Dispatcher.UIThread
        .InvokeAsync(static () => { }, Avalonia.Threading.DispatcherPriority.Background).GetTask();

#if DEBUG
    [Fact]
    public Task RenderingOneLineDoesNotVisitAllClassifications() => EditorTestHost.Run(() =>
    {
        var editor = new AkburaCodeEditor { Text = string.Join('\n', Enumerable.Repeat("word", 5000)) };
        var performance = new EditorPerformance();
        var colors = new WorkspaceColorizer(editor) { Performance = performance };
        editor.TextArea.TextView.LineTransformers.Add(colors);
        colors.SetSpans(editor.Document, CreateClassifications(5000, 5));
        var window = new Window { Content = editor, Width = 800, Height = 500 };
        try
        {
            window.Show();
            window.UpdateLayout();
            editor.TextArea.TextView.EnsureVisualLines();
            performance.Reset();
            editor.TextArea.TextView.Redraw(0, 4);
            editor.TextArea.TextView.EnsureVisualLines();
            Assert.InRange(performance.CountOf("RenderedSpanVisits"), 1, 100);
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });
#endif

#if DEBUG
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public Task AnInternalEditOnlyRemapsIntersectingPresentation(string newline) => EditorTestHost.Run(() =>
    {
        var editor = new AkburaCodeEditor { Text = string.Join(newline, Enumerable.Repeat("word", 5000)) };
        var performance = new EditorPerformance();
        var colors = new WorkspaceColorizer(editor) { Performance = performance };
        var stride = 4 + newline.Length;
        colors.SetSpans(editor.Document, CreateClassifications(5000, stride));
        editor.Document.Changed += (_, args) => colors.TrackChange(editor.Document, args);
        performance.Reset();
        editor.Document.Insert(2, "Я😀");
        Assert.InRange(performance.CountOf("PresentationRemapped"), 0, 1);
        Assert.Equal(0, performance.CountOf("PresentationRebuild"));
        Assert.Equal(0, performance.CountOf("FullRedraw"));
        Assert.Equal(new TextSpan(0, 7), colors.Spans[0].Span);
        Assert.Equal(new TextSpan(4999 * stride + 3, 4), colors.Spans[^1].Span);
        return Task.CompletedTask;
    });
#endif

    [Fact]
    public Task CompatibleInputKeepsTheCompletionAndDescriptionWindows() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button W />" };
        using var session = editor.Bind(workspace.DefaultProject, new Uri("memory:///Completion.akbura"), new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
        });
        var window = new Window { Content = editor, Width = 800, Height = 500 };
        try
        {
            window.Show();
            editor.TextArea.Focus();
            await session.Ready;
            editor.CaretOffset = editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
            await session.RequestCompletionAsync();
            var popup = session.CompletionPopup;
            var description = session.DescriptionPopup;
            Assert.NotNull(popup);
            window.KeyTextInput("i");
            await session.RequestCompletionAsync();
            Assert.Same(popup, session.CompletionPopup);
            Assert.Same(description, session.DescriptionPopup);
            var current = Assert.Single(session.Completions, item => item.DisplayText == "Width");
            Assert.Same(current, session.CompletionPopup!.CompletionList.SelectedItem);
            Assert.All(session.CompletionPopup.CompletionList.CurrentList,
                item => Assert.Contains(item, session.Completions));
            var before = editor.Text;
            Assert.True(await session.CommitCompletionAsync(current));
            Assert.Contains("Width=", editor.Text);
            editor.Undo();
            Assert.Equal(before, editor.Text);
        }
        finally { window.Close(); }
    });
}
