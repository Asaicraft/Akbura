using Akbura.AvaloniaEdit.Completion;
using Akbura.AvaloniaEdit.Highlighting;
using Akbura.AvaloniaEdit.Diagnostics;
using Akbura.AvaloniaEdit.Infrastructure;
using Avalonia;
using Avalonia.Threading;
using AvaloniaEdit.Document;

namespace Akbura.AvaloniaEdit;

/// <summary>
/// Owns one editable binding. Dispose on the UI thread. Documents are retained in the
/// host workspace on disposal; the host alone decides when to close or remove them.
/// </summary>
public sealed partial class AkburaEditorSession : IDisposable
{
    private readonly AkburaCodeEditor _editor;
    private readonly AkburaWorkspace _workspace;
    private readonly AkburaEditorOptions _options;
    private readonly IDisposable _binding;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _workspaceGate = new(1, 1);
    private readonly WorkspaceColorizer _colorizer;
    private readonly DiagnosticRenderer _markers;
    private TextDocument _document;
    private SourceText _text;
    private SourceText? _publishingText;
    private SourceText? _publishedText;
    private long _revision;
    private long _analysisSequence;
    private CancellationTokenSource? _analysisCancellation;
    private bool _suppressBufferEvents;
    private bool _applyingCompletion;
    private volatile bool _disposed;

    internal AkburaEditorSession(AkburaCodeEditor editor, AkburaWorkspace workspace,
        AkburaProjectId projectId, Uri uri, AkburaEditorOptions options)
    {
        _editor = editor;
        _workspace = workspace;
        ProjectId = projectId;
        DocumentUri = uri;
        _options = options;
        _binding = DocumentBindingRegistry.Acquire(workspace, uri);
        _document = editor.Document ??= new TextDocument();
        if (workspace.CurrentSolution.TryGetDocument(uri, out var existing))
        {
            _document.Text = existing.Text.ToString();
            _document.UndoStack.ClearAll();
        }
        _text = SourceText.From(_document.Text);
        _publishedText = existing?.Text;
        _colorizer = new WorkspaceColorizer(editor);
        _markers = new DiagnosticRenderer(editor);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Add(_markers);
        SubscribeBuffer();
        editor.PropertyChanged += EditorPropertyChanged;
        workspace.Changed += WorkspaceChanged;
        AttachCompletion();
        AttachHover();
        Ready = RefreshAsync();
    }

    public AkburaProjectId ProjectId
    {
        get;
    }
    public Uri DocumentUri
    {
        get;
    }
    /// <summary>
    /// Initial synchronization and analysis; service failures are also exposed through ServiceFailed.
    /// </summary>
    public Task Ready
    {
        get;
    }
    public ImmutableArray<AkburaDiagnosticSpan> Diagnostics
    {
        get; private set;
    } = [];
    /// <summary>
    /// Unfiltered canonical issues. Foreign and locationless issues are exposed but never drawn on this buffer.
    /// </summary>
    public ImmutableArray<Akbura.Diagnostics.AkburaDiagnosticRecord> DiagnosticRecords
    {
        get; private set;
    } = [];
    public ImmutableArray<AkburaClassifiedSpan> Classifications
    {
        get; private set;
    } = [];
    public Exception? LastServiceError
    {
        get; private set;
    }
    public event EventHandler? DiagnosticsChanged;
    public event EventHandler? AnalysisUpdated;
    public event EventHandler<AkburaEditorServiceFailedEventArgs>? ServiceFailed;

    internal sealed record RequestSnapshot(long Revision, TextDocument Buffer, SourceText Text, int Position);

    private RequestSnapshot Capture(int? position = null)
    {
        VerifyUsable();
        return new(_revision, _document, _text, position ?? _editor.CaretOffset);
    }

