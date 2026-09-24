using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Threading;

namespace Akbura.Collections;

/// <summary>
/// Owns the connection between a stable component list and an external source.
/// Collection notifications and mutations must occur on the owning UI thread.
/// This is content synchronization, not a two-way binding of collection references.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class CollectionParameterBinding<T> : IDisposable
{
    private readonly Action _changed;
    private readonly Action _verifyAccess;
    private readonly string _parameterName;
    private object? _source;
    private ListAccess? _sourceList;
    private SourceLease? _lease;
    private bool _hasSource;
    private bool _suspended;
    private bool _disposed;
    private bool _applying;
    private bool _writingSource;
    private bool _refreshPending;
    private bool _localDiverged;
    private bool _repairQueued;

    public CollectionParameterBinding(ObservableCollection<T> items, Action changed,
        Action? verifyAccess = null, string? parameterName = null)
    {
        Items = items ?? throw new ArgumentNullException(nameof(items));
        _changed = changed ?? throw new ArgumentNullException(nameof(changed));
        _verifyAccess = verifyAccess ?? (() => { });
        _parameterName = string.IsNullOrWhiteSpace(parameterName)
            ? typeof(T).Name + " collection parameter"
            : parameterName!;
        Items.CollectionChanged += OnItemsChanged;
    }

    public ObservableCollection<T> Items { get; }
    public object? Source => _source;

    public bool IsTwoWay => !_suspended && _lease != null && _sourceList?.CanWrite == true;

    /// <summary>
    /// A new source replaces the contents, never Items itself. Reassigning the
    /// current source or Items is a no-op. Null disconnects and empties the list.
    /// Validate and materialize before disconnecting the previous source.
    /// </summary>
    public void SetSource(object? source)
    {
        CheckAccess();
        if (ReferenceEquals(source, Items) ||
            _hasSource && ReferenceEquals(source, _source) && (source != null || Items.Count == 0))
        {
            return;
        }
        if (_applying)
        {
            throw new InvalidOperationException("A collection parameter source cannot be replaced during synchronization.");
        }

        var values = Snapshot(source);
        var access = ListAccess.Create(source);
        _lease?.Dispose();
        _lease = null;
        _source = source;
        _sourceList = access;
        _hasSource = true;
        _localDiverged = false;
        Subscribe();
        UpdateItems(() => Reconcile(values));
    }

    /// <summary>Refreshes a non-notifying source explicitly, preserving Items.</summary>
    public void Refresh()
    {
        CheckAccess();
        if (_applying)
        {
            _refreshPending = true;
            return;
        }
        UpdateItems(() => Reconcile(Snapshot(_source), invalidateUnchanged: true));
    }

    /// <summary>Disconnects event delivery while retaining the source and owned list.</summary>
    public void Suspend()
    {
        CheckAccess();
        _suspended = true;
        _lease?.Dispose();
        _lease = null;
    }

    /// <summary>The source is authoritative on reattachment; detached local edits are not replayed.</summary>
    public void Resume()
    {
        CheckAccess();
        if (!_suspended) return;
        _suspended = false;
        Subscribe();
        if (_hasSource) Refresh();
    }

    private void Subscribe()
    {
        if (!_suspended && _source is INotifyCollectionChanged notifier)
        {
            _lease = new SourceLease(this, notifier);
        }
    }

    private void OnSourceChanged(SourceLease lease, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed || !ReferenceEquals(lease, _lease)) return;
        _verifyAccess();
        if (_writingSource) return; // echo; the reverse operation verifies the final source below
        if (_applying)
        {
            _refreshPending = true;
            return;
        }
        UpdateItems(() =>
        {
            if (_localDiverged || _sourceList == null || !TryApplyDelta(
                    new ListAccess(Items), args, _sourceList.Count))
            {
                // A Reset or an unaddressable notification can invalidate data
                // on an unchanged object. Relay replacements rather than losing
                // that notification merely because its reference is unchanged.
                Reconcile(Snapshot(_source), invalidateUnchanged: true);
            }
        });
    }

    private void OnItemsChanged(object? sender, NotifyCollectionChangedEventArgs args)
    {
        if (_disposed || _applying) return;
        _verifyAccess();
        if (!IsTwoWay)
        {
            // An observed readonly source stays authoritative on its next event.
            _localDiverged = true;
            _changed();
            return;
        }

        _applying = true;
        _writingSource = true;
        try
        {
            if (!TryApplyDelta(_sourceList!, args, Items.Count))
            {
                // Reset (Clear) has no OldItems. Rebuild only in this fallback,
                // not on an ordinary Add/Remove/Replace/Move notification.
                var values = Snapshot(Items);
                _sourceList!.Clear();
                foreach (var value in values) _sourceList.Insert(_sourceList.Count, value);
            }
        }
        finally
        {
            _writingSource = false;
            try
            {
                // Other source listeners may have transformed a write or thrown
                // after changing the source. Reflect its actual state, not a
                // guessed inverse operation; arbitrary IList writes are not atomic.
                var actual = Snapshot(_source);
                if (!ContentsMatch(actual))
                {
                    _localDiverged = true;
                    QueueRefresh();
                }
                else _localDiverged = false;
            }
            finally
            {
                _applying = false;
                // This callback is still inside the owned collection's event.
                // A pending repair must not mutate that collection reentrantly.
                if (_refreshPending)
                {
                    _refreshPending = false;
                    QueueRefresh();
                }
                _changed();
            }
        }
    }

    private void QueueRefresh()
    {
        if (_repairQueued) return;
        _repairQueued = true;
        Dispatcher.UIThread.Post(() =>
        {
            _repairQueued = false;
            if (!_disposed && !_suspended) Refresh();
        });
    }

    private void UpdateItems(Action update)
    {
        _applying = true;
        try
        {
            update();
            _localDiverged = false;
        }
        finally
        {
            _applying = false;
            DrainPendingRefresh();
            _changed();
        }
    }

    private void DrainPendingRefresh()
    {
        // Reentrant source notifications during an outbound Items event are
        // reconciled after that event unwinds. No recursive CollectionChanged writes.
        var attempts = 0;
        while (_refreshPending)
        {
            _refreshPending = false;
            if (++attempts > 8)
            {
                throw new InvalidOperationException("Collection listeners keep changing the source during synchronization.");
            }
            _applying = true;
            try { Reconcile(Snapshot(_source), invalidateUnchanged: true); _localDiverged = false; }
            finally { _applying = false; }
        }
    }

    private bool ContentsMatch(IReadOnlyList<T> values)
    {
        if (Items.Count != values.Count) return false;
        for (var i = 0; i < Items.Count; i++) if (!Same(Items[i], values[i])) return false;
        return true;
    }

    private void Reconcile(IReadOnlyList<T> values, bool invalidateUnchanged = false)
    {
        // Linear positional reconciliation. Source replacement preserves identical
        // occurrences; Reset/explicit refresh invalidate even unchanged references.
        // Reference VMs use identity, so distinct equal objects are not substituted.
        var common = Math.Min(Items.Count, values.Count);
        for (var i = 0; i < common; i++)
        {
            if (invalidateUnchanged || !Same(Items[i], values[i])) Items[i] = values[i];
        }
        for (var i = Items.Count - 1; i >= values.Count; i--) Items.RemoveAt(i);
        for (var i = common; i < values.Count; i++) Items.Add(values[i]);
    }

    private static bool TryApplyDelta(ListAccess target, NotifyCollectionChangedEventArgs args, int afterCount)
    {
        var oldValues = ReadItems(args.OldItems);
        var newValues = ReadItems(args.NewItems);
        var oldIndex = args.OldStartingIndex;
        var newIndex = args.NewStartingIndex;
        switch (args.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (newValues.Length == 0 || newIndex < 0 || newIndex > target.Count ||
                    target.Count + newValues.Length != afterCount) return false;
                for (var i = 0; i < newValues.Length; i++) target.Insert(newIndex + i, newValues[i]);
                return true;
            case NotifyCollectionChangedAction.Remove:
                if (!Matches(target, oldIndex, oldValues) || target.Count - oldValues.Length != afterCount) return false;
                for (var i = 0; i < oldValues.Length; i++) target.RemoveAt(oldIndex);
                return true;
            case NotifyCollectionChangedAction.Replace:
                if (newIndex != oldIndex || !Matches(target, oldIndex, oldValues) ||
                    target.Count - oldValues.Length + newValues.Length != afterCount) return false;
                if (oldValues.Length == newValues.Length)
                {
                    for (var i = 0; i < newValues.Length; i++) target[oldIndex + i] = newValues[i];
                }
                else
                {
                    for (var i = 0; i < oldValues.Length; i++) target.RemoveAt(oldIndex);
                    for (var i = 0; i < newValues.Length; i++) target.Insert(oldIndex + i, newValues[i]);
                }
                return true;
            case NotifyCollectionChangedAction.Move:
                if (target.Count != afterCount || !Matches(target, oldIndex, oldValues) ||
                    newIndex < 0 || newIndex > target.Count - oldValues.Length ||
                    oldValues.Length != newValues.Length) return false;
                for (var i = 0; i < oldValues.Length; i++)
                    if (!Same(oldValues[i], newValues[i])) return false;
                if (oldIndex < newIndex)
                {
                    for (var i = 0; i < oldValues.Length; i++) target.Move(oldIndex, newIndex + oldValues.Length - 1);
                }
                else if (oldIndex > newIndex)
                {
                    for (var i = 0; i < oldValues.Length; i++) target.Move(oldIndex + i, newIndex + i);
                }
                return true;
            default:
                return false;
        }
    }

    private static bool Matches(ListAccess target, int index, T[] expected)
    {
        if (expected.Length == 0 || index < 0 || index > target.Count - expected.Length) return false;
        for (var i = 0; i < expected.Length; i++)
            if (!Same(target[index + i], expected[i])) return false;
        return true;
    }

    private static bool Same(T left, T right)
    {
        if (typeof(T).IsValueType) return EqualityComparer<T>.Default.Equals(left, right);
        if (ReferenceEquals(left, right)) return true;
        // A nongeneric IList boxes value items afresh when indexed/enumerated.
        // Compare those values without conflating equal but distinct reference VMs.
        return left is object a && right is object b && a.GetType().IsValueType &&
            a.GetType() == b.GetType() && a.Equals(b);
    }

    private static T[] ReadItems(IList? values)
    {
        if (values == null) return Array.Empty<T>();
        var result = new T[values.Count];
        for (var i = 0; i < result.Length; i++) result[i] = Cast(values[i]);
        return result;
    }

    private static T Cast(object? value)
    {
        if (value is T typed) return typed;
        if (value == null && default(T) is null) return default!;
        throw new ArgumentException($"Collection item '{value?.GetType().FullName ?? "null"}' is not compatible with '{typeof(T)}'.");
    }

    private T[] Snapshot(object? source)
    {
        if (source == null) return Array.Empty<T>();
        if (source is not IEnumerable sequence)
        {
            throw new ArgumentException(
                $"Cannot assign collection source to parameter '{_parameterName}'.{Environment.NewLine}" +
                $"Source type: {source.GetType().FullName}.{Environment.NewLine}" +
                "Expected an enumerable source.",
                nameof(source));
        }
        var values = new List<T>();
        foreach (var value in sequence)
        {
            var index = values.Count;
            if (value is T typed)
            {
                values.Add(typed);
                continue;
            }
            if (value == null && default(T) is null)
            {
                values.Add(default!);
                continue;
            }
            throw new ArgumentException(
                $"Cannot assign collection source to parameter '{_parameterName}'.{Environment.NewLine}" +
                $"Source type: {source.GetType().FullName}.{Environment.NewLine}" +
                $"Item index: {index}.{Environment.NewLine}" +
                $"Expected item type: {typeof(T).FullName}.{Environment.NewLine}" +
                $"Actual item type: {value?.GetType().FullName ?? "null"}.",
                nameof(source));
        }
        return values.ToArray();
    }

    private void CheckAccess()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _verifyAccess();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _verifyAccess();
        _disposed = true;
        _lease?.Dispose();
        _lease = null;
        Items.CollectionChanged -= OnItemsChanged;
        _source = null;
        _sourceList = null;
    }

    private sealed class ListAccess
    {
        private readonly IList<T>? _generic;
        private readonly IList? _untyped;
        private readonly ObservableCollection<T>? _observable;

        public ListAccess(ObservableCollection<T> list) : this(list, list, list) { }
        private ListAccess(IList<T>? generic, IList? untyped, ObservableCollection<T>? observable)
        {
            _generic = generic; _untyped = untyped; _observable = observable;
        }
        public static ListAccess? Create(object? source) => source is IList<T> generic
            ? new ListAccess(generic, source as IList, source as ObservableCollection<T>)
            : source is IList untyped ? new ListAccess(null, untyped, null) : null;
        public int Count => _generic?.Count ?? _untyped!.Count;
        public bool CanWrite => (_generic == null || !_generic.IsReadOnly) &&
            (_untyped == null || !_untyped.IsReadOnly && !_untyped.IsFixedSize);
        public T this[int index]
        {
            get => _generic != null ? _generic[index] : Cast(_untyped![index]);
            set { if (_generic != null) _generic[index] = value; else _untyped![index] = value; }
        }
        public void Insert(int index, T value)
        {
            if (_generic != null) _generic.Insert(index, value); else _untyped!.Insert(index, value);
        }
        public void RemoveAt(int index)
        {
            if (_generic != null) _generic.RemoveAt(index); else _untyped!.RemoveAt(index);
        }
        public void Clear()
        {
            if (_generic != null) _generic.Clear(); else _untyped!.Clear();
        }
        public void Move(int from, int to)
        {
            if (_observable != null) { _observable.Move(from, to); return; }
            var value = this[from];
            RemoveAt(from);
            Insert(to, value);
        }
    }

    // An application-scoped resource must not keep an abandoned component alive.
    // Replacement/detach/disposal unsubscribe eagerly; the weak owner is a GC safety net.
    private sealed class SourceLease : IDisposable
    {
        private readonly WeakReference<CollectionParameterBinding<T>> _owner;
        private INotifyCollectionChanged? _source;
        public SourceLease(CollectionParameterBinding<T> owner, INotifyCollectionChanged source)
        {
            _owner = new(owner);
            _source = source;
            source.CollectionChanged += OnChanged;
        }
        private void OnChanged(object? sender, NotifyCollectionChangedEventArgs args)
        {
            if (_owner.TryGetTarget(out var owner)) owner.OnSourceChanged(this, args);
            else Dispose();
        }
        public void Dispose()
        {
            if (_source == null) return;
            _source.CollectionChanged -= OnChanged;
            _source = null;
        }
    }
}
