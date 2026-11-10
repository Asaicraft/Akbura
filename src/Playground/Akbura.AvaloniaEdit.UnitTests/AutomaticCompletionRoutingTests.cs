using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Workspaces;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Avalonia.Controls;
using Avalonia.Headless;
using Microsoft.CodeAnalysis.Text;
using Xunit.Abstractions;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class AutomaticCompletionRoutingTests(ITestOutputHelper output)
{
    [Theory]
    [InlineData("Rejected.akcss", "// ordinary comment |")]
    [InlineData("Rejected.akbura", "@akcss {\n// ordinary comment |\n}\n<Button />")]
    [InlineData("Rejected.akcss", "@using Avalonia.Controls;\nButton.card { /* ordinary |comment */ }")]
    public Task UnsupportedAutomaticInputSkipsCompletionSyncButNormalAnalysisPublishesText(string file, string source) =>
        EditorTestHost.Run(async () =>
        {
#if DEBUG
            var performance = new EditorPerformance();
#endif
            using var host = new Host(source, file, new()
            {
                Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
                Performance = performance,
#endif
            });
            await host.Session.Ready;
            var published = host.Workspace.CurrentSolution;
#if DEBUG
            performance.Reset();
#endif
            host.Editor.TextArea.PerformTextInput("z");
            await host.Session.CompletionIdle;
            AssertUnsupported(host);
            Assert.Empty(host.Session.Completions);
            Assert.Null(host.Session.CompletionPopup);
            Assert.Same(published, host.Workspace.CurrentSolution);
#if DEBUG
            AssertCounts(performance, completionSync: 0, analysisSync: 0, native: 0, projected: 0);
            Assert.Equal(1, performance.CountOf("WithText"));
#endif
            var syntax = host.Session.CachedSyntax;
            await host.Session.RefreshAsync();
            Assert.True(host.Workspace.CurrentSolution.TryGetDocument(host.Uri, out var document));
            Assert.Equal(host.Editor.Text, document.Text.ToString());
            Assert.Same(syntax, host.Session.CachedSyntax);
            Assert.NotEmpty(host.Session.Classifications);
            Assert.Null(host.Session.LastServiceError);
            output.WriteLine($"Rejected completion for {file}; normal analysis published the edit.");
#if DEBUG
            AssertCounts(performance, completionSync: 0, analysisSync: 1, native: 0, projected: 0);
            Assert.Equal(1, performance.CountOf("WithText"));
#endif
        });

    [Fact]
    public Task MarkupPrefixSynchronizesOnceAndKeepsCanonicalInsertion() => EditorTestHost.Run(async () =>
    {
#if DEBUG
        var performance = new EditorPerformance();
#endif
        using var host = new Host("using Avalonia.Controls;\n<Button W| />", options: new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
            Performance = performance,
#endif
        });
        await host.Session.Ready;
#if DEBUG
        performance.Reset();
#endif
        host.Editor.TextArea.PerformTextInput("i");
        await host.Session.CompletionIdle;
        var item = Assert.Single(host.Session.Completions, item => !item.IsProjected && item.DisplayText == "Width");
        var change = host.Workspace.LanguageServices.Completion.GetCompletionChange(
            item.Syntax, item.Context, host.Editor.CaretOffset, item.Native!);
        var before = host.Editor.Text;
#if DEBUG
        AssertCounts(performance, completionSync: 1, analysisSync: 0, native: 1, projected: 0);
        Assert.Equal(1, performance.CountOf("WithText"));
#endif
        Assert.True(await host.Session.CommitCompletionAsync(item));
        Assert.Equal(SourceText.From(before).WithChanges(change.Changes).ToString(), host.Editor.Text);
        Assert.Equal(change.NewPosition, host.Editor.CaretOffset);
        host.Editor.Undo();
        Assert.Equal(before, host.Editor.Text);
        Assert.Null(host.Session.LastServiceError);
    });

    [Fact]
    public Task MixedUsingContextPreservesNativeModulesAndProjectedNamespaces() => EditorTestHost.Run(async () =>
    {
#if DEBUG
        var performance = new EditorPerformance();
#endif
        using var host = new Host("using |;", options: new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
            Performance = performance,
#endif
        }, configure: workspace => workspace.OpenOrChangeDocument(new Uri("memory:///Routing/Oak.akcss"),
            SourceText.From("@using Avalonia.Controls;\nButton.primary { Width: 1; }")));
        await host.Session.Ready;
#if DEBUG
        performance.Reset();
#endif
        await host.Session.RequestCompletionAsync();
        Assert.Contains(host.Session.Completions, item => item.Native?.Kind == AkburaCompletionKind.AkcssModule);
        Assert.Contains(host.Session.Completions, item => item.IsProjected && item.DisplayText == "System");
        Assert.Null(host.Session.LastServiceError);
#if DEBUG
        AssertCounts(performance, completionSync: 1, analysisSync: 0, native: 1, projected: 1);
        Assert.Equal(0, performance.CountOf("Parse"));
        Assert.Equal(0, performance.CountOf("WithText"));
#endif
    });

    [Fact]
    public Task BindingMemberDotRetainsProjectedMembersAndSourceEdits() => EditorTestHost.Run(async () =>
    {
#if DEBUG
        var performance = new EditorPerformance();
#endif
        using var host = new Host("using Avalonia.Controls;\nusing Demo;\nparam ViewModel Vm;\n<TextBlock Text=${x.bind Vm.Profile|} />",
            options: new()
            {
                Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
                Performance = performance,
#endif
            }, configure: workspace => workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(workspace.DefaultProject.Context,
                "namespace Demo { public class ViewModel { public Profile Profile { get; set; } } public class Profile { public string Name { get; set; } } }")));
        await host.Session.Ready;
#if DEBUG
        performance.Reset();
#endif
        host.Editor.TextArea.PerformTextInput(".");
        await host.Session.CompletionIdle;
        var item = Assert.Single(host.Session.Completions, item => item.IsProjected && item.DisplayText == "Name");
#if DEBUG
        AssertCounts(performance, completionSync: 1, analysisSync: 0, native: 1, projected: 1);
        Assert.Equal(1, performance.CountOf("WithText"));
#endif
        var before = host.Editor.Text;
        Assert.Equal(before.IndexOf("Vm.Profile.", StringComparison.Ordinal) + "Vm.Profile.".Length, item.SourceSpan.Start);
        Assert.True(await host.Session.CommitCompletionAsync(item));
        Assert.Contains("${x.bind Vm.Profile.Name}", host.Editor.Text);
        host.Editor.Undo();
        Assert.Equal(before, host.Editor.Text);
        Assert.Null(host.Session.LastServiceError);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ExplicitInvocationAndIncompleteContinuationBypassAutomaticRejection(bool incomplete) => EditorTestHost.Run(async () =>
    {
#if DEBUG
        var performance = new EditorPerformance();
#endif
        using var host = new Host("using Avalonia.Controls;\n@akcss {\n// comment |\n}\n<Button W />", options: new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
#if DEBUG
            Performance = performance,
#endif
        });
        await host.Session.Ready;
        var position = host.Editor.CaretOffset;
        if (incomplete)
        {
            host.Editor.CaretOffset = host.Editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
            await host.Session.RequestCompletionAsync();
            Assert.True(host.Workspace.CurrentSolution.TryGetDocumentContext(host.Uri, out var context));
            Assert.True(host.Workspace.LanguageServices.Completion.GetCompletions(
                host.Session.CachedSyntax!, context, host.Editor.CaretOffset).IsIncomplete);
            Assert.NotNull(host.Session.CompletionPopup);
            host.Editor.CaretOffset = position;
        }
#if DEBUG
        performance.Reset();
#endif
        host.Editor.TextArea.PerformTextInput("z");
        await host.Session.CompletionIdle;
        if (!incomplete)
        {
#if DEBUG
            AssertCounts(performance, completionSync: 0, analysisSync: 0, native: 0, projected: 0);
#endif
            await host.Session.RequestCompletionAsync();
        }
        AssertUnsupported(host);
        Assert.Empty(host.Session.Completions);
        Assert.Null(host.Session.CompletionPopup);
        Assert.Null(host.Session.LastServiceError);
#if DEBUG
        AssertCounts(performance, completionSync: 1, analysisSync: 0, native: 1, projected: 0);
#endif
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task StaleRejectionCannotDismissANewerValidPopup(bool editAgain) => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var host = new Host("using Avalonia.Controls;\n@akcss {\n// comment |\n}\n<Button W />", options: new()
        {
            Scheduler = scheduler, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        host.Editor.TextArea.PerformTextInput("z");
        var rejected = host.Session.CompletionIdle;
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            AssertUnsupported(host);
            Assert.False(rejected.IsCompleted);
            host.Editor.CaretOffset = host.Editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
            if (editAgain)
            {
                host.Editor.TextArea.PerformTextInput("i");
                await host.Session.CompletionIdle;
            }
            else
            {
                await host.Session.RequestCompletionAsync();
            }
            var popup = host.Session.CompletionPopup;
            var items = host.Session.Completions;
            Assert.NotNull(popup);
            Assert.Contains(items, item => item.DisplayText == "Width");
            held.Release.TrySetResult();
            await rejected;
            Assert.Same(popup, host.Session.CompletionPopup);
            Assert.Equal(items, host.Session.Completions);
            Assert.Null(host.Session.LastServiceError);
        }
        finally { held.Release.TrySetResult(); }
    });

    [Fact]
    public Task RejectionSupersedesAPendingValidRequest() => EditorTestHost.Run(async () =>
    {
        var scheduler = new ControlledScheduler();
        using var host = new Host("using Avalonia.Controls;\n@akcss {\n// comment |\n}\n<Button W />", options: new()
        {
            Scheduler = scheduler, AnalysisDelay = TimeSpan.FromHours(1),
        });
        await host.Session.Ready;
        var position = host.Editor.CaretOffset;
        host.Editor.CaretOffset = host.Editor.Text.IndexOf("W /", StringComparison.Ordinal) + 1;
        var held = scheduler.HoldNextResult(skipOperations: 2);
        var valid = host.Session.RequestCompletionAsync();
        await held.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            host.Editor.CaretOffset = position;
            host.Editor.TextArea.PerformTextInput("z");
            await host.Session.CompletionIdle;
            AssertUnsupported(host);
            held.Release.TrySetResult();
            await valid;
            Assert.Empty(host.Session.Completions);
            Assert.Null(host.Session.CompletionPopup);
            Assert.Null(host.Session.LastServiceError);
        }
        finally { held.Release.TrySetResult(); }
    });

