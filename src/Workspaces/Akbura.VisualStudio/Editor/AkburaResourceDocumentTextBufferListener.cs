using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Utilities;
using System.Collections.Immutable;
using System.ComponentModel.Composition;

namespace Akbura.VisualStudio.Editor;

/// <summary>
/// Delivers live Avalonia resource buffers to the shared Akbura workspace
/// without changing their content type or taking ownership from Avalonia's
/// XAML editor.
/// </summary>
[Export(typeof(ITextBufferContentTypeListener))]
[Name(nameof(AkburaResourceDocumentTextBufferListener))]
[ContentType(StandardContentTypeNames.Text)]
[PartCreationPolicy(CreationPolicy.Shared)]
internal sealed class AkburaResourceDocumentTextBufferListener :
    ITextBufferContentTypeListener
{
    private readonly ITextDocumentFactoryService _documentFactory;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    [ImportingConstructor]
    public AkburaResourceDocumentTextBufferListener(ITextDocumentFactoryService documentFactory, AkburaVisualStudioWorkspace workspaceHost)
    {
        _documentFactory = documentFactory ??
            throw new ArgumentNullException(nameof(documentFactory));
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));

        _documentFactory.TextDocumentCreated += OnTextDocumentCreated;
        _documentFactory.TextDocumentDisposed += OnTextDocumentDisposed;
    }

    public void ContentTypeChanged(ITextBuffer textBuffer, IContentType beforeContentType, IContentType afterContentType)
    {
        if (_documentFactory.TryGetTextDocument(
                textBuffer,
                out var document))
        {
            TryAttach(document);
        }
    }

    private void OnTextDocumentCreated(object sender, TextDocumentEventArgs eventArgs)
    {
        var document = eventArgs.TextDocument;
        document.FileActionOccurred += OnTextDocumentFileActionOccurred;
        TryAttach(document);
    }

    private void OnTextDocumentDisposed(object sender, TextDocumentEventArgs eventArgs)
    {
        var document = eventArgs.TextDocument;
        document.FileActionOccurred -= OnTextDocumentFileActionOccurred;

        if (document.TextBuffer.Properties.TryGetProperty(
                typeof(AkburaResourceDocumentTextBufferContext),
                out AkburaResourceDocumentTextBufferContext context))
        {
            context.Dispose();
        }
    }

    private void OnTextDocumentFileActionOccurred(object sender, TextDocumentFileActionEventArgs eventArgs)
    {
        if (eventArgs.FileActionType ==
                FileActionTypes.DocumentRenamed &&
            sender is ITextDocument document)
        {
            TryAttach(document);
        }
    }

    private void TryAttach(ITextDocument document)
    {
        if (!AkburaResourceDocumentTextBufferContext.IsResourceDocument(
                document.FilePath))
        {
            return;
        }

        _ = document.TextBuffer.Properties.GetOrCreateSingletonProperty(
            () => new AkburaResourceDocumentTextBufferContext(
                document,
                _workspaceHost));
    }
}

internal sealed class AkburaResourceDocumentTextBufferContext : IDisposable
{
    private readonly object _stateGate = new();

    private readonly ITextDocument _document;

    private readonly ITextBuffer _textBuffer;

    private readonly AkburaVisualStudioWorkspace _workspaceHost;

    private ImmutableArray<AkburaResourceDocumentRegistration>
        _registrations =
            ImmutableArray<AkburaResourceDocumentRegistration>.Empty;

    private SourceText? _appliedText;

    private bool _ownershipResolved;

    private string _filePath;

    private UpdateRequest? _pendingRequest;

    private CancellationTokenSource? _activeCancellation;

    private Task _workerTask = Task.CompletedTask;

    private long _requestedVersion;

    private int _workerState;

    private int _disposeState;

    public AkburaResourceDocumentTextBufferContext(ITextDocument document, AkburaVisualStudioWorkspace workspaceHost)
    {
        _document = document ??
            throw new ArgumentNullException(nameof(document));
        _workspaceHost = workspaceHost ??
            throw new ArgumentNullException(nameof(workspaceHost));
        _textBuffer = document.TextBuffer;
        _filePath = Path.GetFullPath(document.FilePath);

        _textBuffer.ChangedLowPriority += OnTextBufferChangedLowPriority;
        _document.FileActionOccurred += OnFileActionOccurred;
        _workspaceHost.ResourceProjectContextChanged +=
            OnProjectContextChanged;

        Enqueue(_textBuffer.CurrentSnapshot, forceRebind: true);
    }

