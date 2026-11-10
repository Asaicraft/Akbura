using Akbura.Collections;
using Akbura.Pools;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Engine;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia.Interactivity;
using System.Reflection;

namespace Akbura.AvaloniaEdit.BrowserSmoke;

internal sealed partial class SmokeView : AkburaControl
{
    private AkburaWorkspace? _workspace;
    private AkburaEditorSession? _session;
    private bool _smokeStarted;

    private AkburaWorkspace Workspace =>
        _workspace ?? throw new InvalidOperationException("The workspace has not been initialized.");

    private AkburaEditorSession Session =>
        _session ?? throw new InvalidOperationException("The editor session has not been initialized.");

    public SmokeView()
        : base(AkburaEngine.Empty)
    {
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_smokeStarted)
        {
            return;
        }

        _smokeStarted = true;
        _ = RunSmokeAsync();
    }


    private const string ViewModelPath = "Models/ViewModel.cs";

    private const string InitialSource =
        """
        using Avalonia.Controls;
        <TextBlock IsHitTestV />
        """;

    private const string BindingSource =
        """
        using Avalonia.Controls;
        using Demo;
        param ViewModel Vm;
        <TextBlock Text=${x.bind Vm.Profile.} />
        """;

    private async Task RunSmokeAsync()
    {
        var stage = "metadata setup";

        try
        {
            await InitializeWorkspaceAsync();

            stage = "initial analysis";
            VerifyMetadata();

            stage = "inherited completion";
            await VerifyInheritedCompletionAsync();

            await VerifyProjectedCompletionLifecycleAsync(
                currentStage => stage = currentStage);

            await VerifyAkcssAsync(currentStage => stage = currentStage);

            status.Text =
                "PASS: real metadata, utilities, styles/@apply, AKCSS input, " +
                "live source updates, completion, hover and undo.";
#if DEBUG
            InitializePerformance();
#endif
        }
        catch (Exception error)
        {
            Console.Error.WriteLine($"Smoke failure during {stage}: {error}");

            status.Text =
                $"FAILED ({stage}): " +
                $"{error.GetType().Name}: {error.Message}";
        }
    }

    private async Task InitializeWorkspaceAsync()
    {
        var bundle = await ReadReferencesAsync();

        _workspace = new AkburaWorkspace();

        var project = _workspace.DefaultProject;

        project.UseAvalonia(bundle);
        project.AddSource(ViewModelPath, ViewModelSource);

        editor.Text = InitialSource;

        _session = editor.Bind(
            project,
            new Uri("memory:///Views/Smoke.akbura"),
            new()
            {
                Scheduler = new AkburaInlineScheduler()
            });

        _session.ServiceFailed += (_, e) =>
        {
            Console.Error.WriteLine($"Editor service failure during {e.Operation}: {e.Exception}");

            status.Text = $"FAILED: {e.Operation}: {e.Exception.Message}";
        };

        await _session.Ready;
        EnsureHealthy();
    }

    private void VerifyMetadata()
    {
        Require(
            !Session.Classifications.IsEmpty,
            "No classifications from real metadata.");

        var textBlock = Workspace.DefaultProject
            .Context
            .CSharpCompilation
            .GetTypeByMetadataName("Avalonia.Controls.TextBlock");

        Require(
            textBlock?.ContainingAssembly.Name == "Avalonia.Controls",
            "TextBlock is not supplied by real Avalonia metadata.");
    }

    private async Task VerifyInheritedCompletionAsync()
    {
        editor.CaretOffset = PositionAfter(
            InitialSource,
            "IsHitTestV");

        await RequestCompletionAsync();

        var completion = RequireNotNull(
            Session.Completions.SingleOrDefault(static item => !item.IsProjected &&
                item.DisplayText == "IsHitTestVisible"),
            "No inherited InputElement.IsHitTestVisible completion.");

        Require(
            await Session.CommitCompletionAsync(completion),
            "Native completion was rejected.");

        await Session.RefreshAsync();

        var hover = await Session.GetHoverAsync(
            PositionOf(editor.Text, "IsHitTestVisible"));

        Require(
            hover is not null,
            "No hover for the inherited Avalonia member.");

        VerifyUndo(InitialSource, "Native completion");
    }

    private async Task VerifyProjectedCompletionLifecycleAsync(Action<string> setStage)
    {
        var project = Workspace.DefaultProject;

        setStage("projected source completion");

        await ApplyAndAnalyzeAsync(() => editor.Text = BindingSource);

        editor.CaretOffset = PositionAfter(BindingSource, "Vm.Profile.");

        await RequestCompletionAsync();

        var completion = RequireNotNull(
            Session.Completions.SingleOrDefault(static item => item.IsProjected &&
                item.DisplayText == "Name"),
            "No projected Name completion from AddSource.");

        Require(
            await Session.CommitCompletionAsync(completion),
            "Projected commit was rejected.");

        VerifyUndo(BindingSource, "Projected completion");

        var hover = await Session.GetHoverAsync(PositionOf(BindingSource, "x.bind"));

        Require(hover is not null, "No hover information.");

        setStage("live source update");

        await ApplyAndAnalyzeAsync(() =>
            project.UpdateSource(
                ViewModelPath,
                ViewModelSource.Replace("Name", "DisplayName")));

        Require(
            editor.Text == BindingSource &&
            ReferenceEquals(editor.Session, Session),
            "A source update changed or rebound the editor.");

        Require(
            !await Session.CommitCompletionAsync(completion),
            "An obsolete completion was accepted.");

        await RequestCompletionAsync();

        RequireProjectedCompletion(
            "DisplayName",
            expected: true);

        RequireProjectedCompletion(
            "Name",
            expected: false);

        setStage("interactive editor");

        await ApplyAndAnalyzeAsync(() =>
            project.UpdateSource(
                ViewModelPath,
                ViewModelSource));

        await RequestCompletionAsync();

        RequireProjectedCompletion(
            "Name",
            expected: true);
    }

    private async Task ApplyAndAnalyzeAsync(Action update)
    {
        var analysis = NextAnalysis(Session);

        update();

        await analysis;
    }

    private async Task RequestCompletionAsync()
    {
        await Session.RequestCompletionAsync();
        EnsureHealthy();
    }

    private void RequireProjectedCompletion(
        string propertyName,
        bool expected)
    {
        var exists = Session.Completions.Any(
            static (item, name) => item.IsProjected && item.DisplayText == name, propertyName);

        Require(
            exists == expected,
            expected
                ? $"Projected completion '{propertyName}' was not found."
                : $"Obsolete projected completion '{propertyName}' still exists.");
    }

    private void VerifyUndo(string expectedSource, string operation)
    {
        editor.Undo();

        Require(
            editor.Text == expectedSource,
            $"{operation} undo did not restore the source.");
    }

    private void EnsureHealthy()
    {
        var session = Session;

        if (session.LastServiceError is { } error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }
    }

    private static int PositionOf(string source, string text)
    {
        var index = source.IndexOf(
            text,
            StringComparison.Ordinal);

        Require(
            index >= 0,
            $"Text '{text}' was not found in the source.");

        return index;
    }

    private static int PositionAfter(string source, string text)
    {
        return PositionOf(source, text) + text.Length;
    }

    private static T RequireNotNull<T>(T? value, string message) where T : class =>
        value ?? throw new InvalidOperationException(message);

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static Task NextAnalysis(AkburaEditorSession session)
    {
        var updated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        session.AnalysisUpdated += Complete;

        return updated.Task.WaitAsync(TimeSpan.FromSeconds(60));

        void Complete(object? sender, EventArgs args)
        {
            session.AnalysisUpdated -= Complete;
            updated.TrySetResult();
        }
    }

    private static async Task<AkburaReferenceSet> ReadReferencesAsync()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var images = ArrayBuilder<byte[]>.GetInstance();
        try
        {
            foreach (var name in assembly.GetManifestResourceNames())
            {
                if (!name.StartsWith("SmokeReferences.", StringComparison.Ordinal))
                {
                    continue;
                }
                using var stream = assembly.GetManifestResourceStream(name)
                    ?? throw new InvalidOperationException($"Embedded metadata '{name}' is missing.");
                using var image = new MemoryStream();
                await stream.CopyToAsync(image);
                images.Add(image.ToArray());
            }

            return images.Count == 0
                ? throw new InvalidOperationException("No resolved compiler metadata was embedded by the build.")
                : AkburaReferenceSet.FromImages(images);
        }
        finally
        {
            images.Free();
        }
    }

    private const string ViewModelSource = """
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
