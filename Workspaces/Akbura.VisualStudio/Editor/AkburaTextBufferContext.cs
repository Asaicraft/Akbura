using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Synchronizes one Visual Studio text buffer with one Akbura document.
///
/// Editor callbacks only publish immutable update requests. One background
/// worker publishes a fast syntactic state first and a semantic state later.
/// Each completed stage is published atomically and read without locks.
/// </summary>
internal sealed class AkburaTextBufferContext : IDisposable
{
    private static readonly TimeSpan UpdateDelay =
        TimeSpan.FromMilliseconds(100);

    private readonly ITextBuffer _textBuffer;

    private readonly AkburaVisualStudioWorkspace _visualStudioWorkspace;

    private readonly AkburaWorkspace _workspace;

    private readonly AkburaParserService _parserService;

    private AkburaProjectId? _projectId;

    private readonly object _projectInitializationGate = new();

    private Task<AkburaProjectId?>? _projectInitializationTask;

    private int _projectContextVersion;

    private int _projectInitializationVersion = -1;

    private readonly IAkburaClassificationService _classificationService;

    private readonly IAkburaDiagnosticService _diagnosticService;

    private readonly JoinableTaskFactory _joinableTaskFactory;

    private readonly Uri _uri;

    private readonly CancellationTokenSource _disposeCancellation = new();

    private readonly ITextDocumentFactoryService? _subscribedDocumentFactory;

    private readonly ITextDocument? _textDocument;

    /// <summary>
    /// Stores the newest request that has not yet been consumed.
    ///
    /// Every new edit atomically replaces the previous pending request.
    /// Intermediate editor versions do not need to be parsed.
    /// </summary>
    private UpdateRequest? _pendingRequest;

    /// <summary>
    /// Stores the cancellation source of the currently executing parse.
    /// </summary>
    private CancellationTokenSource? _activeParseCancellation;

    /// <summary>
    /// Stores the newest classification state. It can contain either the
    /// fast syntactic pass or the completed semantic pass.
    /// </summary>
    private AkburaClassifiedBufferState? _publishedClassificationState;

    /// <summary>
    /// Stores the latest state backed by a project semantic model. Editor
    /// features such as navigation keep using this state while a newer
    /// syntactic classification is being displayed.
    /// </summary>
    private AkburaParsedBufferState? _publishedSemanticState;

    /// <summary>
    /// Stores the latest document context independently of semantic
    /// classification. Completion can consume this state as soon as the
    /// workspace has accepted the document.
    /// </summary>
    private PublishedDocumentContext? _publishedDocumentContext;

    private event Action? DocumentContextPublished;

    /// <summary>
    /// Monotonically increasing editor request version.
    /// </summary>
    private long _requestedVersion;

    /// <summary>
    /// Zero means that no worker owns the processing loop.
    /// One means that exactly one worker owns it.
    /// </summary>
    private int _workerState;

    /// <summary>
    /// Zero means active and one means disposed.
    /// </summary>
    private int _disposeState;

    private long _enqueueCount;

    public AkburaTextBufferContext(
        ITextBuffer textBuffer,
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace visualStudioWorkspace,
        AkburaParserService parserService)
    {
        _textBuffer = textBuffer ?? throw new ArgumentNullException(nameof(textBuffer));

        _visualStudioWorkspace = visualStudioWorkspace ?? throw new ArgumentNullException(nameof(visualStudioWorkspace));

        _workspace = visualStudioWorkspace.Workspace;

        _parserService = parserService ??
            throw new ArgumentNullException(
                nameof(parserService));

        if (textDocumentFactory == null)
        {
            throw new ArgumentNullException(nameof(textDocumentFactory));
        }

        _classificationService = _workspace.LanguageServices.Classification;

        _diagnosticService = _workspace.LanguageServices.Diagnostics;

        _visualStudioWorkspace.ProjectContextChanged +=
            OnProjectContextChanged;

        _joinableTaskFactory = ThreadHelper.JoinableTaskFactory;

        if (textDocumentFactory.TryGetTextDocument(
                textBuffer,
                out var textDocument) &&
            !string.IsNullOrWhiteSpace(
                textDocument.FilePath))
        {
            _textDocument = textDocument;

            _subscribedDocumentFactory =
                textDocumentFactory;

            _uri = new Uri(
                Path.GetFullPath(
                    textDocument.FilePath));

            textDocumentFactory.TextDocumentDisposed +=
                OnTextDocumentDisposed;
        }
        else
        {
            _uri = new Uri(
                $"untitled://akbura/" +
                $"{Guid.NewGuid():N}.akbura");
        }

        _textBuffer.ChangedLowPriority +=
            OnTextBufferChangedLowPriority;

        _ = _joinableTaskFactory.RunAsync(
            WarmUpProjectAsync);

        EnqueueSnapshot(
            _textBuffer.CurrentSnapshot);
    }