    internal static bool IsResourceDocument(string? filePath)
    {
        return !string.IsNullOrWhiteSpace(filePath) &&
            string.Equals(
                Path.GetExtension(filePath),
                ".axaml",
                StringComparison.OrdinalIgnoreCase);
    }

    private void OnTextBufferChangedLowPriority(object sender, TextContentChangedEventArgs eventArgs)
    {
        Enqueue(eventArgs.After, forceRebind: false);
    }

    private void OnFileActionOccurred(object sender, TextDocumentFileActionEventArgs eventArgs)
    {
        if (eventArgs.FileActionType == FileActionTypes.DocumentRenamed)
        {
            var registrations = ResetRegistrations();
            _workspaceHost.RemoveResourceDocuments(registrations);
            _filePath = Path.GetFullPath(eventArgs.FilePath);

            if (IsResourceDocument(_filePath))
            {
                Enqueue(_textBuffer.CurrentSnapshot, forceRebind: true);
            }

            return;
        }

        if (eventArgs.FileActionType is
            FileActionTypes.ContentLoadedFromDisk or
            FileActionTypes.ContentSavedToDisk)
        {
            Enqueue(
                _textBuffer.CurrentSnapshot,
                forceRebind:
                    eventArgs.FileActionType ==
                    FileActionTypes.ContentLoadedFromDisk);
        }
    }

    private void OnProjectContextChanged(object? sender, Microsoft.CodeAnalysis.WorkspaceChangeEventArgs eventArgs)
    {
        if (IsResourceDocument(_filePath) &&
            IsResourceOwnershipChange(eventArgs.Kind))
        {
            Enqueue(_textBuffer.CurrentSnapshot, forceRebind: true);
        }
    }

    private static bool IsResourceOwnershipChange(Microsoft.CodeAnalysis.WorkspaceChangeKind kind)
    {
        return kind is
            Microsoft.CodeAnalysis.WorkspaceChangeKind.SolutionChanged or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.SolutionAdded or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.SolutionRemoved or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.SolutionCleared or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.SolutionReloaded or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.ProjectAdded or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.ProjectRemoved or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.ProjectChanged or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.ProjectReloaded or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.AdditionalDocumentAdded or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.AdditionalDocumentRemoved or
            Microsoft.CodeAnalysis.WorkspaceChangeKind.AdditionalDocumentReloaded;
    }

    private void Enqueue(ITextSnapshot snapshot, bool forceRebind)
    {
        if (Volatile.Read(ref _disposeState) != 0)
        {
            return;
        }

        var request = new UpdateRequest(
            Interlocked.Increment(ref _requestedVersion),
            _filePath,
            snapshot.AsText(),
            forceRebind);

        Interlocked.Exchange(ref _pendingRequest, request);
        CancelActiveUpdate();
        EnsureWorker();
    }

    private void EnsureWorker()
    {
        if (Interlocked.CompareExchange(
                ref _workerState,
                1,
                0) != 0)
        {
            return;
        }

        lock (_stateGate)
        {
            if (Volatile.Read(ref _disposeState) != 0)
            {
                Interlocked.Exchange(ref _workerState, 0);
                return;
            }

            _workerTask = Task.Run(ProcessPendingRequestsAsync);
        }
    }

