using Avalonia.Threading;
using System.ComponentModel;

namespace Akbura.HotReload;

public sealed partial class AkburaForeachRegion<TItem, TChild>
{
    private Dictionary<object, ItemPropertyLease>? _itemLeases;
    private Dictionary<object, long>? _dirtyItems;
    private long _itemSequence;

    private bool HasDirtyItems => _dirtyItems is { Count: > 0 };

    private void MarkItemDirty(object item)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            throw new InvalidOperationException("Observed foreach item properties must change on the Avalonia UI thread.");
        }

        (_dirtyItems ??= new(ReferenceEqualityComparer.Instance))[item] = ++_itemSequence;
        ScheduleInvalidation();
    }

    private bool IsItemDirty(TItem item) => item is object instance &&
        _dirtyItems?.ContainsKey(instance) == true;

    private void CompleteItemPropertyLeases(RegionUpdate update)
    {
        if ((update.Dependencies & AkburaForeachDependencies.ReadsNotifyingItemProperties) == 0)
        {
            ReleaseItemPropertyLeases();
            return;
        }

        var desired = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var record in update.Records)
        {
            if (record.Item is INotifyPropertyChanged notifier)
            {
                desired.Add(notifier);
            }
        }

        var leases = _itemLeases ??= new(ReferenceEqualityComparer.Instance);
        foreach (var previous in leases.Keys.ToArray())
        {
            if (!desired.Contains(previous))
            {
                leases[previous].Dispose();
                leases.Remove(previous);
            }
        }

        foreach (var item in desired)
        {
            if (!leases.ContainsKey(item))
            {
                leases.Add(item, new ItemPropertyLease(this, (INotifyPropertyChanged)item));
            }
        }

        if (_dirtyItems != null)
        {
            foreach (var pair in _dirtyItems.ToArray())
            {
                if (pair.Value <= update.ItemSequence || !desired.Contains(pair.Key))
                {
                    _dirtyItems.Remove(pair.Key);
                }
            }
        }
    }

    private void ReleaseItemPropertyLeases()
    {
        if (_itemLeases != null)
        {
            foreach (var lease in _itemLeases.Values)
            {
                lease.Dispose();
            }

            _itemLeases = null;
        }

        _dirtyItems?.Clear();
    }

    private sealed class ItemPropertyLease : IDisposable
    {
        private readonly WeakReference<AkburaForeachRegion<TItem, TChild>> _owner;
        private INotifyPropertyChanged? _item;

        public ItemPropertyLease(AkburaForeachRegion<TItem, TChild> owner, INotifyPropertyChanged item)
        {
            _owner = new(owner);
            _item = item;
            item.PropertyChanged += OnChanged;
        }

        public void Dispose()
        {
            if (_item is { } item)
            {
                _item = null;
                item.PropertyChanged -= OnChanged;
            }
        }

        private void OnChanged(object? sender, PropertyChangedEventArgs args)
        {
            if (_owner.TryGetTarget(out var owner) && _item != null)
            {
                owner.MarkItemDirty(_item);
            }
            else
            {
                Dispose();
            }
        }
    }
}
