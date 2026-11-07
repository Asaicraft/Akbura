using Akbura.AvaloniaEdit.Completion;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;

namespace Akbura.AvaloniaEdit;

public sealed partial class AkburaEditorSession
{
    private CompletionWindow? _completionWindow;
    private CompletionDescriptionPopup? _completionDescription;
    private CancellationTokenSource? _completionCancellation;
    private CancellationTokenSource? _commitCancellation;
    private long _completionSequence;
    private long _commitSequence;
    private bool _incompleteCompletion;
    private int _completionPosition = -1;
    private int _lastCaretOffset = -1;

    public ImmutableArray<AkburaEditorCompletionItem> Completions
    {
        get; private set;
    } = [];
    internal CompletionWindow? CompletionPopup => _completionWindow;
    internal Avalonia.Controls.Primitives.Popup? DescriptionPopup => _completionDescription?.Popup;

    private void AttachCompletion()
    {
        _editor.TextArea.TextEntered += TextEntered;
        _editor.TextArea.TextEntering += TextEntering;
        _editor.TextArea.Caret.PositionChanged += CaretMoved;
        _editor.AddHandler(InputElement.KeyDownEvent, KeyDown, RoutingStrategies.Tunnel);
        _editor.DetachedFromVisualTree += EditorDetached;
    }

    private void DetachCompletion()
    {
        _editor.TextArea.TextEntered -= TextEntered;
        _editor.TextArea.TextEntering -= TextEntering;
        _editor.TextArea.Caret.PositionChanged -= CaretMoved;
        _editor.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        _editor.DetachedFromVisualTree -= EditorDetached;
    }