    private bool IsCurrent(RequestSnapshot snapshot, AkburaDocumentContext? context = null, bool checkPosition = false) =>
        !_disposed && snapshot.Revision == Interlocked.Read(ref _revision) &&
        ReferenceEquals(snapshot.Buffer, _document) &&
        (context == null || context.Project.Id == ProjectId && ReferenceEquals(context.Solution, _workspace.CurrentSolution)) &&
        (!checkPosition || snapshot.Position == _editor.CaretOffset);

    private void VerifyUsable()
    {
        Dispatcher.UIThread.VerifyAccess();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void SubscribeBuffer()
    {
        _document.Changed += BufferChanged;
        _document.UpdateFinished += BufferUpdateFinished;
    }

    private void UnsubscribeBuffer()
    {
        _document.Changed -= BufferChanged;
        _document.UpdateFinished -= BufferUpdateFinished;
    }

    private void BufferChanged(object? sender, DocumentChangeEventArgs e)
    {
        if (_suppressBufferEvents) return;
        _text = _text.WithChanges(new TextChange(new TextSpan(e.Offset, e.RemovalLength), e.InsertedText.Text));
        Interlocked.Increment(ref _revision);
        InvalidatePresentation();
    }

    private void BufferUpdateFinished(object? sender, EventArgs e)
    {
        if (!_suppressBufferEvents && !_applyingCompletion) _ = RefreshAsync(_options.AnalysisDelay);
    }

    private void EditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == global::AvaloniaEdit.TextEditor.DocumentProperty)
        {
            if (_editor.Document == null)
            {
                // Let the nested property notification install the replacement exactly once.
                _editor.Document = new TextDocument();
                return;
            }
            UnsubscribeBuffer();
            _document = _editor.Document;
            _text = SourceText.From(_document.Text);
            Interlocked.Increment(ref _revision);
            InvalidatePresentation();
            SubscribeBuffer();
            _ = RefreshAsync();
        }
        else if (e.Property == global::AvaloniaEdit.TextEditor.IsReadOnlyProperty && _editor.IsReadOnly)
        {
            DismissCompletion();
        }
    }

    private void WorkspaceChanged(object? sender, AkburaWorkspaceChangedEventArgs e)
    {
        // Workspace events are synchronous on the publishing thread. Ignore exactly our own text publication.
        if (e.DocumentId is { } id && e.NewSolution.TryGetDocument(id, out var changed) &&
            changed.Uri == DocumentUri && ReferenceEquals(changed.Text, Volatile.Read(ref _publishingText))) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(e.NewSolution, _workspace.CurrentSolution)) return;
            // An external text update is adopted only if the editor has no unpublished edits.
            if (e.NewSolution.TryGetDocument(DocumentUri, out var document) &&
                _publishedText is { } published && _text.ContentEquals(published) && !_text.ContentEquals(document.Text))
            {
                _suppressBufferEvents = true;
                try { _document.Text = document.Text.ToString(); }
                finally { _suppressBufferEvents = false; }
                _text = document.Text;
                _publishedText = document.Text;
                Interlocked.Increment(ref _revision);
            }
            InvalidatePresentation();
            _ = RefreshAsync();
        });
    }

    private void InvalidatePresentation()
    {
        _analysisCancellation?.Cancel();
        CancelCompletionRequests();
        DismissCompletion();
        DismissHover();
        Classifications = [];
        Diagnostics = [];
        DiagnosticRecords = [];
        Completions = [];
        _colorizer.SetSpans(_document, []);
        _markers.SetDiagnostics(_document, []);
        DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
    }

    private Task<AkburaDocumentContext> SynchronizeAsync(RequestSnapshot snapshot, CancellationToken token) =>
        _options.Scheduler.RunAsync(async cancellationToken =>
        {
            await _workspaceGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (_disposed || snapshot.Revision != Interlocked.Read(ref _revision))
                    throw new OperationCanceledException(cancellationToken);
                var solution = _workspace.CurrentSolution;
                solution.TryGetDocumentContext(DocumentUri, out var current);
                if (current != null && current.Project.Id != ProjectId)
                    throw new InvalidOperationException("The bound document has moved to another project. Rebind the editor.");
                if (current != null && current.Document.Text.ContentEquals(snapshot.Text))
                {
                    _publishedText = snapshot.Text;
                    return current;
                }
                var ranges = current == null ? null : snapshot.Text.GetChangeRanges(current.Document.Text);
                Volatile.Write(ref _publishingText, snapshot.Text);
                try
                {
                    var result = _workspace.OpenOrChangeDocumentContext(ProjectId, DocumentUri, snapshot.Text, ranges, cancellationToken);
                    _publishedText = snapshot.Text;
                    return result;
                }
                finally { Volatile.Write(ref _publishingText, null); }
            }
            finally { _workspaceGate.Release(); }
        }, token);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(TimeSpan.Zero, cancellationToken);

    private async Task RefreshAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var snapshot = Capture();
        _analysisCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _analysisCancellation = cancellation;
        var sequence = ++_analysisSequence;
        AkburaDocumentContext? context = null;
        try
        {
            if (delay > TimeSpan.Zero) await Task.Delay(delay, cancellation.Token);
            context = await SynchronizeAsync(snapshot, cancellation.Token);
            var syntax = await _options.Scheduler.RunAsync(token => Task.FromResult(
                AkburaSyntacticDocument.Parse(snapshot.Text, context.Document.FilePath, token)), cancellation.Token);
            var span = new TextSpan(0, snapshot.Text.Length);
            // Publish syntax colors before potentially expensive semantic analysis.
            var syntactic = await _options.Scheduler.RunAsync(token => Task.FromResult(
                _workspace.LanguageServices.Classification.GetSyntacticClassifications(syntax, span, token)), cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _analysisSequence || !IsCurrent(snapshot, context)) return;
            Classifications = syntactic;
            _colorizer.SetSpans(_document, syntactic);
            var analysis = await _options.Scheduler.RunAsync(token => Task.FromResult((
                Colors: _workspace.LanguageServices.Classification.GetClassifications(context, span, token),
                Report: _workspace.LanguageServices.Diagnostics.GetDiagnosticReport(context, span, token))), cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _analysisSequence || !IsCurrent(snapshot, context)) return;
            LastServiceError = null;
            Classifications = analysis.Colors;
            Diagnostics = analysis.Report.SourceDiagnostics;
            DiagnosticRecords = analysis.Report.Diagnostics;
            _colorizer.SetSpans(_document, Classifications);
            _markers.SetDiagnostics(_document, Diagnostics);
            DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
            AnalysisUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot)) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _analysisSequence)
                ReportFailure("Analysis", error, snapshot, context);
        }
        finally { if (ReferenceEquals(_analysisCancellation, cancellation)) _analysisCancellation = null; }
    }

    private void ReportFailure(string operation, Exception error, RequestSnapshot snapshot, AkburaDocumentContext? context = null)
    {
        if (!IsCurrent(snapshot, context)) return;
        LastServiceError = error;
        ServiceFailed?.Invoke(this, new(operation, error));
    }

    public void Dispose()
    {
        Dispatcher.UIThread.VerifyAccess();
        if (_disposed) return;
        _disposed = true;
        Interlocked.Increment(ref _revision);
        _lifetime.Cancel();
        DismissCompletion();
        DismissHover();
        UnsubscribeBuffer();
        _editor.PropertyChanged -= EditorPropertyChanged;
        _workspace.Changed -= WorkspaceChanged;
        DetachCompletion();
        DetachHover();
        _editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_markers);
        _editor.TextArea.TextView.Redraw();
        _binding.Dispose();
        _editor.Release(this);
        // Active requests still own their linked tokens and the serialization gate.
        _lifetime.Dispose();
    }
}

public sealed class AkburaEditorServiceFailedEventArgs(string operation, Exception exception) : EventArgs
{
    public string Operation
    {
        get;
    } = operation;
    public Exception Exception
    {
        get;
    } = exception;
}
