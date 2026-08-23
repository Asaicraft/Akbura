using Akbura.VisualStudio.Editor;
using Akbura.Workspaces;
using Microsoft.CodeAnalysis.Text;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;

namespace Akbura.VisualStudio.SuggestedActions;

internal sealed class AkburaSuggestedAction : ISuggestedAction
{
    private readonly ITextBuffer _buffer;
    private readonly AkburaTextBufferContext _bufferContext;
    private readonly IAkburaCodeActionService _codeActionService;
    private readonly ITextUndoHistoryRegistry _undoHistoryRegistry;
    private readonly ITrackingSpan _diagnosticSpan;
    private readonly string _equivalenceKey;
    private readonly string _subjectText;

    public AkburaSuggestedAction(
        ITextBuffer buffer,
        AkburaTextBufferContext bufferContext,
        IAkburaCodeActionService codeActionService,
        ITextUndoHistoryRegistry undoHistoryRegistry,
        AkburaParsedBufferState semanticState,
        AkburaCodeAction action)
    {
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _bufferContext = bufferContext ??
            throw new ArgumentNullException(nameof(bufferContext));
        _codeActionService = codeActionService ??
            throw new ArgumentNullException(nameof(codeActionService));
        _undoHistoryRegistry = undoHistoryRegistry ??
            throw new ArgumentNullException(nameof(undoHistoryRegistry));
        if (semanticState == null)
        {
            throw new ArgumentNullException(nameof(semanticState));
        }

        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        DisplayText = action.Title;
        _equivalenceKey = action.EquivalenceKey;
        _subjectText = action.SubjectText;
        _diagnosticSpan = semanticState.Snapshot.CreateTrackingSpan(
            new Span(
                action.DiagnosticSpan.Start,
                action.DiagnosticSpan.Length),
            SpanTrackingMode.EdgeExclusive);
    }

    public string DisplayText { get; }

    public string? IconAutomationText => null;

    public ImageMoniker IconMoniker => default;

    public string? InputGestureText => null;

    public bool HasActionSets => false;

    public bool HasPreview => false;

    public Task<IEnumerable<SuggestedActionSet>?> GetActionSetsAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult<IEnumerable<SuggestedActionSet>?>(
            Array.Empty<SuggestedActionSet>());
    }

    public Task<object?> GetPreviewAsync(
        CancellationToken cancellationToken)
    {
        return Task.FromResult<object?>(null);
    }

    public bool TryGetTelemetryId(out Guid telemetryId)
    {
        telemetryId = Guid.Empty;
        return false;
    }

    public void Invoke(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var currentSnapshot = _buffer.CurrentSnapshot;

        SnapshotSpan currentDiagnosticSpan;
        try
        {
            currentDiagnosticSpan = _diagnosticSpan.GetSpan(currentSnapshot);
        }
        catch (ArgumentException)
        {
            return;
        }

        if (!string.Equals(
                currentDiagnosticSpan.GetText(),
                _subjectText,
                StringComparison.Ordinal) ||
            !_bufferContext.TryGetPublishedState(
                currentSnapshot,
                out var semanticState))
        {
            return;
        }

        SnapshotSpan semanticDiagnosticSpan;
        try
        {
            semanticDiagnosticSpan = currentDiagnosticSpan.TranslateTo(
                semanticState.Snapshot,
                SpanTrackingMode.EdgeInclusive);
        }
        catch (ArgumentException)
        {
            return;
        }

        var freshAction = _codeActionService.GetCodeActions(
                semanticState.Context,
                new TextSpan(
                    semanticDiagnosticSpan.Start.Position,
                    semanticDiagnosticSpan.Length),
                cancellationToken)
            .FirstOrDefault(action => string.Equals(
                action.EquivalenceKey,
                _equivalenceKey,
                StringComparison.Ordinal));
        if (freshAction == null)
        {
            return;
        }

        var currentText = currentSnapshot.AsText();
        var syntacticDocument = AkburaSyntacticDocument.Parse(
            currentText,
            _bufferContext.FilePath,
            cancellationToken);
        if (!AkburaUsingEditService.TryCreateNamespaceImportChange(
                currentText,
                syntacticDocument.SyntaxTree,
                freshAction.NamespaceName,
                out var currentChange))
        {
            return;
        }

        ApplyChange(currentSnapshot, currentChange);
    }

    public void Dispose()
    {
    }

    private void ApplyChange(
        ITextSnapshot snapshot,
        TextChange change)
    {
        if (!ReferenceEquals(snapshot, _buffer.CurrentSnapshot) ||
            (uint)change.Span.Start > (uint)snapshot.Length ||
            change.Span.End > snapshot.Length)
        {
            return;
        }

        if (!_undoHistoryRegistry.TryGetHistory(
                _buffer,
                out var undoHistory))
        {
            undoHistory = _undoHistoryRegistry.RegisterHistory(_buffer);
        }

        using var transaction = undoHistory.CreateTransaction(DisplayText);
        using var edit = _buffer.CreateEdit();
        if (!edit.Replace(
                new Span(change.Span.Start, change.Span.Length),
                change.NewText ?? string.Empty))
        {
            return;
        }

        edit.Apply();
        transaction.Complete();
    }
}
