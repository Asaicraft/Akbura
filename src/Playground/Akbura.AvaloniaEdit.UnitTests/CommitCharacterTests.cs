using Avalonia.Controls;
using Avalonia.Headless;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class CommitCharacterTests
{
    [Fact]
    public Task TypingACommitCharacterAfterAProjectChangeKeepsNormalInput() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "st" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Commit.akbura"),
            new() { AutomaticCompletion = false });
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        try
        {
            window.Show();
            editor.TextArea.Focus();
            await session.Ready;
            editor.CaretOffset = 2;
            await session.RequestCompletionAsync();
            Assert.NotNull(session.CompletionPopup);
            session.CompletionPopup!.CompletionList.SelectedItem = Assert.Single(session.Completions, item => item.DisplayText == "state");
            var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
            workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context));
            // The workspace notification is queued, so the old popup is still visible here.
            window.KeyTextInput(" ");
            Assert.Equal("st ", editor.Text);
        }
        finally { window.Close(); }
    });

    [Theory]
    [InlineData("st", "state", ' ', "state ")]
    [InlineData("using Avalonia.Controls;\n<Button Wid />", "Width", '=', "using Avalonia.Controls;\n<Button Width=\"\" />")]
    public Task CommitCharactersAreNotLostOrDuplicatedAndUndoAsOneStep(string source, string label, char character, string expected) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Commit.akbura"),
            new() { AutomaticCompletion = false, AnalysisDelay = TimeSpan.FromHours(1) });
        await session.Ready;
        editor.CaretOffset = label == "Width" ? source.IndexOf("Wid", StringComparison.Ordinal) + 3 : source.Length;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == label && !item.IsProjected);
        Assert.True(await session.CommitCompletionAsync(item, character));
        Assert.Equal(expected, editor.Text);
        editor.Undo();
        Assert.Equal(source, editor.Text);
    });

    [Fact]
    public Task AutomaticCompletionUsesLanguageContextsAndConfiguredGesture() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor();
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Automatic.akbura"),
            new() { AnalysisDelay = TimeSpan.FromMilliseconds(10) });
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        try
        {
            window.Show();
            editor.TextArea.Focus();
            await session.Ready;
            window.KeyTextInput("st");
            Assert.Equal("st", editor.Text);
            await Until(() => session.CompletionPopup != null);
            Assert.Contains(session.Completions, item => item.DisplayText == "state");
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            Assert.Null(session.CompletionPopup);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.Control);
            window.KeyTextInput(" ");
            Assert.Equal("st", editor.Text);
            await Until(() => session.CompletionPopup != null);
            window.KeyReleaseQwerty(Avalonia.Input.PhysicalKey.Space, Avalonia.Input.RawInputModifiers.Control);
            window.KeyPressQwerty(Avalonia.Input.PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            window.KeyTextInput(" ");
            Assert.Equal("st ", editor.Text);
            editor.Text = "// plain comment ";
            editor.CaretOffset = editor.Text.Length;
            window.KeyTextInput("x");
            await Task.Delay(100);
            Assert.Null(session.CompletionPopup);
        }
        finally { window.Close(); }
    });

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}