    private void EditorDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        CancelCompletionRequests();
        DismissCompletion();
        DismissHover();
    }

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        if (_options.CompletionGesture?.Matches(e) == true)
        {
            e.Handled = true;
            _ = RequestCompletionAsync();
        }
        else if (e.Key == Key.Escape)
        {
            CancelCompletionRequests();
            DismissCompletion();
            DismissHover();
        }
    }

    private void CaretMoved(object? sender, EventArgs e)
    {
        var position = _editor.CaretOffset;
        // AvaloniaEdit also raises PositionChanged when layout recalculates the same caret.
        if (position == _lastCaretOffset) return;
        _lastCaretOffset = position;
        if (_completionPosition != position) _completionCancellation?.Cancel();
        DismissHover();
        if (!_applyingCompletion && _completionPosition != position) DismissCompletion();
    }

    private void TextEntered(object? sender, TextInputEventArgs e)
    {
        if (!_options.AutomaticCompletion || _editor.IsReadOnly || string.IsNullOrEmpty(e.Text)) return;
        var character = e.Text[^1];
        // Trigger eligibility is checked against the shared syntactic/C# contexts in the request.
        if (!char.IsControl(character)) _ = RequestCompletionAsync(false, character, CancellationToken.None);
    }

    private void TextEntering(object? sender, TextInputEventArgs e)
    {
        if (_editor.IsReadOnly || e.Text is not { Length: 1 } ||
            _completionWindow?.CompletionList.SelectedItem is not AkburaEditorCompletionItem item) return;
        if (!Completions.Contains(item) || !IsCurrent(item.Snapshot, item.Context, true))
        {
            DismissCompletion();
            return; // Keep normal text input when the visible catalog has just become obsolete.
        }
        var character = e.Text[0];
        if (char.IsLetterOrDigit(character) || character == '_') return;
        e.Handled = true;
        _ = CommitCompletionAsync(item, character);
    }

    private void CancelCompletionRequests()
    {
        _completionCancellation?.Cancel();
        // Commit cancellation also happens on text changes. A character already inserted by a commit remains in the buffer.
        if (!_applyingCompletion) _commitCancellation?.Cancel();
    }

    private void DismissCompletion()
    {
        var popup = _completionWindow;
        _completionWindow = null;
        _completionDescription?.Dispose();
        _completionDescription = null;
        popup?.Hide();
    }

    public Task RequestCompletionAsync(CancellationToken cancellationToken = default) =>
        RequestCompletionAsync(true, '\0', cancellationToken);

    private async Task RequestCompletionAsync(bool isExplicit, char character, CancellationToken cancellationToken)
    {
        var snapshot = Capture();
        if (_editor.IsReadOnly) return;
        _completionPosition = snapshot.Position;
        _completionCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _completionCancellation = cancellation;
        var sequence = ++_completionSequence;
        var incomplete = _incompleteCompletion;
        AkburaDocumentContext? context = null;
        try
        {
            context = await SynchronizeAsync(snapshot, cancellation.Token);
            var results = await _options.Scheduler.RunAsync(async token =>
            {
                var syntax = AkburaSyntacticDocument.Parse(snapshot.Text, context.Document.FilePath, token);
                var nativeContext = syntax.GetCompletionContext(snapshot.Position, token);
                var csharp = syntax.TryGetCSharpCompletionContext(snapshot.Position, out _, token);
                if (!isExplicit && !incomplete && nativeContext.Kind == AkburaCompletionContextKind.None && !csharp)
                    return (Syntax: syntax, Native: default(AkburaCompletionResult), Projected: (AkburaProjectedCompletionResult?)null);
                var native = _workspace.LanguageServices.Completion.GetCompletions(syntax, context, snapshot.Position, token);
                var projected = await _workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(syntax, context,
                    snapshot.Position, new(isExplicit, incomplete, character), token).ConfigureAwait(false);
                return (Syntax: syntax, Native: native, Projected: projected);
            }, cancellation.Token);
            if (cancellation.IsCancellationRequested || sequence != _completionSequence || !IsCurrent(snapshot, context, checkPosition: true)) return;
            var items = ImmutableArray.CreateBuilder<AkburaEditorCompletionItem>();
            var identities = new HashSet<string>(StringComparer.Ordinal);
            if (results.Projected is { } projected)
                foreach (var item in projected.Items)
                    if (identities.Add("csharp\0" + item.ResolveKey))
                        items.Add(new(this, snapshot, results.Syntax, context, null, item, item.SourceSpan));
            foreach (var item in results.Native.Items)
                if (identities.Add("akbura\0" + item.ResolveKey))
                    items.Add(new(this, snapshot, results.Syntax, context, item, null, results.Native.ApplicableSpan));
            Completions = items.OrderBy(item => item.SortText, StringComparer.Ordinal).ToImmutableArray();
            _incompleteCompletion = results.Native.IsIncomplete || results.Projected?.IsIncomplete == true;
            DismissCompletion();
            if (Completions.IsEmpty || TopLevel.GetTopLevel(_editor) == null) return;
            var popup = new CompletionWindow(_editor.TextArea)
            {
                StartOffset = Math.Min(snapshot.Position, Completions.Min(item => item.SourceSpan.Start)),
                EndOffset = snapshot.Position,
            };
            foreach (var item in Completions) popup.CompletionList.CompletionData.Add(item);
            _completionWindow = popup;
            _completionDescription = new CompletionDescriptionPopup(popup, GetCompletionDescriptionAsync);
            popup.Closed += (_, _) =>
            {
                if (ReferenceEquals(_completionWindow, popup))
                {
                    _completionWindow = null;
                    _completionDescription?.Dispose();
                    _completionDescription = null;
                    _completionCancellation?.Cancel();
                    _completionSequence++;
                    _incompleteCompletion = false;
                }
            };
            popup.Show();
            popup.CompletionList.SelectItem(snapshot.Text.ToString(TextSpan.FromBounds(popup.StartOffset, snapshot.Position)));
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot)) { }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _completionSequence)
                ReportFailure("Completion", error, snapshot, context);
        }
        finally { if (ReferenceEquals(_completionCancellation, cancellation)) _completionCancellation = null; }
    }

    internal void CommitFromPopup(AkburaEditorCompletionItem item) => _ = CommitCompletionAsync(item);

    private async Task<string?> GetCompletionDescriptionAsync(AkburaEditorCompletionItem item, CancellationToken cancellationToken)
    {
        if (!IsCurrent(item.Snapshot, item.Context, true))
        {
            return null;
        }
        try
        {
            var description = await _options.Scheduler.RunAsync(async token =>
            {
                if (item.Native is { } native)
                {
                    token.ThrowIfCancellationRequested();
                    return native.Description;
                }
                var resolution = await _workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(
                    item.Syntax, item.Context, item.Snapshot.Position, item.ResolveKey, token).ConfigureAwait(false);
                return string.IsNullOrWhiteSpace(resolution?.Documentation) ? item.Projected?.Detail : resolution.Documentation;
            }, cancellationToken);
            return !cancellationToken.IsCancellationRequested && IsCurrent(item.Snapshot, item.Context, true)
                ? description : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested || _disposed)
        {
            return null;
        }
        catch (Exception error)
        {
            if (!cancellationToken.IsCancellationRequested)
            {
                ReportFailure("Completion description", error, item.Snapshot, item.Context);
            }
            return null;
        }
    }

    public async Task<bool> CommitCompletionAsync(AkburaEditorCompletionItem item, char? commitCharacter = null,
        CancellationToken cancellationToken = default)
    {
        VerifyUsable();
        ArgumentNullException.ThrowIfNull(item);
        var snapshot = item.Snapshot;
        if (_editor.IsReadOnly || !Completions.Contains(item) || !IsCurrent(snapshot, item.Context, true)) return false;
        DismissCompletion();
        _commitCancellation?.Cancel();
        _analysisCancellation?.Cancel();
        // Insert a typed commit character immediately: editing remains responsive and a stale resolution cannot lose it.
        _applyingCompletion = true;
        try
        {
            if (commitCharacter is { } character)
            {
                _document.Insert(snapshot.Position, character.ToString());
                _editor.CaretOffset = snapshot.Position + 1;
            }
        }
        finally { _applyingCompletion = false; }
        var expected = Capture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _commitCancellation = cancellation;
        var sequence = ++_commitSequence;
        try
        {
            var change = await _options.Scheduler.RunAsync(async token => item.Projected is { } projected
                ? (await _workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(item.Syntax, item.Context,
                    snapshot.Position, projected.ResolveKey, token).ConfigureAwait(false))?.Change
                : _workspace.LanguageServices.Completion.GetCompletionChange(item.Syntax, item.Context,
                    snapshot.Position, item.Native!, token), cancellation.Token);
            if (change == null || cancellation.IsCancellationRequested || sequence != _commitSequence ||
                _editor.IsReadOnly || !IsCurrent(expected, item.Context, true)) return false;
            var edits = CompletionEdits.Validate(change, snapshot.Text.Length);
            _applyingCompletion = true;
            if (commitCharacter != null) _document.UndoStack.StartContinuedUndoGroup(null);
            try
            {
                using (_document.RunUpdate())
                {
                    if (commitCharacter != null) _document.Remove(snapshot.Position, 1);
                    foreach (var edit in edits.Reverse())
                        _document.Replace(edit.Span.Start, edit.Span.Length, edit.NewText ?? "");
                    var caret = change.NewPosition;
                    if (commitCharacter is { } character)
                    {
                        // Canonical edits may already contain the delimiter (for example an attribute '=').
                        if (caret < _document.TextLength && _document.GetCharAt(caret) == character) caret++;
                        else if ((caret == 0 || _document.GetCharAt(caret - 1) != character) &&
                            !CanonicalSuffixContainsCharacter(item, edits, snapshot.Position, character))
                        {
                            _document.Insert(caret, character.ToString());
                            caret++;
                        }
                    }
                    _editor.CaretOffset = caret;
                }
            }
            finally
            {
                if (commitCharacter != null) _document.UndoStack.EndUndoGroup();
                _applyingCompletion = false;
            }
            _ = RefreshAsync(_options.AnalysisDelay);
            if (change.TriggerNextCompletion || commitCharacter == '.')
            {
                var followup = Capture();
                var completionSequence = _completionSequence;
                Dispatcher.UIThread.Post(() =>
                {
                    if (IsCurrent(followup, checkPosition: true) && completionSequence == _completionSequence)
                        _ = RequestCompletionAsync();
                });
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(expected)) { return false; }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _commitSequence)
                ReportFailure("Completion commit", error, expected, item.Context);
            return false;
        }
        finally
        {
            if (ReferenceEquals(_commitCancellation, cancellation)) _commitCancellation = null;
            if (!_disposed && commitCharacter != null) _ = RefreshAsync(_options.AnalysisDelay);
        }
    }

    private static bool CanonicalSuffixContainsCharacter(AkburaEditorCompletionItem item,
        ImmutableArray<TextChange> edits, int position, char character)
    {
        foreach (var edit in edits)
        {
            if (edit.Span.Start > position || edit.Span.End < position || edit.NewText is not { } inserted) continue;
            if (inserted.StartsWith(item.DisplayText, StringComparison.Ordinal) &&
                inserted.AsSpan(item.DisplayText.Length).Contains(character)) return true;
        }
        return false;
    }
}
