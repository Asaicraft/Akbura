using Akbura.AvaloniaEdit;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;

namespace Akbura.Playground;

public sealed partial class PlaygroundView : UserControl
{
    private const string PreviewFilePath = "Preview.akbura";
    private static readonly Uri PreviewDocument = new("memory:///" + PreviewFilePath);
    private static readonly Uri StarterAsset = new("avares://Akbura.Playground/" + PreviewFilePath);
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(350);

    private readonly Func<AkburaReferenceSet>? _prepareReferences;
    private readonly IAkburaEditorScheduler _scheduler;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _previewRequest;
    private AkburaWorkspace? _workspace;
    private AkburaEditorSession? _editorSession;
    private PreviewCompiler? _compiler;
    private PreviewInstance? _preview;

    public PlaygroundView() : this(null, null)
    {
    }

    internal PlaygroundView(Func<AkburaReferenceSet>? prepareReferences, IAkburaEditorScheduler? scheduler)
    {
        _prepareReferences = prepareReferences;
        _scheduler = scheduler ?? (OperatingSystem.IsBrowser()
            ? new AkburaInlineScheduler()
            : new AkburaThreadPoolScheduler());
        InitializeComponent();
        using var reader = new StreamReader(AssetLoader.Open(StarterAsset));
        SourceEditor.Text = reader.ReadToEnd();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        if (_lifetime == null)
        {
            _lifetime = new CancellationTokenSource();
            _ = InitializeAsync(_lifetime.Token);
        }
    }

    private async Task InitializeAsync(CancellationToken token)
    {
        try
        {
            PreviewStatus.Text = "Preparing editor…";
            await YieldBeforeInlineWorkAsync(token);
            var (workspace, compiler) = await _scheduler.RunAsync(
                cancellation => Task.FromResult(CreateEnvironment(cancellation)), token);
            if (token.IsCancellationRequested)
            {
                workspace.Dispose();
                return;
            }

            _workspace = workspace;
            _editorSession = SourceEditor.Bind(workspace.DefaultProject, PreviewDocument, new()
            {
                Scheduler = _scheduler,
            });
            _compiler = compiler;
            SourceEditor.TextChanged += SourceChanged;
            SourceEditor.TextArea.Focus();
            await RefreshPreviewAsync(compiler, SourceEditor.Text, TimeSpan.Zero, token);
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                ShowFailure("Editor setup failed", error);
            }
        }
    }

    private (AkburaWorkspace Workspace, PreviewCompiler Compiler) CreateEnvironment(CancellationToken token)
    {
        var workspace = new AkburaWorkspace(ProjectContext.CreateSyntaxOnly(
            assemblyName: "Akbura.Playground.Editor", rootNamespace: "Akbura.Playground"));
        try
        {
            var project = workspace.DefaultProject;
            if (_prepareReferences is { } prepareReferences)
            {
                project.UseAvalonia(prepareReferences());
            }
            else
            {
                project.UseAvalonia();
            }
            token.ThrowIfCancellationRequested();
            var context = project.Context;
            var references = ImmutableArray.CreateRange(context.CSharpCompilation.References);
            return (workspace, new PreviewCompiler(references, context.RootNamespace, context.ProjectDirectory));
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private void SourceChanged(object? sender, EventArgs e)
    {
        if (_compiler is { } compiler && _lifetime is { } lifetime)
        {
            _ = RefreshPreviewAsync(compiler, SourceEditor.Text, PreviewDelay, lifetime.Token);
        }
    }

    private async Task RefreshPreviewAsync(PreviewCompiler compiler, string source, TimeSpan delay, CancellationToken lifetime)
    {
        _previewRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _previewRequest = request;
        var token = request.Token;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                PreviewStatus.Text = "Pending…";
                await Task.Delay(delay, token);
            }

            PreviewStatus.Text = "Compiling…";
            await YieldBeforeInlineWorkAsync(token);
            var result = await _scheduler.RunAsync(cancellation => Task.FromResult(
                compiler.Compile(SourceText.From(source), PreviewFilePath, cancellation)), token);
            token.ThrowIfCancellationRequested();
            ShowDiagnostics(result.Diagnostics);
            if (!result.Success)
            {
                PreviewStatus.Text = "Compile failed";
                return;
            }

            ReplacePreview(result.CreatePreview());
            PreviewStatus.Text = "Updated";
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                ShowFailure("Preview failed", error);
            }
        }
        finally
        {
            if (ReferenceEquals(_previewRequest, request))
            {
                _previewRequest = null;
            }
        }
    }

    private void ReplacePreview(PreviewInstance replacement)
    {
        var previous = _preview;
        try
        {
            PreviewHost.Content = replacement.View;
            PreviewHost.UpdateLayout();
        }
        catch
        {
            try
            {
                PreviewHost.Content = previous?.View;
            }
            finally
            {
                replacement.Dispose();
            }
            throw;
        }

        _preview = replacement;
        previous?.Dispose();
    }

    private void ShowFailure(string status, Exception error)
    {
        PreviewStatus.Text = status;
        ShowDiagnostics([error.GetBaseException().Message]);
    }

    private void ShowDiagnostics(ImmutableArray<string> diagnostics)
    {
        DiagnosticPanel.IsVisible = !diagnostics.IsDefaultOrEmpty;
        PreviewDiagnostics.Text = diagnostics.IsDefaultOrEmpty ? string.Empty : string.Join(Environment.NewLine, diagnostics);
    }

    private async Task YieldBeforeInlineWorkAsync(CancellationToken token)
    {
        if (_scheduler is AkburaInlineScheduler)
        {
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, token);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        var lifetime = _lifetime;
        _lifetime = null;
        SourceEditor.TextChanged -= SourceChanged;
        lifetime?.Cancel();
        _compiler = null;
        _editorSession?.Dispose();
        _editorSession = null;
        _workspace?.Dispose();
        _workspace = null;
        PreviewHost.Content = null;
        _preview?.Dispose();
        _preview = null;
        lifetime?.Dispose();
        base.OnUnloaded(e);
    }
}
