using Akbura.Pools;
using Akbura.AvaloniaEdit.Completion;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;

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
    private bool _completionInputPosted;
    private long _completionInputEpoch;
    private char _pendingCompletionCharacter;
    private string? _completionGestureText;
    private TextDocument? _completionBuffer;
    private string? _completionContext;
    private bool _completionEditsCompatible;
    private bool _completionCatalogNeedsRefresh;
    private AkburaEditorCompletionItem? _preferredCompletionItem;
    private Avalonia.Vector? _preferredCompletionScroll;

    internal Task CompletionIdle => _automaticCompletionTask ?? Task.CompletedTask;
    private Task? _automaticCompletionTask;

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
        _editor.AddHandler(InputElement.KeyUpEvent, KeyUp, RoutingStrategies.Tunnel, handledEventsToo: true);
        _editor.DetachedFromVisualTree += EditorDetached;
    }

    private void DetachCompletion()
    {
        _editor.TextArea.TextEntered -= TextEntered;
        _editor.TextArea.TextEntering -= TextEntering;
        _editor.TextArea.Caret.PositionChanged -= CaretMoved;
        _editor.RemoveHandler(InputElement.KeyDownEvent, KeyDown);
        _editor.RemoveHandler(InputElement.KeyUpEvent, KeyUp);
        _editor.DetachedFromVisualTree -= EditorDetached;
    }

    private void EditorDetached(object? sender, Avalonia.VisualTreeAttachmentEventArgs e)
    {
        _completionGestureText = null;
        CancelCompletionRequests();
        DismissCompletion();
        DismissHover();
    }

    private void KeyDown(object? sender, KeyEventArgs e)
    {
        _completionGestureText = null;
        if (_options.CompletionGesture?.Matches(e) == true)
        {
            e.Handled = true;
            _completionGestureText = e.Key == Key.Space ? " " : e.KeySymbol;
            _ = RequestCompletionAsync();
        }
        else if (e.Key == Key.Escape)
        {
            CancelCompletionRequests();
            DismissCompletion();
            DismissHover();
        }
    }

    private void KeyUp(object? sender, KeyEventArgs e) => _completionGestureText = null;

    private void CaretMoved(object? sender, EventArgs e)
    {
        var position = _editor.CaretOffset;
        // AvaloniaEdit also raises PositionChanged when layout recalculates the same caret.
        if (position == _lastCaretOffset)
        {
            return;
        }

        _lastCaretOffset = position;
        if (_completionPosition != position)
        {
            _completionCancellation?.Cancel();
        }

        DismissHover();
        if (!_applyingCompletion && _completionPosition != position)
        {
            if (_completionWindow is { } popup && position >= popup.StartOffset && position <= popup.EndOffset)
            {
                _completionPosition = position;
                ScheduleCompletion('\0');
            }
            else
            {
                DismissCompletion();
            }
        }
    }

    private void TextEntered(object? sender, TextInputEventArgs e)
    {
        if (!_options.AutomaticCompletion || _editor.IsReadOnly || string.IsNullOrEmpty(e.Text))
        {
            return;
        }

        var character = e.Text[^1];
        // Trigger eligibility is checked against the shared syntactic/C# contexts in the request.
        if (!char.IsControl(character))
        {
            ScheduleCompletion(character);
        }
    }

    private void ScheduleCompletion(char character)
    {
        _pendingCompletionCharacter = character;
        if (_completionInputPosted)
        {
            return;
        }

        _completionInputPosted = true;
        var epoch = _completionInputEpoch;
        var idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _automaticCompletionTask = idle.Task;
        Dispatcher.UIThread.Post(async () =>
        {
            if (epoch != _completionInputEpoch)
            {
                idle.TrySetResult();
                return;
            }

            _completionInputPosted = false;

            try
            {
                if (!_disposed)
                {
                    await RequestCompletionAsync(false, _pendingCompletionCharacter, CancellationToken.None);
                }

                idle.TrySetResult();
            }
            catch (Exception error)
            {
                if (!_disposed)
                {
                    ReportFailure("Completion", error, Capture());
                }

                idle.TrySetResult();
            }
        }, DispatcherPriority.Background);
    }

    private bool TrackCompletionChange(DocumentChangeEventArgs change)
    {
        if (_applyingCompletion || _completionWindow is not { } popup || !ReferenceEquals(_completionBuffer, _document))
        {
            return false;
        }

        var oldEnd = popup.EndOffset - change.InsertionLength + change.RemovalLength;
        if (change.Offset < popup.StartOffset || change.Offset + change.RemovalLength > oldEnd ||
            !IsCompletionPrefix(change.InsertedText.Text))
        {
            return false;
        }

        _completionPosition = change.Offset + change.InsertionLength;
        _completionCatalogNeedsRefresh = true;
        _preferredCompletionItem ??= popup.CompletionList.SelectedItem as AkburaEditorCompletionItem;
        _preferredCompletionScroll ??= popup.CompletionList.ScrollViewer?.Offset;
        // Retain the last visible rows while awaiting the provider. Its old prefix
        // catalog is not a promise that longer utility arguments can be filtered locally.
        popup.CompletionList.IsFiltering = false;
        return true;
    }

    private void TextEntering(object? sender, TextInputEventArgs e)
    {
        var gestureText = _completionGestureText;
        _completionGestureText = null;
        // Browser input can deliver text separately after the handled key event.
        if (gestureText != null && e.Text == gestureText)
        {
            e.Handled = true;
            return;
        }

        if (_editor.IsReadOnly || e.Text is not { Length: 1 } ||
            _completionWindow?.CompletionList.SelectedItem is not AkburaEditorCompletionItem item)
        {
            return;
        }

        var character = e.Text[0];
        if (!CompletionCommitPolicy.ShouldCommit(item, character))
        {
            return;
        }

        if (!Completions.Contains(item) || !IsCurrent(item.Snapshot, item.Context, true))
        {
            // A retained UI row is not a current edit. Keep the typed character and
            // let normal input obtain a fresh catalog instead of accepting stale offsets.
            return;
        }
        e.Handled = true;
        _ = CommitCompletionAsync(item, character);
    }

    private void CancelCompletionRequests()
    {
        _completionCancellation?.Cancel();
        _completionInputEpoch++;
        _completionInputPosted = false;

        // Commit cancellation also happens on text changes. A character already inserted by a commit remains in the buffer.
        if (!_applyingCompletion)
        {
            _commitCancellation?.Cancel();
        }
    }

    private void DismissCompletion()
    {
        var popup = _completionWindow;
        _completionWindow = null;
        _completionEditsCompatible = false;
        _completionCatalogNeedsRefresh = false;
        _preferredCompletionItem = null;
        _preferredCompletionScroll = null;
        _completionBuffer = null;
        _completionContext = null;
        _completionDescription?.Dispose();
        _completionDescription = null;
#if DEBUG
        if (popup != null) _options.Performance?.Count("PopupDisposed");
#endif
        popup?.Hide();
    }

    public Task RequestCompletionAsync(CancellationToken cancellationToken = default)
    {
        // An explicit invocation supersedes an unresolved insertion. Its typed
        // character is already in the buffer and must survive the fresh request.
        _commitCancellation?.Cancel();
        _completionInputEpoch++;
        _completionInputPosted = false;
        return RequestCompletionAsync(true, '\0', cancellationToken);
    }

    private async Task RequestCompletionAsync(bool isExplicit, char character, CancellationToken cancellationToken)
    {
        var snapshot = Capture();
        if (_editor.IsReadOnly)
        {
            return;
        }

        _completionPosition = snapshot.Position;
        _completionCancellation?.Cancel();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _completionCancellation = cancellation;
        var sequence = ++_completionSequence;
        var incomplete = _incompleteCompletion;
        AkburaDocumentContext? context = null;
        try
        {
            var solution = _workspace.CurrentSolution;
            AkburaSyntacticDocument syntax;
            if (solution.TryGetDocument(DocumentUri, out var document))
            {
                syntax = await GetSyntaxAsync(snapshot, document.FilePath, cancellation.Token);
            }
            else
            {
                context = await SynchronizeAsync(snapshot, cancellation.Token, "completion-sync");
                syntax = await GetSyntaxAsync(snapshot, context.Document.FilePath, cancellation.Token);
            }

            var completionContext = await _services.RunAsync("completion-context", 0, token =>
            {
                var nativeContext = syntax.GetCompletionContext(snapshot.Position, token);
                var akcssContext = syntax.GetAkcssCompletionContext(snapshot.Position, token);
                var csharp = syntax.TryGetCSharpCompletionContext(snapshot.Position, out _, token);
                return Task.FromResult((Native: nativeContext, Akcss: akcssContext, CSharp: csharp));
            }, cancellation.Token);

            if (!isExplicit && !incomplete && completionContext.Native.IsDefault &&
                completionContext.Akcss.IsDefault && !completionContext.CSharp)
            {
                if (!cancellation.IsCancellationRequested && sequence == _completionSequence &&
                    IsCurrent(snapshot, context, checkPosition: true) &&
                    (context != null || ReferenceEquals(solution, _workspace.CurrentSolution)))
                {
                    Completions = [];
                    _incompleteCompletion = false;
                    DismissCompletion();
                }
                return;
            }

            context ??= await SynchronizeAsync(snapshot, cancellation.Token, "completion-sync");
            var results = await _services.RunAsync("completion", 0, async token =>
            {
#if DEBUG
                using var providerMeasurement = _options.Performance?.Measure("CompletionProvider");
                _options.Performance?.Count("CompletionProvider");
                _options.Performance?.Count("NativeCompletionProvider");
#endif
                var native = _workspace.LanguageServices.Completion.GetCompletions(syntax, context, snapshot.Position, token);
                AkburaProjectedCompletionResult? projected = null;
                if (completionContext.CSharp)
                {
#if DEBUG
                    _options.Performance?.Count("ProjectedCompletionProvider");
#endif
                    projected = await _workspace.LanguageServices.ProjectedCSharp.GetCompletionsAsync(syntax, context,
                        snapshot.Position, new(isExplicit, incomplete, character), token).ConfigureAwait(false);
                }
                return (Native: native, Projected: projected);
            }, cancellation.Token);

            if (cancellation.IsCancellationRequested || sequence != _completionSequence || !IsCurrent(snapshot, context, checkPosition: true))
            {
                return;
            }

#if DEBUG
            using var adaptationMeasurement = _options.Performance?.Measure("CompletionAdaptation");
            _options.Performance?.Count("CompletionAdaptation");
#endif
            Completions = AdaptCompletionItems(snapshot, syntax, context, results.Native, results.Projected);
#if DEBUG
            _options.Performance?.Count("CompletionPublished");
#endif
            _incompleteCompletion = results.Native.IsIncomplete || results.Projected?.IsIncomplete == true;
            if (Completions.IsEmpty || TopLevel.GetTopLevel(_editor) == null)
            {
                DismissCompletion();
                return;
            }
            var startOffset = snapshot.Position;
            foreach (var item in Completions)
            {
                startOffset = Math.Min(startOffset, item.SourceSpan.Start);
            }
            var popup = _completionWindow;
            var previous = _preferredCompletionItem ?? popup?.CompletionList.SelectedItem as AkburaEditorCompletionItem;
            var scroll = _preferredCompletionScroll ?? popup?.CompletionList.ScrollViewer?.Offset;
            var contextKey = $"{completionContext.Native.Kind}/{completionContext.Akcss.Kind}/{completionContext.CSharp}";
            if (popup == null || !ReferenceEquals(_completionBuffer, snapshot.Buffer) ||
                popup.StartOffset != startOffset || _completionContext != contextKey)
            {
                DismissCompletion();
#if DEBUG
                _options.Performance?.Count("PopupCreated");
#endif
                popup = new CompletionWindow(_editor.TextArea) { StartOffset = startOffset, EndOffset = snapshot.Position };
                _completionWindow = popup;
                _completionBuffer = snapshot.Buffer;
                _completionContext = contextKey;
                _completionDescription = new CompletionDescriptionPopup(popup, GetCompletionDescriptionAsync);
                var ownedPopup = popup;
                popup.Closed += (_, _) =>
                {
                    if (ReferenceEquals(_completionWindow, ownedPopup))
                    {
#if DEBUG
                        _options.Performance?.Count("PopupDisposed");
#endif
                        _completionWindow = null;
                        _completionBuffer = null;
                        _completionDescription?.Dispose();
                        _completionDescription = null;
                        CancelCompletionRequests();
                        _completionSequence++;
                        _incompleteCompletion = false;
                    }
                };
            }
#if DEBUG
            else _options.Performance?.Count("PopupReused");
#endif
            _completionEditsCompatible = true;
            popup.EndOffset = snapshot.Position;
            using var selectionUpdate = _completionDescription!.BeginUpdate();
            popup.CompletionList.CompletionData.Clear();
            foreach (var item in Completions)
            {
                popup.CompletionList.CompletionData.Add(item);
            }

            var filter = snapshot.Text.ToString(TextSpan.FromBounds(popup.StartOffset, snapshot.Position));
            AkburaEditorCompletionItem? argumentItem = null;
            foreach (var item in Completions)
            {
                if (item.Native?.Kind == AkburaCompletionKind.TailwindUtility &&
                    filter.Length > item.Text.Length && filter.StartsWith(item.Text, StringComparison.OrdinalIgnoreCase))
                {
                    argumentItem = item;
                    break;
                }
            }
            // AvaloniaEdit caches both the last query and the filtered item objects.
            // Reset through public APIs before filtering replacement data. A distinct
            // nonmatching query also handles a previously empty prefix without reflection.
            popup.CompletionList.IsFiltering = false;
            popup.CompletionList.SelectItem("\0");
            popup.CompletionList.IsFiltering = true;
            popup.CompletionList.SelectItem("");
            popup.CompletionList.IsFiltering = argumentItem == null;
            if (!popup.IsOpen)
            {
                popup.Show();
            }

            popup.CompletionList.SelectItem(filter);
            // SelectItemFiltering posts its best selection at Loaded priority. Restore
            // user selection and scroll after those callbacks, before completing this request.
            await Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Loaded);

            if (cancellation.IsCancellationRequested || sequence != _completionSequence ||
                !ReferenceEquals(_completionWindow, popup) || !IsCurrent(snapshot, context, true))
            {
                return;
            }

            var retained = previous == null ? null : FindCurrentCandidate(previous);
            if (retained != null && popup.CompletionList.CurrentList?.Contains(retained) == true)
            {
                popup.CompletionList.SelectedItem = retained;
                if (scroll is { } offset && popup.CompletionList.ScrollViewer is { } viewer)
                {
                    viewer.Offset = offset;
                }
            }
            else if (argumentItem != null && popup.CompletionList.SelectedItem == null)
            {
                popup.CompletionList.SelectedItem = argumentItem;
            }

            _preferredCompletionItem = null;
            _preferredCompletionScroll = null;
            popup.CompletionList.IsVisible = popup.CompletionList.CurrentList?.Count > 0;
            if (!popup.CompletionList.IsVisible)
            {
                DismissCompletion();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(snapshot))
        {
#if DEBUG
            _options.Performance?.Count("CompletionCanceled");
#endif
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _completionSequence)
            {
                ReportFailure("Completion", error, snapshot, context);
            }
        }
        finally
        {
            if (ReferenceEquals(_completionCancellation, cancellation))
            {
                _completionCancellation = null;
            }
        }
    }

    private AkburaEditorCompletionItem? FindCurrentCandidate(AkburaEditorCompletionItem previous)
    {
        // Compare provider fields, never assume a resolve key survives another snapshot.
        // A unique current candidate supplies its own syntax, offsets and resolve identity.
        AkburaEditorCompletionItem? match = null;
        foreach (var item in Completions)
        {
            if (item.IsProjected != previous.IsProjected || item.DisplayText != previous.DisplayText ||
                item.Text != previous.Text || item.SortText != previous.SortText || item.Kind != previous.Kind ||
                item.Native?.Suffix != previous.Native?.Suffix || item.Native?.NamespaceImport != previous.Native?.NamespaceImport)
            {
                continue;
            }
            if (match != null)
            {
                return null;
            }
            match = item;
        }
        return match;
    }

    private static readonly IComparer<(AkburaEditorCompletionItem Item, int Order)> CompletionOrderComparer =
        Comparer<(AkburaEditorCompletionItem Item, int Order)>.Create(static (left, right) =>
        {
            var order = StringComparer.Ordinal.Compare(left.Item.SortText, right.Item.SortText);
            return order != 0 ? order : left.Order.CompareTo(right.Order);
        });

    private ImmutableArray<AkburaEditorCompletionItem> AdaptCompletionItems(RequestSnapshot snapshot,
        AkburaSyntacticDocument syntax, AkburaDocumentContext context, AkburaCompletionResult native,
        AkburaProjectedCompletionResult? projected)
    {
        var items = ArrayBuilder<(AkburaEditorCompletionItem Item, int Order)>.GetInstance();
        var identities = PooledHashSet<string>.GetInstance();
        try
        {
            if (projected is { } projection)
            {
                foreach (var item in projection.Items)
                {
                    if (identities.Add(item.ResolveKey))
                    {
                        items.Add((new(this, snapshot, syntax, context, null, item, item.SourceSpan), items.Count));
                    }
                }
            }
            foreach (var item in native.Items)
            {
                items.Add((new(this, snapshot, syntax, context, item, null, native.ApplicableSpan), items.Count));
            }
            items.Sort(CompletionOrderComparer);
            using var result = ImmutableArrayBuilder<AkburaEditorCompletionItem>.Rent(items.Count);
            foreach (var item in items)
            {
                result.Add(item.Item);
            }
            return result.ToImmutable();
        }
        finally
        {
            identities.Free();
            items.Free();
        }
    }

    private static bool IsCompletionPrefix(ReadOnlySpan<char> text)
    {
        foreach (var character in text)
        {
            if (!char.IsLetterOrDigit(character) && character is not ('_' or '-' or '.'))
            {
                return false;
            }
        }
        return true;
    }

    private bool HasObsoleteCompletions()
    {
        foreach (var item in Completions)
        {
            if (!IsCurrent(item.Snapshot, item.Context))
            {
                return true;
            }
        }
        return false;
    }

    private Task? _popupCommitTask;

    internal Task CompletionCommitIdle => _popupCommitTask ?? Task.CompletedTask;

    internal void CommitFromPopup(AkburaEditorCompletionItem item) => _popupCommitTask = CommitCompletionAsync(item);

    private async Task<string?> GetCompletionDescriptionAsync(AkburaEditorCompletionItem item, CancellationToken cancellationToken)
    {
        if (!IsCurrent(item.Snapshot, item.Context, true))
        {
            return null;
        }
        try
        {
            var description = await _services.RunAsync("description", 0, async token =>
            {
#if DEBUG
                _options.Performance?.Count("DescriptionProvider");
#endif
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
        catch (OperationCanceledException)
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
        if (commitCharacter == null && Completions.Contains(item) && !IsCurrent(item.Snapshot, item.Context, true) &&
            _completionEditsCompatible && ReferenceEquals(item.Snapshot.Buffer, _document))
        {
            var current = Capture();
            await RequestCompletionAsync(cancellationToken);
            if (!IsCurrent(current, checkPosition: true) || FindCurrentCandidate(item) is not { } refreshed)
            {
                return false;
            }

            item = refreshed;
        }
        var snapshot = item.Snapshot;
        if (_editor.IsReadOnly || !Completions.Contains(item) || !IsCurrent(snapshot, item.Context, true))
        {
            return false;
        }

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
        finally
        {
            _applyingCompletion = false;
        }
        var expected = Capture();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken);
        _commitCancellation = cancellation;
        var sequence = ++_commitSequence;
        try
        {
            var change = await _services.RunAsync("commit", 0, async token => item.Projected is { } projected
                ? (await _workspace.LanguageServices.ProjectedCSharp.ResolveCompletionAsync(item.Syntax, item.Context,
                    snapshot.Position, projected.ResolveKey, token).ConfigureAwait(false))?.Change
                : _workspace.LanguageServices.Completion.GetCompletionChange(item.Syntax, item.Context,
                    snapshot.Position, item.Native!, token), cancellation.Token);

            if (change == null || cancellation.IsCancellationRequested || sequence != _commitSequence ||
                _editor.IsReadOnly || !IsCurrent(expected, item.Context, true))
            {
                return false;
            }

            var edits = CompletionEdits.Validate(change, snapshot.Text.Length);
            _applyingCompletion = true;
            if (commitCharacter != null)
            {
                _document.UndoStack.StartContinuedUndoGroup(null);
            }

            try
            {
                using (_document.RunUpdate())
                {
                    if (commitCharacter != null)
                    {
                        _document.Remove(snapshot.Position, 1);
                    }

                    for (var index = edits.Length - 1; index >= 0; index--)
                    {
                        var edit = edits[index];
                        _document.Replace(edit.Span.Start, edit.Span.Length, edit.NewText ?? "");
                    }

                    var caret = change.NewPosition;
                    if (commitCharacter is { } character)
                    {
                        // Canonical edits may already contain the delimiter (for example an attribute '=').
                        if (caret < _document.TextLength && _document.GetCharAt(caret) == character)
                        {
                            caret++;
                        }
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
                if (commitCharacter != null)
                {
                    _document.UndoStack.EndUndoGroup();
                }

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
                    {
                        _ = RequestCompletionAsync();
                    }
                });
            }
            return true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested || !IsCurrent(expected))
        {
            return false;
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && sequence == _commitSequence)
            {
                ReportFailure("Completion commit", error, expected, item.Context);
            }

            return false;
        }
        finally
        {
            if (ReferenceEquals(_commitCancellation, cancellation))
            {
                _commitCancellation = null;
            }

            if (!_disposed && commitCharacter != null)
            {
                _ = RefreshAsync(_options.AnalysisDelay);
            }
        }
    }

    private static bool CanonicalSuffixContainsCharacter(AkburaEditorCompletionItem item,
        ImmutableArray<TextChange> edits, int position, char character)
    {
        foreach (var edit in edits)
        {
            if (edit.Span.Start > position || edit.Span.End < position || edit.NewText is not { } inserted)
            {
                continue;
            }

            if (inserted.StartsWith(item.DisplayText, StringComparison.Ordinal) &&
                inserted.AsSpan(item.DisplayText.Length).Contains(character))
            {
                return true;
            }
        }
        return false;
    }
}
