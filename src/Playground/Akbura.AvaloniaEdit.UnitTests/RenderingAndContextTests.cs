using Akbura.AvaloniaEdit.Diagnostics;
using Akbura.Language.Syntax;
using Akbura.Workspaces.Diagnostics;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using Microsoft.CodeAnalysis.Text;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class RenderingAndContextTests
{
    [Theory]
    [InlineData(false, "#0000BF")]
    [InlineData(true, "#73A9E6")]
    public Task ClassificationBrushUsesTheActiveThemeDictionary(bool dark, string expectedColor) => EditorTestHost.Run(() =>
    {
        var editor = new AkburaCodeEditor();
        var window = new Window
        {
            Content = editor,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
        };
        try
        {
            window.Show();
            var brush = Assert.IsAssignableFrom<ISolidColorBrush>(Highlighting.EditorBrushes.Get(editor, "Keyword"));
            Assert.Equal(Color.Parse(expectedColor), brush.Color);
        }
        finally
        {
            window.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ZeroLengthEofMarkersAndMultilineColorsRenderSafelyWithCustomThemeBrushes() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "/* comment\r\n😀 next line */\r\nparam int" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Render.akbura"),
            new() { AutomaticCompletion = false });
        var window = new Window { Content = editor, Width = 700, Height = 400 };
        try
        {
            window.Show();
            await session.Ready;
            Assert.Null(session.LastServiceError);
            Assert.NotEmpty(session.Diagnostics);
            Assert.Contains(session.Classifications, span => span.Span.Length > 20);
            editor.Resources["AkburaEditor.CommentBrush"] = Brushes.Magenta;
            Assert.Same(Brushes.Magenta, Highlighting.EditorBrushes.Get(editor, "Comment"));
            var renderer = new DiagnosticRenderer(editor);
            renderer.SetDiagnostics(editor.Document,
            [
                new(new TextSpan(0, 0), "TEST_START", "At start", AkburaDiagnosticSeverity.Warning),
                new(new TextSpan(editor.Text.Length, 0), "TEST_EOF", "At end", AkburaDiagnosticSeverity.Error),
            ]);
            editor.TextArea.TextView.EnsureVisualLines();
            var drawing = new DrawingGroup();
            using (var context = drawing.Open()) renderer.Draw(editor.TextArea.TextView, context);
            window.RequestedThemeVariant = ThemeVariant.Light;
            editor.TextArea.TextView.Redraw();
            editor.Text = "";
            renderer.SetDiagnostics(editor.Document, [new(new TextSpan(0, 0), "TEST_EMPTY", "Empty", AkburaDiagnosticSeverity.Error)]);
            editor.TextArea.TextView.EnsureVisualLines();
            using (var context = drawing.Open()) renderer.Draw(editor.TextArea.TextView, context);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task ReferencedDocumentChangeRefreshesAnExistingBinding() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var editor = new AkburaCodeEditor { Text = "namespace Demo;\n<Card/>" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///View.akbura"),
            new() { AutomaticCompletion = false });
        await session.Ready;
        Assert.Contains(session.Diagnostics, issue => issue.Message.Contains("Card"));
        var refreshed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AnalysisUpdated += (_, _) => refreshed.TrySetResult();
        workspace.OpenDocument(workspace.DefaultProjectId, new("memory:///Card.akbura"),
            SourceText.From("namespace Demo; using Avalonia.Controls; <Button/>"));
        await refreshed.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.DoesNotContain(session.Diagnostics, issue => issue.Message.Contains("Card"));
        Assert.Equal("namespace Demo;\n<Card/>", editor.Text);
    });

    [Fact]
    public Task ReadonlyEditorStillProvidesProjectedCSharpHover() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context,
            "namespace Demo { public class ViewModel { public string Title { get; set; } } }"));
        var editor = new AkburaCodeEditor
        {
            Text = "using Avalonia.Controls; using Demo; param ViewModel Vm; <TextBlock Text=${x.bind Vm.Title} />",
            IsReadOnly = true,
        };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new("memory:///Hover.akbura"));
        await session.Ready;
        var hover = await session.GetHoverAsync(editor.Text.IndexOf("Title", StringComparison.Ordinal));
        Assert.NotNull(hover);
        Assert.Contains("Title", hover.Signature);
        Assert.NotEmpty(session.Classifications);
        Assert.Null(session.LastServiceError);
    });
}