    private async Task ProcessPendingRequestsAsync()
    {
        try
        {
            while (Interlocked.Exchange(
                       ref _pendingRequest,
                       null) is { } request)
            {
                using var cancellation = new CancellationTokenSource();
                Interlocked.Exchange(
                    ref _activeCancellation,
                    cancellation);

                try
                {
                    await ApplyAsync(request, cancellation.Token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                    when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception exception)
                {
                    AkburaWorkspaceDiagnostics.Write(
                        AkburaWorkspaceDiagnostics.Category.Workspace,
                        $"Avalonia resource buffer synchronization failed: " +
                        $"{exception}");
                }
                finally
                {
                    Interlocked.CompareExchange(
                        ref _activeCancellation,
                        null,
                        cancellation);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _workerState, 0);
            if (Volatile.Read(ref _pendingRequest) != null &&
                Volatile.Read(ref _disposeState) == 0)
            {
                EnsureWorker();
            }
        }
    }

    private async Task ApplyAsync(UpdateRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        ImmutableArray<AkburaResourceDocumentRegistration> registrations;
        SourceText? appliedText;
        bool ownershipResolved;
        lock (_stateGate)
        {
            registrations = _registrations;
            appliedText = _appliedText;
            ownershipResolved = _ownershipResolved;
        }

        if (request.ForceRebind || !ownershipResolved)
        {
            var newRegistrations = await _workspaceHost
                .OpenOrChangeResourceDocumentAsync(
                    request.FilePath,
                    request.Text,
                    changes: null,
                    cancellationToken)
                .ConfigureAwait(false);

            if (request.Version !=
                    Volatile.Read(ref _requestedVersion) ||
                Volatile.Read(ref _disposeState) != 0)
            {
                _workspaceHost.RestorePersistedResourceDocuments(
                    request.FilePath,
                    newRegistrations,
                    request.Text);
                return;
            }

            RemoveLostProjectRegistrations(
                registrations,
                newRegistrations);

            lock (_stateGate)
            {
                _registrations = newRegistrations;
                _appliedText = request.Text;
                _ownershipResolved = true;
            }

            return;
        }

        var changes = appliedText == null
            ? null
            : request.Text.GetChangeRanges(appliedText);
        _workspaceHost.ChangeResourceDocument(
            registrations,
            request.Text,
            changes,
            cancellationToken);

        lock (_stateGate)
        {
            _appliedText = request.Text;
        }
    }

    private void RemoveLostProjectRegistrations(ImmutableArray<AkburaResourceDocumentRegistration> previous, ImmutableArray<AkburaResourceDocumentRegistration> current)
    {
        if (previous.IsDefaultOrEmpty)
        {
            return;
        }

        var removed = ImmutableArray.CreateBuilder<
            AkburaResourceDocumentRegistration>();
        foreach (var registration in previous)
        {
            if (!current.Any(candidate =>
                    candidate.ProjectId == registration.ProjectId))
            {
                removed.Add(registration);
            }
        }

        if (removed.Count > 0)
        {
            _workspaceHost.RemoveResourceDocuments(removed.ToImmutable());
        }
    }

    private ImmutableArray<AkburaResourceDocumentRegistration> ResetRegistrations()
    {
        CancelActiveUpdate();
        Interlocked.Increment(ref _requestedVersion);
        Interlocked.Exchange(ref _pendingRequest, null);

        lock (_stateGate)
        {
            var registrations = _registrations;
            _registrations =
                ImmutableArray<AkburaResourceDocumentRegistration>.Empty;
            _appliedText = null;
            _ownershipResolved = false;
            return registrations;
        }
    }

    private void CancelActiveUpdate()
    {
        var cancellation = Volatile.Read(ref _activeCancellation);
        if (cancellation == null)
        {
            return;
        }

        try
        {
            cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _textBuffer.ChangedLowPriority -= OnTextBufferChangedLowPriority;
        _document.FileActionOccurred -= OnFileActionOccurred;
        _workspaceHost.ResourceProjectContextChanged -=
            OnProjectContextChanged;

        Interlocked.Exchange(ref _pendingRequest, null);
        CancelActiveUpdate();

        Task worker;
        lock (_stateGate)
        {
            worker = _workerTask;
        }

        _ = RestorePersistedStateAsync(worker);
    }

    private async Task RestorePersistedStateAsync(Task worker)
    {
        try
        {
#pragma warning disable VSTHRD003 // The background buffer worker is intentionally joined during asynchronous disposal cleanup.
            await worker.ConfigureAwait(false);
#pragma warning restore VSTHRD003
        }
        catch
        {
            // Worker failures are logged at their source.
        }

        ImmutableArray<AkburaResourceDocumentRegistration> registrations;
        SourceText? appliedText;
        lock (_stateGate)
        {
            registrations = _registrations;
            _registrations =
                ImmutableArray<AkburaResourceDocumentRegistration>.Empty;
            appliedText = _appliedText;
            _appliedText = null;
        }

        if (registrations.IsDefaultOrEmpty || appliedText == null)
        {
            return;
        }

        try
        {
            _workspaceHost.RestorePersistedResourceDocuments(
                _filePath,
                registrations,
                appliedText);
        }
        catch (Exception exception)
        {
            AkburaWorkspaceDiagnostics.Write(
                AkburaWorkspaceDiagnostics.Category.Workspace,
                $"Avalonia resource buffer close restore failed: " +
                $"{exception}");
        }
    }

    private sealed class UpdateRequest
    {
        public UpdateRequest(long version, string filePath, SourceText text, bool forceRebind)
        {
            Version = version;
            FilePath = filePath;
            Text = text;
            ForceRebind = forceRebind;
        }

        public long Version { get; }

        public string FilePath { get; }

        public SourceText Text { get; }

        public bool ForceRebind { get; }
    }
}
