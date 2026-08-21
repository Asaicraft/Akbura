using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Text.Operations;
using System.Collections.Immutable;

namespace Akbura.VisualStudio.SuggestedActions;

internal sealed class AkburaSuggestedActionsSource : ISuggestedActionsSource
{
    private readonly ITextView _textView;
    private readonly ITextBuffer _textBuffer;
    private readonly AkburaTextBufferContext _bufferContext;
    private readonly IAkburaCodeActionService _codeActionService;
    private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;
    private CachedActions? _cache;
    private int _disposeState;

    public AkburaSuggestedActionsSource(
        ITextView textView,
        ITextBuffer textBuffer,
        AkburaTextBufferContext bufferContext,
        IAkburaCodeActionService codeActionService,
        ITextUndoHistoryRegistry undoHistoryRegistry)
    {
        _textView = textView ??
            throw new ArgumentNullException(nameof(textView));
        _textBuffer = textBuffer ??
            throw new ArgumentNullException(nameof(textBuffer));
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(nameof(bufferContext));
        _codeActionService = codeActionService ??
            throw new ArgumentNullException(nameof(codeActionService));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(nameof(undoHistoryRegistry));

        _bufferContext.Changed += OnBufferContextChanged;
        _textView.Closed += OnTextViewClosed;
    }

    public event EventHandler<EventArgs>? SuggestedActionsChanged;

    public async Task<bool> HasSuggestedActionsAsync(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var result = await Task.Run(
                () =>
                {
                    var actions = GetActions(
                        range,
                        cancellationToken,
                        out var state);

                    return new ActionQueryResult(
                        actions,
                        state);
                },
                cancellationToken)
            .ConfigureAwait(false);

        cancellationToken.ThrowIfCancellationRequested();

        if (result.State == null ||
            Volatile.Read(ref _disposeState) != 0)
        {
            return false;
        }

        Volatile.Write(
            ref _cache,
            new CachedActions(
                result.State,
                range,
                result.Actions));

        return !result.Actions.IsDefaultOrEmpty;
    }

    public IEnumerable<SuggestedActionSet> GetSuggestedActions(
        ISuggestedActionCategorySet requestedActionCategories,
        SnapshotSpan range,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cache = Volatile.Read(ref _cache);

        ImmutableArray<AkburaCodeAction> actions;
        AkburaParsedBufferState? state;
        if (cache != null && cache.Matches(range))
        {
            actions = cache.Actions;
            state = cache.State;
        }
        else
        {
            actions = GetActions(
                range,
                cancellationToken,
                out state);
        }

        if (state == null || actions.IsDefaultOrEmpty)
        {
            return Array.Empty<SuggestedActionSet>();
        }

        var suggestedActions = actions.Select(action =>
            (ISuggestedAction)new AkburaSuggestedAction(
                _textBuffer,
                _bufferContext,
                _codeActionService,
                _undoHistoryRegistry,
                state,
                action));

        return
        [
            new SuggestedActionSet(
                PredefinedSuggestedActionCategoryNames.ErrorFix,
                suggestedActions),
        ];
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0)
        {
            return;
        }

        _bufferContext.Changed -= OnBufferContextChanged;
        _textView.Closed -= OnTextViewClosed;
        Volatile.Write(ref _cache, null);
    }

    private ImmutableArray<AkburaCodeAction> GetActions(
        SnapshotSpan range,
        CancellationToken cancellationToken,
        out AkburaParsedBufferState? state)
    {
        state = null;
        if (Volatile.Read(ref _disposeState) != 0 ||
            !ReferenceEquals(range.Snapshot.TextBuffer, _textBuffer) ||
            !_bufferContext.TryGetPublishedState(
                range.Snapshot,
                out var semanticState))
        {
            return ImmutableArray<AkburaCodeAction>.Empty;
        }

        SnapshotSpan semanticRange;
        try
        {
            semanticRange = range.TranslateTo(
                semanticState.Snapshot,
                SpanTrackingMode.EdgeInclusive);
        }
        catch (ArgumentException)
        {
            return ImmutableArray<AkburaCodeAction>.Empty;
        }

        state = semanticState;
        return _codeActionService.GetCodeActions(
            semanticState.Context,
            new TextSpan(
                semanticRange.Start.Position,
                semanticRange.Length),
            cancellationToken);
    }

    private void OnBufferContextChanged(
        object? sender,
        AkburaBufferChangedEventArgs e)
    {
        Volatile.Write(ref _cache, null);
        SuggestedActionsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnTextViewClosed(object? sender, EventArgs e)
    {
        Dispose();
    }

    private sealed class CachedActions
    {
        public CachedActions(
            AkburaParsedBufferState state,
            SnapshotSpan requestedRange,
            ImmutableArray<AkburaCodeAction> actions)
        {
            State = state;
            RequestedRange = requestedRange;
            Actions = actions.IsDefault
                ? ImmutableArray<AkburaCodeAction>.Empty
                : actions;
        }

        public AkburaParsedBufferState State { get; }

        public SnapshotSpan RequestedRange { get; }

        public ImmutableArray<AkburaCodeAction> Actions { get; }

        public bool Matches(SnapshotSpan range)
        {
            return ReferenceEquals(
                    RequestedRange.Snapshot,
                    range.Snapshot) &&
                RequestedRange.Span == range.Span;
        }
    }

    private readonly struct ActionQueryResult
    {
        public ActionQueryResult(
            ImmutableArray<AkburaCodeAction> actions,
            AkburaParsedBufferState? state)
        {
            Actions = actions;
            State = state;
        }

        public ImmutableArray<AkburaCodeAction> Actions { get; }

        public AkburaParsedBufferState? State { get; }
    }
}