#if DEBUG
    [Fact]
    public Task UnknownDocumentPathSynchronizesBeforeParsingAndReusesThatSyntax() => EditorTestHost.Run(async () =>
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var performance = new EditorPerformance
        {
            BeforeService = async (stream, _) =>
            {
                if (stream == "analysis-sync")
                {
                    entered.TrySetResult();
                    await release.Task;
                }
            },
        };
        using var host = new Host("using Avalonia.Controls;\n<Button W| />", options: new()
        {
            Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1), Performance = performance,
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            Assert.False(host.Workspace.CurrentSolution.TryGetDocument(host.Uri, out _));
            await host.Session.RequestCompletionAsync();
            Assert.Contains(host.Session.Completions, item => item.DisplayText == "Width");
            Assert.True(host.Workspace.CurrentSolution.TryGetDocument(host.Uri, out var document));
            var syntax = host.Session.CachedSyntax;
            Assert.Equal(document.FilePath, syntax!.FilePath);
            AssertCounts(performance, completionSync: 1, analysisSync: 0, native: 1, projected: 0);
            Assert.Equal(1, performance.CountOf("Parse"));
            release.TrySetResult();
            await host.Session.Ready;
            Assert.Same(syntax, host.Session.CachedSyntax);
            Assert.Equal(1, performance.CountOf("Parse"));
            Assert.Equal(0, performance.CountOf("WithText"));
            Assert.Null(host.Session.LastServiceError);
        }
        finally { release.TrySetResult(); }
    });

    private void AssertCounts(EditorPerformance performance, long completionSync, long analysisSync, long native, long projected)
    {
        output.WriteLine($"completion-sync={performance.CountOf("completion-sync")}; analysis-sync={performance.CountOf("analysis-sync")}; " +
            $"native={performance.CountOf("NativeCompletionProvider")}; projected={performance.CountOf("ProjectedCompletionProvider")}");
        Assert.Equal(completionSync, performance.CountOf("completion-sync"));
        Assert.Equal(analysisSync, performance.CountOf("analysis-sync"));
        Assert.Equal(native, performance.CountOf("NativeCompletionProvider"));
        Assert.Equal(projected, performance.CountOf("ProjectedCompletionProvider"));
        Assert.Equal(native, performance.CountOf("CompletionProvider"));
    }
