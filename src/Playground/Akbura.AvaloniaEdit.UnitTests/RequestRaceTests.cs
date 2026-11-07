using Akbura.AvaloniaEdit.Infrastructure;
using Avalonia.Controls;
using AvaloniaEdit.Document;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class RequestRaceTests
{
    [Fact]
    public Task CancelledUncooperativeCompletionCannotPublishEvenAtTheSameCaretAndSnapshot() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "st" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        editor.CaretOffset = 2;
        var held = scheduler.HoldNextResult();
        using var cancellation = new CancellationTokenSource();
        var request = session.RequestCompletionAsync(cancellation.Token);
        await held.Started.Task;
        cancellation.Cancel();
        held.Release.SetResult();
        await request;
        Assert.Empty(session.Completions);
        Assert.Null(session.CompletionPopup);
    });

    [Fact]
    public Task OutOfOrderCompletionDoesNotReplaceNewerResultsOrReopenPopup() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "st" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        editor.CaretOffset = 2;
        var held = scheduler.HoldNextResult();
        var old = session.RequestCompletionAsync();
        await held.Started.Task;
        await session.RequestCompletionAsync();
        var latest = session.Completions;
        held.Release.SetResult();
        await old;
        Assert.Equal(latest, session.Completions);
        Assert.Contains(session.Completions, item => item.DisplayText == "state");
    });

    [Fact]
    public Task BufferReplacementAndDisposalInvalidateUncooperativeAnalysis() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "param Missing Value;" };
        var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        var held = scheduler.HoldNextResult();
        var old = session.RefreshAsync();
        await held.Started.Task;
        editor.Document = new TextDocument("// new 😀\r\nstate int Count = 0;");
        await session.RefreshAsync();
        var latest = session.Diagnostics;
        held.Release.SetResult();
        await old;
        Assert.Equal(latest, session.Diagnostics);
        Assert.DoesNotContain(session.Diagnostics, issue => issue.Message.Contains("Missing"));
        held = scheduler.HoldNextResult();
        old = session.RefreshAsync();
        await held.Started.Task;
        var published = 0;
        session.AnalysisUpdated += (_, _) => published++;
        session.Dispose();
        held.Release.SetResult();
        await old;
        Assert.Equal(0, published);
        Assert.Null(editor.Session);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task TypingDuringResolutionRejectsStaleEditsAndRetainsCommitCharacter(bool withCharacter) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "st" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        editor.CaretOffset = 2;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == "state");
        var held = scheduler.HoldNextResult();
        var commit = session.CommitCompletionAsync(item, withCharacter ? ' ' : null);
        await held.Started.Task;
        Assert.Equal(withCharacter ? "st " : "st", editor.Text);
        editor.Document.Insert(0, "// typed 😀\n");
        held.Release.SetResult();
        Assert.False(await commit);
        Assert.Equal(withCharacter ? "// typed 😀\nst " : "// typed 😀\nst", editor.Text);
    });

    [Fact]
    public Task StaleHoverCannotReplaceNewerPosition() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button Width=\"10\" Tag=\"text\" />" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        var held = scheduler.HoldNextResult();
        var old = session.GetHoverAsync(editor.Text.IndexOf("Width", StringComparison.Ordinal));
        await held.Started.Task;
        var latest = await session.GetHoverAsync(editor.Text.IndexOf("Tag", StringComparison.Ordinal));
        Assert.NotNull(latest);
        held.Release.SetResult();
        Assert.Null(await old);
        Assert.Same(latest, session.Hover);
    });

    [Fact]
    public Task ServiceFailuresRemainDistinguishableFromEmptyDiagnostics() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Race.akbura"), Options(scheduler));
        await session.Ready;
        AkburaEditorServiceFailedEventArgs? failure = null;
        session.ServiceFailed += (_, args) => failure = args;
        scheduler.FailNext = true;
        await session.RefreshAsync();
        Assert.NotNull(failure);
        Assert.Equal("Analysis", failure.Operation);
        Assert.IsType<InvalidOperationException>(session.LastServiceError);
        await session.RefreshAsync();
        Assert.Null(session.LastServiceError);
    });

    private static AkburaEditorOptions Options(ControlledScheduler scheduler) => new()
    {
        Scheduler = scheduler, AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
    };

    private sealed class ControlledScheduler : IAkburaEditorScheduler
    {
        private Hold? _next;
        public bool FailNext
        {
            get; set;
        }
        public Hold HoldNextResult()
        {
            var hold = new Hold();
            Interlocked.Exchange(ref _next, hold);
            return hold;
        }

        public Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken)
        {
            var hold = Interlocked.Exchange(ref _next, null);
            if (FailNext)
            {
                FailNext = false;
                return Task.FromException<T>(new InvalidOperationException("Controlled service failure"));
            }
            return Task.Run(async () =>
            {
                // Deliberately ignore cancellation for a held result: identity checks must still protect the UI.
                var result = await operation(hold == null ? cancellationToken : CancellationToken.None);
                if (hold != null)
                {
                    hold.Started.TrySetResult();
                    await hold.Release.Task.WaitAsync(TimeSpan.FromSeconds(30));
                }
                return result;
            });
        }
    }

    private sealed class Hold
    {
        public TaskCompletionSource Started
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release
        {
            get;
        } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