    public event EventHandler<AkburaBufferChangedEventArgs>? Changed;

    internal event EventHandler? Disposed;

    internal string FilePath => _textDocument?.FilePath ??
        (_uri.IsFile
            ? Path.GetFullPath(_uri.LocalPath)
            : _uri.AbsoluteUri);

    /// <summary>
    /// Returns the newest syntactic or semantic classification state that is
    /// not newer than the requested editor snapshot.
    /// </summary>
    internal bool TryGetPublishedClassificationState(
        ITextSnapshot requestedSnapshot,
        out AkburaClassifiedBufferState state)
    {
        ValidateRequestedSnapshot(requestedSnapshot);

        var current =
            Volatile.Read(
                ref _publishedClassificationState);

        if (current == null ||
            current.Snapshot.Version.VersionNumber >
                requestedSnapshot.Version.VersionNumber)
        {
            state = null!;
            return false;
        }

        state = current;
        return true;
    }

    /// <summary>
    /// Returns the latest published state that is not newer than the
    /// requested editor snapshot.
    ///
    /// The returned state can belong to an older snapshot. Callers must
    /// translate spans between the published and requested snapshots.
    ///
    /// This method never starts parsing and never waits for parsing.
    /// </summary>
    internal bool TryGetPublishedState(
        ITextSnapshot requestedSnapshot,
        out AkburaParsedBufferState state)
    {
        ValidateRequestedSnapshot(requestedSnapshot);

        var current =
            Volatile.Read(
                ref _publishedSemanticState);

        if (current == null ||
            current.Snapshot.Version.VersionNumber >
                requestedSnapshot.Version.VersionNumber)
        {
            state = null!;
            return false;
        }

        state = current;
        return true;
    }

    /// <summary>
    /// Returns a published document context, waiting only until the workspace
    /// has accepted a document. Semantic classification may still be running.
    /// </summary>
    internal async Task<AkburaDocumentContext?>
        GetPublishedDocumentContextAsync(
        ITextSnapshot requestedSnapshot,
        CancellationToken cancellationToken)
    {
        ValidateRequestedSnapshot(requestedSnapshot);

        var current = Volatile.Read(
            ref _publishedDocumentContext);
        if (current != null &&
            current.Snapshot.Version.VersionNumber <=
                requestedSnapshot.Version.VersionNumber)
        {
            return current.Context;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return null;
        }

        var completion =
            new TaskCompletionSource<AkburaDocumentContext?>(
                TaskCreationOptions.RunContinuationsAsynchronously);

        void OnPublished()
        {
            var published = Volatile.Read(
                ref _publishedDocumentContext);
            if (published != null)
            {
                completion.TrySetResult(published.Context);
            }
            else if (Volatile.Read(ref _disposeState) != 0)
            {
                completion.TrySetResult(null);
            }
        }

        DocumentContextPublished += OnPublished;
        try
        {
            /*
             * A semantic publication can race with subscribing to Changed.
             * Check once more before awaiting the next notification.
             */
            var published = Volatile.Read(
                ref _publishedDocumentContext);
            if (published != null)
            {
                return published.Context;
            }

            using (cancellationToken.Register(
                       () => completion.TrySetCanceled()))
            using (_disposeCancellation.Token.Register(
                       () => completion.TrySetResult(null)))
            {
                return await completion.Task.ConfigureAwait(false);
            }
        }
        finally
        {
            DocumentContextPublished -= OnPublished;
        }
    }

