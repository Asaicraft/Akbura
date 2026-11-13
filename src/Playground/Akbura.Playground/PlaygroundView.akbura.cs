using Akbura.AvaloniaEdit;
using Akbura.AvaloniaEdit.Infrastructure;
using Akbura.Pools;
using Akbura.Workspaces;
using Akbura.Workspaces.Projects;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit.Document;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
using ProjectId = Microsoft.CodeAnalysis.ProjectId;

namespace Akbura.Playground;

public sealed partial class PlaygroundView : AkburaControl
{
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(350);

    private readonly Func<AkburaReferenceSet>? _prepareReferences;
    private readonly IAkburaEditorScheduler _scheduler;
    private readonly ImmutableArray<PlaygroundExample> _examples;
    private ImmutableArray<AkburaProjectId> _projectIds;
    private CancellationTokenSource? _lifetime;
    private CancellationTokenSource? _bindingRequest;
    private CancellationTokenSource? _previewRequest;
    private AkburaWorkspace? _workspace;
    private AkburaEditorSession? _editorSession;
    private PreviewCompiler? _compiler;
    private PreviewInstance? _preview;
    private PlaygroundFile? _activeFile;

    public PlaygroundView() : this(null, null)
    {
    }

    internal PlaygroundView(Func<AkburaReferenceSet>? prepareReferences, IAkburaEditorScheduler? scheduler)
    {
        _prepareReferences = prepareReferences;
        _scheduler = scheduler ?? (OperatingSystem.IsBrowser()
            ? new AkburaInlineScheduler()
            : new AkburaThreadPoolScheduler());
        _examples =
        [
            new("Counter", "Preview.akbura"),
            new("Purity UI", "Examples/PurityUI/CreditCard.akbura"),
        ];
        InitializeIfNeeded();
        ExampleSelector.ItemsSource = _examples;
        FileSelector.SelectionChanged += FileChanged;
        ExampleSelector.SelectionChanged += ExampleChanged;
        ExampleSelector.SelectedIndex = 0;
        SourceEditor.TextChanged += SourceChanged;
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
            var (workspace, compiler, projectIds) = await _scheduler.RunAsync(
                cancellation => Task.FromResult(CreateEnvironment(cancellation)), token);
            if (token.IsCancellationRequested)
            {
                workspace.Dispose();
                return;
            }

            _workspace = workspace;
            _projectIds = projectIds;
            _compiler = compiler;
            if (ExampleSelector.SelectedItem is PlaygroundExample example && _activeFile is { } file)
            {
                _ = BindFileAsync(workspace, _projectIds[ExampleSelector.SelectedIndex], example, file, token);
                await RefreshPreviewAsync(compiler, example, TimeSpan.Zero, token);
            }
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                ShowFailure("Editor setup failed", error);
            }
        }
    }

    private (AkburaWorkspace Workspace, PreviewCompiler Compiler, ImmutableArray<AkburaProjectId> ProjectIds)
        CreateEnvironment(CancellationToken token)
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
            using var projectIds = ImmutableArrayBuilder<AkburaProjectId>.Rent(_examples.Length);
            projectIds.Add(project.Id);
            for (var index = 1; index < _examples.Length; index++)
            {
                token.ThrowIfCancellationRequested();
                var exampleContext = new ProjectContext(ProjectId.CreateNewId(), context.ProjectFilePath,
                    context.ProjectDirectory, context.RootNamespace, context.CSharpCompilation, context.ProjectReferences);
                projectIds.Add(workspace.AddOrUpdateProject(exampleContext).Id);
            }
            return (workspace, new PreviewCompiler(references, context.RootNamespace, context.ProjectDirectory),
                projectIds.ToImmutable());
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    private void ExampleChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ExampleSelector.SelectedItem is not PlaygroundExample example)
        {
            return;
        }

        FileSelector.ItemsSource = example.Files;
        FileSelector.SelectedItem = example.EntryFile;
        if (_compiler is { } compiler && _lifetime is { } lifetime)
        {
            ClearPreview();
            ShowDiagnostics([]);
            _ = RefreshPreviewAsync(compiler, example, TimeSpan.Zero, lifetime.Token);
        }
    }

    private void FileChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (FileSelector.SelectedItem is not PlaygroundFile file || ReferenceEquals(file, _activeFile) ||
            ExampleSelector.SelectedItem is not PlaygroundExample example)
        {
            return;
        }

        _bindingRequest?.Cancel();
        _editorSession?.Dispose();
        _editorSession = null;
        _activeFile = file;
        SourceEditor.IsReadOnly = true;
        SourceEditor.Document = new TextDocument(file.Text.ToString());
        if (_workspace is { } workspace && _lifetime is { } lifetime)
        {
            _ = BindFileAsync(workspace, _projectIds[ExampleSelector.SelectedIndex], example, file, lifetime.Token);
        }
    }

    private async Task BindFileAsync(AkburaWorkspace workspace, AkburaProjectId projectId,
        PlaygroundExample example, PlaygroundFile file, CancellationToken lifetime)
    {
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _bindingRequest = request;
        var token = request.Token;
        try
        {
            var sources = example.GetSources();
            await _scheduler.RunAsync(cancellation =>
            {
                foreach (var (filePath, text) in sources)
                {
                    cancellation.ThrowIfCancellationRequested();
                    var uri = PlaygroundFile.GetDocumentUri(filePath);
                    if (!workspace.CurrentSolution.TryGetDocument(uri, out var document) ||
                        !document.Text.ContentEquals(text))
                    {
                        workspace.OpenOrChangeDocumentContext(projectId, uri, text,
                            document == null ? null : text.GetChangeRanges(document.Text), cancellation);
                    }
                }
                return Task.FromResult(true);
            }, token);
            token.ThrowIfCancellationRequested();

            _editorSession = SourceEditor.Bind(workspace, projectId, PlaygroundFile.GetDocumentUri(file.FilePath),
                new() { Scheduler = _scheduler });
            SourceEditor.IsReadOnly = false;
        }
        catch (Exception error)
        {
            if (!token.IsCancellationRequested)
            {
                ShowFailure("Editor setup failed", error);
            }
        }
        finally
        {
            if (ReferenceEquals(_bindingRequest, request))
            {
                _bindingRequest = null;
            }
        }
    }

    private void SourceChanged(object? sender, EventArgs e)
    {
        if (_editorSession == null || _activeFile == null)
        {
            return;
        }

        _activeFile.Text = SourceText.From(SourceEditor.Text);
        if (_compiler is { } compiler && _lifetime is { } lifetime &&
            ExampleSelector.SelectedItem is PlaygroundExample example)
        {
            _ = RefreshPreviewAsync(compiler, example, PreviewDelay, lifetime.Token);
        }
    }

    private async Task RefreshPreviewAsync(PreviewCompiler compiler, PlaygroundExample example, TimeSpan delay, CancellationToken lifetime)
    {
        _previewRequest?.Cancel();
        using var request = CancellationTokenSource.CreateLinkedTokenSource(lifetime);
        _previewRequest = request;
        var token = request.Token;
        try
        {
            var sources = example.GetSources();
            if (delay > TimeSpan.Zero)
            {
                PreviewStatus.Text = "Pending…";
                await Task.Delay(delay, token);
            }

            PreviewStatus.Text = "Compiling…";
            await YieldBeforeInlineWorkAsync(token);
            var result = await _scheduler.RunAsync(cancellation => Task.FromResult(
                compiler.Compile(sources, example.EntryFile.FilePath, cancellation)), token);
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

    private void ClearPreview()
    {
        PreviewHost.Content = null;
        _preview?.Dispose();
        _preview = null;
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
        lifetime?.Cancel();
        SourceEditor.IsReadOnly = true;
        _compiler = null;
        _editorSession?.Dispose();
        _editorSession = null;
        _workspace?.Dispose();
        _workspace = null;
        ClearPreview();
        lifetime?.Dispose();
        base.OnUnloaded(e);
    }
}
