using Akbura.AvaloniaEdit.Completion;
using Akbura.AvaloniaEdit.Highlighting;
using Akbura.AvaloniaEdit.Diagnostics;
using Akbura.AvaloniaEdit.Infrastructure;
using Avalonia;
using Avalonia.Controls;
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
    private readonly EditorServiceQueue _services;
    private readonly EditorSyntaxCache _syntax;
    private readonly WorkspaceColorizer _colorizer;
    private readonly DiagnosticRenderer _markers;
    private TextDocument _document;
    private SourceText _text;
    private SourceText? _publishingText;
    private SourceText? _publishedText;
    private long _revision;
    private long _analysisSequence;
    private object? _classificationSolution;
    private long _classificationRevision = -1;
    private object? _diagnosticSolution;
    private long _diagnosticRevision = -1;
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
        _services = new(options.Scheduler)
        {
#if DEBUG
            Performance = options.Performance,
#endif
        };
        _syntax = new(_services, _lifetime.Token, key =>
            !_disposed && ReferenceEquals(key.Buffer, _document) && key.Revision == Interlocked.Read(ref _revision))
        {
#if DEBUG
            Performance = options.Performance,
#endif
        };
        _colorizer = new WorkspaceColorizer(editor)
        {
#if DEBUG
            Performance = options.Performance,
#endif
        };
        _markers = new DiagnosticRenderer(editor);
        editor.TextArea.TextView.LineTransformers.Add(_colorizer);
        editor.TextArea.TextView.BackgroundRenderers.Add(_markers);
        SubscribeBuffer();
        editor.PropertyChanged += EditorPropertyChanged;
        editor.ResourcesChanged += ResourcesChanged;
        editor.ActualThemeVariantChanged += AppearanceChanged;
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

    /// <summary>
    /// Classifications accepted for the current source and project context. While analysis is
    /// pending, the renderer may retain separately tracked provisional colors.
    /// </summary>
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
#if DEBUG
        using var measurement = _options.Performance?.Measure("TextChanged");
