using Akbura.Engine;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.LogicalTree;
using AvaloniaEdit.CodeCompletion;

namespace Akbura.AvaloniaEdit.Completion;

/// <summary>
/// Anchors completion documentation to the actual list in native and overlay popup hosts.
/// </summary>
internal sealed partial class CompletionDescriptionPopup : AkburaControl, IDisposable
{
    private readonly CompletionWindow _owner;
    private readonly Func<AkburaEditorCompletionItem, CancellationToken, Task<string?>> _getDescription;
    private CancellationTokenSource? _pending;
    private long _sequence;
    private Size _placementTargetSize;
    private bool _disposed;
    private int _updates;
    private readonly Dictionary<AkburaEditorCompletionItem, DescriptionEntry> _descriptions = [];
    private CancellationTokenSource _cacheLifetime = new();
    private AkburaEditorCompletionItem? _cacheIdentity;

    internal Popup Popup => descriptionPopup;

    public CompletionDescriptionPopup(CompletionWindow owner,
        Func<AkburaEditorCompletionItem, CancellationToken, Task<string?>> getDescription)
        : base(AkburaEngine.Empty)
    {
        _owner = owner;
        _getDescription = getDescription;
        ((ISetLogicalParent)this).SetParent(owner.CompletionList);
        InitializeIfNeeded();
        owner.CompletionList.SelectionChanged += SelectionChanged;
        owner.CompletionList.LayoutUpdated += OnListLayoutUpdated;
        owner.Closed += OwnerClosed;
    }

    internal IDisposable BeginUpdate()
    {
        _updates++;
        return new SelectionUpdate(this);
    }

    private sealed class SelectionUpdate(CompletionDescriptionPopup owner) : IDisposable
    {
        public void Dispose()
        {
            if (--owner._updates == 0)
            {
                _ = owner.UpdateDescriptionAsync();
            }
        }
    }

    private void SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_updates == 0)
        {
            _ = UpdateDescriptionAsync();
        }
    }

    private Task<string?> CachedDescription(AkburaEditorCompletionItem item)
    {
        if (_cacheIdentity is { } previous && (previous.Snapshot.Revision != item.Snapshot.Revision ||
            !ReferenceEquals(previous.Snapshot.Buffer, item.Snapshot.Buffer) || !ReferenceEquals(previous.Context.Solution, item.Context.Solution)))
        {
            _cacheLifetime.Cancel();
            _cacheLifetime.Dispose();
            _cacheLifetime = new();
            _descriptions.Clear();
        }
        _cacheIdentity = item;
        if (_descriptions.TryGetValue(item, out var cached))
        {
            return cached.Task;
        }

        if (_descriptions.Count >= 64)
        {
            _descriptions.Clear();
        }

        var entry = new DescriptionEntry();
        _descriptions[item] = entry;
        return entry.Task = FetchDescriptionAsync(item, _cacheLifetime.Token, entry);
    }

    private sealed class DescriptionEntry
    {
        internal Task<string?> Task
        {
            get; set;
        } = System.Threading.Tasks.Task.FromResult<string?>(null);
    }

    private async Task<string?> FetchDescriptionAsync(AkburaEditorCompletionItem item, CancellationToken token, DescriptionEntry entry)
    {
        string? result;
        try { result = await _getDescription(item, token); }
        catch (OperationCanceledException) { result = null; }
        // The session reports service failures. Observe a fault even if selection stopped waiting.
        catch (Exception) { result = null; }
        if (string.IsNullOrWhiteSpace(result) && _descriptions.TryGetValue(item, out var cached) && ReferenceEquals(cached, entry))
            _descriptions.Remove(item);
        return result;
    }

    private async Task UpdateDescriptionAsync()
    {
        _pending?.Cancel();
        Popup.IsOpen = false;
        descriptionText.Text = null;
        var sequence = ++_sequence;
        if (_disposed || _owner.CompletionList.SelectedItem is not AkburaEditorCompletionItem item)
        {
            return;
        }

        using var cancellation = new CancellationTokenSource();
        _pending = cancellation;
        try
        {
            var description = await CachedDescription(item).WaitAsync(cancellation.Token);
            if (_disposed || cancellation.IsCancellationRequested || sequence != _sequence ||
                !ReferenceEquals(_owner.CompletionList.SelectedItem, item) || string.IsNullOrWhiteSpace(description))
            {
                return;
            }
            descriptionText.Text = description;
            UpdateAnchor();
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        finally
        {
            if (ReferenceEquals(_pending, cancellation))
            {
                _pending = null;
            }
        }
    }

    private void OnListLayoutUpdated(object? sender, EventArgs e)
    {
        if (!_disposed && !string.IsNullOrWhiteSpace(descriptionText.Text))
        {
            UpdateAnchor();
        }
    }

    private void UpdateAnchor()
    {
        var list = _owner.CompletionList;
        var row = list.ListBox.ContainerFromIndex(list.ListBox.SelectedIndex);
        var position = row?.TranslatePoint(default, list);
        var placementTarget = TopLevel.GetTopLevel(list);
        var listOrigin = placementTarget == null ? null : list.TranslatePoint(default, placementTarget);
        if (!_owner.IsOpen || row == null || position == null ||
            placementTarget == null || listOrigin == null ||
            position.Value.Y + row.Bounds.Height <= 0 || position.Value.Y >= list.Bounds.Height)
        {
            Popup.IsOpen = false;
            return;
        }

        // Nested overlay popups can have a different presentation root. Convert the actual
        // list rectangle into its TopLevel's coordinates instead of anchoring to that root's edges.
        if (_placementTargetSize != placementTarget.Bounds.Size)
        {
            // Browser TopLevels do not raise Window resize events for an open Popup host.
            // Reopen it so constraint adjustment uses the new available area.
            Popup.IsOpen = false;
            _placementTargetSize = placementTarget.Bounds.Size;
        }
        Popup.PlacementTarget = placementTarget;
        Popup.PlacementRect = new Rect(listOrigin.Value.X,
            listOrigin.Value.Y + Math.Max(0, position.Value.Y), list.Bounds.Width, row.Bounds.Height);
        Popup.IsOpen = true;
    }

    private void OwnerClosed(object? sender, EventArgs e) => Dispose();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _pending?.Cancel();
        _cacheLifetime.Cancel();
        _cacheLifetime.Dispose();
        _descriptions.Clear();
        _cacheIdentity = null;
        _owner.CompletionList.SelectionChanged -= SelectionChanged;
        _owner.CompletionList.LayoutUpdated -= OnListLayoutUpdated;
        _owner.Closed -= OwnerClosed;
        Popup.IsOpen = false;
        Popup.Child = null;
        ((ISetLogicalParent)this).SetParent(null);
    }
}