#endif

    private static void AssertUnsupported(Host host)
    {
        var syntax = Assert.IsType<AkburaSyntacticDocument>(host.Session.CachedSyntax);
        Assert.Equal(host.Editor.Text, syntax.Text.ToString());
        Assert.True(syntax.GetCompletionContext(host.Editor.CaretOffset).IsDefault);
        Assert.True(syntax.GetAkcssCompletionContext(host.Editor.CaretOffset).IsDefault);
        Assert.False(syntax.TryGetCSharpCompletionContext(host.Editor.CaretOffset, out _));
    }

    private sealed class Host : IDisposable
    {
        internal Host(string marked, string file = "Routing.akbura", AkburaEditorOptions? options = null,
            Action<AkburaWorkspace>? configure = null)
        {
            Workspace = EditorTestHost.Workspace(true);
            configure?.Invoke(Workspace);
            Uri = new("memory:///Routing/" + file);
            var position = marked.IndexOf('|');
            Assert.True(position >= 0);
            Editor = new() { Text = marked.Remove(position, 1) };
            Session = Editor.Bind(Workspace.DefaultProject, Uri, options ?? new()
            {
                Scheduler = new AkburaInlineScheduler(), AnalysisDelay = TimeSpan.FromHours(1),
            });
            Window = new() { Content = Editor, Width = 800, Height = 500 };
            Window.Show();
            Editor.TextArea.Focus();
            Editor.CaretOffset = position;
        }

        internal AkburaWorkspace Workspace
        {
            get;
        }

        internal Uri Uri
        {
            get;
        }

        internal AkburaCodeEditor Editor
        {
            get;
        }

        internal AkburaEditorSession Session
        {
            get;
        }

        internal Window Window
        {
            get;
        }

        public void Dispose()
        {
            Session.Dispose();
            Window.Close();
            Workspace.Dispose();
        }
    }
}
