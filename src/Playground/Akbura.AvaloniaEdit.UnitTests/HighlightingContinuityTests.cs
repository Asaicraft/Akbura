using Akbura.AvaloniaEdit.Highlighting;
using Akbura.Workspaces;
using Akbura.Workspaces.Classification;
using Avalonia.Controls;
using Avalonia.Media;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class HighlightingContinuityTests
{
    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public Task HeldAnalysisKeepsMappedSemanticForegroundAfterLayout(string newline) => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = SemanticSource.Replace("\n", newline) };
        editor.Resources["AkburaEditor.PropertyNameBrush"] = Brushes.Magenta;
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        var window = new Window { Content = editor, Width = 900, Height = 500 };
        try
        {
            window.Show();
            await session.Ready;
            AssertForeground(window, editor, "Title", AkburaClassificationKind.PropertyName);
            var before = Colors(editor).Spans;
            var prefix = "// Привет 😀" + newline;
            editor.Document.Insert(0, prefix);
            Assert.Empty(session.Classifications);
            Assert.Equal(before.Select(span => span.Span.Start + prefix.Length), Colors(editor).Spans.Select(span => span.Span.Start));

            // Hold the combined result after synchronization. A syntax-only publication here
            // would turn Title back into a generic embedded-expression color.
            var held = scheduler.HoldNextResult(skipOperations: 1);
            var refresh = session.RefreshAsync();
            await held.Started.Task.WaitAsync(Timeout);
            Assert.Empty(session.Classifications);
            AssertForeground(window, editor, "Title", AkburaClassificationKind.PropertyName);
            editor.Document.Insert(editor.Text.IndexOf("1;", StringComparison.Ordinal), "2");
            editor.Document.Insert(0, "\t");
            editor.Document.Remove(0, 1);
            AssertForeground(window, editor, "Title", AkburaClassificationKind.PropertyName);
            held.Release.SetResult();
            await refresh;
            await session.RefreshAsync();
            AssertCurrentWorkspaceColors(workspace, editor, session);
            AssertForeground(window, editor, "Title", AkburaClassificationKind.PropertyName);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("state string Caption = \"Привет мир 😀\";\r\nstate int Count = 1;", "akbura", "Привет")]
    [InlineData("/* Привет\r\nмир 😀 */\r\nstate int Count = 1;", "akbura", "Привет")]
    [InlineData("state int Count = 12345;", "akbura", "state")]
    [InlineData("state int Count = 12345;", "akbura", "12345")]
    [InlineData("using Avalonia.Controls;\n<TextBlock Text=\"value\" />", "akbura", "TextBlock")]
    [InlineData(SemanticSource, "akbura", "Title")]
    [InlineData("@utilities {\n Control.card { Width: 12345; }\n}", "akcss", "card")]
    [InlineData("@utilities {\n Control.card { Width: 12345; }\n}", "akcss", "12345")]
    public Task InternalEditsRetainTheTokensActualForegroundWhileAnalysisIsHeld(string source, string extension, string token) =>
        EditorTestHost.Run(async () =>
        {
            using var workspace = SemanticWorkspace();
            var scheduler = new ControlledScheduler();
            var editor = new AkburaCodeEditor { Text = source };
            using var session = editor.Bind(workspace, workspace.DefaultProjectId,
                new("memory:///InternalEdit." + extension), Options(scheduler));
            var window = new Window { Content = editor, Width = 900, Height = 500 };
            try
            {
                window.Show();
                await session.Ready;
                var offset = source.IndexOf(token, StringComparison.Ordinal) + 1;
                var original = Assert.Single(Colors(editor).Spans, span => span.Span.Contains(offset));
                Assert.InRange(offset, original.Span.Start + 1, original.Span.End - 1);
                editor.Resources[$"AkburaEditor.{original.Kind}Brush"] = Brushes.Magenta;
                editor.TextArea.TextView.Redraw();
                AssertForegroundRange(window, editor, original.Span, original.Kind);

                editor.Document.Insert(offset, "Я😀");
                var held = scheduler.HoldNextResult(skipOperations: 1);
                var refresh = session.RefreshAsync();
                await held.Started.Task.WaitAsync(Timeout);
                Assert.Empty(session.Classifications);
                AssertForegroundRange(window, editor, new TextSpan(original.Span.Start, original.Span.Length + 3), original.Kind);

                // Continue editing without accepting any analysis result, including a UTF-16
                // replacement and deletion. Both original and inserted characters stay colored.
                editor.Document.Replace(offset + 1, 2, "Ж");
                AssertForegroundRange(window, editor, new TextSpan(original.Span.Start, original.Span.Length + 2), original.Kind);
                editor.Document.Remove(offset, 1);
                AssertForegroundRange(window, editor, new TextSpan(original.Span.Start, original.Span.Length + 1), original.Kind);
                held.Release.SetResult();
                await refresh;
                await session.RefreshAsync();
                AssertCurrentWorkspaceColors(workspace, editor, session);
            }
            finally { window.Close(); }
        });

    [Theory]
    [InlineData(0, 0, "😀", new[] { 2, 3, 6, 3, 10, 3 })]
    [InlineData(3, 0, "X", new[] { 0, 3, 5, 3, 9, 3 })]
    [InlineData(4, 0, "😀", new[] { 0, 3, 6, 3, 10, 3 })]
    [InlineData(5, 0, "😀", new[] { 0, 3, 4, 5, 10, 3 })]
    [InlineData(5, 1, "", new[] { 0, 3, 4, 2, 7, 3 })]
    [InlineData(5, 1, "Я😀", new[] { 0, 3, 4, 5, 10, 3 })]
    [InlineData(4, 1, "Z", new[] { 0, 3, 4, 3, 8, 3 })]
    [InlineData(6, 1, "Z", new[] { 0, 3, 4, 3, 8, 3 })]
    [InlineData(4, 3, "", new[] { 0, 3, 5, 3 })]
    [InlineData(4, 3, "Я😀\r\n", new[] { 0, 3, 10, 3 })]
    [InlineData(2, 7, "z", new[] { 0, 2, 3, 2 })]
    [InlineData(2, 3, "😀", new[] { 0, 2, 4, 2, 7, 3 })]
    public Task EditsTrackSurvivingTokensAndFragmentsWithoutColoringBoundaryInsertions(int offset, int length, string inserted, int[] ranges) =>
        EditorTestHost.Run(() =>
        {
            var editor = new AkburaCodeEditor { Text = "abc def ghi" };
            var colors = new WorkspaceColorizer(editor);
            colors.SetSpans(editor.Document,
            [
                new(new TextSpan(0, 3), AkburaClassificationKind.Keyword),
                new(new TextSpan(4, 3), AkburaClassificationKind.String),
                new(new TextSpan(8, 3), AkburaClassificationKind.Type),
            ]);
            editor.Document.Changed += (_, change) => colors.TrackChange(editor.Document, change);
            editor.Document.Replace(offset, length, inserted);
            var expected = ranges.Chunk(2).Select(range => new TextSpan(range[0], range[1]));
            Assert.Equal(expected, colors.Spans.Select(span => span.Span));
            Assert.All(colors.Spans, span => Assert.InRange(span.Span.End, 1, editor.Document.TextLength));
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("state string Name = \"Привет 😀\";\r\nstate int Count = 1;\r\n", "View.akbura", "Привет", "Count", true)]
    [InlineData("@utilities {\n Control.card {\n Width: 10;\n Height: 20;\n }\n}", "Styles.akcss", "10", "Height", false)]
    public Task BatchedPasteReplacementAndUndoRedoTrackEachIntermediateBuffer(string source, string file, string replaced, string target, bool retainsInsertedColor) =>
        EditorTestHost.Run(async () =>
        {
            using var workspace = EditorTestHost.Workspace(true);
            var editor = new AkburaCodeEditor { Text = source };
            using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///" + file), Options());
            await session.Ready;
            var original = Token(editor, target);
            editor.Document.UndoStack.ClearAll();
            using (editor.Document.RunUpdate())
            {
                editor.Document.Insert(0, "// вставка 😀\r\n\n");
                AssertToken(editor, target, original.Kind);
                editor.Document.Replace(editor.Text.IndexOf(replaced, StringComparison.Ordinal), replaced.Length, "こんにちは😀\r\n");
                AssertToken(editor, target, original.Kind);
                editor.Document.Remove(0, 3);
                AssertToken(editor, target, original.Kind);
            }
            var changed = editor.Text;
            Assert.Empty(session.Classifications);
            Assert.Equal(retainsInsertedColor,
                Colors(editor).Spans.Any(span => span.Span.Contains(editor.Text.IndexOf("こんにちは", StringComparison.Ordinal))));
            editor.Undo();
            Assert.Equal(source, editor.Text);
            Assert.False(editor.Document.UndoStack.CanUndo);
            AssertToken(editor, target, original.Kind);
            editor.Redo();
            Assert.Equal(changed, editor.Text);
            AssertToken(editor, target, original.Kind);
            await session.RefreshAsync();
            AssertCurrentWorkspaceColors(workspace, editor, session);
        });

    [Fact]
    public Task InitialLoadStillPublishesSyntaxBeforeCombinedAnalysis() => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var held = scheduler.HoldNextResult(skipOperations: 3);
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        await held.Started.Task.WaitAsync(Timeout);
        Assert.NotEmpty(session.Classifications);
        Assert.NotEmpty(Colors(editor).Spans);
        Assert.DoesNotContain(Colors(editor).Spans, span => span.Kind == AkburaClassificationKind.PropertyName);
        held.Release.SetResult();
        await session.Ready;
        AssertToken(editor, "Title", AkburaClassificationKind.PropertyName);
    });

    [Fact]
    public Task ContextRefreshRetainsColorsUntilItsReplacementIsReady() => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        await session.Ready;
        var before = Colors(editor).Spans;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var updated = NextAnalysis(session);
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context));
        await held.Started.Task.WaitAsync(Timeout);
        Assert.Empty(session.Classifications);
        Assert.True(before == Colors(editor).Spans);
        AssertToken(editor, "Title", AkburaClassificationKind.PropertyName);
        held.Release.SetResult();
        await updated.WaitAsync(Timeout);
        AssertCurrentWorkspaceColors(workspace, editor, session);
    });

    [Fact]
    public Task ObsoleteAnalysisCannotOverwriteNewerMappedPresentation() => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        await session.Ready;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var old = session.RefreshAsync();
        await held.Started.Task.WaitAsync(Timeout);
        editor.Document.Insert(0, "// новый 😀\r\n");
        await session.RefreshAsync();
        var latest = Colors(editor).Spans;
        held.Release.SetResult();
        await old;
        Assert.True(latest == Colors(editor).Spans);
        AssertCurrentWorkspaceColors(workspace, editor, session);
    });

    [Fact]
    public Task CancellationAndServiceFailureRetainAvailablePresentation() => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        await session.Ready;
        editor.Document.Insert(0, "// ё😀\n");
        var provisional = Colors(editor).Spans;
        using var cancellation = new CancellationTokenSource();
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var refresh = session.RefreshAsync(cancellation.Token);
        await held.Started.Task.WaitAsync(Timeout);
        cancellation.Cancel();
        held.Release.SetResult();
        await refresh;
        Assert.True(provisional == Colors(editor).Spans);
        Assert.Null(session.LastServiceError);
        AkburaEditorServiceFailedEventArgs? failure = null;
        session.ServiceFailed += (_, args) => failure = args;
        scheduler.FailNext = true;
        await session.RefreshAsync();
        Assert.Equal("Analysis", failure?.Operation);
        Assert.IsType<InvalidOperationException>(session.LastServiceError);
        Assert.True(provisional == Colors(editor).Spans);
        await session.RefreshAsync();
        Assert.Null(session.LastServiceError);
        AssertCurrentWorkspaceColors(workspace, editor, session);
    });

    [Fact]
    public Task ReplacementDisposalAndRebindingReleaseTheOldPresentation() => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options(scheduler));
        await session.Ready;
        var colors = Colors(editor);
        var oldBuffer = editor.Document;
        var held = scheduler.HoldNextResult(skipOperations: 1);
        var old = session.RefreshAsync();
        await held.Started.Task.WaitAsync(Timeout);
        var replacement = scheduler.HoldNextResult();
        var updated = NextAnalysis(session);
        editor.Document = new TextDocument("// replacement 😀\r\nstate int Value = 2;");
        Assert.Empty(colors.Spans);
        oldBuffer.Insert(0, "ignored");
        Assert.Empty(colors.Spans);
        await replacement.Started.Task.WaitAsync(Timeout);
        replacement.Release.SetResult();
        await updated.WaitAsync(Timeout);
        var latest = colors.Spans;
        held.Release.SetResult();
        await old;
        Assert.True(latest == colors.Spans);
        AssertCurrentWorkspaceColors(workspace, editor, session);

        held = scheduler.HoldNextResult(skipOperations: 1);
        old = session.RefreshAsync();
        await held.Started.Task.WaitAsync(Timeout);
        session.Dispose();
        Assert.Empty(colors.Spans);
        Assert.DoesNotContain(colors, editor.TextArea.TextView.LineTransformers);
        using var rebound = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options());
        await rebound.Ready;
        var reboundColors = Colors(editor).Spans;
        held.Release.SetResult();
        await old;
        Assert.Empty(colors.Spans);
        Assert.True(reboundColors == Colors(editor).Spans);
    });

    [Fact]
    public Task EmptyBuffersAndAcceptedEmptyResultsRemoveColors() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "state int Count = 1;" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options());
        await session.Ready;
        var colors = Colors(editor);
        Assert.NotEmpty(colors.Spans);
        colors.SetSpans(editor.Document, []);
        Assert.Empty(colors.Spans);
        await session.RefreshAsync();
        Assert.NotEmpty(colors.Spans);
        editor.Document.Remove(0, editor.Document.TextLength);
        Assert.Empty(colors.Spans);
        await session.RefreshAsync();
        Assert.Empty(session.Classifications);
        Assert.Empty(colors.Spans);
        editor.Document.Insert(0, " \r\n\t");
        await session.RefreshAsync();
        Assert.Empty(session.Classifications);
        Assert.Empty(colors.Spans);
    });

    [Theory]
    [InlineData("/*")]
    [InlineData("// ")]
    [InlineData("\"")]
    public Task SyntaxChangingEditsEventuallyReplaceAllProvisionalColors(string delimiter) => EditorTestHost.Run(async () =>
    {
        using var workspace = SemanticWorkspace();
        var editor = new AkburaCodeEditor { Text = SemanticSource };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options());
        await session.Ready;
        editor.Document.Insert(editor.Text.IndexOf("param", StringComparison.Ordinal), delimiter);
        AssertToken(editor, "Title", AkburaClassificationKind.PropertyName);
        await session.RefreshAsync();
        AssertCurrentWorkspaceColors(workspace, editor, session);
        Assert.NotEmpty(session.Classifications);
    });

    [Fact]
    public Task CompletionImportAndReplacementKeepTrackedColorsAndOneUndo() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        const string source = "// 😀\r\nstate int Tail = 1;\r\n<ButtonSp />";
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, Uri, Options());
        await session.Ready;
        var tail = Token(editor, "Tail");
        editor.Document.UndoStack.ClearAll();
        editor.CaretOffset = source.IndexOf("ButtonSp", StringComparison.Ordinal) + "ButtonSp".Length;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == "ButtonSpinner");
        Assert.True(await session.CommitCompletionAsync(item));
        var committed = editor.Text;
        Assert.Contains("using Avalonia.Controls;", committed);
        AssertToken(editor, "Tail", tail.Kind);
        editor.Undo();
        Assert.Equal(source, editor.Text);
        Assert.False(editor.Document.UndoStack.CanUndo);
        AssertToken(editor, "Tail", tail.Kind);
        editor.Redo();
        Assert.Equal(committed, editor.Text);
        AssertToken(editor, "Tail", tail.Kind);
        await session.RefreshAsync();
        AssertCurrentWorkspaceColors(workspace, editor, session);
    });

    private static readonly Uri Uri = new("memory:///Continuity.akbura");
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(30);
    private const string SemanticSource = "using Avalonia.Controls;\nusing Demo;\nparam ViewModel Vm;\nstate int Count = 1;\n<TextBlock Text=${x.bind Vm.Title} />";

    private static AkburaEditorOptions Options(ControlledScheduler? scheduler = null) => new()
    {
        Scheduler = scheduler ?? new ControlledScheduler(), AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1),
    };

    private static AkburaWorkspace SemanticWorkspace()
    {
        var workspace = EditorTestHost.Workspace();
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context,
            "namespace Demo { public class ViewModel { public string Title { get; set; } } }"));
        return workspace;
    }

    private static WorkspaceColorizer Colors(AkburaCodeEditor editor) => Assert.Single(editor.TextArea.TextView.LineTransformers.OfType<WorkspaceColorizer>());

    private static AkburaClassifiedSpan Token(AkburaCodeEditor editor, string token) =>
        Assert.Single(Colors(editor).Spans, span => editor.Document.GetText(span.Span.Start, span.Span.Length) == token);

    private static void AssertToken(AkburaCodeEditor editor, string token, AkburaClassificationKind kind)
    {
        var span = Token(editor, token);
        Assert.Equal(kind, span.Kind);
        Assert.Equal(editor.Text.IndexOf(token, StringComparison.Ordinal), span.Span.Start);
    }

    private static void AssertForeground(Window window, AkburaCodeEditor editor, string token, AkburaClassificationKind kind)
    {
        AssertToken(editor, token, kind);
        var offset = editor.Text.IndexOf(token, StringComparison.Ordinal);
        AssertForegroundRange(window, editor, new TextSpan(offset, token.Length), kind);
    }

    private static void AssertForegroundRange(Window window, AkburaCodeEditor editor, TextSpan span, AkburaClassificationKind kind)
    {
        window.UpdateLayout();
        var view = editor.TextArea.TextView;
        view.EnsureVisualLines();
        for (var offset = span.Start; offset < span.End; offset++)
        {
            if (editor.Document.GetCharAt(offset) is '\r' or '\n') continue;
            var line = Assert.Single(view.VisualLines, line => offset >= line.FirstDocumentLine.Offset && offset < line.LastDocumentLine.EndOffset);
            var relative = offset - line.FirstDocumentLine.Offset;
            var element = Assert.Single(line.Elements, element => relative >= element.RelativeTextOffset && relative < element.RelativeTextOffset + element.DocumentLength);
            Assert.Equal(EditorBrushes.Get(editor, kind.ToString()), element.TextRunProperties.ForegroundBrush);
        }
    }

    private static void AssertCurrentWorkspaceColors(AkburaWorkspace workspace, AkburaCodeEditor editor, AkburaEditorSession session)
    {
        Assert.Null(session.LastServiceError);
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(session.DocumentUri, out var context));
        var expected = workspace.LanguageServices.Classification.GetClassifications(context, new TextSpan(0, editor.Document.TextLength));
        Assert.Equal(expected.ToArray(), session.Classifications.ToArray());
        Assert.Equal(expected.Where(span => span.Span.Length > 0).ToArray(), Colors(editor).Spans.ToArray());
    }

    private static Task NextAnalysis(AkburaEditorSession session)
    {
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AnalysisUpdated += Complete;
        return updated.Task;
        void Complete(object? sender, EventArgs args)
        {
            session.AnalysisUpdated -= Complete;
            updated.TrySetResult();
        }
    }
}
