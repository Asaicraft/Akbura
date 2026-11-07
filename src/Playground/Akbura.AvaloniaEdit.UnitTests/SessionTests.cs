using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.AvaloniaEdit.Completion;
using Akbura.Workspaces;
using Akbura.Workspaces.Classification;
using Akbura.Workspaces.Completion;
using Akbura.Workspaces.Documents;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class SessionTests
{
    private static readonly Uri DocumentUri = new("memory:///Demo/View.akbura");
    private static AkburaEditorOptions Options(IAkburaEditorScheduler? scheduler = null) => new()
    {
        AutomaticCompletion = false,
        AnalysisDelay = TimeSpan.FromHours(1),
        Scheduler = scheduler ?? new AkburaThreadPoolScheduler(),
    };

    [Fact]
    public Task ExistingDocumentInitializesEmptyEditorWithoutOverwritingWorkspace() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        workspace.OpenDocument(workspace.DefaultProjectId, DocumentUri, SourceText.From("state int Count = 1;\r\n"));
        var editor = new AkburaCodeEditor();
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        Assert.Equal("state int Count = 1;\r\n", editor.Text);
        Assert.Equal(editor.Text, workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Documents.Values.Single().Text.ToString());
        Assert.Null(session.LastServiceError);
    });

    [Theory]
    [InlineData("state string Name = \"Привет 😀\";\r\n", "\r\n")]
    [InlineData("// é😀\nstate int Count = 1;\n", "\n")]
    public Task EditsPasteUndoRedoAndBufferReplacementPreserveUtf16AndLineEndings(string initial, string newline) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var oldBuffer = new TextDocument(initial);
        var editor = new AkburaCodeEditor { Document = oldBuffer };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        using (oldBuffer.RunUpdate())
        {
            oldBuffer.Insert(0, "// pasted 😀" + newline);
            oldBuffer.Replace(oldBuffer.TextLength - newline.Length, newline.Length, " /* end */" + newline);
            oldBuffer.Remove(0, 3);
        }
        var changed = editor.Text;
        await session.RefreshAsync();
        AssertText(workspace, changed);
        editor.Undo();
        await session.RefreshAsync();
        AssertText(workspace, initial);
        editor.Redo();
        await session.RefreshAsync();
        AssertText(workspace, changed);
        editor.Document = new TextDocument("// replacement 😀" + newline + "param int Value;");
        await session.RefreshAsync();
        AssertText(workspace, editor.Text);
        oldBuffer.Insert(0, "ignored");
        Assert.DoesNotContain("ignored", editor.Text);
        editor.Document = null!;
        Assert.NotNull(editor.Document);
        editor.Document.Insert(0, "state int Restored = 1;");
        await session.RefreshAsync();
        Assert.Equal("state int Restored = 1;", editor.Text);
        AssertText(workspace, editor.Text);
        Assert.Null(session.LastServiceError);
    });

    [Theory]
    [InlineData("state int Count = 1;\n<Button Width=", ".akbura")]
    [InlineData("@utilities { Control.card { Width: 10; } }", ".akcss")]
    [InlineData("/* comment\r\nnext 😀 line */\r\n.class {", ".akcss")]
    public Task HighlightingUsesRealWorkspaceClassifications(string source, string extension) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var uri = new Uri("memory:///Demo/Test" + extension);
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, uri, Options());
        await session.Ready;
        Assert.Null(session.LastServiceError);
        Assert.NotEmpty(session.Classifications);
        Assert.True(workspace.CurrentSolution.TryGetDocumentContext(uri, out var context));
        var expected = workspace.LanguageServices.Classification.GetClassifications(context, new TextSpan(0, source.Length));
        Assert.Equal(expected.ToArray(), session.Classifications.ToArray());
        editor.Document.Insert(0, "// 😀\n");
        Assert.Empty(session.Classifications); // Old numeric spans are invalid immediately.
        await session.RefreshAsync();
        Assert.NotEmpty(session.Classifications);
    });

    [Theory]
    [InlineData("using Avalonia.Controls;\n<Bu|", "Button")]
    [InlineData("using Avalonia.Controls;\n<Button Wid| />", "Width")]
    [InlineData("st|", "state")]
    [InlineData("// 😀\r\n<ButtonSp|", "ButtonSpinner")]
    public Task NativeCompletionUsesCanonicalEditsAndOneUndo(string marked, string label) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        var (source, position) = Caret(marked);
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        editor.CaretOffset = position;
        await session.RequestCompletionAsync();
        Assert.Null(session.LastServiceError);
        var item = Assert.Single(session.Completions, candidate => !candidate.IsProjected && candidate.DisplayText == label);
        workspace.CurrentSolution.TryGetDocumentContext(DocumentUri, out var context);
        var syntax = AkburaSyntacticDocument.Parse(context.Document.Text, context.Document.FilePath);
        var native = workspace.LanguageServices.Completion.GetCompletions(syntax, context, position).Items.First(candidate => candidate.ResolveKey == item.ResolveKey);
        var expected = workspace.LanguageServices.Completion.GetCompletionChange(syntax, context, position, native);
        if (label == "ButtonSpinner") Assert.True(expected.Changes.Length > 1);
        Assert.True(await session.CommitCompletionAsync(item));
        Assert.Equal(SourceText.From(source).WithChanges(expected.Changes).ToString(), editor.Text);
        Assert.Equal(expected.NewPosition, editor.CaretOffset);
        editor.Undo();
        Assert.Equal(source, editor.Text);
    });

    [Fact]
    public Task StandaloneAkcssCompletionUsesWorkspaceSnippet() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = ".cl" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, new Uri("memory:///Styles.akcss"), Options());
        await session.Ready;
        editor.CaretOffset = editor.Text.Length;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == ".class");
        Assert.True(await session.CommitCompletionAsync(item));
        Assert.StartsWith(".class {", editor.Text);
        editor.Undo();
        Assert.Equal(".cl", editor.Text);
    });

    [Fact]
    public Task SemanticContextArrivesAfterBindingAndRefreshesWithoutAnEdit() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<Button/>" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        Assert.Contains(session.Diagnostics, issue => issue.Message.Contains("Button"));
        var source = editor.Text;
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AnalysisUpdated += (_, _) => updated.TrySetResult();
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        await Task.Run(() => workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context)));
        await updated.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Equal(source, editor.Text);
        Assert.DoesNotContain(session.Diagnostics, issue => issue.Message.Contains("Button"));
        Assert.Contains(session.Classifications, span => span.Kind == AkburaClassificationKind.Type || span.Kind == AkburaClassificationKind.Component);
        editor.Document.Replace(editor.Text.IndexOf("Button", StringComparison.Ordinal), 6, "Bu");
        editor.CaretOffset = editor.Text.IndexOf("Bu", StringComparison.Ordinal) + 2;
        await session.RequestCompletionAsync();
        Assert.Contains(session.Completions, item => item.DisplayText == "Button");
    });

    [Theory]
    [InlineData("Vm.Pro|", "Profile")]
    [InlineData("Vm.Profile.|", "Name")]
    public Task ProjectedCSharpUsesHostViewModelAndRefreshesAfterHostCSharpChanges(string expression, string expected) => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        const string model = "namespace Demo { public class ViewModel { public Profile Profile { get; set; } } public class Profile { public string Name { get; set; } } }";
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context, model));
        var (source, position) = Caret("using Avalonia.Controls;\nusing Demo;\nparam ViewModel Vm;\n<TextBlock Text=${x.bind " + expression + "} />");
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        editor.CaretOffset = position;
        await session.RequestCompletionAsync();
        Assert.Null(session.LastServiceError);
        var item = Assert.Single(session.Completions, item => item.IsProjected && item.DisplayText == expected);
        Assert.True(await session.CommitCompletionAsync(item));
        Assert.Contains(expected, editor.Text);
        editor.Undo();
        Assert.Equal(source, editor.Text);
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context, model.Replace("Name", "Renamed")));
        await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
        editor.CaretOffset = position;
        await session.RequestCompletionAsync();
        if (expected == "Name")
        {
            Assert.DoesNotContain(session.Completions, item => item.IsProjected && item.DisplayText == "Name");
            Assert.Contains(session.Completions, item => item.IsProjected && item.DisplayText == "Renamed");
        }
    });

    [Fact]
    public Task DiagnosticsAndQuickInfoHaveSourceCoordinatesAndClearAfterFix() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace(true);
        const string source = "// 😀\r\nusing Avalonia.Controls;\r\n<Button Width=\"10\" WrongProperty=\"x\" />";
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        Assert.NotEmpty(session.Diagnostics);
        var issue = Assert.Single(session.Diagnostics, issue => issue.Message.Contains("WrongProperty"));
        Assert.True(issue.Span.Start >= source.IndexOf("WrongProperty", StringComparison.Ordinal));
        Assert.NotNull(issue.CanonicalDiagnostic);
        var diagnosticHover = await session.GetHoverAsync(issue.Span.Start);
        Assert.NotNull(diagnosticHover);
        Assert.Contains(diagnosticHover.Diagnostics, candidate => candidate.Code == issue.Code);
        var typeHover = await session.GetHoverAsync(source.IndexOf("Width", StringComparison.Ordinal) + 2);
        Assert.NotNull(typeHover);
        Assert.Contains("Width", typeHover.Signature);
        editor.Text = "using Avalonia.Controls;\r\n<Button Width=\"10\" />";
        Assert.Empty(session.Diagnostics);
        Assert.Null(session.Hover);
        await session.RefreshAsync();
        Assert.DoesNotContain(session.Diagnostics, issue => issue.Message.Contains("WrongProperty"));
    });

    [Fact]
    public Task DisposalRebindingSharedOwnershipAndReadonly() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "st" };
        var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        var second = new AkburaCodeEditor();
        Assert.Throws<InvalidOperationException>(() => second.Bind(workspace, workspace.DefaultProjectId, DocumentUri));
        using var otherSession = second.Bind(workspace, workspace.DefaultProjectId, new Uri("memory:///Other.akcss"), Options());
        await otherSession.Ready;
        editor.CaretOffset = 2;
        await session.RequestCompletionAsync();
        var completion = Assert.Single(session.Completions, item => item.DisplayText == "state");
        editor.IsReadOnly = true;
        Assert.False(await session.CommitCompletionAsync(completion));
        Assert.Equal("st", editor.Text);
        await session.RefreshAsync();
        Assert.NotEmpty(session.Classifications);
        session.Dispose();
        session.Dispose();
        Assert.Null(editor.Session);
        Assert.Empty(editor.TextArea.TextView.LineTransformers.OfType<Highlighting.WorkspaceColorizer>());
        Assert.True(workspace.CurrentSolution.TryGetDocument(DocumentUri, out _));
        using var rebound = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await rebound.Ready;
        Assert.Single(editor.TextArea.TextView.LineTransformers.OfType<Highlighting.WorkspaceColorizer>());
        workspace.OpenDocument(workspace.DefaultProjectId, new Uri("memory:///StillAlive.akbura"), SourceText.From(""));
    });

    [Fact]
    public Task RealPopupCanAcceptDismissReopenAndSurviveDetach() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "st", ShowLineNumbers = true };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        var window = new Window { Width = 800, Height = 500, Content = editor };
        try
        {
            window.Show();
            editor.Focus();
            await session.Ready;
            editor.CaretOffset = 2;
            await session.RequestCompletionAsync();
            Assert.NotNull(session.CompletionPopup);
            var popup = session.CompletionPopup!;
            popup.CompletionList.SelectedItem = Assert.Single(session.Completions, item => item.DisplayText == "state");
            popup.CompletionList.RequestInsertion(EventArgs.Empty);
            await Until(() => editor.Text == "state ");
            editor.Undo();
            Assert.Equal("st", editor.Text);
            editor.CaretOffset = 2;
            await session.RequestCompletionAsync();
            Assert.NotNull(session.CompletionPopup);
            session.CompletionPopup!.Hide();
            await session.RequestCompletionAsync();
            Assert.NotNull(session.CompletionPopup);
            window.Content = null;
            Assert.Null(session.CompletionPopup);
            window.Content = editor;
            editor.CaretOffset = 2;
            await session.RequestCompletionAsync();
            Assert.NotNull(session.CompletionPopup);
            Assert.Null(session.LastServiceError);
        }
        finally { window.Close(); }
    });

    [Fact]
    public Task StaleCompletionCannotApplyAfterTextOrProjectChanges() => EditorTestHost.Run(async () =>
    {
        using var workspace = EditorTestHost.Workspace();
        var editor = new AkburaCodeEditor { Text = "st" };
        using var session = editor.Bind(workspace, workspace.DefaultProjectId, DocumentUri, Options());
        await session.Ready;
        editor.CaretOffset = 2;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == "state");
        editor.Document.Insert(0, "// 😀\n");
        Assert.False(await session.CommitCompletionAsync(item));
        Assert.Equal("// 😀\nst", editor.Text);
        await session.RequestCompletionAsync();
        item = Assert.Single(session.Completions, item => item.DisplayText == "state");
        var context = workspace.CurrentSolution.GetRequiredProject(workspace.DefaultProjectId).Context;
        workspace.AddOrUpdateProject(EditorTestHost.SemanticContext(context));
        Assert.False(await session.CommitCompletionAsync(item));
    });

    [Fact]
    public void InvalidMultiEditSetIsRejectedBeforeMutation()
    {
        var valid = new AkburaCompletionChange([new(new TextSpan(0, 0), "// 😀\r\n"), new(new TextSpan(5, 2), "Name")], 10, true);
        Assert.Equal(2, CompletionEdits.Validate(valid, 10).Length);
        var prefixAndReplacement = new AkburaCompletionChange([
            new(new TextSpan(0, 2), "Name"), new(new TextSpan(0, 0), "using Demo;\r\n")], 17, false);
        var ordered = CompletionEdits.Validate(prefixAndReplacement, 2);
        var buffer = new TextDocument("Na");
        foreach (var edit in ordered.Reverse()) buffer.Replace(edit.Span.Start, edit.Span.Length, edit.NewText ?? "");
        Assert.Equal("using Demo;\r\nName", buffer.Text);
        Assert.Throws<InvalidOperationException>(() => CompletionEdits.Validate(new([
            new(new TextSpan(2, 4), "x"), new(new TextSpan(3, 0), "y")], 1, false), 10));
        Assert.Throws<InvalidOperationException>(() => CompletionEdits.Validate(new([new(new TextSpan(10, 1), "x")], 1, false), 10));
        Assert.Throws<InvalidOperationException>(() => CompletionEdits.Validate(new([], 11, false), 10));
    }

    private static (string Source, int Position) Caret(string marked) => (marked.Replace("|", ""), marked.IndexOf('|'));
    private static void AssertText(AkburaWorkspace workspace, string expected)
    {
        Assert.True(workspace.CurrentSolution.TryGetDocument(DocumentUri, out var document));
        Assert.Equal(expected, document.Text.ToString());
    }

    private static async Task Until(Func<bool> predicate)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        while (!predicate()) await Task.Delay(10, timeout.Token);
    }
}
