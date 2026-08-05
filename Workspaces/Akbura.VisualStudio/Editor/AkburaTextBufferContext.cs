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
    private readonly AkburaWorkspace _workspace;

    private readonly IAkburaClassificationService
        _classificationService;

    private readonly JoinableTaskFactory
        _joinableTaskFactory;

    private readonly Uri _uri;

    private readonly CancellationTokenSource
        _disposeCancellation = new();

    private readonly ITextDocumentFactoryService?
        _subscribedDocumentFactory;

    private readonly ITextDocument?
        _textDocument;

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
    private CancellationTokenSource?
        _activeParseCancellation;

    /// <summary>
    /// Stores the latest completely calculated immutable state.
    /// </summary>
    private AkburaParsedBufferState?
        _publishedState;

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

    public AkburaTextBufferContext(
        ITextBuffer textBuffer,
        ITextDocumentFactoryService textDocumentFactory,
        AkburaWorkspace workspace)
    {
        _textBuffer = textBuffer ??
            throw new ArgumentNullException(nameof(textBuffer));

        _workspace = workspace ??
            throw new ArgumentNullException(nameof(workspace));

        if (textDocumentFactory == null)
        {
            throw new ArgumentNullException(
                nameof(textDocumentFactory));
        }

        _classificationService =
            workspace.LanguageServices.Classification;

        _joinableTaskFactory =
            ThreadHelper.JoinableTaskFactory;

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

    public event EventHandler<AkburaBufferChangedEventArgs>?
        Changed;

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

        CancelActiveParse();
        EnsureWorker();
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
            while (Volatile.Read(
                       ref _disposeState) == 0)
            {
                var request =
                    Interlocked.Exchange(
                        ref _pendingRequest,
                        null);

                if (request == null)
                {
                    if (TryTransitionWorkerToIdle())
                    {
                        return;
                    }

                    continue;
                }

                request = await DebounceAsync(
                        request,
                        disposalToken)
                    .ConfigureAwait(false);

                if (!IsCurrentRequest(
                        request.RequestVersion))
                {
                    continue;
                }

                await ParseAndPublishAsync(
                        request,
                        disposalToken)
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
                $"Akbura update worker failed: {exception}");
        }
        finally
        {
            /*
             * An unexpected worker exit must release ownership.
             */
            if (Interlocked.Exchange(
                    ref _workerState,
                    0) != 0 &&
                Volatile.Read(ref _disposeState) == 0 &&
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

            request =
                GetNewestPendingRequest(request);

            /*
             * Restart the delay when another edit arrived while waiting.
             * This prevents starting a parse for every character during
             * continuous typing.
             */
            if (versionBeforeDelay ==
                Volatile.Read(
                    ref _requestedVersion))
            {
                return request;
            }
        }
    }

    private UpdateRequest GetNewestPendingRequest(
        UpdateRequest current)
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
            CancellationTokenSource
                .CreateLinkedTokenSource(
                    disposalToken);

        var previousCancellation =
            Interlocked.Exchange(
                ref _activeParseCancellation,
                parseCancellation);

        /*
         * One worker owns parsing, so this should normally be null.
         * Cancel defensively if a previous source is still visible.
         */
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

            /*
             * Task.Run guarantees that parsing and classification cannot
             * execute on the Visual Studio main thread.
             */
            var state = await Task.Run(
                    () => CreateParsedState(
                        request,
                        cancellationToken),
                    cancellationToken)
                .ConfigureAwait(false);

            cancellationToken
                .ThrowIfCancellationRequested();

            /*
             * A newer request normally cancels this operation. The version
             * check also protects against a parser that completed during
             * the cancellation race.
             */
            if (!IsCurrentRequest(
                    request.RequestVersion))
            {
                return;
            }

            PublishState(state);

            await RaiseChangedAsync(
                    state,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (parseCancellation.IsCancellationRequested)
        {
            /*
             * A newer editor snapshot superseded this parse.
             */
        }
        catch (Exception exception)
        {
            /*
             * Parser and classification failures must not break editing.
             */
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

    private AkburaParsedBufferState CreateParsedState(
        UpdateRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken
            .ThrowIfCancellationRequested();

        var document =
            _workspace.OpenOrChangeDocument(
                _uri,
                request.Text,
                changes: null,
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        /*
         * Calculate classifications once for the complete document.
         * Scrolling must not traverse the syntax tree synchronously.
         */
        var classifications =
            _classificationService.GetClassifications(
                document,
                new TextSpan(
                    start: 0,
                    length: request.Text.Length),
                cancellationToken);

        cancellationToken
            .ThrowIfCancellationRequested();

        return new AkburaParsedBufferState(
            request.RequestVersion,
            request.Snapshot,
            request.Text,
            document,
            classifications);
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

    private bool TryTransitionWorkerToIdle()
    {
        /*
         * Release worker ownership before checking for a request that may
         * have arrived concurrently.
         */
        Volatile.Write(
            ref _workerState,
            0);

        if (Volatile.Read(ref _disposeState) != 0 ||
            Volatile.Read(ref _pendingRequest) == null)
        {
            return true;
        }

        /*
         * Reacquire ownership if no producer has already started a new
         * worker. Otherwise the new worker owns the pending request.
         */
        return Interlocked.CompareExchange(
                   ref _workerState,
                   1,
                   0) != 0;
    }

    private bool IsCurrentRequest(
        long requestVersion)
    {
        return requestVersion ==
                   Volatile.Read(
                       ref _requestedVersion) &&
               Volatile.Read(
                   ref _disposeState) == 0;
    }

    private void CancelActiveParse()
    {
        var cancellation =
            Volatile.Read(
                ref _activeParseCancellation);

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

        if (_subscribedDocumentFactory != null)
        {
            _subscribedDocumentFactory.TextDocumentDisposed -=
                OnTextDocumentDisposed;
        }

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