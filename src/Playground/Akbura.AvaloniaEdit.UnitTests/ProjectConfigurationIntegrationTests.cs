using Akbura.AvaloniaEdit.Highlighting;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Akbura.AvaloniaEdit.UnitTests;

public sealed class ProjectConfigurationIntegrationTests
{
    [Fact]
    public Task PresetUsesRealInheritedMembersAndKeepsExistingBindingOverloads() => EditorTestHost.Run(async () =>
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.UseAvalonia();
        var configured = workspace.CurrentSolution;
        project.UseAvalonia();
        Assert.Same(configured, workspace.CurrentSolution);
        var control = project.Context.CSharpCompilation.GetTypeByMetadataName("Avalonia.Controls.TextBlock");
        Assert.Equal("Avalonia.Controls", control!.ContainingAssembly.Name);
        Assert.NotNull(project.Context.CSharpCompilation.GetTypeByMetadataName("Akbura.AkburaControl"));
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<TextBlock IsHitTestV />" };
        using var session = editor.Bind(project, new("memory:///Real.akbura"), new() { AutomaticCompletion = false });
        await session.Ready;
        Assert.Null(session.LastServiceError);
        Assert.Equal(project.Id, session.ProjectId);
        editor.CaretOffset = editor.Text.IndexOf("IsHitTestV", StringComparison.Ordinal) + "IsHitTestV".Length;
        await session.RequestCompletionAsync();
        var item = Assert.Single(session.Completions, item => item.DisplayText == "IsHitTestVisible");
        Assert.True(await session.CommitCompletionAsync(item));
        Assert.Contains("IsHitTestVisible", editor.Text);
        await session.RefreshAsync();
        var hover = await session.GetHoverAsync(editor.Text.IndexOf("IsHitTestVisible", StringComparison.Ordinal));
        Assert.NotNull(hover);
        Assert.Contains("IsHitTestVisible", hover.Signature);
        editor.Undo();
        Assert.Equal("using Avalonia.Controls;\n<TextBlock IsHitTestV />", editor.Text);
    });

    [Fact]
    public Task SourceAddRenameAndRemovalRefreshTheOpenEditorAndRejectOldCompletionEdits() => EditorTestHost.Run(async () =>
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.UseAvalonia();
        const string source = "using Avalonia.Controls;\nusing Demo;\nparam ViewModel Vm;\n<TextBlock Text=${x.bind Vm.Profile.} />";
        var editor = new AkburaCodeEditor { Text = source };
        using var session = editor.Bind(project, new("memory:///Live.akbura"), new() { AutomaticCompletion = false });
        await session.Ready;
        var updated = NextAnalysis(session);
        project.AddSource("Models/ViewModel.cs", ModelSource);
        await updated.WaitAsync(TimeSpan.FromSeconds(30));
        editor.CaretOffset = source.IndexOf("Vm.Profile.", StringComparison.Ordinal) + "Vm.Profile.".Length;
        await session.RequestCompletionAsync();
        var oldItem = Assert.Single(session.Completions, item => item.IsProjected && item.DisplayText == "Name");
        updated = NextAnalysis(session);
        project.UpdateSource("Models/ViewModel.cs", ModelSource.Replace("Name", "DisplayName"));
        await updated.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Same(session, editor.Session);
        Assert.Equal(source, editor.Text);
        Assert.False(await session.CommitCompletionAsync(oldItem));
        await session.RequestCompletionAsync();
        Assert.Contains(session.Completions, item => item.IsProjected && item.DisplayText == "DisplayName");
        Assert.DoesNotContain(session.Completions, item => item.IsProjected && item.DisplayText == "Name");
        updated = NextAnalysis(session);
        Assert.True(project.RemoveSource("Models/ViewModel.cs"));
        await updated.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Contains(session.Diagnostics, issue => issue.Message.Contains("ViewModel", StringComparison.Ordinal));
        Assert.Equal(source, editor.Text);
        await session.RequestCompletionAsync();
        Assert.DoesNotContain(session.Completions, item => item.IsProjected && item.DisplayText == "DisplayName");
    });

    [Fact]
    public Task ConfigurationChangesRetainColorsAndRejectCancellationResistantOldResults() => EditorTestHost.Run(async () =>
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.UseAvalonia();
        project.AddSource("ViewModel.cs", ModelSource);
        var scheduler = new ControlledScheduler();
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls; using Demo; param ViewModel Vm; <TextBlock Text=${x.bind Vm.Profile.Name} />" };
        using var session = editor.Bind(project, new("memory:///Race.akbura"), new() { AutomaticCompletion = false, Scheduler = scheduler });
        await session.Ready;
        var colors = Assert.Single(editor.TextArea.TextView.LineTransformers.OfType<WorkspaceColorizer>());
        var previous = colors.Spans;
        var oldHold = scheduler.HoldNextResult(skipOperations: 1);
        var old = session.RefreshAsync();
        await oldHold.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        var newHold = scheduler.HoldNextResult(skipOperations: 1);
        var updated = NextAnalysis(session);
        project.UpdateSource("ViewModel.cs", ModelSource.Replace("Name", "DisplayName"));
        await newHold.Started.Task.WaitAsync(TimeSpan.FromSeconds(30));
        Assert.Empty(session.Classifications);
        Assert.True(previous == colors.Spans);
        newHold.Release.SetResult();
        await updated.WaitAsync(TimeSpan.FromSeconds(30));
        var latest = colors.Spans;
        oldHold.Release.SetResult();
        await old;
        Assert.True(latest == colors.Spans);
        Assert.Contains(session.Diagnostics, issue => issue.Message.Contains("Name", StringComparison.Ordinal));
    });

    [Fact]
    public Task RealCustomControlMetadataBecomesAvailableAfterBinding() => EditorTestHost.Run(async () =>
    {
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.UseAvalonia();
        var compilation = CSharpCompilation.Create("CustomControls_" + Guid.NewGuid().ToString("N"),
            [CSharpSyntaxTree.ParseText("namespace Custom { public class Badge : Avalonia.Controls.TextBlock { public string Caption { get; set; } } }")],
            project.Context.CSharpCompilation.References,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var image = new MemoryStream();
        var emitted = compilation.Emit(image);
        Assert.True(emitted.Success, string.Join(Environment.NewLine, emitted.Diagnostics));
        var editor = new AkburaCodeEditor { Text = "using Custom;\n<Badge />" };
        using var session = editor.Bind(project, new("memory:///Library.akbura"), new() { AutomaticCompletion = false });
        await session.Ready;
        var updated = NextAnalysis(session);
        project.AddAssemblyImage(image.ToArray());
        await updated.WaitAsync(TimeSpan.FromSeconds(30));
        editor.CaretOffset = editor.Text.IndexOf(" />", StringComparison.Ordinal) + 1;
        await session.RequestCompletionAsync();
        Assert.Contains(session.Completions, item => item.DisplayText == "Caption");
        Assert.Contains(session.Completions, item => item.DisplayText == "IsHitTestVisible");
    });

    private static Task NextAnalysis(AkburaEditorSession session)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AnalysisUpdated += Complete;
        return completion.Task;
        void Complete(object? sender, EventArgs args)
        {
            session.AnalysisUpdated -= Complete;
            completion.TrySetResult();
        }
    }

    [Fact]
    public Task PreparedCompileAssetsSupportVisibleNativeCompletionAndDocumentation() => EditorTestHost.Run(async () =>
    {
        var paths = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "CompilerReferences.txt"));
        var references = AkburaReferenceSet.FromFiles(paths);
        using var workspace = new AkburaWorkspace();
        var project = workspace.DefaultProject;
        project.UseAvalonia(references);
        project.AddSource("Models/ViewModel.cs", ModelSource);
        var editor = new AkburaCodeEditor { Text = "using Avalonia.Controls;\n<TextBlock IsHitTestV />" };
        using var session = editor.Bind(project, new("memory:///Prepared.akbura"), new() { AutomaticCompletion = false });
        var window = new Window { Content = editor, Width = 900, Height = 500 };
        try
        {
            window.Show();
            await session.Ready;
            Assert.Null(session.LastServiceError);
            editor.CaretOffset = editor.Text.IndexOf("IsHitTestV", StringComparison.Ordinal) + "IsHitTestV".Length;
            await session.RequestCompletionAsync();
            Assert.Null(session.LastServiceError);
            Assert.NotNull(session.CompletionPopup);
            var item = Assert.Single(session.Completions, item => item.DisplayText == "IsHitTestVisible");
            Assert.NotEmpty(item.Native!.Description);
            Assert.True(await session.CommitCompletionAsync(item));
            await session.RefreshAsync();
            Assert.NotNull(await session.GetHoverAsync(editor.Text.IndexOf("IsHitTestVisible", StringComparison.Ordinal)));
        }
        finally { window.Close(); }
    });

    private const string ModelSource = """
        namespace Demo;

        public sealed class ViewModel
        {
            public Profile Profile
            {
                get; set;
            } = new();
        }

        public sealed class Profile
        {
            public string Name
            {
                get; set;
            } = "";
        }
        """;
}