    private void ValidateRequestedSnapshot(
        ITextSnapshot requestedSnapshot)
    {
        if (requestedSnapshot == null)
        {
            throw new ArgumentNullException(
                nameof(requestedSnapshot));
        }

        if (!ReferenceEquals(
                requestedSnapshot.TextBuffer,
                _textBuffer))
        {
            throw new ArgumentException(
                "The snapshot belongs to another text buffer.",
                nameof(requestedSnapshot));
        }
    }

    /// <summary>
    /// Compatibility helper for future editor services that only need
    /// the parsed document and its editor snapshot.
    /// </summary>
    public bool TryGetDocument(
        ITextSnapshot requestedSnapshot,
        out AkburaDocumentSnapshot document,
        out ITextSnapshot parsedSnapshot)
    {
        if (!TryGetPublishedState(
                requestedSnapshot,
                out var state))
        {
            document = null!;
            parsedSnapshot = null!;
            return false;
        }

        document = state.Document;
        parsedSnapshot = state.Snapshot;
        return true;
    }

    internal bool TryGetLatestDocumentContext(
        out AkburaDocumentContext context,
        out ITextSnapshot snapshot)
    {
        var published = Volatile.Read(ref _publishedDocumentContext);

        if (published == null)
        {
            context = null!;
            snapshot = null!;
            return false;
        }

        context = published.Context;
        snapshot = published.Snapshot;

        return true;
    }

    private void OnTextBufferChangedLowPriority(
        object sender,
        TextContentChangedEventArgs e)
    {
        EnqueueSnapshot(e.After);
    }

    private void EnqueueSnapshot(
        ITextSnapshot snapshot)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        /*
         * AsText creates a SourceText backed by the Visual Studio snapshot.
         * It does not materialize the complete document as one string.
         */
        var sourceText = snapshot.AsText();

        var requestVersion =
            Interlocked.Increment(
                ref _requestedVersion);

        var request = new UpdateRequest(
            requestVersion,
            snapshot,
            sourceText);

        /*
         * Only the newest unprocessed snapshot is useful.
         */
        Interlocked.Exchange(
            ref _pendingRequest,
            request);
        AkburaWorkspaceDiagnostics.Write(
            AkburaWorkspaceDiagnostics.Category.Workspace,
            $"Enqueue #{Interlocked.Increment(ref _enqueueCount)}, " +
            $"request={requestVersion}, " +
            $"snapshot={snapshot.Version.VersionNumber}");

        CancelActiveParse();
        EnsureWorker();
    }

    private async Task<AkburaProjectId?> GetProjectIdAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            if (_projectId is { } projectId)
            {
                return projectId;
            }

            var initializationTask =
                GetOrCreateProjectInitializationTaskAsync(
                    out var initializationVersion);
            var synchronizedProjectId = await AwaitWithoutCancelingSourceAsync(
                    initializationTask,
                    cancellationToken)
                .ConfigureAwait(false);

            if (synchronizedProjectId is { } result)
            {
                _projectId = result;
                return result;
            }

            if (initializationVersion == Volatile.Read(
                    ref _projectContextVersion))
            {
                return null;
            }
        }
    }

    private Task<AkburaProjectId?>
        GetOrCreateProjectInitializationTaskAsync(
            out int initializationVersion)
    {
        lock (_projectInitializationGate)
        {
            if (_projectId is { } projectId)
            {
                initializationVersion = Volatile.Read(
                    ref _projectContextVersion);
                return Task.FromResult<AkburaProjectId?>(
                    projectId);
            }

            var projectContextVersion = Volatile.Read(
                ref _projectContextVersion);
            var current = _projectInitializationTask;
            if (current != null &&
                (!current.IsCompleted ||
                 _projectInitializationVersion == projectContextVersion))
            {
                initializationVersion =
                    _projectInitializationVersion;
                return current;
            }

            _projectInitializationVersion = projectContextVersion;
            initializationVersion = projectContextVersion;
            return _projectInitializationTask =
                InitializeProjectAsync(
                    _disposeCancellation.Token);
        }
    }

    private async Task<AkburaProjectId?> InitializeProjectAsync(
        CancellationToken cancellationToken)
    {
        var filePath = _textDocument?.FilePath;
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        var projectId = await _visualStudioWorkspace
            .SynchronizeProjectAsync(
                filePath!,
                cancellationToken)
            .ConfigureAwait(false);

        if (projectId is { } result)
        {
            _projectId = result;
        }

        return projectId;
    }

    private async Task WarmUpProjectAsync()
    {
        try
        {
            var projectId = await GetProjectIdAsync(
                    _disposeCancellation.Token)
                .ConfigureAwait(false);
            if (projectId is { } result)
            {
                _projectId = result;
                EnqueueSnapshot(
                    _textBuffer.CurrentSnapshot);
            }
        }
        catch (OperationCanceledException)
            when (_disposeCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Project warm-up failed: {exception}");
        }
    }

