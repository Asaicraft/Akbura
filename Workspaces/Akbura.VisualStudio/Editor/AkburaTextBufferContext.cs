using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using System.Diagnostics;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Synchronizes one Visual Studio text buffer with one Akbura document.
///
/// Editor callbacks only publish immutable update requests.
/// Parsing and classification are performed by one background worker.
/// Completed state is published atomically and read without locks.
/// </summary>
internal sealed class AkburaTextBufferContext : IDisposable
{
    private static readonly TimeSpan UpdateDelay =
        TimeSpan.FromMilliseconds(100);

    private readonly ITextBuffer _textBuffer;

    private readonly AkburaVisualStudioWorkspace _visualStudioWorkspace;

    private readonly AkburaWorkspace _workspace;

    private AkburaProjectId? _projectId;

    private readonly IAkburaClassificationService _classificationService;

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
    /// Stores the latest completely calculated immutable state.
    /// </summary>
    private AkburaParsedBufferState? _publishedState;

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

#if DEBUG
    private long _enqueueCount;
    private long _processingCount;
#endif

    public AkburaTextBufferContext(
        ITextBuffer textBuffer,
        ITextDocumentFactoryService textDocumentFactory,
        AkburaVisualStudioWorkspace visualStudioWorkspace)
    {
        _textBuffer = textBuffer ?? throw new ArgumentNullException(nameof(textBuffer));

        _visualStudioWorkspace = visualStudioWorkspace ?? throw new ArgumentNullException(nameof(visualStudioWorkspace));

        _workspace = visualStudioWorkspace.Workspace;

        if (textDocumentFactory == null)
        {
            throw new ArgumentNullException(nameof(textDocumentFactory));
        }

        _classificationService = _workspace.LanguageServices.Classification;

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

        EnqueueSnapshot(
            _textBuffer.CurrentSnapshot);
    }

    public event EventHandler<AkburaBufferChangedEventArgs>? Changed;

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

        var current =
            Volatile.Read(ref _publishedState);

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
#if DEBUG
        var enqueueCount = Interlocked.Increment(ref _enqueueCount);

        Debug.WriteLine(
            $"[Akbura] Enqueue #{enqueueCount}, " +
            $"request={requestVersion}, " +
            $"snapshot={snapshot.Version.VersionNumber}");
#endif

        CancelActiveParse();
        EnsureWorker();
    }

    private async Task<AkburaProjectId?> GetProjectIdAsync(CancellationToken cancellationToken)
    {
        if (_projectId is { } projectId)
        {
            return projectId;
        }

        var filePath =
            _textDocument?.FilePath;

        if (string.IsNullOrWhiteSpace(
                filePath))
        {
            return null;
        }

        var synchronizedProjectId =
            await _visualStudioWorkspace
                .SynchronizeProjectAsync(
                    filePath,
                    cancellationToken)
                .ConfigureAwait(false);

        if (synchronizedProjectId is { } result)
        {
            _projectId = result;
        }

        return synchronizedProjectId;
    }

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
        _joinableTaskFactory.RunAsync(
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

                request = await DebounceAsync(request, disposalToken)
                    .ConfigureAwait(false);

                if (!IsCurrentRequest(request.RequestVersion))
                {
                    continue;
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
            Debug.WriteLine(
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


    private async Task<UpdateRequest> DebounceAsync(
        UpdateRequest request,
        CancellationToken cancellationToken)
    {
        if (UpdateDelay <= TimeSpan.Zero)
        {
            return GetNewestPendingRequest(request);
        }

        while (true)
        {
            var versionBeforeDelay =
                Volatile.Read(
                    ref _requestedVersion);

            await Task.Delay(
                    UpdateDelay,
                    cancellationToken)
                .ConfigureAwait(false);

            request = GetNewestPendingRequest(request);

            if (versionBeforeDelay ==
                Volatile.Read(
                    ref _requestedVersion))
            {
                return request;
            }
        }
    }

    private UpdateRequest GetNewestPendingRequest(UpdateRequest current)
    {
        var newer =
            Interlocked.Exchange(
                ref _pendingRequest,
                null);

        return newer ?? current;
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

            PublishState(state);

            await RaiseChangedAsync(
                    state,
                    disposalToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (disposalToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Debug.WriteLine(
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

            var context = projectId is { } resolvedProjectId
                ? _workspace.OpenOrChangeDocumentContext(
                    resolvedProjectId,
                    _uri,
                    request.Text,
                    changes: null,
                    cancellationToken)
                : _workspace.OpenOrChangeDocumentContext(
                    _uri,
                    request.Text,
                    changes: null,
                    cancellationToken);

            Debug.WriteLine(
                $"[Akbura] Document project: " +
                $"assembly={context.Project.CSharpCompilation.AssemblyName}, " +
                $"trees={context.Project.CSharpCompilation.SyntaxTrees.Count()}, " +
                $"references={context.Project.CSharpCompilation.References.Count()}");

            var document =
                context.Document;

            if (cancellationToken.IsCancellationRequested)
            {
                return null;
            }

            var classifications =
                _classificationService.GetClassifications(
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
                document,
                classifications);
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
    }

    private void PublishState(
        AkburaParsedBufferState state)
    {
        while (true)
        {
            var previous =
                Volatile.Read(
                    ref _publishedState);

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
                        ref _publishedState,
                        state,
                        previous),
                    previous))
            {
                return;
            }
        }
    }

    private async Task RaiseChangedAsync(
        AkburaParsedBufferState state,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(
                state,
                Volatile.Read(
                    ref _publishedState)))
        {
            return;
        }

        await _joinableTaskFactory
            .SwitchToMainThreadAsync(
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        if (Volatile.Read(ref _disposeState) != 0 ||
            !ReferenceEquals(
                state,
                Volatile.Read(
                    ref _publishedState)))
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

        /*
         * Release classifier subscriptions held by this context.
         */
        Changed = null;
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
}