#endif
        _colorizer.TrackChange(_document, e);
        if (_suppressBufferEvents) return;
        _text = _text.WithChanges(new TextChange(new TextSpan(e.Offset, e.RemovalLength), e.InsertedText.Text));
        Interlocked.Increment(ref _revision);
        InvalidateAnalysis(TrackCompletionChange(e));
    }

    private void BufferUpdateFinished(object? sender, EventArgs e)
    {
        if (!_suppressBufferEvents && !_applyingCompletion)
        {
            _ = RefreshAsync(_options.AnalysisDelay);
        }

        if (_completionCatalogNeedsRefresh && !_suppressBufferEvents && !_applyingCompletion)
        {
            _completionCatalogNeedsRefresh = false;
            if (_options.AutomaticCompletion)
            {
                ScheduleCompletion('\0');
            }
        }
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
            _syntax.Clear();
            _text = SourceText.From(_document.Text);
            Interlocked.Increment(ref _revision);
            InvalidateAnalysis();
            _colorizer.SetSpans(_document, []);
            SubscribeBuffer();
            _ = RefreshAsync();
        }
        else if (e.Property == global::AvaloniaEdit.TextEditor.IsReadOnlyProperty && _editor.IsReadOnly)
        {
            CancelCompletionRequests();
            DismissCompletion();
        }
        else if (e.Property == global::AvaloniaEdit.TextEditor.ForegroundProperty)
        {
            AppearanceChanged(sender, EventArgs.Empty);
        }
    }

    private bool _appearanceInvalidationPosted;

    private void ResourcesChanged(object? sender, ResourcesChangedEventArgs e) => AppearanceChanged(sender, EventArgs.Empty);

    private void AppearanceChanged(object? sender, EventArgs e)
    {
        if (_appearanceInvalidationPosted)
        {
            return;
        }

        _appearanceInvalidationPosted = true;
        Dispatcher.UIThread.Post(() =>
        {
            _appearanceInvalidationPosted = false;
            if (!_disposed)
            {
                _colorizer.InvalidateAppearance();
            }
        }, DispatcherPriority.Background);
    }

    private void WorkspaceChanged(object? sender, AkburaWorkspaceChangedEventArgs e)
    {
        // Workspace events are synchronous on the publishing thread. Ignore exactly our own text publication.
        if (e.DocumentId is { } id && e.NewSolution.TryGetDocument(id, out var changed) &&
            changed.Uri == DocumentUri && ReferenceEquals(changed.Text, Volatile.Read(ref _publishingText)))
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || !ReferenceEquals(e.NewSolution, _workspace.CurrentSolution))
            {
                return;
            }
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
            if (!_refreshDelayed && _refreshTask is { IsCompleted: false } && _analysisCancellation is { IsCancellationRequested: false } &&
                _refreshSnapshot?.Revision == _revision && ReferenceEquals(_refreshSnapshot.Buffer, _document) &&
                ReferenceEquals(_refreshSolution, e.NewSolution))
            {
                // A host may have explicitly started this context's refresh before
                // its queued notification. Invalidate old public results, but keep
                // the already current pipeline instead of duplicating its service calls.
                if (_classificationRevision != _revision || !ReferenceEquals(_classificationSolution, e.NewSolution))
                {
                    Classifications = [];
                }

                if (_diagnosticRevision != _revision || !ReferenceEquals(_diagnosticSolution, e.NewSolution))
                {
                    PublishDiagnostics([], []);
                }

                if (HasObsoleteCompletions())
                {
                    DismissCompletion();
                    Completions = [];
                }
                DismissHover();
                return;
            }
            InvalidateAnalysis();
            _ = RefreshAsync();
        });
    }

    private void InvalidateAnalysis(bool retainCompletion = false)
    {
        _analysisCancellation?.Cancel();
        CancelCompletionRequests();
        if (!retainCompletion)
        {
            DismissCompletion();
            Completions = [];
        }
        DismissHover();
        Classifications = [];
        _classificationSolution = null;
        _classificationRevision = -1;
        _diagnosticSolution = null;
        _diagnosticRevision = -1;
        PublishDiagnostics([], []);
    }

    private Task<AkburaDocumentContext> SynchronizeAsync(RequestSnapshot snapshot, CancellationToken token, string stream = "analysis-sync") =>
        _services.RunAsync(stream, 0, cancellationToken =>
        {
#if DEBUG
            using var measurement = _options.Performance?.Measure("Synchronize");
            _options.Performance?.Count("Synchronize");
            _options.Performance?.Count(stream);
#endif
            cancellationToken.ThrowIfCancellationRequested();
            if (_disposed || snapshot.Revision != Interlocked.Read(ref _revision))
            {
                throw new OperationCanceledException(cancellationToken);
            }

            var solution = _workspace.CurrentSolution;
            solution.TryGetDocumentContext(DocumentUri, out var current);
            if (current != null && current.Project.Id != ProjectId)
            {
                throw new InvalidOperationException("The bound document has moved to another project. Rebind the editor.");
            }

            if (current != null && current.Document.Text.ContentEquals(snapshot.Text))
            {
                _publishedText = snapshot.Text;
                return Task.FromResult(current);
            }
            var ranges = current == null ? null : snapshot.Text.GetChangeRanges(current.Document.Text);
            Volatile.Write(ref _publishingText, snapshot.Text);
            try
            {
                var result = _workspace.OpenOrChangeDocumentContext(ProjectId, DocumentUri, snapshot.Text, ranges, cancellationToken);
                _publishedText = snapshot.Text;
                return Task.FromResult(result);
            }
            finally
            {
                Volatile.Write(ref _publishingText, null);
            }
        }, token);

    public Task RefreshAsync(CancellationToken cancellationToken = default) => RefreshAsync(TimeSpan.Zero, cancellationToken);

    private Task? _refreshTask;
    private RequestSnapshot? _refreshSnapshot;
    private object? _refreshSolution;
    private bool _refreshDelayed;

    private Task RefreshAsync(TimeSpan delay, CancellationToken cancellationToken = default)
    {
        var snapshot = Capture();

        if (CanReuseRefresh())
        {
            return _refreshTask!.WaitAsync(cancellationToken);
        }

        _refreshSnapshot = snapshot;
        _refreshSolution = _workspace.CurrentSolution;
        _refreshDelayed = delay > TimeSpan.Zero;

        return _refreshTask = RunRefreshAsync(snapshot, delay, cancellationToken);

        bool CanReuseRefresh() =>
            !cancellationToken.CanBeCanceled &&
            delay == TimeSpan.Zero &&
            !_refreshDelayed &&
            _analysisCancellation is { IsCancellationRequested: false } &&
            _refreshTask is { IsCompleted: false } &&
            _refreshSnapshot?.Revision == snapshot.Revision &&
            ReferenceEquals(_refreshSnapshot.Buffer, snapshot.Buffer) &&
            ReferenceEquals(_refreshSolution, _workspace.CurrentSolution);
    }

    private Task<AkburaSyntacticDocument> GetSyntaxAsync(RequestSnapshot snapshot, string filePath, CancellationToken token) =>
        _syntax.GetAsync(new(snapshot.Buffer, snapshot.Revision, filePath, snapshot.Text), token);

    internal AkburaSyntacticDocument? CachedSyntax => _syntax.Accepted;

    private async Task RunRefreshAsync(RequestSnapshot snapshot, TimeSpan delay, CancellationToken cancellationToken)
    {
        _analysisCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _analysisCancellation = cancellation;
        var sequence = ++_analysisSequence;
        AkburaDocumentContext? context = null;
        try
        {
            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay, cancellation.Token);
            }

            if (sequence == _analysisSequence)
            {
                _refreshDelayed = false;
            }

            context = await SynchronizeAsync(snapshot, cancellation.Token);
            if (sequence == _analysisSequence)
            {
                _refreshSolution = context.Solution;
            }

            var span = new TextSpan(0, snapshot.Text.Length);
            var syntax = await GetSyntaxAsync(snapshot, context.Document.FilePath, cancellation.Token);
            if (!_colorizer.HasPresentation)
            {
                var syntactic = await _services.RunAsync("syntactic-colors", 1, token =>
                {
#if DEBUG
                    _options.Performance?.Count("SyntacticClassification");
#endif
                    return Task.FromResult(_workspace.LanguageServices.Classification.GetSyntacticClassifications(syntax, span, token));
                }, cancellation.Token);

                if (cancellation.IsCancellationRequested || sequence != _analysisSequence || !IsCurrent(snapshot, context))
                {
                    return;
                }

                Classifications = syntactic;
                _classificationSolution = context.Solution;
                _classificationRevision = snapshot.Revision;
                _colorizer.SetSpans(_document, syntactic);
                await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background);
                cancellation.Token.ThrowIfCancellationRequested();
            }
            var colors = await _services.RunAsync("classification", 1, token =>
            {
#if DEBUG
                using var measurement = _options.Performance?.Measure("Classification");
                _options.Performance?.Count("Classification");
                _options.Performance?.Count("ClassificationCharacters", span.Length);
#endif
                return Task.FromResult(_workspace.LanguageServices.Classification.GetClassifications(context, span, token));
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _analysisSequence || !IsCurrent(snapshot, context)) return;
            Classifications = colors;
            _classificationSolution = context.Solution;
            _classificationRevision = snapshot.Revision;
            _colorizer.SetSpans(_document, colors);
#if DEBUG
            _options.Performance?.Count("HighlightingPublished");
#endif
            // Publish authoritative colors now. The dispatcher can process input/rendering
            // before starting lower-priority diagnostics, including on single-threaded WASM.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background, cancellationToken);

            var report = await _services.RunAsync("diagnostics", 2, token =>
            {
#if DEBUG
                using var measurement = _options.Performance?.Measure("Diagnostics");
                _options.Performance?.Count("Diagnostics");
#endif
                return Task.FromResult(_workspace.LanguageServices.Diagnostics.GetDiagnosticReport(context, span, token));
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _analysisSequence || !IsCurrent(snapshot, context)) return;
            LastServiceError = null;
            PublishDiagnostics(report.SourceDiagnostics, report.Diagnostics);
            _diagnosticSolution = context.Solution;
            _diagnosticRevision = snapshot.Revision;
#if DEBUG
            _options.Performance?.Count("AnalysisPublished");
#endif
            AnalysisUpdated?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot))
        {
#if DEBUG
            _options.Performance?.Count("AnalysisCanceled");
#endif
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _analysisSequence)
                ReportFailure("Analysis", error, snapshot, context);
        }
        finally { if (ReferenceEquals(_analysisCancellation, cancellation)) _analysisCancellation = null; }
    }

    private void PublishDiagnostics(ImmutableArray<AkburaDiagnosticSpan> diagnostics,
        ImmutableArray<Akbura.Diagnostics.AkburaDiagnosticRecord> records)
    {
        var changed = !Diagnostics.SequenceEqual(diagnostics) ||
            !DiagnosticRecords.SequenceEqual(records, Akbura.Diagnostics.AkburaDiagnosticCanonicalComparer.Instance);
        Diagnostics = diagnostics;
        DiagnosticRecords = records;
        _markers.SetDiagnostics(_document, diagnostics);
        if (changed) DiagnosticsChanged?.Invoke(this, EventArgs.Empty);
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
        _syntax.Dispose();
        _services.Dispose();
        _refreshTask = null;
        _refreshSnapshot = null;
        _refreshSolution = null;
        _classificationSolution = null;
        _diagnosticSolution = null;
        Completions = [];
        DismissCompletion();
        DismissHover();
        UnsubscribeBuffer();
        _editor.PropertyChanged -= EditorPropertyChanged;
        _editor.ResourcesChanged -= ResourcesChanged;
        _editor.ActualThemeVariantChanged -= AppearanceChanged;
        _workspace.Changed -= WorkspaceChanged;
        DetachCompletion();
        DetachHover();
        _editor.TextArea.TextView.LineTransformers.Remove(_colorizer);
        _colorizer.Clear();
        _editor.TextArea.TextView.BackgroundRenderers.Remove(_markers);
        _editor.TextArea.TextView.Redraw();
        _binding.Dispose();
        _editor.Release(this);
        // Active requests still own their linked tokens.
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