#pragma warning disable VSTHRD003 // The initialization task deliberately outlives an editor snapshot.
    private static async Task<T> AwaitWithoutCancelingSourceAsync<T>(
        Task<T> task,
        CancellationToken cancellationToken)
    {
        if (task.IsCompleted || !cancellationToken.CanBeCanceled)
        {
            return await task.ConfigureAwait(false);
        }

        var cancellation = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(
                   () => cancellation.TrySetResult(true)))
        {
            if (!ReferenceEquals(
                    await Task.WhenAny(task, cancellation.Task)
                        .ConfigureAwait(false),
                    task))
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            return await task.ConfigureAwait(false);
        }
    }
#pragma warning restore VSTHRD003

    private void EnsureWorker()
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        /*
         * The successful transition from zero to one assigns ownership
         * of the processing loop to exactly one worker.
         */
        if (Interlocked.CompareExchange(
                ref _workerState,
                1,
                0) != 0)
        {
            return;
        }

        /*
         * ProcessPendingUpdatesAsync catches all non-cancellation
         * exceptions, so the joinable task cannot leak an exception.
         */
        _= _joinableTaskFactory.RunAsync(
            ProcessPendingUpdatesAsync);
    }

    private async Task ProcessPendingUpdatesAsync()
    {
        var disposalToken =
            _disposeCancellation.Token;

        try
        {
            while (Volatile.Read(ref _disposeState) == 0)
            {
                var request = Interlocked.Exchange(ref _pendingRequest, null);

                if (request == null)
                {
                    return;
                }

                await ParseAndPublishAsync(request, disposalToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
            when (disposalToken.IsCancellationRequested)
        {
            /*
             * Normal context shutdown.
             */
        }
        catch (Exception exception)
        {
            /*
             * A worker failure must not terminate Visual Studio editing.
             */
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Akbura update worker failed: " +
                $"{exception}");
        }
        finally
        {
            /*
             * Release ownership exactly once when this worker exits.
             */
            Volatile.Write(ref _workerState, 0);

            /*
             * A request may have arrived while the worker was exiting.
             * EnsureWorker uses compare-exchange and therefore cannot create
             * another owner when a producer has already started one.
             */
            if (Volatile.Read(ref _disposeState) == 0 &&
                Volatile.Read(ref _pendingRequest) != null)
            {
                EnsureWorker();
            }
        }
    }


    private async Task ParseAndPublishAsync(
        UpdateRequest request,
        CancellationToken disposalToken)
    {
        using var parseCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                disposalToken);

        var previousCancellation =
            Interlocked.Exchange(
                ref _activeParseCancellation,
                parseCancellation);

        if (previousCancellation != null &&
            !ReferenceEquals(
                previousCancellation,
                parseCancellation))
        {
            TryCancel(previousCancellation);
        }

        try
        {
            if (!IsCurrentRequest(
                    request.RequestVersion))
            {
                return;
            }

            var cancellationToken =
                parseCancellation.Token;

            var syntacticDocument = await _parserService
                .GetSyntacticDocumentAsync(
                    request.Snapshot)
                .ConfigureAwait(false);

            cancellationToken
                .ThrowIfCancellationRequested();

            var syntacticState = TryCreateSyntacticState(
                request,
                syntacticDocument,
                cancellationToken);

            if (!IsCurrentRequest(
                    request.RequestVersion))
            {
                return;
            }

            if (syntacticState != null)
            {
                PublishClassificationState(
                    syntacticState);

                await RaiseChangedAsync(
                        syntacticState,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (UpdateDelay > TimeSpan.Zero)
            {
                await Task.Delay(
                        UpdateDelay,
                        cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!IsCurrentRequest(
                    request.RequestVersion))
            {
                return;
            }

            var projectId = await GetProjectIdAsync(cancellationToken)
                .ConfigureAwait(false);

            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            var state = await Task.Run(
                    () => TryCreateParsedState(
                        request,
                        projectId,
                        cancellationToken))
                .ConfigureAwait(false);

            if (state == null)
            {
                return;
            }

            if (!IsCurrentRequest(
                    request.RequestVersion))
            {
                return;
            }

            PublishSemanticState(state);
            PublishClassificationState(state);

            await RaiseChangedAsync(
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (parseCancellation.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Akbura background processing failed: " +
                $"{exception}");
        }
        finally
        {
            Interlocked.CompareExchange(
                ref _activeParseCancellation,
                null,
                parseCancellation);
        }
    }

    private AkburaClassifiedBufferState?
        TryCreateSyntacticState(
            UpdateRequest request,
            AkburaSyntacticDocument document,
            CancellationToken cancellationToken)
    {
        try
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            var classifications =
                _classificationService
                    .GetSyntacticClassifications(
                        document,
                        new TextSpan(
                            start: 0,
                            length: request.Text.Length),
                        cancellationToken);

            var diagnostics =
                _diagnosticService
                    .GetSyntacticDiagnostics(
                        document,
                        new TextSpan(
                            start: 0,
                            length: request.Text.Length),
                        cancellationToken);

            cancellationToken
                .ThrowIfCancellationRequested();

            return new AkburaClassifiedBufferState(
                request.RequestVersion,
                request.Snapshot,
                request.Text,
                classifications,
                diagnostics,
                includesSemanticClassifications: false);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Akbura syntactic classification failed: " +
                $"{exception}");

            return null;
        }
    }

    private AkburaParsedBufferState? TryCreateParsedState(
        UpdateRequest request,
        AkburaProjectId? projectId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            if (projectId is not { } resolvedProjectId)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Workspace,
                    "Semantic state deferred: " +
                    "the owning Roslyn project is not available yet.");
                return null;
            }

            var context = _workspace.OpenOrChangeDocumentContext(
                resolvedProjectId,
                _uri,
                request.Text,
                changes: null,
                cancellationToken);

            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Document project: " +
                $"assembly={context.Project.CSharpCompilation.AssemblyName}, " +
                $"trees={context.Project.CSharpCompilation.SyntaxTrees.Count()}, " +
                $"references={context.Project.CSharpCompilation.References.Count()}");

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            PublishDocumentContext(
                new PublishedDocumentContext(
                    request.RequestVersion,
                    request.Snapshot,
                    context));

            var classifications =
                _classificationService.GetClassifications(
                    context,
                    new TextSpan(
                        start: 0,
                        length: request.Text.Length),
                    cancellationToken);

            var diagnostics =
                _diagnosticService.GetDiagnostics(
                    context,
                    new TextSpan(
                        start: 0,
                        length: request.Text.Length),
                    cancellationToken);

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            return new AkburaParsedBufferState(
                request.RequestVersion,
                request.Snapshot,
                request.Text,
                context,
                classifications,
                diagnostics);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void PublishSemanticState(
        AkburaParsedBufferState state)
    {
        while (true)
        {
            var previous =
                Volatile.Read(
                    ref _publishedSemanticState);

            /*
             * Never replace a newer publication with an older one.
             */
            if (previous != null &&
                previous.RequestVersion >=
                    state.RequestVersion)
            {
                return;
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _publishedSemanticState,
                        state,
                        previous),
                    previous))
            {
                return;
            }
        }
    }

    private void PublishDocumentContext(
        PublishedDocumentContext state)
    {
        while (true)
        {
            var previous = Volatile.Read(
                ref _publishedDocumentContext);
            if (previous != null &&
                previous.RequestVersion >= state.RequestVersion)
            {
                return;
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _publishedDocumentContext,
                        state,
                        previous),
                    previous))
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    $"Document context published: " +
                    $"request={state.RequestVersion}, " +
                    $"snapshot={state.Snapshot.Version.VersionNumber}.");
                DocumentContextPublished?.Invoke();
                return;
            }
        }
    }

    private void PublishClassificationState(
        AkburaClassifiedBufferState state)
    {
        while (true)
        {
            var previous =
                Volatile.Read(
                    ref _publishedClassificationState);

            if (previous != null)
            {
                if (previous.RequestVersion >
                    state.RequestVersion)
                {
                    return;
                }

                if (previous.RequestVersion ==
                        state.RequestVersion &&
                    (previous.IncludesSemanticClassifications ||
                     !state.IncludesSemanticClassifications))
                {
                    return;
                }
            }

            if (ReferenceEquals(
                    Interlocked.CompareExchange(
                        ref _publishedClassificationState,
                        state,
                        previous),
                    previous))
            {
                return;
            }
        }
    }

    private async Task RaiseChangedAsync(
        AkburaClassifiedBufferState state,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(
                state,
                Volatile.Read(
                    ref _publishedClassificationState)))
        {
            return;
        }

        await _joinableTaskFactory
            .SwitchToMainThreadAsync(
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposeState) != 0 ||
            !IsCurrentRequest(
                state.RequestVersion) ||
            !ReferenceEquals(
                state,
                Volatile.Read(
                    ref _publishedClassificationState)))
        {
            return;
        }

        /*
         * Notify for the current editor snapshot. The classifier translates
         * spans from the parsed snapshot when editing has already continued.
         */
        var currentSnapshot =
            _textBuffer.CurrentSnapshot;

        Changed?.Invoke(
            this,
            new AkburaBufferChangedEventArgs(
                new SnapshotSpan(
                    currentSnapshot,
                    0,
                    currentSnapshot.Length)));
    }

    private bool IsCurrentRequest(
        long requestVersion)
    {
        return requestVersion == Volatile.Read(ref _requestedVersion)
            && Volatile.Read(ref _disposeState) == 0;
    }

    private void CancelActiveParse()
    {
        var cancellation = Volatile.Read(ref _activeParseCancellation);

        if (cancellation != null)
        {
            TryCancel(cancellation);
        }
    }

    private static void TryCancel(
        CancellationTokenSource cancellation)
    {
        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            /*
             * The worker completed and disposed the source after another
             * thread observed it.
             */
        }
    }

    private void OnTextDocumentDisposed(
        object sender,
        TextDocumentEventArgs e)
    {
        if (_textDocument != null &&
            ReferenceEquals(
                e.TextDocument,
                _textDocument))
        {
            Dispose();
        }
    }

    private void OnProjectContextChanged(
        object? sender,
        EventArgs e)
    {
        if (Volatile.Read(ref _disposeState) != 0 ||
            _projectId != null)
        {
            return;
        }

        Interlocked.Increment(
            ref _projectContextVersion);

        EnqueueSnapshot(
            _textBuffer.CurrentSnapshot);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(
                ref _disposeState,
                1) != 0)
        {
            return;
        }

        _textBuffer.ChangedLowPriority -=
            OnTextBufferChangedLowPriority;

        _visualStudioWorkspace.ProjectContextChanged -=
            OnProjectContextChanged;

        _subscribedDocumentFactory?.TextDocumentDisposed -=
                OnTextDocumentDisposed;

        Interlocked.Exchange(
            ref _pendingRequest,
            null);

        try
        {
            _disposeCancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }

        CancelActiveParse();

        try
        {
            Disposed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Akbura buffer disposal notification failed: " +
                $"{exception}");
        }

        /*
         * Release classifier subscriptions held by this context.
         */
        Changed = null;
        Disposed = null;
        DocumentContextPublished = null;
    }

    private sealed class UpdateRequest
    {
        public UpdateRequest(
            long requestVersion,
            ITextSnapshot snapshot,
            SourceText text)
        {
            RequestVersion = requestVersion;

            Snapshot = snapshot ??
                throw new ArgumentNullException(
                    nameof(snapshot));

            Text = text ??
                throw new ArgumentNullException(
                    nameof(text));
        }

        public long RequestVersion { get; }

        public ITextSnapshot Snapshot { get; }

        public SourceText Text { get; }
    }

    private sealed class PublishedDocumentContext
    {
        public PublishedDocumentContext(
            long requestVersion,
            ITextSnapshot snapshot,
            AkburaDocumentContext context)
        {
            RequestVersion = requestVersion;
            Snapshot = snapshot;
            Context = context;
        }

        public long RequestVersion { get; }

        public ITextSnapshot Snapshot { get; }

        public AkburaDocumentContext Context { get; }
    }
}
