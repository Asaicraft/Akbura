using System;
using System.Threading;
using System.Threading.Tasks;
using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Akbura.Workspaces.Completion;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Threading;

namespace Akbura.VisualStudio.Completion;

/// <summary>
/// Replaces an already active attribute list when typing enters ${.
/// A native completion session filters its original items; it does not switch
/// syntax catalogs just because another trigger character was inserted.
/// </summary>
internal sealed class AkburaMarkupExtensionCompletionController : IDisposable
{
    private readonly ITextView _view;
    private readonly ITextBuffer _buffer;
    private readonly AkburaParserService _parserService;
    private readonly IAsyncCompletionBroker _broker;
    private readonly AkburaLatestRequestCancellation _requests = new();
    private int _disposed;

    public AkburaMarkupExtensionCompletionController(
        ITextView view,
        AkburaParserService parserService,
        IAsyncCompletionBroker broker)
    {
        _view = view ?? throw new ArgumentNullException(nameof(view));
        _buffer = view.TextBuffer;
        _parserService = parserService ?? throw new ArgumentNullException(nameof(parserService));
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        _buffer.Changed += OnBufferChanged;
        _view.Closed += OnViewClosed;
    }

    private void OnBufferChanged(object? sender, TextContentChangedEventArgs args)
    {
        if (Volatile.Read(ref _disposed) != 0)
        {
            return;
        }

        foreach (var change in args.Changes)
        {
            // Inspect only inserted text, not the whole document on each key.
            // Also works when a brace-completion handler inserts the delimiter.
            for (var offset = 0; offset < change.NewLength; offset++)
            {
                var position = change.NewPosition + offset;
                if (args.After[position] != '{' || position == 0 || args.After[position - 1] != '$')
                {
                    continue;
                }

                var openingPoint = args.After.CreateTrackingPoint(position, PointTrackingMode.Negative);
                AkburaLatestRequest request;
                try
                {
                    request = _requests.Begin(CancellationToken.None);
                }
                catch (ObjectDisposedException) when (Volatile.Read(ref _disposed) != 0)
                {
                    return;
                }

#pragma warning disable VSSDK007 // Deliberately detached; RestartAsync observes cancellation and logs failures.
                ThreadHelper.JoinableTaskFactory.RunAsync(
                    () => RestartAsync(openingPoint, request))
                    .FileAndForget("Akbura/Completion/MarkupExtensionContext");
#pragma warning restore VSSDK007
                return;
            }
        }
    }

    private async Task RestartAsync(ITrackingPoint openingPoint, AkburaLatestRequest request)
    {
        using (request)
        {
            try
            {
                // Changed fires before the typing command has updated the caret
                // and before automatic pairing may insert }. Do not start a
                // new completion session in the middle of that buffer edit.
                await Task.Yield();
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(request.Token);
                if (!CanUseView() || !request.IsCurrent)
                {
                    return;
                }

                var oldSession = _broker.GetSession(_view);
                if (oldSession == null || oldSession.IsDismissed)
                {
                    // With no live session the normal insertion trigger is
                    // responsible for starting one. Never undo an Escape here.
                    return;
                }

                while (request.IsCurrent && CanUseView())
                {
                    var snapshot = _buffer.CurrentSnapshot;
                    var caretPosition = _view.Caret.Position.BufferPosition.Position;
                    var openingPosition = openingPoint.GetPoint(snapshot).Position;
                    if (openingPosition < 1 || openingPosition >= snapshot.Length ||
                        caretPosition <= openingPosition || snapshot[openingPosition] != '{' ||
                        snapshot[openingPosition - 1] != '$')
                    {
                        return;
                    }

                    var document = await _parserService.GetSyntacticDocumentAsync(snapshot)
                        .ConfigureAwait(false);
                    var matches = AkburaMarkupExtensionCompletionFacts.TryGetTypeNameSpan(
                        document, caretPosition, openingPosition, out var nameSpan, request.Token);

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(request.Token);
                    if (!CanUseView() || !request.IsCurrent || oldSession.IsDismissed ||
                        !ReferenceEquals(_broker.GetSession(_view), oldSession))
                    {
                        return;
                    }

                    if (!ReferenceEquals(snapshot, _buffer.CurrentSnapshot) ||
                        caretPosition != _view.Caret.Position.BufferPosition.Position)
                    {
                        // Fast typing may have already produced ${St. Re-evaluate
                        // that current name instead of showing a stale empty list.
                        continue;
                    }

                    if (!matches)
                    {
                        return;
                    }

                    var tracked = oldSession.ApplicableToSpan.GetSpan(snapshot);
                    if (AkburaMarkupExtensionCompletionFacts.IsTypeNameSession(
                            new TextSpan(tracked.Start.Position, tracked.Length), nameSpan))
                    {
                        return;
                    }

                    if (!_broker.IsCompletionSupported(_buffer.ContentType, _view.Roles))
                    {
                        return;
                    }

                    // TriggerCompletion alone returns an existing session. A new
                    // name span and a new catalog require dismissing the old one.
                    oldSession.Dismiss();
                    var trigger = new CompletionTrigger(CompletionTriggerReason.Invoke, snapshot, '\0');
                    var location = new SnapshotPoint(snapshot, caretPosition);
                    var session = _broker.TriggerCompletion(_view, trigger, location, CancellationToken.None);
                    session?.OpenOrUpdate(trigger, location, CancellationToken.None);
                    AkburaWorkspaceDiagnostics.Write(
                        AkburaWorkspaceDiagnostics.Category.Completion,
                        $"Markup extension session switched: snapshot={snapshot.Version.VersionNumber}, " +
                        $"oldSpan={tracked.Span}, nameSpan={nameSpan}, started={session != null}.");
                    return;
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception error)
            {
                AkburaWorkspaceDiagnostics.Write(
                    AkburaWorkspaceDiagnostics.Category.Completion,
                    "Markup extension session switch failed: " + error);
            }
        }
    }

    private bool CanUseView()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return Volatile.Read(ref _disposed) == 0 && !_view.IsClosed &&
            _view.Selection.IsEmpty && !_view.GetMultiSelectionBroker().HasMultipleSelections &&
            ReferenceEquals(_view.Caret.Position.BufferPosition.Snapshot.TextBuffer, _buffer);
    }

    private void OnViewClosed(object? sender, EventArgs args) => Dispose();

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _buffer.Changed -= OnBufferChanged;
        _view.Closed -= OnViewClosed;
        _requests.Dispose();
    }
}
